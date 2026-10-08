using System;

namespace HeroesClientSDK;

/// <summary>Where clock discovery stands, and the reason of the read that put it there.</summary>
/// <param name="State">
/// <see cref="ClockTelemetry.Discovering"/>, <see cref="ClockTelemetry.MemoryLocked"/>, or
/// <see cref="ClockTelemetry.Unlocked"/>.
/// </param>
/// <param name="Reason">The read reason, as in <see cref="StableClockSample.Reason"/>.</param>
public readonly record struct ClockTelemetryReport(string State, string Reason);

/// <summary>
/// Pattern discovery finds the clock. Until a read is locked and ok, there is no match clock.
/// The same state and reason are reported once, not on every poll.
/// </summary>
public static class ClockTelemetry
{
    /// <summary>The clock has not been searched for in this process yet.</summary>
    public const string Discovering = "discovering";

    /// <summary>The clock is located and the last read was ok.</summary>
    public const string MemoryLocked = "memory-locked";

    /// <summary>The search ran, but the last read is not a trusted clock.</summary>
    public const string Unlocked = "memory-unlocked";

    internal static ClockTelemetryReport Describe(bool discovered, bool located, string reason)
    {
        string detail = reason ?? "";
        if (!discovered)
        {
            return new ClockTelemetryReport(Discovering, detail);
        }

        if (located && string.Equals(detail, "ok", StringComparison.Ordinal))
        {
            return new ClockTelemetryReport(MemoryLocked, detail);
        }

        return new ClockTelemetryReport(Unlocked, detail);
    }

    /// <summary>True when the state or the reason differs, so a caller logs it once.</summary>
    public static bool Changed(ClockTelemetryReport previous, ClockTelemetryReport next)
    {
        return !string.Equals(previous.State, next.State, StringComparison.Ordinal)
            || !string.Equals(previous.Reason, next.Reason, StringComparison.Ordinal);
    }
}
