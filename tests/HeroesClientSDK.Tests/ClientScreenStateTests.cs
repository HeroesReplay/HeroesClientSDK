using System.Collections.Generic;
using Xunit;

namespace HeroesClientSDK.Tests;

/// <summary>
/// The states HeroesReplay#292 still read with OCR: the boot splash against a map loading screen,
/// Battle.net authentication against the email and password form, and a game-launch message
/// dialog with its result. The layouts are the ones recorded from 2.57.0.98348 and 2.57.0.98304
/// on 2026-10-08 (whole-tree captures and memory images, read-only).
/// </summary>
[Trait(TestCategories.Category, TestCategories.Unit)]
public class ClientScreenStateTests
{
    private const ulong HomeMask = 0x6181;
    private const ulong LoginMask = 0x60C1;
    private const ulong BootMask = 0x20;

    // CLoadingBar and CCustomLoadingPanel under CScreenLoading.
    private const byte BarHidden = 0x7A;
    private const byte PanelHidden = 0x72;
    private const byte Shown = 0x7B;

    [Fact]
    public void Read_TheBootSplashIsNotAMapEvenAfterAMenu_AndTheMapPanelIs()
    {
        // Current patch: boot splash, home, then the replay's map loading screen from home.
        var client = new FakeGlueClient();
        using var memory = new ClientScreen();
        ClientModule module = client.Module(80);

        client.ShowScreens(BootMask);
        client.AddLoadingScreen(BarHidden, PanelHidden);
        ClientScreenSample boot = memory.Read(module, client);
        client.ShowScreens(HomeMask);
        ClientScreenSample home = memory.Read(module, client);
        client.ShowScreens(BootMask);
        ClientScreenSample splashAfterMenu = memory.Read(module, client);
        client.AddLoadingScreen(Shown, Shown);
        ClientScreenSample map = memory.Read(module, client);

        Assert.Equal(ClientScreenKind.Splash, boot.Screen);
        Assert.False(boot.MapLoading);
        Assert.True(boot.OnLoading);
        Assert.Equal(ClientScreenKind.Home, home.Screen);
        Assert.Equal(ClientScreenKind.Splash, splashAfterMenu.Screen);
        Assert.False(splashAfterMenu.MapLoading);
        Assert.Equal(ClientScreenKind.MapLoading, map.Screen);
        Assert.True(map.MapLoading);
        Assert.True(map.OnLoading);
        Assert.False(map.OnHome);
    }

    [Fact]
    public void Read_APreviousPatchMapScreenWithNoScreenBitIsMapLoading()
    {
        // 2.57.0.98304 opened through HeroesSwitcher with the replay: the boot splash shows
        // ScreenLoading alone, then the map loading screen shows with an empty mask but the
        // ScreenLoading frame's map panel shown, then the match.
        var client = new FakeGlueClient(fileVersion: "2.57.0.98304");
        using var memory = new ClientScreen();
        ClientModule module = client.Module(81);

        client.ShowScreens(BootMask);
        client.AddLoadingScreen(BarHidden, PanelHidden);
        ClientScreenSample boot = memory.Read(module, client);
        client.ShowScreens(0);
        client.AddLoadingScreen(Shown, Shown);
        ClientScreenSample map = memory.Read(module, client);
        client.TearDownMenus();
        ClientScreenSample match = memory.Read(module, client);

        Assert.Equal(ClientScreenKind.Splash, boot.Screen);
        Assert.False(boot.MapLoading);
        Assert.Equal(ClientScreenKind.MapLoading, map.Screen);
        Assert.Equal("map-panel", map.Reason);
        Assert.True(map.MapLoading);
        Assert.False(map.MenuSeen);
        Assert.Equal(ClientScreenKind.Match, match.Screen);
    }

    [Fact]
    public void Read_NoScreenWithTheMapPanelHiddenIsStillNoScreen()
    {
        // 2.57.0.98348 on the game-data DOWNLOADING screen of a switcher handoff: no screen bit,
        // and the loading screen and its panels hidden.
        var client = new FakeGlueClient();
        using var memory = new ClientScreen();
        client.ShowScreens(0);
        client.AddLoadingScreen(0x5A, 0x52);

        ClientScreenSample sample = memory.Read(client.Module(82), client);

        Assert.Equal(ClientScreenKind.NoScreen, sample.Screen);
        Assert.False(sample.MapLoading);
        Assert.False(sample.OnHome);
    }

    [Fact]
    public void Read_BattleNetAuthenticationIsNotTheLoginForm()
    {
        // 2.57.0.98348 started by Battle.net: ScreenLoginUnified under a shown CLoginDialog
        // (AUTHENTICATION, Connecting..., 0x73) for a few seconds, then home.
        var client = new FakeGlueClient();
        using var memory = new ClientScreen();
        ClientModule module = client.Module(83);
        client.ShowScreens(LoginMask);
        long login = client.AddDialog("CLoginDialog", 0x73);
        client.AddDialog("CBoostPurchaseDialog", 0x7B);

        ClientScreenSample connecting = memory.Read(module, client);
        client.SetFlags(login, 0x72);
        ClientScreenSample form = memory.Read(module, client);

        Assert.Equal(ClientScreenKind.Authenticating, connecting.Screen);
        Assert.False(connecting.OnLogin);
        Assert.True(connecting.OnAuthenticating);
        Assert.False(connecting.SignedIn);
        Assert.False(connecting.OnHome);
        Assert.Equal(new[] { "CLoginDialog", "CBoostPurchaseDialog" }, connecting.Dialogs);
        Assert.Equal(ClientScreenKind.Login, form.Screen);
        Assert.True(form.OnLogin);
        Assert.False(form.SignedIn);
        Assert.False(form.DialogShown("CLoginDialog"));
    }

    [Fact]
    public void Read_ALoginDialogCreatedAfterTheFirstReadIsSeenAtOnce()
    {
        // 2.57.0.98348, 2026-10-08 15:56:22 (final e2e run): the client creates CLoginDialog
        // when the login screen first shows. A dialog list cached since the boot splash read the
        // AUTHENTICATION panel as the login form for 0.8 s.
        var client = new FakeGlueClient();
        using var memory = new ClientScreen();
        ClientModule module = client.Module(87);
        client.ShowScreens(BootMask);
        client.AddLoadingScreen(BarHidden, PanelHidden);

        ClientScreenSample boot = memory.Read(module, client);
        client.ShowScreens(LoginMask);
        client.AddDialog("CLoginDialog", 0x73);
        ClientScreenSample connecting = memory.Read(module, client);

        Assert.Equal(ClientScreenKind.Splash, boot.Screen);
        Assert.Equal(ClientScreenKind.Authenticating, connecting.Screen);
        Assert.False(connecting.OnLogin);
    }

    [Fact]
    public void Read_AGameLaunchMessageDialogNamesItsResult()
    {
        // 2.57.0.98348 asked by HeroesSwitcher for a replay of a build Blizzard no longer serves:
        // a shown CStandardDialog (0x73) over ScreenLoginUnified, CLoginDialog hidden (0x72), and
        // the launch manager's result.
        var client = new FakeGlueClient();
        using var memory = new ClientScreen();
        ClientModule module = client.Module(84);
        client.ShowScreens(LoginMask);
        client.AddDialog("CLoginDialog", 0x72);
        long dialog = client.AddDialog("CStandardDialog", 0x73);
        client.AddLaunchManager(10);

        ClientScreenSample shown = memory.Read(module, client);
        client.SetFlags(dialog, 0x72);
        client.SetLaunchResult(0);
        ClientScreenSample gone = memory.Read(module, client);

        Assert.Equal(ClientScreenKind.Dialog, shown.Screen);
        Assert.Equal(new[] { "CStandardDialog" }, shown.Dialogs);
        Assert.Equal(10, shown.LaunchResultCode);
        Assert.Equal("GameLaunchBaseBuildMissing", shown.LaunchResult);
        Assert.False(shown.OnLogin);
        Assert.False(shown.OnHome);
        Assert.Equal(ClientScreenKind.Login, gone.Screen);
        Assert.Equal(0, gone.LaunchResultCode);
        Assert.Null(gone.LaunchResult);
        Assert.Equal(FakeGlueClient.LaunchGlobalRva, memory.LaunchGlobalRva);
        Assert.Equal(8, memory.LaunchResultOffset);
        Assert.Equal(25, memory.LaunchKeys.Count);
    }

    [Fact]
    public void Read_TheGameDataDownloadDialogIsNotHome()
    {
        // 2.57.0.98348, 2026-10-08 15:39, HeroesSwitcher handing a 2.57.0.98304 replay to its
        // build: the boot splash, then no screen with the loading screen hidden, then
        // "DOWNLOADING ... Calculating..." in a shown CProgressBarDialog (0x73) with launch state
        // 6, then state 8 before the client exits. LoadingScreenMemory read this as a menu.
        var client = new FakeGlueClient();
        using var memory = new ClientScreen();
        ClientModule module = client.Module(86);
        client.ShowScreens(BootMask);
        client.AddLoadingScreen(BarHidden, PanelHidden);
        long download = client.AddDialog("CProgressBarDialog", 0x72);
        client.AddLaunchManager(0);

        ClientScreenSample boot = memory.Read(module, client);
        client.ShowScreens(0);
        client.AddLoadingScreen(0x5A, 0x52);
        client.SetLaunchState(6);
        ClientScreenSample calculating = memory.Read(module, client);
        client.SetFlags(download, 0x73);
        ClientScreenSample downloading = memory.Read(module, client);

        Assert.Equal(ClientScreenKind.Splash, boot.Screen);
        Assert.Equal(0, boot.LaunchState);
        Assert.Equal(ClientScreenKind.NoScreen, calculating.Screen);
        Assert.Equal(6, calculating.LaunchState);
        Assert.Equal(ClientScreenKind.Download, downloading.Screen);
        Assert.True(downloading.OnDownload);
        Assert.False(downloading.OnHome);
        Assert.False(downloading.OnLogin);
        Assert.False(downloading.MapLoading);
        Assert.Equal(0x20, memory.LaunchStateOffset);
    }

    [Fact]
    public void Read_WithoutTheLaunchSites_TheScreensStillRead()
    {
        var client = new FakeGlueClient();
        using var memory = new ClientScreen();
        client.ShowScreens(HomeMask);

        ClientScreenSample sample = memory.Read(client.Module(85), client);

        Assert.Equal(ClientScreenKind.Home, sample.Screen);
        Assert.Null(sample.LaunchResultCode);
        Assert.Null(sample.LaunchResult);
        Assert.Equal(0, memory.LaunchGlobalRva);
    }

    [Fact]
    public void GameLaunchPattern_FindsTheGlobalAndTheResultOffsetInRecordedCode()
    {
        // 2.57.0.98348, 2026-10-08: the creator at RVA 0xCFB530 (global 0x3771BA8) and the result
        // store at 0xCFB024. 2.57.0.98304 has the same bytes at 0xD07F10 (global 0x3772BA8) and
        // 0xD07A04.
        byte[] creator = ClientScreenTests.Hex(
            "48 83 EC 28 48 83 3D 6C 66 A7 02 00 75 2A B9 58 69 03 00 E8 E8 6F 67 00 48 85 C0 74 14 48 8B C8 E8 0B B0 FF FF 48 89 05 4C 66 A7 02"
        );
        byte[] store = ClientScreenTests.Hex(
            "8B 02 48 8B D9 85 C0 0F 84 B9 04 00 00 83 F8 02 0F 85 80 00 00 00 48 63 7A 04 8D 47 FF 83 F8 17 77 6D 89 79 08"
        );

        List<long> globals = GameLaunchPattern.FindGlobals(creator, 0xCFB530);
        List<int> offsets = GameLaunchPattern.FindResultOffsets(store);

        Assert.Equal(new long[] { 0x3771BA8 }, globals);
        Assert.Equal(new[] { 8 }, offsets);
        Assert.True(GameLaunchPattern.TryAgree(globals, out long global));
        Assert.Equal(0x3771BA8, global);
        Assert.False(GameLaunchPattern.TryAgree(new long[] { 1, 2 }, out _));
        Assert.False(GameLaunchPattern.TryAgree(new long[0], out _));
    }
}
