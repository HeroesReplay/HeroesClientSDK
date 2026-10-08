using System.Diagnostics;
using Xunit;

namespace HeroesClientSDK.Tests;

/// <summary>
/// The public readers work on any process and any build: a missing process or a module that is
/// not a known Heroes build reads as not ok with a reason, never an exception (README).
/// </summary>
[Trait(TestCategories.Category, TestCategories.Unit)]
public class AnyClientTests
{
    [Fact]
    public void Read_NoProcess_ReportsNoProcess()
    {
        using var clock = new StableMatchClock();
        using var screens = new LoadingScreenMemory();

        StableClockSample sample = clock.Read(null);
        LoadingScreenSample screen = screens.Read(null);

        Assert.False(sample.Ok);
        Assert.Equal("no-process", sample.Reason);
        Assert.Equal(ClientScreen.Unknown, screen.Screen);
        Assert.Equal("no-process", screen.Reason);
    }

    [Fact]
    public void Read_AProcessThatIsNotAHeroesBuild_ReportsWhyAndDoesNotThrow()
    {
        // The test host is a real process with a real module, but not a Heroes build.
        using Process self = Process.GetCurrentProcess();
        using var clock = new StableMatchClock();
        using var screens = new LoadingScreenMemory();

        StableClockSample sample = clock.Read(self);
        LoadingScreenSample screen = screens.Read(self);

        Assert.False(sample.Ok);
        Assert.False(string.IsNullOrEmpty(sample.Reason));
        Assert.Equal(ClientScreen.Unknown, screen.Screen);
        Assert.False(string.IsNullOrEmpty(screen.Reason));
    }
}
