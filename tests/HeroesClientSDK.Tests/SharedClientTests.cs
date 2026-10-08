using Xunit;

namespace HeroesClientSDK.Tests;

/// <summary>
/// One attached client read by every reader: the 2.57 shapes recorded in
/// <see cref="FakeGlueClient"/>, served through <see cref="HeroesClientProcess.FromMemory"/>.
/// </summary>
[Trait(TestCategories.Category, TestCategories.Unit)]
public class SharedClientTests
{
    private const ulong HomeMask = 0x6181;

    [Fact]
    public void ThreeReaders_ShareOneClient_AndLeaveItToTheCaller()
    {
        var memory = new FakeGlueClient();
        using HeroesClientProcess client = HeroesClientProcess.FromMemory(
            memory,
            memory.Module(80)
        );
        var menus = new ClientScreen();
        var screens = new LoadingScreen();
        var clock = new MatchClock();

        memory.ShowScreens(HomeMask);
        ClientScreenSample home = menus.Read(client);
        memory.TearDownMenus();
        ClientScreenSample match = menus.Read(client);
        LoadingScreenSample screen = screens.Read(client);
        MatchClockSample time = clock.Read(client);
        menus.Dispose();
        screens.Dispose();
        clock.Dispose();

        Assert.Equal(ClientScreenKind.Home, home.Screen);
        Assert.Equal(ClientScreenKind.Match, match.Screen);
        // [[G]+0x218] is the ScreenLoading frame (0x1F0 + 5 * 8), which a match tears down.
        Assert.Equal(LoadingScreenKind.Match, screen.Screen);
        Assert.Equal("unsupported-build", time.Reason);
        Assert.Equal(new HeroesClientVersion(2, 57, 0, 98348), time.ClientVersion);
        Assert.True(client.Ok);
        using var later = new ClientScreen();
        Assert.Equal(ClientScreenKind.Unknown, later.Read(client).Screen);
        Assert.Equal("starting", later.Read(client).Reason);
    }

    [Fact]
    public void FromMemory_WithoutMemory_ReadsAsReadFailed()
    {
        using HeroesClientProcess client = HeroesClientProcess.FromMemory(
            null,
            new ClientModule(81, 0x140000000L, 0x40000, "2.57.0.98348")
        );
        using var clock = new MatchClock();
        using var screens = new LoadingScreen();

        Assert.Equal("read-failed", clock.Read(client).Reason);
        Assert.Equal("no-module", screens.Read(client).Reason);
    }
}
