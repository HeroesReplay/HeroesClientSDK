using System.Collections.Generic;
using System.Text;
using Xunit;

namespace HeroesClientSDK.Tests;

/// <summary>
/// The awards and download screens: UI panels found in the client's frame tree by their frame
/// type, against shapes recorded from 2.57.0.98348 and 2.57.0.98304 on 2026-10-08.
/// </summary>
[Trait(TestCategories.Category, TestCategories.Unit)]
public class ClientScreenPanelTests
{
    private const ulong HomeMask = 0x6181;

    [Fact]
    public void Read_TheAwardsScreenAtTheEndOfAMatch()
    {
        // The awards panel exists for the whole match under the end game panel, which shows when
        // the match ends.
        var client = new FakeGlueClient();
        client.AddPanels();
        using var memory = new ClientScreenMemory();
        StableClockModule module = client.Module(70);

        client.ShowScreens(HomeMask);
        ClientScreenSample home = memory.Read(module, client.Read);
        client.TearDownMenus();
        ClientScreenSample match = memory.Read(module, client.Read);
        client.ShowEndGame(true);
        ClientScreenSample awards = memory.Read(module, client.Read);

        Assert.Equal(FakeGlueClient.Base + FakeGlueClient.AwardsVtableRva, memory.AwardsVtable);
        Assert.Equal(FakeGlueClient.Base + FakeGlueClient.DownloadVtableRva, memory.DownloadVtable);
        Assert.Equal(ClientScreenKind.Home, home.Screen);
        Assert.Equal(ClientScreenKind.Match, match.Screen);
        Assert.False(match.AwardsScreen);
        Assert.Equal(ClientScreenKind.Awards, awards.Screen);
        Assert.True(awards.AwardsScreen);
        Assert.False(awards.Home);
    }

    [Fact]
    public void Read_AReaderThatStartsMidMatch_SeesAMatchByTheAwardsPanel()
    {
        // A spectator that restarts while the client plays: no screen was seen, but the in-game
        // awards panel exists, so this is a match and not a client still starting.
        var client = new FakeGlueClient();
        client.AddPanels();
        using var memory = new ClientScreenMemory();
        client.TearDownMenus();

        ClientScreenSample sample = memory.Read(client.Module(71), client.Read);

        Assert.Equal(ClientScreenKind.Match, sample.Screen);
    }

    [Fact]
    public void Read_TheDownloadPanelIsTheDownloadScreen()
    {
        // 2.57.0.98348 while HeroesSwitcher handed a 98304 replay over: "DOWNLOADING ... All data
        // files must be fully downloaded", with no menu screen in the mask.
        var client = new FakeGlueClient();
        client.AddPanels();
        using var memory = new ClientScreenMemory();
        StableClockModule module = client.Module(72);

        client.ShowScreens(0);
        ClientScreenSample before = memory.Read(module, client.Read);
        client.ShowDownload(true);
        ClientScreenSample download = memory.Read(module, client.Read);

        Assert.Equal(ClientScreenKind.NoScreen, before.Screen);
        Assert.False(before.Downloading);
        Assert.Equal(ClientScreenKind.Download, download.Screen);
        Assert.True(download.Downloading);
        Assert.False(download.Home);
    }

    [Fact]
    public void Read_WithoutPanels_TheScreensStillRead()
    {
        var client = new FakeGlueClient();
        using var memory = new ClientScreenMemory();
        client.ShowScreens(HomeMask);

        ClientScreenSample sample = memory.Read(client.Module(73), client.Read);

        Assert.Equal(0, memory.AwardsVtable);
        Assert.Equal(ClientScreenKind.Home, sample.Screen);
    }

    [Theory]
    // 2.57.0.98348: EndOfGameAwardsPanel registration at 0x185F54, factory 0x79FDA0, ctor
    // 0x82BC20, vtable 0x2674F68.
    [InlineData(
        0x185F54,
        "48 8D 0D AD 1E 4E 02 48 8D 05 3E 9E 61 00",
        0x2667E08,
        0x79FDA0,
        "40 53 48 83 EC 20 48 8B D9 B9 C0 02 00 00 E8 7D 27 BD 00 48 85 C0 74 10 48 8B D3 48 8B C8 48 83 C4 20 5B E9 58 BE 08 00 48 83 C4 20 5B C3",
        0x82BC20,
        "40 53 48 83 EC 20 48 8B D9 E8 E2 0D CE 00 48 8D 05 33 93 E4 01 C6 83 B8 02 00 00 20 48 89 03",
        0x2674F68
    )]
    // 2.57.0.98304: registration at 0x18A374, factory 0x7A5860, ctor 0x831D60, vtable 0x267BF68.
    [InlineData(
        0x18A374,
        "48 8D 0D 8D 4A 4E 02 48 8D 05 DE B4 61 00",
        0x266EE08,
        0x7A5860,
        "40 53 48 83 EC 20 48 8B D9 B9 C0 02 00 00 E8 1D 8B BD 00 48 85 C0 74 10 48 8B D3 48 8B C8 48 83 C4 20 5B E9 D8 C4 08 00 48 83 C4 20 5B C3",
        0x831D60,
        "40 53 48 83 EC 20 48 8B D9 E8 42 19 CE 00 48 8D 05 F3 A1 E4 01 C6 83 B8 02 00 00 20 48 89 03",
        0x267BF68
    )]
    public void FrameTypeLocator_FollowsRecordedRegistrationToTheAwardsVtable(
        long site,
        string registration,
        long name,
        long factory,
        string factoryBytes,
        long constructor,
        string constructorBytes,
        long vtable
    )
    {
        List<(long Name, long Factory)> found = FrameTypeLocator.FindRegistrations(
            ClientScreenMemoryTests.Hex(registration),
            site,
            new[] { name }
        );

        Assert.Equal(new[] { (name, factory) }, found);
        Assert.Equal(
            constructor,
            FrameTypeLocator.Constructor(ClientScreenMemoryTests.Hex(factoryBytes), factory)
        );
        Assert.Equal(
            vtable,
            FrameTypeLocator.Vtable(ClientScreenMemoryTests.Hex(constructorBytes), constructor)
        );
    }

    [Fact]
    public void FrameTypeLocator_NamesMustStartAString()
    {
        byte[] data = Encoding.ASCII.GetBytes("\0CDownloadPanel\0DownloadPanel\0");

        Assert.Equal(
            new long[] { 0x1000 + 16 },
            FrameTypeLocator.NameRvas(data, 0x1000, "DownloadPanel")
        );
    }
}
