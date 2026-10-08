using System;
using System.Threading.Tasks;
using Xunit;

namespace HeroesClientSDK.Tests;

#pragma warning disable CS0618 // These tests are about the obsolete 0.3 names.

/// <summary>The 0.3 names still compile and forward to the 0.4 readers for one release.</summary>
[Trait(TestCategories.Category, TestCategories.Unit)]
public class ObsoleteNamesTests
{
    [Fact]
    public void Readers_ForwardTo04()
    {
        using var clock = new StableMatchClock();
        using var screens = new LoadingScreenMemory();
        using var menus = new ClientScreenMemory();

        StableClockSample time = clock.Read(null);
        LoadingScreenSample screen = screens.Read(null);
        ClientScreenSample menu = menus.Read(null);
        clock.BeginMatch();

        Assert.Equal("no-process", time.Reason);
        Assert.Equal("no-process", screen.Reason);
        Assert.Equal("no-process", menu.Reason);
        Assert.Equal(MatchClock.RunningProbe, StableMatchClock.RunningProbe);
        Assert.True(StableMatchClock.IsRunning(TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(2)));
        Assert.Equal(default, clock.LastTelemetry);
    }

    [Fact]
    public async Task ReadRunningAsync_ForwardsTheOldSample()
    {
        double seconds = 10;
        TimeSpan? running = await StableMatchClock.ReadRunningAsync(
            () => new StableClockSample(true, "ok", 0, 0, seconds),
            () =>
            {
                seconds += 0.25;
                return Task.CompletedTask;
            }
        );

        Assert.Equal(TimeSpan.FromSeconds(10.25), running);
    }

    [Fact]
    public void Telemetry_KeepsItsConstantsAndChanged()
    {
        var a = new ClockTelemetryReport(ClockTelemetry.Unlocked, "read-failed");
        var b = new ClockTelemetryReport(ClockTelemetry.MemoryLocked, "ok");

        Assert.False(ClockTelemetry.Changed(a, a));
        Assert.True(ClockTelemetry.Changed(a, b));
        Assert.Equal(MatchClockTelemetry.Discovering, ClockTelemetry.Discovering);
        Assert.Equal(MatchClockTelemetry.Locked, ClockTelemetry.MemoryLocked);
    }

    [Fact]
    public void ClientScreenSample_OldMembersForward()
    {
        var home = new ClientScreenSample(
            ClientScreenKind.Home,
            Array.Empty<string>(),
            true,
            "screens"
        );

        Assert.True(home.Known);
        Assert.True(home.Home);
        Assert.False(home.LoginForm);
        Assert.False(home.Loading);
        Assert.False(home.ScoreScreen);
        Assert.False(home.AwardsScreen);
    }
}
