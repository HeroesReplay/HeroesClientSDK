using Xunit;

namespace HeroesClientSDK.Tests;

[Trait(TestCategories.Category, TestCategories.Unit)]
public class MatchClockTelemetryTests
{
    [Fact]
    public void Describe_ReportsDiscoveringUntilTheScanFinishes()
    {
        MatchClockTelemetry report = MatchClockTelemetry.Describe(false, false, "pattern");

        Assert.Equal(MatchClockTelemetry.Discovering, report.State);
        Assert.Equal("pattern", report.Reason);
    }

    [Fact]
    public void Describe_ReportsMemoryLockedOnlyForATrustedRead()
    {
        MatchClockTelemetry report = MatchClockTelemetry.Describe(true, true, "ok");

        Assert.Equal(MatchClockTelemetry.Locked, report.State);
        Assert.Equal("ok", report.Reason);
    }

    [Fact]
    public void Describe_ReportsUnlockedWithTheReadReason()
    {
        MatchClockTelemetry missing = MatchClockTelemetry.Describe(true, false, "read-failed");
        MatchClockTelemetry stalled = MatchClockTelemetry.Describe(true, true, "stalled");
        MatchClockTelemetry malformed = MatchClockTelemetry.Describe(true, false, "bad-scale");

        Assert.Equal(MatchClockTelemetry.Unlocked, missing.State);
        Assert.Equal("read-failed", missing.Reason);
        Assert.Equal(MatchClockTelemetry.Unlocked, stalled.State);
        Assert.Equal("stalled", stalled.Reason);
        Assert.Equal(MatchClockTelemetry.Unlocked, malformed.State);
        Assert.Equal("bad-scale", malformed.Reason);
    }

    [Fact]
    public void Changed_IsFalseWhenTheStateAndReasonStayTheSame()
    {
        var report = new MatchClockTelemetry(MatchClockTelemetry.Unlocked, "read-failed");

        Assert.False(
            report != new MatchClockTelemetry(MatchClockTelemetry.Unlocked, "read-failed")
        );
        Assert.True(report != new MatchClockTelemetry(MatchClockTelemetry.Locked, "ok"));
    }

    [Fact]
    public void States_KeepTheStringsLogsAlreadyUse()
    {
        Assert.Equal("discovering", MatchClockTelemetry.Discovering);
        Assert.Equal("memory-locked", MatchClockTelemetry.Locked);
        Assert.Equal("memory-unlocked", MatchClockTelemetry.Unlocked);
    }

    [Fact]
    public void Read_TransientFailureReportsOnceAndDoesNotScanAgain()
    {
        using var clock = new MatchClock();
        var module = new ClientModule(9, 0x140000000, 0x3400000, MatchTickClock.SupportedBuild);

        MatchClockSample sample = clock.Read(module, TestMemory.Unreadable);

        Assert.Equal("read-failed", sample.Reason);
        Assert.Equal(
            MatchClockTelemetry.Describe(false, false, "no-process"),
            clock.DiscoveryTelemetry
        );
        Assert.Equal(MatchClockTelemetry.Describe(true, false, "read-failed"), clock.LastTelemetry);
        int emitted = clock.TelemetryEmissions;
        Assert.True(emitted >= 2);

        clock.Read(module, TestMemory.Unreadable);

        Assert.Equal(emitted, clock.TelemetryEmissions);
        Assert.Equal("read-failed", clock.LastTelemetry.Reason);
    }

    [Fact]
    public void Read_ModuleChangeReportsDiscoveryAgain()
    {
        using var clock = new MatchClock();
        IProcessMemory memory = TestMemory.Unreadable;
        clock.Read(new ClientModule(4, 0x140000000, 0x20000, "0.0.0.0"), memory);
        int emitted = clock.TelemetryEmissions;

        clock.Read(new ClientModule(5, 0x150000000, 0x20000, "0.0.0.1"), memory);

        Assert.True(clock.TelemetryEmissions > emitted);
        Assert.Equal(MatchClockTelemetry.Discovering, clock.DiscoveryTelemetry.State);
        Assert.Equal(MatchClockTelemetry.Unlocked, clock.LastTelemetry.State);
    }
}
