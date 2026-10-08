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

int interval = 0;
HeroesClientVersion expected = null;
for (int i = 0; i < args.Length; i++)
{
    if (args[i] == "--watch")
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
        Console.WriteLine("heroes-client-probe [--watch [ms]] [--version 2.57.0.98304]");
        return 0;
    }
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

internal sealed class Readers : IDisposable
{
    private readonly ClientScreenMemory screens = new();
    private readonly LoadingScreenMemory loading = new();
    private readonly StableMatchClock clock = new();

    public string Describe(Process client, HeroesClientVersion expected)
    {
        ClientScreenSample screen = screens.Read(client, expected);
        LoadingScreenSample legacy = loading.Read(client);
        StableClockSample time = clock.Read(client);
        string shown = screen.Shown.Count == 0 ? "-" : string.Join(",", screen.Shown);
        string clockText = time.Ok
            ? TimeSpan.FromSeconds(time.Seconds).ToString(@"mm\:ss", CultureInfo.InvariantCulture)
            : time.Reason;
        string mismatch = screen.VersionMismatch ? $" (expected {expected})" : string.Empty;
        return $"{screen.ClientVersion?.ToString() ?? "?"}{mismatch} screen {screen.Screen} ({screen.Reason}) shown [{shown}] signed-in {Show(screen.SignedIn)} | loading-screen {legacy.Screen} ({legacy.Reason}, menu seen {legacy.MenuSeen}) | clock {clockText}";
    }

    private static string Show(bool? value) => value?.ToString() ?? "unknown";

    public void Dispose()
    {
        screens.Dispose();
        loading.Dispose();
        clock.Dispose();
    }
}
