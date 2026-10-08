using Xunit;

namespace HeroesClientSDK.Tests;

/// <summary>
/// The MVP and awards screen: the in-game <c>CEndOfGameAwardsPanel</c>, found in the client's
/// frame tree by its class name, against shapes recorded from 2.57.0.98348 and 2.57.0.98304 on
/// 2026-10-08.
/// </summary>
[Trait(TestCategories.Category, TestCategories.Unit)]
public class ClientScreenPanelTests
{
    private const ulong HomeMask = 0x6181;

    [Fact]
    public void Read_TheAwardsScreenAtTheEndOfAMatch()
    {
        // 2.57.0.98348, replay 65823392: the awards panel exists for the whole match under
        // CGameUI, flags 0x7A, and turns visible (0x7B) on the MVP screen.
        var client = new FakeGlueClient();
        client.AddPanels();
        using var memory = new ClientScreen();
        ClientModule module = client.Module(70);

        client.ShowScreens(HomeMask);
        ClientScreenSample home = memory.Read(module, client);
        client.TearDownMenus();
        ClientScreenSample match = memory.Read(module, client);
        client.ShowAwards(true);
        ClientScreenSample awards = memory.Read(module, client);

        Assert.Equal(ClientScreenKind.Home, home.Screen);
        Assert.Equal(ClientScreenKind.Match, match.Screen);
        Assert.False(match.OnAwards);
        Assert.Equal(ClientScreenKind.Awards, awards.Screen);
        Assert.True(awards.OnAwards);
        Assert.False(awards.OnHome);
        Assert.Equal(client.VtableOf("CEndOfGameAwardsPanel"), memory.AwardsVtable);
    }

    [Fact]
    public void Read_AReaderThatStartsMidMatch_SeesAMatchByTheAwardsPanel()
    {
        // A spectator that restarts while the client plays: no screen was seen, but the in-game
        // awards panel exists, so this is a match and not a client still starting.
        var client = new FakeGlueClient();
        client.AddPanels();
        using var memory = new ClientScreen();
        client.TearDownMenus();

        ClientScreenSample sample = memory.Read(client.Module(71), client);

        Assert.Equal(ClientScreenKind.Match, sample.Screen);
    }

    [Fact]
    public void Read_WithoutTheAwardsPanel_TheScreensStillRead()
    {
        var client = new FakeGlueClient();
        using var memory = new ClientScreen();
        client.ShowScreens(HomeMask);

        ClientScreenSample sample = memory.Read(client.Module(73), client);

        Assert.Equal(0, memory.AwardsVtable);
        Assert.Equal(ClientScreenKind.Home, sample.Screen);
    }

    // A patch that moves the frame tree: parent, next sibling and IsA slot at new offsets.
    private static readonly FrameTreeLayout Moved = new(
        ParentOffset: 0x58,
        NextOffset: 0x28,
        IsASlot: 0x248
    );

    [Fact]
    public void Read_AMovedFrameTree_ReadsWithItsBuildsProfile()
    {
        // HeroesClientSDK#12: the frame-tree layout is profile data, so a patch that moves it
        // needs a registry entry, not a code change.
        BuildProfileRegistry profiles = BuildProfileRegistry.Default.WithBuild(
            new HeroesClientVersion(2, 57, 0, 98348),
            new BuildProfile { Name = "moved", FrameTree = Moved }
        );
        var client = new FakeGlueClient(layout: Moved);
        client.AddPanels();
        using var memory = new ClientScreen(new HeroesClientOptions { Profiles = profiles });
        using var unaware = new ClientScreen();
        client.ShowScreens(HomeMask);
        memory.Read(client.Module(74), client);
        unaware.Read(client.Module(74), client);
        client.TearDownMenus();
        client.ShowAwards(true);

        ClientScreenSample awards = memory.Read(client.Module(74), client);
        ClientScreenSample notFound = unaware.Read(client.Module(74), client);

        Assert.Equal(Moved, memory.FrameLayout);
        Assert.Equal(ClientScreenKind.Awards, awards.Screen);
        Assert.Equal(client.VtableOf("CEndOfGameAwardsPanel"), memory.AwardsVtable);
        // With the 2.57 layout the moved tree does not read: never a wrong frame, only no panel.
        Assert.Equal(FrameTreeLayout.Default, unaware.FrameLayout);
        Assert.Equal(ClientScreenKind.Match, notFound.Screen);
        Assert.Equal(0, unaware.AwardsVtable);
    }

    [Fact]
    public void Read_TheFrameTreeLayoutFollowsTheRunningExe_APassedVersionOnlyWhenItHasNone()
    {
        BuildProfileRegistry profiles = BuildProfileRegistry.Default.WithBuild(
            new HeroesClientVersion(2, 57, 0, 98348),
            new BuildProfile { Name = "moved", FrameTree = Moved }
        );
        var options = new HeroesClientOptions { Profiles = profiles };
        var current = new HeroesClientVersion(2, 57, 0, 98348);
        var noVersion = new FakeGlueClient(fileVersion: null, layout: Moved);
        var previous = new FakeGlueClient(fileVersion: "2.57.0.98304");
        using var first = new ClientScreen(options);
        using var second = new ClientScreen(options);

        noVersion.ShowScreens(HomeMask);
        previous.ShowScreens(HomeMask);
        ClientScreenSample unversioned = first.Read(noVersion.Module(75), noVersion, current);
        ClientScreenSample mismatch = second.Read(previous.Module(76), previous, current);

        Assert.Equal(Moved, first.FrameLayout);
        Assert.Equal(ClientScreenKind.Home, unversioned.Screen);
        Assert.Equal(FrameTreeLayout.Default, second.FrameLayout);
        Assert.True(mismatch.VersionMismatch);
        Assert.Equal(ClientScreenKind.Home, mismatch.Screen);
    }

    [Fact]
    public void FrameClass_FollowsRecordedIsAAndStaticTypeToTheClassName()
    {
        // 2.57.0.98348: CScreenLoading's vtable slot 0x240 holds IsA at 0xD08600, which calls
        // StaticType at 0xD07F90, which loads "CScreenLoading" at 0x26FDC48.
        byte[] isA = ClientScreenTests.Hex("40 53 48 83 EC 20 48 8B DA E8 82 F9 FF FF");
        byte[] staticType = ClientScreenTests.Hex(
            "40 53 48 83 EC 30 8B 05 3C 3F A9 02 A8 01 75 65 83 C8 01 48 C7 44 24 28 0E 00 00 00 48 8D 0D 95 5C 9F 01"
        );

        Assert.Equal(0xD07F90, FrameClass.FirstCall(isA, 0xD08600));
        Assert.Equal(0x26FDC48, FrameClass.FirstLeaRcx(staticType, 0xD07F90));
    }
}
