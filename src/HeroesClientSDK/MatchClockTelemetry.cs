using System;

namespace HeroesClientSDK;

/// <summary>
/// Where clock discovery stands, and the reason of the read that put it there. Pattern discovery
/// finds the clock; until a read is locked and ok, there is no match clock. Two reports are equal
/// when their state and reason are, so a caller that logs on <c>!=</c> logs each change once.
/// </summary>
/// <param name="State"><see cref="Discovering"/>, <see cref="Locked"/>, or <see cref="Unlocked"/>.</param>
/// <param name="Reason">The read reason, as in <see cref="MatchClockSample.Reason"/>.</param>
public readonly record struct MatchClockTelemetry(string State, string Reason)
{
    /// <summary>The clock has not been searched for in this process yet.</summary>
    public const string Discovering = "discovering";

    /// <summary>The clock is located and the last read was ok.</summary>
    public const string Locked = "memory-locked";

    /// <summary>The search ran, but the last read is not a trusted clock.</summary>
    public const string Unlocked = "memory-unlocked";

    internal static MatchClockTelemetry Describe(bool discovered, bool located, string reason)
    {
        string detail = reason ?? "";
        if (!discovered)
        {
            return new MatchClockTelemetry(Discovering, detail);
        }

        if (located && string.Equals(detail, "ok", StringComparison.Ordinal))
        {
            return new MatchClockTelemetry(Locked, detail);
        }

        return new MatchClockTelemetry(Unlocked, detail);
    }
}
