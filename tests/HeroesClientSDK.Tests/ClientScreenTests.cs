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
