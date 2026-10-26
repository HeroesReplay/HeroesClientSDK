using System;
using System.Threading;
using System.Threading.Tasks;

namespace HeroesClientSDK;

/// <summary>Where a replay is: the screen the client shows and its match clock.</summary>
/// <param name="Screen">The client's screen (<see cref="ClientScreen"/>).</param>
/// <param name="Time">The match clock of an ok read, null otherwise (<see cref="MatchClock"/>).</param>
/// <param name="ClockReason">The clock's reason: "ok", "confirming", "stalled", ...</param>
public readonly record struct ReplayPosition(
    ClientScreenKind Screen,
    TimeSpan? Time,
    string ClockReason
)
{
    /// <summary>True when the client shows a match.</summary>
    public bool InMatch => Screen == ClientScreenKind.Match;
}

/// <summary>How <see cref="ReplaySeeker"/> polls and when it gives up. Every property is optional.</summary>
public sealed record ReplaySeekOptions
{
    /// <summary>How close to the target counts as there (2 s).</summary>
    public TimeSpan Tolerance { get; init; } = TimeSpan.FromSeconds(2);

    /// <summary>
    /// How far before the target the speed goes back to Normal (6 s of match time). At x8 a
    /// 100 ms poll moves the match about 1 s, so this leaves room to stop on the second.
    /// </summary>
    public TimeSpan SlowDownLead { get; init; } = TimeSpan.FromSeconds(6);

    /// <summary>The time between reads (100 ms).</summary>
    public TimeSpan PollInterval { get; init; } = TimeSpan.FromMilliseconds(100);

    /// <summary>The time between two speed key presses (60 ms).</summary>
    public TimeSpan KeyInterval { get; init; } = TimeSpan.FromMilliseconds(60);

    /// <summary>
    /// A match whose clock has not moved for this long is taken as paused, and P is pressed
    /// once (5 s). A paused clock reads "confirming" or "stalled", never a time.
    /// </summary>
    public TimeSpan PausedAfter { get; init; } = TimeSpan.FromSeconds(5);

    /// <summary>How long to wait for a match with a running clock (3 min).</summary>
    public TimeSpan MatchTimeout { get; init; } = TimeSpan.FromMinutes(3);

    /// <summary>How long the whole seek may take (15 min).</summary>
    public TimeSpan SeekTimeout { get; init; } = TimeSpan.FromMinutes(15);

    /// <summary>The clock for timeouts (<see cref="TimeProvider.System"/> when null).</summary>
    public TimeProvider TimeProvider { get; init; } = TimeProvider.System;

    /// <summary>The wait between reads and key presses (<see cref="Task.Delay(TimeSpan, CancellationToken)"/> when null).</summary>
    public Func<TimeSpan, CancellationToken, Task> Delay { get; init; }
}

/// <summary>What a seek did.</summary>
/// <param name="Ok">True when the match clock reached the target.</param>
/// <param name="Reason">
/// "reached"; "behind-target" (the match is already past the target: a replay only plays
/// forward, so reopen it and seek from the start); "no-match" (no match with a running clock
/// within <see cref="ReplaySeekOptions.MatchTimeout"/>); "left-match" (the match ended or the
/// client left it, as when the target is past the replay's end); "no-window" (no game window
/// for the keys); "timeout".
/// </param>
/// <param name="Target">The match time asked for.</param>
/// <param name="From">The match time when the seek started, or null.</param>
/// <param name="Reached">The last match time read, or null.</param>
/// <param name="Elapsed">How long the seek took.</param>
public readonly record struct ReplaySeekResult(
    bool Ok,
    string Reason,
    TimeSpan Target,
    TimeSpan? From,
    TimeSpan? Reached,
    TimeSpan Elapsed
);

/// <summary>
/// Moves a running replay forward to a match time with the replay's own speed keys: up the
/// ladder to x8 (about 11 times real time), back to Normal just before the target, and stop on
/// it. The match clock decides when. It reads the client only through <see cref="ClientScreen"/>
/// and <see cref="MatchClock"/>, sends keys only through <see cref="IReplayKeys"/>, and writes
/// nothing to the client.
/// </summary>
public sealed class ReplaySeeker
{
    /// <summary>Presses that reach either end of the speed ladder (Normal to x8 is four).</summary>
    public const int SpeedSteps = 5;

    private readonly IReplayKeys keys;
    private readonly Func<ReplayPosition> read;
    private readonly ReplaySeekOptions options;
    private readonly Func<TimeSpan, CancellationToken, Task> delay;

    /// <summary>A seeker over any position source and key sender, such as a test's.</summary>
    public ReplaySeeker(
        IReplayKeys keys,
        Func<ReplayPosition> read,
        ReplaySeekOptions options = null
    )
    {
        this.keys = keys ?? throw new ArgumentNullException(nameof(keys));
        this.read = read ?? throw new ArgumentNullException(nameof(read));
        this.options = options ?? new ReplaySeekOptions();
        delay = this.options.Delay ?? ((wait, token) => Task.Delay(wait, token));
    }

    /// <summary>
    /// A seeker for one client: its screen and clock read through the attachment, and keys
    /// posted to its game window (<see cref="WindowReplayKeys"/>). The readers must belong to
    /// that client; keep them for the life of the client as usual.
    /// </summary>
    public static ReplaySeeker For(
        HeroesClientProcess client,
        ClientScreen screens,
        MatchClock clock,
        ReplaySeekOptions options = null
    )
    {
        ArgumentNullException.ThrowIfNull(client);
        ArgumentNullException.ThrowIfNull(screens);
        ArgumentNullException.ThrowIfNull(clock);
        return new ReplaySeeker(
            new WindowReplayKeys(client.Module.ProcessId),
            () => Read(client, screens, clock),
            options
        );
    }

    /// <summary>One read of a client's screen and match clock.</summary>
    public static ReplayPosition Read(
        HeroesClientProcess client,
        ClientScreen screens,
        MatchClock clock
    )
    {
        ClientScreenSample screen = screens.Read(client);
        MatchClockSample time = clock.Read(client);
        return new ReplayPosition(screen.Screen, time.Time, time.Reason);
    }

    /// <summary>
    /// Moves the replay to <paramref name="target"/> match time. With <paramref name="pause"/>
    /// it presses P on arrival. A target behind the match is not reached (<c>behind-target</c>).
    /// </summary>
    public async Task<ReplaySeekResult> SeekAsync(
        TimeSpan target,
        bool pause = false,
        CancellationToken cancellationToken = default
    )
    {
        TimeProvider time = options.TimeProvider ?? TimeProvider.System;
        DateTimeOffset started = time.GetUtcNow();
        TimeSpan Elapsed() => time.GetUtcNow() - started;
        TimeSpan? from = null;
        TimeSpan? last = null;
        ReplaySeekResult Result(bool ok, string reason) =>
            new(ok, reason, target, from, last, Elapsed());

        // A match with a running clock. A clock that stays still for PausedAfter is paused.
        DateTimeOffset stillSince = time.GetUtcNow();
        bool pressedPlay = false;
        while (true)
        {
            ReplayPosition position = read();
            if (position.InMatch && position.Time is TimeSpan at)
            {
                from = last = at;
                break;
            }

            if (!position.InMatch)
            {
                stillSince = time.GetUtcNow();
            }
            else if (!pressedPlay && time.GetUtcNow() - stillSince >= options.PausedAfter)
            {
                if (!keys.Send(ReplayKey.Pause))
                {
                    return Result(false, "no-window");
                }

                pressedPlay = true;
            }

            if (Elapsed() >= options.MatchTimeout)
            {
                return Result(false, "no-match");
            }

            await delay(options.PollInterval, cancellationToken).ConfigureAwait(false);
        }

        if (from.Value > target + options.Tolerance)
        {
            return Result(false, "behind-target");
        }

        if (target - from.Value > options.SlowDownLead)
        {
            if (!await Press(ReplayKey.SpeedUp, cancellationToken).ConfigureAwait(false))
            {
                return Result(false, "no-window");
            }

            string fast = await RunUntil(target - options.SlowDownLead).ConfigureAwait(false);
            await Press(ReplayKey.SpeedDown, cancellationToken).ConfigureAwait(false);
            if (fast != null)
            {
                return Result(false, fast);
            }
        }

        string normal = await RunUntil(target).ConfigureAwait(false);
        if (normal != null)
        {
            return Result(false, normal);
        }

        if (pause && !keys.Send(ReplayKey.Pause))
        {
            return Result(false, "no-window");
        }

        return Result(true, "reached");

        // Polls until the match clock reaches the mark; null when it does, else why not.
        async Task<string> RunUntil(TimeSpan mark)
        {
            while (true)
            {
                ReplayPosition position = read();
                if (!position.InMatch)
                {
                    return "left-match";
                }

                if (position.Time is TimeSpan at)
                {
                    last = at;
                    if (at >= mark)
                    {
                        return null;
                    }
                }

                if (Elapsed() >= options.SeekTimeout)
                {
                    return "timeout";
                }

                await delay(options.PollInterval, cancellationToken).ConfigureAwait(false);
            }
        }
    }

    private async Task<bool> Press(ReplayKey key, CancellationToken cancellationToken)
    {
        for (int i = 0; i < SpeedSteps; i++)
        {
            if (!keys.Send(key))
            {
                return false;
            }

            await delay(options.KeyInterval, cancellationToken).ConfigureAwait(false);
        }

        return true;
    }
}
