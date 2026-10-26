using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using HeroesClientSDK;

/// <summary>
/// <c>heroes-client-probe --seek mm:ss</c>: moves a running replay to a match time with the
/// replay speed keys (<see cref="ReplaySeeker"/>), then optionally pauses, follows a player,
/// toggles AhliObs' observer tools and saves a PNG of the game window. With
/// <c>--replay</c> it also opens the replay from the home screen or a menu, and replays a target
/// behind the match from the start (Esc, Quit Replay, open it again through HeroesSwitcher). It
/// never starts a client: start one as the heroes-client-launch skill says, under the lock.
/// </summary>
internal sealed class SeekCommand
{
    private static readonly TimeSpan MenuTimeout = TimeSpan.FromSeconds(60);

    public TimeSpan Target { get; init; }
    public bool Pause { get; init; }
    public int? Player { get; init; }
    public bool TogglePanel { get; init; }
    public string Shot { get; init; }
    public string Replay { get; init; }
    public string Switcher { get; init; }
    public int? ProcessId { get; init; }

    /// <summary>Parses <c>mm:ss</c>, <c>h:mm:ss</c> or seconds.</summary>
    public static bool TryParseTime(string text, out TimeSpan time)
    {
        time = TimeSpan.Zero;
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        if (
            double.TryParse(
                text,
                NumberStyles.AllowDecimalPoint,
                CultureInfo.InvariantCulture,
                out double seconds
            )
        )
        {
            time = TimeSpan.FromSeconds(seconds);
            return true;
        }

        string[] parts = text.Split(':');
        int total = 0;
        foreach (string part in parts)
        {
            if (
                parts.Length > 3
                || !int.TryParse(part, NumberStyles.None, CultureInfo.InvariantCulture, out int n)
            )
            {
                return false;
            }

            total = total * 60 + n;
        }

        time = TimeSpan.FromSeconds(total);
        return true;
    }

    public async Task<int> RunAsync()
    {
        var watch = Stopwatch.StartNew();
        TimeSpan restart = TimeSpan.Zero;
        using Client client = Client.Pick(ProcessId);
        if (client == null)
        {
            return Report("no-client", null, watch.Elapsed, restart, null);
        }

        if (
            Replay != null
            && client.Position().Screen is ClientScreenKind.Home or ClientScreenKind.Menu
        )
        {
            OpenReplay(client);
        }

        ReplaySeekResult result = await client.Seeker().SeekAsync(Target, Pause);
        if (result.Reason == "behind-target" && Replay != null)
        {
            var again = Stopwatch.StartNew();
            Console.WriteLine(
                $"Match is at {Show(result.From)}, past {Show(Target)}: quitting the replay to play it from the start."
            );
            client.Keys.Send(ReplayKey.Escape);
            Thread.Sleep(800);
            client.Keys.Send(ReplayKey.QuitReplay);
            if (
                !client.WaitFor(
                    MenuTimeout,
                    p => p.Screen is ClientScreenKind.Home or ClientScreenKind.Menu
                )
            )
            {
                return Report("no-menu", result, watch.Elapsed, again.Elapsed, null);
            }

            Thread.Sleep(1000);
            OpenReplay(client);
            if (!client.WaitFor(TimeSpan.FromMinutes(3), p => p.Time != null))
            {
                return Report("no-match", result, watch.Elapsed, again.Elapsed, null);
            }

            restart = again.Elapsed;
            result = await client.Seeker().SeekAsync(Target, Pause);
        }

        if (!result.Ok)
        {
            return Report(result.Reason, result, watch.Elapsed, restart, null);
        }

        if (Player is int player)
        {
            client.Keys.Send(WindowReplayKeys.Player(player));
        }

        if (TogglePanel)
        {
            client.Keys.Send(ReplayKey.ToggleObserverTools);
        }

        GameWindowCapture? shot = null;
        if (Shot != null)
        {
            // The camera moves and the panel animates for a moment after a key.
            Thread.Sleep(Player != null || TogglePanel ? 1000 : 200);
            shot = GameWindow.CapturePng(client.Process, Shot);
        }

        string reason = shot is { Ok: false } failed ? "shot-" + failed.Reason : "reached";
        return Report(reason, result, watch.Elapsed, restart, shot);
    }

    private void OpenReplay(Client client)
    {
        string switcher = Switcher ?? client.SwitcherPath();
        Console.WriteLine($"Opening {Replay} with {switcher}");
        using Process opened = Process.Start(
            new ProcessStartInfo(switcher) { ArgumentList = { Path.GetFullPath(Replay) } }
        );
    }

    private int Report(
        string reason,
        ReplaySeekResult? result,
        TimeSpan total,
        TimeSpan restart,
        GameWindowCapture? shot
    )
    {
        bool ok = reason == "reached";
        Console.WriteLine(
            $"{(ok ? "ok" : "NOT OK")} {reason}: target {Show(Target)}, from {Show(result?.From)}, reached {Show(result?.Reached)}, seek {Seconds(result?.Elapsed)}, restart {Seconds(restart)}, total {Seconds(total)}{(shot is { Ok: true } s ? $", shot {s.Path} {s.Width}x{s.Height}{(s.Black ? " (black)" : string.Empty)}" : string.Empty)}"
        );
        var json = new Dictionary<string, object>
        {
            ["ok"] = ok,
            ["reason"] = reason,
            ["target"] = Target.TotalSeconds,
            ["from"] = result?.From?.TotalSeconds,
            ["reached"] = result?.Reached?.TotalSeconds,
            ["seekSeconds"] = result?.Elapsed.TotalSeconds,
            ["restartSeconds"] = restart.TotalSeconds,
            ["totalSeconds"] = total.TotalSeconds,
            ["screenshot"] = shot?.Path,
            ["screenshotBlack"] = shot?.Black,
        };
        Console.WriteLine(JsonSerializer.Serialize(json));
        return ok ? 0 : 1;
    }

    private static string Show(TimeSpan? time) =>
        time is TimeSpan t
            ? t.ToString(t.TotalHours >= 1 ? @"h\:mm\:ss" : @"mm\:ss", CultureInfo.InvariantCulture)
            : "-";

    private static string Seconds(TimeSpan? time) =>
        time is TimeSpan t
            ? t.TotalSeconds.ToString("0.0", CultureInfo.InvariantCulture) + " s"
            : "-";

    /// <summary>One client process with its read-only readers and its window keys.</summary>
    private sealed class Client : IDisposable
    {
        private readonly HeroesClientProcess attached;
        private readonly ClientScreen screens = new();
        private readonly MatchClock clock = new();

        private Client(Process process)
        {
            Process = process;
            attached = HeroesClientProcess.Attach(process);
            Keys = new WindowReplayKeys(process.Id);
        }

        public Process Process { get; }
        public WindowReplayKeys Keys { get; }

        /// <summary>
        /// The client with this pid, else the only client, else the first one that shows a match.
        /// Null when there is none.
        /// </summary>
        public static Client Pick(int? processId)
        {
            Process[] all = Process.GetProcessesByName("HeroesOfTheStorm_x64");
            Process chosen =
                processId is int pid ? all.FirstOrDefault(p => p.Id == pid)
                : all.Length == 1 ? all[0]
                : null;
            Client client = null;
            if (chosen != null)
            {
                client = new Client(chosen);
            }
            else
            {
                foreach (Process candidate in all)
                {
                    var probe = new Client(candidate);
                    if (client == null && probe.Position().InMatch)
                    {
                        client = probe;
                        continue;
                    }

                    probe.Dispose();
                }
            }

            foreach (Process other in all.Where(p => client?.Process != p))
            {
                other.Dispose();
            }

            return client;
        }

        public ReplayPosition Position() => ReplaySeeker.Read(attached, screens, clock);

        public ReplaySeeker Seeker() => new(Keys, Position);

        public bool WaitFor(TimeSpan timeout, Func<ReplayPosition, bool> done)
        {
            var watch = Stopwatch.StartNew();
            while (watch.Elapsed < timeout)
            {
                if (done(Position()))
                {
                    return true;
                }

                Thread.Sleep(250);
            }

            return false;
        }

        /// <summary><c>Support64\HeroesSwitcher_x64.exe</c> of the install this client runs from.</summary>
        public string SwitcherPath()
        {
            // <install>\Versions\Base<build>\HeroesOfTheStorm_x64.exe
            string exe = Process.MainModule?.FileName;
            string install = Path.GetFullPath(
                Path.Combine(Path.GetDirectoryName(exe) ?? ".", "..", "..")
            );
            return Path.Combine(install, "Support64", "HeroesSwitcher_x64.exe");
        }

        public void Dispose()
        {
            screens.Dispose();
            clock.Dispose();
            attached.Dispose();
            Process.Dispose();
        }
    }
}
