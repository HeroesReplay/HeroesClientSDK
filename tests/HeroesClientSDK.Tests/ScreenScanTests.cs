using System;
using Xunit;

namespace HeroesClientSDK.Tests;

/// <summary>
/// One walk of the client's code for both screen readers (HeroesClientSDK#12): a
/// <see cref="LoadingScreen"/> and a <see cref="ClientScreen"/> on one
/// <see cref="HeroesClientProcess"/> share it, and each keeps its own 10 s rediscovery.
/// </summary>
[Trait(TestCategories.Category, TestCategories.Unit)]
public class ScreenScanTests
{
    private const ulong HomeMask = 0x6181;
    private static readonly DateTimeOffset Start = new(2026, 10, 8, 18, 0, 0, TimeSpan.Zero);

    [Fact]
    public void BothScreenReadersOnOneClient_WalkTheCodeOnce()
    {
        var memory = new CodeWalks(new FakeGlueClient());
        memory.Client.ShowScreens(HomeMask);
        using HeroesClientProcess client = HeroesClientProcess.FromMemory(
            memory,
            memory.Client.Module(90)
        );
        using var loading = new LoadingScreen();
        using var menus = new ClientScreen();

        loading.Read(client);
        ClientScreenSample menu = menus.Read(client);
        loading.Read(client);
        menus.Read(client);

        Assert.Equal(1, memory.Walks);
        Assert.Equal(1, client.ScreenScans.Walks);
        Assert.Equal("pattern", loading.DiscoveryReason);
        Assert.Equal(ClientScreenKind.Home, menu.Screen);
        Assert.Equal(FakeGlueClient.GlobalRva, loading.GlobalRva);
        Assert.Equal(FakeGlueClient.GlobalRva, menus.GlobalRva);
    }

    [Fact]
    public void ReadersWithTheirOwnAttachment_EachWalkTheCode()
    {
        // Read(Process) gives each reader its own attachment, as before 0.4.2.
        var memory = new CodeWalks(new FakeGlueClient());
        memory.Client.ShowScreens(HomeMask);
        ClientModule module = memory.Client.Module(91);
        using var loading = new LoadingScreen();
        using var menus = new ClientScreen();

        loading.Read(module, memory);
        menus.Read(module, memory);

        Assert.Equal(2, memory.Walks);
    }

    [Fact]
    public void AFailedScan_IsRetriedAfter10s_OnceForBothReaders()
    {
        // A client still unpacking its code: no screen-state or menu-root sites yet.
        var fake = new FakeGlueClient(glueSite: false, screenSites: 0);
        var memory = new CodeWalks(fake);
        fake.ShowScreens(HomeMask);
        DateTimeOffset now = Start;
        using HeroesClientProcess client = HeroesClientProcess.FromMemory(memory, fake.Module(92));
        using var loading = new LoadingScreen(TestTime.Options(() => now));
        using var menus = new ClientScreen(TestTime.Options(() => now));

        LoadingScreenSample unpacking = loading.Read(client);
        ClientScreenSample unpackingMenu = menus.Read(client);
        fake.AddScreenSites(3);
        fake.AddGlueSite();
        now = Start.AddSeconds(5);
        LoadingScreenSample tooSoon = loading.Read(client);
        ClientScreenSample tooSoonMenu = menus.Read(client);
        int walksBefore = memory.Walks;
        now = Start.AddSeconds(11);
        loading.Read(client);
        ClientScreenSample foundMenu = menus.Read(client);

        Assert.Equal("unsupported-build", unpacking.Reason);
        Assert.Equal("unsupported-build", unpackingMenu.Reason);
        Assert.Equal(LoadingScreenKind.Unknown, tooSoon.Screen);
        Assert.Equal(ClientScreenKind.Unknown, tooSoonMenu.Screen);
        Assert.Equal(1, walksBefore);
        Assert.Equal(FakeGlueClient.GlobalRva, loading.GlobalRva);
        Assert.Equal(ClientScreenKind.Home, foundMenu.Screen);
        Assert.Equal(2, memory.Walks);
    }

    [Fact]
    public void AReaderThatStartsLate_DoesNotTakeAFailedScanOlderThan10s()
    {
        var fake = new FakeGlueClient(screenSites: 0);
        var memory = new CodeWalks(fake);
        fake.ShowScreens(HomeMask);
        DateTimeOffset now = Start;
        using HeroesClientProcess client = HeroesClientProcess.FromMemory(memory, fake.Module(93));
        using var loading = new LoadingScreen(TestTime.Options(() => now));
        using var menus = new ClientScreen(TestTime.Options(() => now));

        LoadingScreenSample unpacking = loading.Read(client);
        fake.AddScreenSites(3);
        now = Start.AddSeconds(12);
        ClientScreenSample late = menus.Read(client);

        Assert.Equal("unsupported-build", unpacking.Reason);
        Assert.Equal(ClientScreenKind.Home, late.Screen);
        Assert.Equal(2, memory.Walks);
    }

    [Fact]
    public void ACompleteScan_ServesAReaderThatStartsLater()
    {
        var fake = new FakeGlueClient();
        fake.AddLaunchManager(0);
        var memory = new CodeWalks(fake);
        fake.ShowScreens(HomeMask);
        DateTimeOffset now = Start;
        using HeroesClientProcess client = HeroesClientProcess.FromMemory(memory, fake.Module(94));
        using var menus = new ClientScreen(TestTime.Options(() => now));
        using var loading = new LoadingScreen(TestTime.Options(() => now));

        menus.Read(client);
        now = Start.AddMinutes(5);
        loading.Read(client);

        Assert.Equal(FakeGlueClient.GlobalRva, loading.GlobalRva);
        Assert.Equal(1, memory.Walks);
    }

    [Fact]
    public void AnotherProcess_WalksItsOwnCode()
    {
        // One reader per process: a new pid or start time starts discovery over, scan included.
        var memory = new CodeWalks(new FakeGlueClient());
        memory.Client.ShowScreens(HomeMask);
        using HeroesClientProcess first = HeroesClientProcess.FromMemory(
            memory,
            memory.Client.Module(95)
        );
        using HeroesClientProcess second = HeroesClientProcess.FromMemory(
            memory,
            memory.Client.Module(96)
        );
        using var loading = new LoadingScreen();
        using var menus = new ClientScreen();

        loading.Read(first);
        menus.Read(first);
        loading.Read(second);
        menus.Read(second);

        Assert.Equal(2, memory.Walks);
    }

    [Fact]
    public void Run_CountsASiteInAChunkOverlapOnce()
    {
        // .text is walked in 1 MB chunks that overlap by the widest pattern (44 bytes). A site
        // that starts just past a chunk boundary lies in both chunks; before 0.4.2 both counted
        // it. A screen-state site past the first boundary, a menu-root site past the second.
        const long Base = 0x140000000L;
        int boundary = ModuleScanner.Chunk;
        var image = new RecordedImage(
            Base,
            0x400000,
            new[] { (".text", 0x1000L, 0x300000, 0x60000020u) }
        );
        WriteScreenSite(image, 0x1100);
        WriteScreenSite(image, 0x1200);
        WriteScreenSite(image, 0x1000 + boundary + 5);
        image.Write(
            0x1000 + (2 * boundary) + 2,
            ClientScreenTests.Hex(
                "49 8B 81 D4 01 00 00 8B 51 08 48 0F A3 D0 73 1D 49 8B 8C D1 F0 01 00 00"
            )
        );

        ScreenScan scan = ScreenScan.Run(image, image.Module("2.57.0.98348"), Start);

        Assert.Equal(new long[] { 0x300000, 0x300000, 0x300000 }, scan.ScreenGlobals);
        Assert.Single(scan.GlueSites);
    }

    private static void WriteScreenSite(RecordedImage image, long rva)
    {
        const long Global = 0x300000;
        byte[] site = ClientScreenTests.Hex(
            "48 8B 0D 00 00 00 00 48 85 C9 74 25 33 D2 E8 00 00 00 00 84 C0 74 1A 48 8B 0D 00 00 00 00 E8"
        );
        BitConverter.GetBytes((int)(Global - (rva + 7))).CopyTo(site, 3);
        BitConverter.GetBytes((int)(Global - (rva + 30))).CopyTo(site, 26);
        image.Write(rva, site);
    }

    /// <summary>A client that counts each walk of its code: a read that starts at .text.</summary>
    private sealed class CodeWalks : IProcessMemory
    {
        public CodeWalks(FakeGlueClient client) => Client = client;

        public FakeGlueClient Client { get; }

        public int Walks { get; private set; }

        public bool TryRead(long address, Span<byte> buffer)
        {
            if (address == FakeGlueClient.Base + 0x1000 && buffer.Length >= 0x1000)
            {
                Walks++;
            }

            return Client.TryRead(address, buffer);
        }
    }
}
