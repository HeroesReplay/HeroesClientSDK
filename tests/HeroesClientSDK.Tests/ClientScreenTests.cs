using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Xunit;

namespace HeroesClientSDK.Tests;

/// <summary>
/// The menu screens from memory, against snapshots recorded from running clients on 2026-10-08
/// (2.57.0.98348 and 2.57.0.98304, read-only). Only the few bytes each read needs are here.
/// </summary>
[Trait(TestCategories.Category, TestCategories.Unit)]
public class ClientScreenTests
{
    // 2.57.0.98348 and 2.57.0.98304: the screen template table, in index order (30 screens).
    internal static readonly string[] Screens =
    {
        "ScreenBackgroundHero",
        "ScreenSingle",
        "ScreenReplay",
        "ScreenCreditsHero",
        "ScreenCoopCampaign",
        "ScreenLoading",
        "ScreenLoginUnified",
        "ScreenHeroCutscene",
        "ScreenHome",
        "ScreenHero",
        "ScreenCollection",
        "ScreenLoot",
        "ScreenBuy",
        "ScreenNavigationHero",
        "ScreenForegroundHero",
        "ScreenScore",
        "ScreenPlay",
        "ScreenSkin",
        "ScreenMount",
        "ScreenBoost",
        "ScreenBundle",
        "ScreenBundleList",
        "ScreenCommunity",
        "ScreenMovie",
        "ScreenBanner",
        "ScreenEmoticonPack",
        "ScreenSpray",
        "ScreenLootChest",
        "ScreenAnnouncerPack",
        "ScreenVoiceLine",
    };

    // 2.57.0.98348, 2026-10-08, home after Battle.net "launch Hero": BackgroundHero,
    // HeroCutscene, Home, NavigationHero, ForegroundHero.
    private const ulong HomeMask = 0x6181;

    // 2.57.0.98348, 2026-10-08, the email/password form of a client HeroesSwitcher started
    // without SSO: BackgroundHero, LoginUnified, HeroCutscene, NavigationHero, ForegroundHero.
    private const ulong LoginMask = 0x60C1;

    // Both builds, 2026-10-08: the boot splash shows ScreenLoading alone.
    private const ulong BootMask = 0x20;

    // ScreenScore with the hero backgrounds (bit 15 set; the backgrounds are as on home).
    private const ulong ScoreMask = 0xE081;

    [Fact]
    public void Read_FollowsACurrentPatchClientFromBootToLoginToHomeToAMatch()
    {
        var client = new FakeGlueClient();
        using var memory = new ClientScreen();
        ClientModule module = client.Module(51);

        ClientScreenSample starting = memory.Read(module, client);
        client.ShowScreens(BootMask);
        ClientScreenSample boot = memory.Read(module, client);
        client.ShowScreens(LoginMask);
        ClientScreenSample login = memory.Read(module, client);
        client.ShowScreens(HomeMask);
        ClientScreenSample home = memory.Read(module, client);
        client.ShowScreens(BootMask);
        ClientScreenSample map = memory.Read(module, client);
        client.TearDownMenus();
        ClientScreenSample match = memory.Read(module, client);

        Assert.Equal(ClientScreenKind.Unknown, starting.Screen);
        Assert.Equal("starting", starting.Reason);
        Assert.Equal(ClientScreenKind.Loading, boot.Screen);
        Assert.Null(boot.MapLoading);
        Assert.Equal(ClientScreenKind.Login, login.Screen);
        Assert.True(login.OnLogin);
        Assert.False(login.OnHome);
        Assert.False(login.SignedIn);
        Assert.Contains("ScreenLoginUnified", login.Shown);
        Assert.Equal(ClientScreenKind.Home, home.Screen);
        Assert.True(home.OnHome);
        Assert.False(home.OnLogin);
        Assert.True(home.SignedIn);
        Assert.Equal(
            new[]
            {
                "ScreenBackgroundHero",
                "ScreenHeroCutscene",
                "ScreenHome",
                "ScreenNavigationHero",
                "ScreenForegroundHero",
            },
            home.Shown
        );
        Assert.Equal(ClientScreenKind.Loading, map.Screen);
        Assert.True(map.MapLoading);
        Assert.Equal(ClientScreenKind.Match, match.Screen);
        Assert.False(match.OnHome);
        Assert.False(match.MapLoading);
        Assert.Null(match.SignedIn);
    }

    [Fact]
    public void Read_APreviousPatchClientThatLoadsTheReplayDirectlyIsNeverHome()
    {
        // 2.57.0.98304, 2026-10-08, opened through HeroesSwitcher: boot splash, then the match.
        var client = new FakeGlueClient();
        using var memory = new ClientScreen();
        ClientModule module = client.Module(52);

        client.ShowScreens(BootMask);
        ClientScreenSample boot = memory.Read(module, client);
        client.ShowScreens(0);
        ClientScreenSample between = memory.Read(module, client);
        client.TearDownMenus();
        ClientScreenSample match = memory.Read(module, client);

        Assert.False(boot.OnHome);
        Assert.Equal(ClientScreenKind.NoScreen, between.Screen);
        Assert.False(between.OnHome);
        Assert.Equal(ClientScreenKind.Match, match.Screen);
        Assert.False(match.OnHome);
        Assert.True(match.MenuSeen);
    }

    [Fact]
    public void Read_TheScoreScreenIsNotHome()
    {
        var client = new FakeGlueClient();
        using var memory = new ClientScreen();
        ClientModule module = client.Module(53);

        client.ShowScreens(ScoreMask);
        ClientScreenSample score = memory.Read(module, client);

        Assert.Equal(ClientScreenKind.Score, score.Screen);
        Assert.True(score.OnScore);
        Assert.False(score.OnHome);
    }

    [Fact]
    public void Read_FindsTheOffsetsAndTheNamesFromTheClient()
    {
        var client = new FakeGlueClient();
        using var memory = new ClientScreen();

        client.ShowScreens(HomeMask);
        memory.Read(client.Module(54), client);

        Assert.Equal(FakeGlueClient.GlobalRva, memory.GlobalRva);
        Assert.Equal(0x1D4, memory.MaskOffset);
        Assert.Equal(0x1F0, memory.FramesOffset);
        Assert.Equal(Screens, memory.ScreenNames);
    }

    [Fact]
    public void Read_NoMaskPatternYet_CannotTellAndScansAgainLater()
    {
        var client = new FakeGlueClient(glueSite: false);
        DateTimeOffset now = new(2026, 10, 8, 13, 0, 0, TimeSpan.Zero);
        using var memory = new ClientScreen(TestTime.Options(() => now));
        ClientModule module = client.Module(55);
        client.ShowScreens(HomeMask);

        ClientScreenSample unpacking = memory.Read(module, client);
        client.AddGlueSite();
        ClientScreenSample tooSoon = memory.Read(module, client);
        now = now.AddSeconds(11);
        ClientScreenSample found = memory.Read(module, client);

        Assert.Equal("unsupported-build", unpacking.Reason);
        Assert.Null(unpacking.OnHome);
        Assert.Equal(ClientScreenKind.Unknown, tooSoon.Screen);
        Assert.Equal(ClientScreenKind.Home, found.Screen);
    }

    [Fact]
    public void Read_NoScreenTable_ReportsWhy()
    {
        var client = new FakeGlueClient(table: false);
        using var memory = new ClientScreen();
        client.ShowScreens(HomeMask);

        ClientScreenSample sample = memory.Read(client.Module(56), client);

        Assert.Equal(ClientScreenKind.Unknown, sample.Screen);
        Assert.Equal("no-screen-table", sample.Reason);
    }

    [Fact]
    public void Read_AMaskWithBitsPastTheTable_IsNotTrusted()
    {
        var client = new FakeGlueClient();
        using var memory = new ClientScreen();
        client.ShowScreens(HomeMask | (1UL << 40));

        ClientScreenSample sample = memory.Read(client.Module(57), client);

        Assert.Equal(ClientScreenKind.Unknown, sample.Screen);
        Assert.Equal("mask-out-of-range", sample.Reason);
    }

    [Fact]
    public void Read_ANewProcessStartsOver()
    {
        var client = new FakeGlueClient();
        using var memory = new ClientScreen();
        client.ShowScreens(HomeMask);
        memory.Read(client.Module(58), client);

        client.TearDownMenus();
        ClientScreenSample next = memory.Read(client.Module(59), client);

        Assert.Equal(ClientScreenKind.Unknown, next.Screen);
        Assert.Equal("starting", next.Reason);
        Assert.False(next.MenuSeen);
    }

    [Fact]
    public void Read_NoClientVersionIsNeeded_AndADifferentOneIsReportedNotThrown()
    {
        var client = new FakeGlueClient();
        using var memory = new ClientScreen();
        client.ShowScreens(HomeMask);
        ClientModule module = client.Module(60);

        ClientScreenSample none = memory.Read(module, client);
        ClientScreenSample same = memory.Read(
            module,
            client,
            new HeroesClientVersion(2, 57, 0, 98348)
        );
        ClientScreenSample other = memory.Read(
            module,
            client,
            new HeroesClientVersion(2, 57, 0, 98304)
        );

        Assert.Equal(new HeroesClientVersion(2, 57, 0, 98348), none.ClientVersion);
        Assert.False(none.VersionMismatch);
        Assert.False(same.VersionMismatch);
        Assert.True(other.VersionMismatch);
        Assert.Equal(ClientScreenKind.Home, other.Screen);
    }

    [Fact]
    public void Read_TwoClientsAtOnce_EachReaderKeepsItsOwn()
    {
        var current = new FakeGlueClient();
        var previous = new FakeGlueClient(fileVersion: "2.57.0.98304");
        using var first = new ClientScreen();
        using var second = new ClientScreen();
        current.ShowScreens(HomeMask);
        previous.ShowScreens(BootMask);

        ClientScreenSample home = first.Read(current.Module(61), current);
        ClientScreenSample boot = second.Read(previous.Module(62), previous);

        Assert.Equal(ClientScreenKind.Home, home.Screen);
        Assert.Equal(ClientScreenKind.Loading, boot.Screen);
        Assert.Equal(98304, boot.ClientVersion.Build);
    }

    [Fact]
    public void Classify_LoadingAndLoginCoverTheBackgrounds()
    {
        Assert.Equal(ClientScreenKind.NoScreen, ClientScreen.Classify(new string[0]));
        Assert.Equal(
            ClientScreenKind.Menu,
            ClientScreen.Classify(new[] { "ScreenBackgroundHero", "ScreenCollection" })
        );
        Assert.Equal(
            ClientScreenKind.Loading,
            ClientScreen.Classify(new[] { "ScreenHome", "ScreenLoading" })
        );
        Assert.Equal(
            ClientScreenKind.Login,
            ClientScreen.Classify(new[] { "ScreenHome", "ScreenLoginUnified" })
        );
        Assert.Equal(
            ClientScreenKind.Score,
            ClientScreen.Classify(new[] { "ScreenHome", "ScreenScore" })
        );
    }

    [Fact]
    public void GlueScreenPattern_FindsTheMaskAndFramesOffsetsInRecordedCode()
    {
        // 2.57.0.98348 RVA 0xDD6AC0 (98304: 0xDE3610, same bytes), 2026-10-08.
        byte[] code = Hex(
            "48 8B 43 10 8B CF 48 8B 0C C8 49 8B 81 D4 01 00 00 8B 51 08 48 0F A3 D0 73 1D 49 8B 8C D1 F0 01 00 00 48 85 C9 74 10"
        );

        List<GlueScreenPattern.Offsets> found = GlueScreenPattern.Find(code);

        Assert.Equal(new[] { new GlueScreenPattern.Offsets(0x1D4, 0x1F0) }, found);
    }

    [Fact]
    public void GlueScreenPattern_SitesMustAgree()
    {
        var a = new GlueScreenPattern.Offsets(0x1D4, 0x1F0);
        var b = new GlueScreenPattern.Offsets(0x1D8, 0x1F8);

        Assert.True(GlueScreenPattern.TryAgree(new[] { a, a }, out var agreed));
        Assert.Equal(a, agreed);
        Assert.False(GlueScreenPattern.TryAgree(new[] { a, b }, out _));
        Assert.False(GlueScreenPattern.TryAgree(new GlueScreenPattern.Offsets[0], out _));
    }

    [Fact]
    public void GlueScreenTable_StopsAtTheEntriesWithoutASlash()
    {
        // In the client the 30 template paths are followed by the 30 plain names.
        var image = new TableImage(0x10_0000);
        image.AddTable(Screens.Select(name => name + "/" + name).ToArray());
        image.AddTable(Screens);

        List<string> names = GlueScreenTable.Find(
            image.Bytes,
            TableImage.DataRva,
            TableImage.Base,
            0x20_0000
        );

        Assert.Equal(Screens, names);
    }

    [Fact]
    public void HeroesClientVersion_ParsesComparesAndNeverThrows()
    {
        HeroesClientVersion current = HeroesClientVersion.TryParse("2.57.0.98348");
        HeroesClientVersion previous = HeroesClientVersion.TryParse("2, 57, 0, 98304");

        Assert.Equal(new HeroesClientVersion(2, 57, 0, 98348), current);
        Assert.Equal("2.57", previous.PatchLine);
        Assert.True(current.CompareTo(previous) > 0);
        Assert.Equal("2.57.0.98304", previous.ToString());
        Assert.Null(HeroesClientVersion.TryParse("2.57"));
        Assert.Null(HeroesClientVersion.TryParse("not a version"));
        Assert.Null(HeroesClientVersion.TryParse(null));
        Assert.Null(HeroesClientVersion.FromFile(@"C:\does\not\exist.exe"));
    }

    internal static byte[] Hex(string text) =>
        text.Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Select(b => Convert.ToByte(b, 16))
            .ToArray();

    /// <summary>A module image with a .rdata of {char*, length} tables and their strings.</summary>
    private sealed class TableImage
    {
        public const long Base = 0x140000000L;
        public const long DataRva = 0x8000;
        private int tableAt = 0x100;
        private int stringAt = 0x8000;

        public TableImage(int size) => Bytes = new byte[size];

        public byte[] Bytes { get; }

        public void AddTable(string[] entries)
        {
            foreach (string entry in entries)
            {
                byte[] text = Encoding.ASCII.GetBytes(entry);
                Array.Copy(text, 0, Bytes, stringAt, text.Length);
                BitConverter.GetBytes(Base + DataRva + stringAt).CopyTo(Bytes, tableAt);
                BitConverter.GetBytes((long)text.Length).CopyTo(Bytes, tableAt + 8);
                tableAt += 16;
                stringAt += text.Length + 1;
            }
        }
    }
}

/// <summary>
/// A client module as <see cref="ClientScreenMemory"/> reads it: PE headers, a code section with
/// the screen-state global's sites and the mask/frames site, a .rdata with the screen template
/// table, the global, and the menu root with its mask and frames.
/// </summary>
internal sealed class FakeGlueClient : IProcessMemory
{
    public const long Base = 0x140000000L;
    public const long ModuleSize = 0x40000;
    public const long GlobalRva = 0x30000;
    private const long TextRva = 0x1000;
    private const int TextSize = 0x1000;
    private const long RdataRva = 0x10000;
    private const int RdataSize = 0x4000;
    private const long Root = 0x2_0000_0000L;
    private const int MaskOffset = 0x1D4;
    private const int FramesOffset = 0x1F0;
    private const long FrameBase = 0x3_0000_0000L;

    private readonly byte[] headers = new byte[0x1000];
    private readonly byte[] text = new byte[TextSize];
    private readonly byte[] rdata = new byte[RdataSize];
    private readonly byte[] global = new byte[8];
    private readonly byte[] root = new byte[0x400];
    private readonly string fileVersion;

    public FakeGlueClient(
        bool glueSite = true,
        bool table = true,
        string fileVersion = "2.57.0.98348"
    )
    {
        this.fileVersion = fileVersion;
        WriteHeaders();
        for (int i = 0; i < 3; i++)
        {
            WriteScreenGlobalSite(0x40 + i * 0x40);
        }

        if (glueSite)
        {
            AddGlueSite();
        }

        if (table)
        {
            WriteTable();
        }

        BitConverter.GetBytes(Root).CopyTo(global, 0);
    }

    public ClientModule Module(int processId) =>
        new(processId, Base, ModuleSize, fileVersion, StartedAt: processId);

    public void AddGlueSite()
    {
        byte[] site = ClientScreenTests.Hex(
            "49 8B 81 D4 01 00 00 8B 51 08 48 0F A3 D0 73 1D 49 8B 8C D1 F0 01 00 00"
        );
        Array.Copy(site, 0, text, 0x400, site.Length);
    }

    /// <summary>The menus exist (a frame per screen) and show the screens in the mask.</summary>
    public void ShowScreens(ulong mask)
    {
        for (int i = 0; i < ClientScreenTests.Screens.Length; i++)
        {
            BitConverter.GetBytes(FrameBase + i * 0x1000L).CopyTo(root, FramesOffset + i * 8);
        }

        BitConverter.GetBytes(mask).CopyTo(root, MaskOffset);
    }

    /// <summary>A match: the frames are gone.</summary>
    public void TearDownMenus()
    {
        Array.Clear(root, FramesOffset, ClientScreenTests.Screens.Length * 8);
        BitConverter.GetBytes(0UL).CopyTo(root, MaskOffset);
    }

    public bool TryRead(long address, Span<byte> buffer)
    {
        return Copy(Base, headers, address, buffer)
            || Copy(Base + TextRva, text, address, buffer)
            || Copy(Base + RdataRva, rdata, address, buffer)
            || Copy(Base + GlobalRva, global, address, buffer)
            || Copy(Root, root, address, buffer)
            || CopyHeap(address, buffer);
    }

    private bool CopyHeap(long address, Span<byte> buffer)
    {
        foreach (KeyValuePair<long, byte[]> frame in heap)
        {
            if (Copy(frame.Key, frame.Value, address, buffer))
            {
                return true;
            }
        }

        return false;
    }

    // The frame tree (2.57 layout): parent +0x50, flags +0x48 (bit 0 visible), first child
    // node +0x40, a child's node at +0x18 and its next sibling node at +0x20, and a tagged end.
    public const long Top = 0x5_0000_0000L;
    public const long MenuContainer = 0x5_0000_1000L;
    public const long GameUi = 0x5_0000_2000L;
    public const long AwardsPanel = 0x5_0000_3000L;
    public const long AwardsVtableRva = RdataRva + 0x3800;
    private readonly Dictionary<long, byte[]> heap = new();

    /// <summary>
    /// The frame tree above the menus with the in-game awards panel, and the code that names its
    /// class: vtable slot 0x240 is IsA, which calls the class's StaticType, which loads the name
    /// (recorded shapes from 2.57.0.98348).
    /// </summary>
    public void AddPanels()
    {
        WriteName(0x3000, "CEndOfGameAwardsPanel");
        // vtable[0x240] -> IsA at text 0x700: push rbx; sub rsp,20h; mov rbx,rdx; call StaticType.
        BitConverter.GetBytes(Base + TextRva + 0x700).CopyTo(rdata, 0x3800 + 0x240);
        byte[] isA = ClientScreenTests.Hex("40 53 48 83 EC 20 48 8B DA E8 00 00 00 00");
        BitConverter.GetBytes(0x800 - (0x700 + 9 + 5)).CopyTo(isA, 10);
        Array.Copy(isA, 0, text, 0x700, isA.Length);
        // StaticType at text 0x800: ... lea rcx,[name].
        byte[] staticType = ClientScreenTests.Hex(
            "40 53 48 83 EC 30 8B 05 3C 3F A9 02 A8 01 75 65 83 C8 01 48 C7 44 24 28 0E 00 00 00 48 8D 0D 00 00 00 00"
        );
        BitConverter
            .GetBytes((int)(RdataRva + 0x3000 - (TextRva + 0x800 + 0x1C + 7)))
            .CopyTo(staticType, 0x1F);
        Array.Copy(staticType, 0, text, 0x800, staticType.Length);

        foreach (long frame in new[] { Top, MenuContainer, GameUi, AwardsPanel })
        {
            heap[frame] = new byte[0x100];
            heap[frame][0x48] = 0x7B;
        }

        Link(Top, MenuContainer, GameUi);
        Link(GameUi, AwardsPanel);
        BitConverter.GetBytes(MenuContainer).CopyTo(root, 0x50);
        BitConverter.GetBytes(Base + AwardsVtableRva).CopyTo(heap[AwardsPanel], 0);
        ShowAwards(false);
    }

    /// <summary>
    /// 2.57.0.98348, 2026-10-08: the awards panel is 0x7A in the match and 0x7B on the MVP screen.
    /// </summary>
    public void ShowAwards(bool shown) => heap[AwardsPanel][0x48] = (byte)(shown ? 0x7B : 0x7A);

    private void Link(long parent, params long[] children)
    {
        long end = (parent + 0x38) | 1;
        BitConverter.GetBytes(children[0] + 0x18).CopyTo(heap[parent], 0x40);
        for (int i = 0; i < children.Length; i++)
        {
            long next = i + 1 < children.Length ? children[i + 1] + 0x18 : end;
            BitConverter.GetBytes(next).CopyTo(heap[children[i]], 0x20);
            BitConverter.GetBytes(parent).CopyTo(heap[children[i]], 0x50);
        }
    }

    private void WriteName(int at, string name)
    {
        byte[] bytes = Encoding.ASCII.GetBytes(name + "\0");
        Array.Copy(bytes, 0, rdata, at, bytes.Length);
    }

    private static bool Copy(long start, byte[] source, long address, Span<byte> buffer)
    {
        long offset = address - start;
        if (offset < 0 || offset + buffer.Length > source.Length)
        {
            return false;
        }

        source.AsSpan((int)offset, buffer.Length).CopyTo(buffer);
        return true;
    }

    private void WriteHeaders()
    {
        headers[0] = (byte)'M';
        headers[1] = (byte)'Z';
        const int pe = 0x80;
        BitConverter.GetBytes(pe).CopyTo(headers, 0x3C);
        headers[pe] = (byte)'P';
        headers[pe + 1] = (byte)'E';
        BitConverter.GetBytes((ushort)2).CopyTo(headers, pe + 6);
        BitConverter.GetBytes((ushort)0xF0).CopyTo(headers, pe + 20);
        int table = pe + 24 + 0xF0;
        WriteSection(table, ".text", TextRva, TextSize, 0x60000020);
        WriteSection(table + 40, ".rdata", RdataRva, RdataSize, 0x40000040);
    }

    private void WriteSection(int at, string name, long rva, int size, uint characteristics)
    {
        Encoding.ASCII.GetBytes(name).CopyTo(headers, at);
        BitConverter.GetBytes(size).CopyTo(headers, at + 8);
        BitConverter.GetBytes((uint)rva).CopyTo(headers, at + 12);
        BitConverter.GetBytes(characteristics).CopyTo(headers, at + 36);
    }

    /// <summary>`mov rcx,[G]; test; jz; xor edx,edx; call; test al,al; jz; mov rcx,[G]; call`.</summary>
    private void WriteScreenGlobalSite(int at)
    {
        long site = TextRva + at;
        byte[] bytes = new byte[LoadingScreenPattern.Width];
        bytes[0] = 0x48;
        bytes[1] = 0x8B;
        bytes[2] = 0x0D;
        BitConverter.GetBytes((int)(GlobalRva - (site + 7))).CopyTo(bytes, 3);
        bytes[7] = 0x48;
        bytes[8] = 0x85;
        bytes[9] = 0xC9;
        bytes[10] = 0x74;
        bytes[12] = 0x33;
        bytes[13] = 0xD2;
        bytes[14] = 0xE8;
        bytes[19] = 0x84;
        bytes[20] = 0xC0;
        bytes[21] = 0x74;
        bytes[23] = 0x48;
        bytes[24] = 0x8B;
        bytes[25] = 0x0D;
        BitConverter.GetBytes((int)(GlobalRva - (site + 30))).CopyTo(bytes, 26);
        bytes[30] = 0xE8;
        Array.Copy(bytes, 0, text, at, bytes.Length);
    }

    private void WriteTable()
    {
        // Something that is not an entry before the table, as in the client.
        BitConverter.GetBytes(0x3BA700432FBB3E0FL).CopyTo(rdata, 0x0F0);
        int entry = 0x100;
        int stringAt = 0x1000;
        foreach (string name in ClientScreenTests.Screens)
        {
            byte[] path = Encoding.ASCII.GetBytes(name + "/" + name);
            Array.Copy(path, 0, rdata, stringAt, path.Length);
            BitConverter.GetBytes(Base + RdataRva + stringAt).CopyTo(rdata, entry);
            BitConverter.GetBytes((long)path.Length).CopyTo(rdata, entry + 8);
            entry += 16;
            stringAt += path.Length + 1;
        }
    }
}
