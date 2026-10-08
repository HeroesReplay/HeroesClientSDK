using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Threading;
using HeroesClientSDK;

// Read-only probe: prints what HeroesClientSDK reads from every running Heroes of the Storm
// client. Nothing is written to a client.
//
//   heroes-client-probe                 one read of each client
//   heroes-client-probe --watch [ms]    a line whenever a client's reading changes (Ctrl+C stops)
//   heroes-client-probe --version 2.57.0.98304   pass an expected build (optional)
//   heroes-client-probe --image <file>  every reader's discovery on a saved module image, offline

int interval = 0;
HeroesClientVersion expected = null;
string image = null;
for (int i = 0; i < args.Length; i++)
{
    if (args[i] == "--image" && i + 1 < args.Length)
    {
        image = args[++i];
    }
    else if (args[i] == "--watch")
    {
        interval =
            i + 1 < args.Length
            && int.TryParse(
                args[i + 1],
                NumberStyles.None,
                CultureInfo.InvariantCulture,
                out int ms
            )
                ? ms
                : 250;
    }
    else if (args[i] == "--version" && i + 1 < args.Length)
    {
        expected = HeroesClientVersion.TryParse(args[++i]);
    }
    else if (args[i] is "-h" or "--help")
    {
        Console.WriteLine(
            "heroes-client-probe [--watch [ms]] [--version 2.57.0.98304] | --image <file> [--version ...]"
        );
        return 0;
    }
}

if (image != null)
{
    return CheckImage(image, expected);
}

var readers = new Dictionary<int, Readers>();
var last = new Dictionary<int, string>();
do
{
    Process[] clients = Process.GetProcessesByName("HeroesOfTheStorm_x64");
    if (clients.Length == 0 && interval == 0)
    {
        Console.WriteLine("No HeroesOfTheStorm_x64 process.");
    }

    foreach (Process client in clients)
    {
        if (!readers.TryGetValue(client.Id, out Readers reader))
        {
            reader = new Readers();
            readers[client.Id] = reader;
        }

        string line = reader.Describe(client, expected);
        if (interval == 0 || !last.TryGetValue(client.Id, out string before) || before != line)
        {
            Console.WriteLine(
                $"{DateTime.Now.ToString("HH:mm:ss.fff", CultureInfo.InvariantCulture)} pid {client.Id} {line}"
            );
            last[client.Id] = line;
        }
    }

    foreach (int gone in readers.Keys.Where(id => clients.All(c => c.Id != id)).ToList())
    {
        Console.WriteLine(
            $"{DateTime.Now.ToString("HH:mm:ss.fff", CultureInfo.InvariantCulture)} pid {gone} exited"
        );
        readers[gone].Dispose();
        readers.Remove(gone);
        last.Remove(gone);
    }

    foreach (Process client in clients)
    {
        client.Dispose();
    }

    if (interval > 0)
    {
        Thread.Sleep(interval);
    }
} while (interval > 0);

foreach (Readers reader in readers.Values)
{
    reader.Dispose();
}

return 0;

// Every reader's discovery on a module image that Save-ModuleImage.ps1 saved from a running
// client. Exit 0 when every reader finds what it needs, 1 when one does not, 2 when the file does
// not serve.
static int CheckImage(string path, HeroesClientVersion expected)
{
    using HeroesClientProcess client = HeroesClientProcess.FromImage(path);
    if (!client.Ok)
    {
        Console.WriteLine($"{path}: {client.Reason}");
        return 2;
    }

    var watch = Stopwatch.StartNew();
    ClientDiscovery found = ClientDiscovery.Run(client, clientVersion: expected);
    string Hex(long value) => "0x" + value.ToString("X", CultureInfo.InvariantCulture);
    string Index(string name)
    {
        int at = found.Menus.Screens.ToList().IndexOf(name);
        return at < 0 ? $"{name} -" : $"{name} {at}";
    }

    Console.WriteLine(
        $"{path}: {client.DetectedVersion?.ToString() ?? "no version"}, base {Hex(client.Module.BaseAddress)}, size {Hex(client.Module.Size)}"
    );
    MatchClockDiscovery clock = found.Clock;
    Console.WriteLine(
        $"clock          {clock.Reason}: tick {Hex(clock.TickRva)}, speed {Hex(clock.SpeedRva)}, {clock.Sites} sites"
    );
    LoadingScreenDiscovery loading = found.Loading;
    Console.WriteLine(
        $"loading-screen {loading.Reason}: global {Hex(loading.GlobalRva)}, {loading.Sites} sites"
    );
    ClientScreenDiscovery menus = found.Menus;
    Console.WriteLine(
        $"client-screen  {menus.Reason}: global {Hex(menus.GlobalRva)}, mask +{Hex(menus.MaskOffset)}, frames +{Hex(menus.FramesOffset)}, {menus.Screens.Count} screens ({Index("ScreenLoading")}, {Index("ScreenLoginUnified")}, {Index("ScreenHome")}, {Index("ScreenScore")})"
    );
    Console.WriteLine(
        $"game-launch    global {Hex(menus.LaunchGlobalRva)}, result +{Hex(menus.LaunchResultOffset)}, state +{Hex(menus.LaunchStateOffset)}, {Math.Max(0, menus.LaunchKeys.Count - 1)} keys"
    );
    string missing =
        menus.MissingFrameClasses.Count == 0
            ? "none missing"
            : "missing " + string.Join(", ", menus.MissingFrameClasses);
    Console.WriteLine($"frame-classes  {menus.FrameClasses} named, {missing}");
    Console.WriteLine(
        $"{(found.Ok ? "ok" : "NOT OK")} in {watch.ElapsedMilliseconds.ToString(CultureInfo.InvariantCulture)} ms"
    );
    return found.Ok ? 0 : 1;
}

/// <summary>
/// The three readers of one client process, sharing one read-only attachment
/// (<see cref="HeroesClientProcess"/>), attached again while it is not ok (a client still starting).
/// </summary>
internal sealed class Readers : IDisposable
{
    private readonly ClientScreen screens = new();
    private readonly LoadingScreen loading = new();
    private readonly MatchClock clock = new();
    private HeroesClientProcess attached;

    public string Describe(Process client, HeroesClientVersion expected)
    {
        if (attached is not { Ok: true })
        {
            attached?.Dispose();
            attached = HeroesClientProcess.Attach(client);
        }

        ClientScreenSample screen = screens.Read(attached, expected);
        LoadingScreenSample legacy = loading.Read(attached, expected);
        MatchClockSample time = clock.Read(attached, expected);
        string shown = screen.Shown.Count == 0 ? "-" : string.Join(",", screen.Shown);
        string clockText = time.Time is TimeSpan at
            ? at.ToString(@"mm\:ss", CultureInfo.InvariantCulture)
            : time.Reason;
        string mismatch = screen.VersionMismatch ? $" (expected {expected})" : string.Empty;
        string dialogs =
            screen.Dialogs == null || screen.Dialogs.Count == 0
                ? "-"
                : string.Join(",", screen.Dialogs);
        string launch = screen.LaunchResultCode is int code
            ? $"{code}{(screen.LaunchResult == null ? string.Empty : " " + screen.LaunchResult)}"
            : "?";
        string state = screen.LaunchState?.ToString(CultureInfo.InvariantCulture) ?? "?";
        return $"{attached.DetectedVersion?.ToString() ?? "?"}{mismatch} screen {screen.Screen} ({screen.Reason}) shown [{shown}] dialogs [{dialogs}] launch {launch} state {state} map-loading {Show(screen.MapLoading)} signed-in {Show(screen.SignedIn)} | loading-screen {legacy.Screen} ({legacy.Reason}, menu seen {legacy.MenuSeen}) | clock {clockText}";
    }

    private static string Show(bool? value) => value?.ToString() ?? "unknown";

    public void Dispose()
    {
        screens.Dispose();
        loading.Dispose();
        clock.Dispose();
        attached?.Dispose();
    }
}
