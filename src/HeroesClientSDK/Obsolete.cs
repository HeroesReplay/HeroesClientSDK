using System;
using System.Diagnostics;
using System.Threading.Tasks;

namespace HeroesClientSDK;

// The 0.3 names, kept for one release so a consumer can move to 0.4 in steps. Each forwards to
// its 0.4 type and changes nothing about the reads. They are removed after 0.4.

/// <summary>Obsolete: use <see cref="MatchClockSample"/>.</summary>
[Obsolete("Use MatchClockSample. Removed after 0.4.")]
public readonly record struct StableClockSample(
    bool Ok,
    string Reason,
    int Ticks,
    float Scale,
    double Seconds
)
{
    internal static StableClockSample From(MatchClockSample sample) =>
        new(sample.Ok, sample.Reason, sample.Ticks, sample.Scale, sample.Seconds);

    internal MatchClockSample ToSample() => new(Ok, Reason, Ticks, Scale, Seconds);
}

/// <summary>Obsolete: use <see cref="MatchClock"/>.</summary>
[Obsolete("Use MatchClock. Removed after 0.4.")]
public sealed class StableMatchClock : IDisposable
{
    private readonly MatchClock clock = new();

    /// <summary>Obsolete: use <see cref="MatchClock.RunningProbe"/>.</summary>
    public static readonly TimeSpan RunningProbe = MatchClock.RunningProbe;

    /// <summary>Obsolete: use <see cref="MatchClock.LastTelemetry"/>.</summary>
    public ClockTelemetryReport LastTelemetry =>
        new(clock.LastTelemetry.State, clock.LastTelemetry.Reason);

    /// <summary>Obsolete: use <see cref="MatchClock.Read(Process, HeroesClientVersion)"/>.</summary>
    public StableClockSample Read(Process process) => StableClockSample.From(clock.Read(process));

    /// <summary>Obsolete: use <see cref="MatchClock.BeginMatch"/>.</summary>
    public void BeginMatch() => clock.BeginMatch();

    /// <summary>Obsolete: use <see cref="MatchClock.IsRunning"/>.</summary>
    public static bool IsRunning(TimeSpan? first, TimeSpan? second) =>
        MatchClock.IsRunning(first, second);

    /// <summary>Obsolete: use <see cref="MatchClock.ReadRunningAsync"/>.</summary>
    public static Task<TimeSpan?> ReadRunningAsync(Func<StableClockSample> read, Func<Task> pause)
    {
        ArgumentNullException.ThrowIfNull(read);
        return MatchClock.ReadRunningAsync(() => read().ToSample(), pause);
    }

    /// <summary>Obsolete: use <see cref="MatchClock.Dispose"/>.</summary>
    public void Dispose() => clock.Dispose();
}

/// <summary>Obsolete: use <see cref="MatchClockTelemetry"/>.</summary>
[Obsolete("Use MatchClockTelemetry. Removed after 0.4.")]
public readonly record struct ClockTelemetryReport(string State, string Reason);

/// <summary>Obsolete: use the constants on <see cref="MatchClockTelemetry"/>.</summary>
[Obsolete("Use MatchClockTelemetry. Removed after 0.4.")]
public static class ClockTelemetry
{
    /// <summary>Obsolete: use <see cref="MatchClockTelemetry.Discovering"/>.</summary>
    public const string Discovering = MatchClockTelemetry.Discovering;

    /// <summary>Obsolete: use <see cref="MatchClockTelemetry.Locked"/>.</summary>
    public const string MemoryLocked = MatchClockTelemetry.Locked;

    /// <summary>Obsolete: use <see cref="MatchClockTelemetry.Unlocked"/>.</summary>
    public const string Unlocked = MatchClockTelemetry.Unlocked;

    /// <summary>Obsolete: compare two <see cref="MatchClockTelemetry"/> with <c>!=</c>.</summary>
    public static bool Changed(ClockTelemetryReport previous, ClockTelemetryReport next) =>
        previous != next;
}

/// <summary>Obsolete: use <see cref="LoadingScreen"/>.</summary>
[Obsolete("Use LoadingScreen. Removed after 0.4.")]
public sealed class LoadingScreenMemory : IDisposable
{
    private readonly LoadingScreen screen = new();

    /// <summary>Obsolete: use <see cref="LoadingScreen.Read(Process, HeroesClientVersion)"/>.</summary>
    public LoadingScreenSample Read(Process process) => screen.Read(process);

    /// <summary>Obsolete: use <see cref="LoadingScreen.Dispose"/>.</summary>
    public void Dispose() => screen.Dispose();
}

/// <summary>Obsolete: use <see cref="ClientScreen"/>.</summary>
[Obsolete("Use ClientScreen. Removed after 0.4.")]
public sealed class ClientScreenMemory : IDisposable
{
    private readonly ClientScreen screen = new();

    /// <summary>Obsolete: use <see cref="ClientScreen.Read(Process, HeroesClientVersion)"/>.</summary>
    public ClientScreenSample Read(Process process, HeroesClientVersion clientVersion = null) =>
        screen.Read(process, clientVersion);

    /// <summary>Obsolete: use <see cref="ClientScreen.Dispose"/>.</summary>
    public void Dispose() => screen.Dispose();
}
