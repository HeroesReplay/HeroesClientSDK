using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace HeroesClientSDK.Tests;

/// <summary>
/// Message dialogs and their text, for the Battle.net error dialog HeroesReplay#292 reads from
/// memory instead of OCR. The shapes are recorded from 2.57.0.98348 and 2.57.0.98304 (read-only
/// module images and live reads, 2026-10-08): <c>CBattlenetErrorDialog</c> and
/// <c>CDisconnectedDialog</c> are standard dialogs at the top of the UI, hidden (0x72) until an
/// error shows; <c>CStandardDialog::ApplyParams</c> sets the title label at +0x248 and the message
/// label at +0x250; a <c>CLabel</c> keeps its string at <c>[[label+0x1D8]+0x28]+0x18</c>. The
/// client's Battle.net error table holds the two region messages (codes 169 and 153), and its
/// string table the disconnect ones.
/// </summary>
[Trait(TestCategories.Category, TestCategories.Unit)]
public class DialogTextTests
{
    private const ulong HomeMask = 0x6181;
    private const ulong LoginMask = 0x60C1;
    private const byte Hidden = 0x72;
    private const byte Shown = 0x73;

    // The client's Battle.net error table (BattlenetAPI_GetErrorString), 2.57.0.98348 .rdata.
    internal const string RegionUnavailable =
        "The selected region is currently unavailable. Please try again later or select another region.";
    internal const string VersionMismatch =
        "Game client version mismatch with selected region.  You may be able to continue playing if you exit the client, patch from the launcher, and restart.";

    // The client's string table (UI/BattleNetErrorDialog/Error_ServiceLost,
    // UI/DisconnectedDialogTitle, UI/DisconnectedDialogMessage), read live on 2.57.0.98348.
    internal const string ServiceLost = "You were disconnected from Blizzard services.";
    internal const string ConnectionLost = "Connection Lost";
    internal const string DisconnectedMessage =
        "Blizzard services may be temporarily unavailable or your internet connection may be down. The game will now try to reconnect.";

    // CStandardDialog::ApplyParams, 2.57.0.98348 RVA 0x1468441 (2.57.0.98304 0x146F0E1, the same
    // bytes): mov rcx,[rdi+248h] ... call [rax+380h] ... mov rcx,[rdi+250h].
    internal static readonly byte[] ApplyParams98348 = ClientScreenTests.Hex(
        "48 8B 8F 48 02 00 00 48 C1 E8 02 48 89 45 F8 48 8B 01 FF 90 80 03 00 00 8B 46 34 48 8D 4E 38 D1 E8 A8 01 74 03 48 8B 09 8B 46 30 48 8D 55 F0 48 89 4D F0 48 8B 8F 50 02 00 00"
    );

    // CLabel::SetText, 2.57.0.98348 RVA 0x14B19F9: mov rbx,[rdi+1D8h]; lea rsi,[empty];
    // mov rcx,[rbx+28h]; test rcx,rcx; je; add rcx,18h; jmp; mov rcx,rsi.
    internal static readonly byte[] SetText98348 = ClientScreenTests.Hex(
        "48 8B 9F D8 01 00 00 48 8D 35 C9 DD A7 01 48 8B 4B 28 48 85 C9 74 06 48 83 C1 18 EB 03 48 8B CE"
    );

    // The same at 2.57.0.98304 RVA 0x14B8699: only the rip displacement of the empty string differs.
    private static readonly byte[] SetText98304 = ClientScreenTests.Hex(
        "48 8B 9F D8 01 00 00 48 8D 35 29 81 A7 01 48 8B 4B 28 48 85 C9 74 06 48 83 C1 18 EB 03 48 8B CE"
    );

    [Fact]
    public void Pattern_FindsTheDialogTextOffsetsInTheRecordedCodeOfBothBuilds()
    {
        List<DialogTextPattern.Labels> labels = DialogTextPattern.FindLabels(ApplyParams98348);
        List<DialogTextPattern.Text> text98348 = DialogTextPattern.FindText(SetText98348);
        List<DialogTextPattern.Text> text98304 = DialogTextPattern.FindText(SetText98304);

        Assert.Equal(new[] { new DialogTextPattern.Labels(0x248, 0x250) }, labels);
        Assert.Equal(new[] { new DialogTextPattern.Text(0x1D8, 0x28, 0x18) }, text98348);
        Assert.Equal(text98348, text98304);
        Assert.True(DialogTextPattern.TryAgree(labels, text98348, out DialogTextLayout layout));
        Assert.Equal(DialogTextLayout.Default, layout);
    }

    [Fact]
    public void Pattern_SitesThatDisagreeOrAreMissingAreNoLayout()
    {
        var labels = new List<DialogTextPattern.Labels> { new(0x248, 0x250) };
        var text = new List<DialogTextPattern.Text> { new(0x1D8, 0x28, 0x18) };

        Assert.False(DialogTextPattern.TryAgree(new List<DialogTextPattern.Labels>(), text, out _));
        Assert.False(DialogTextPattern.TryAgree(labels, new List<DialogTextPattern.Text>(), out _));
        Assert.False(
            DialogTextPattern.TryAgree(
                new List<DialogTextPattern.Labels> { new(0x248, 0x250), new(0x250, 0x258) },
                text,
                out _
            )
        );
        Assert.Empty(DialogTextPattern.FindLabels(SetText98348));
        Assert.Empty(DialogTextPattern.FindText(ApplyParams98348));
    }

    [Fact]
    public void Read_ARegionUnavailableErrorIsAShownBattlenetErrorWithItsMessage()
    {
        var client = new FakeGlueClient();
        client.AddDialogTextSites();
        using var memory = new ClientScreen();
        client.ShowScreens(HomeMask);
        long error = client.AddDialog("CBattlenetErrorDialog", Hidden);
        client.AddDialog("CDisconnectedDialog", Hidden);
        client.SetDialogText(error, "Error", RegionUnavailable);
        ClientModule module = client.Module(90);

        ClientScreenSample before = memory.Read(module, client);
        client.SetFlags(error, Shown);
        ClientScreenSample shown = memory.Read(module, client);

        Assert.False(before.BattlenetErrorShown);
        Assert.Null(before.BattlenetError);
        Assert.Equal(ClientScreenKind.Dialog, shown.Screen);
        Assert.True(shown.BattlenetErrorShown);
        DialogMessage message = Assert.IsType<DialogMessage>(shown.BattlenetError);
        Assert.Equal("CBattlenetErrorDialog", message.Dialog);
        Assert.Equal("Error", message.Title);
        Assert.Equal(RegionUnavailable, message.Message);
        Assert.Equal("Error " + RegionUnavailable, message.Text);
        Assert.Equal(new[] { message }, shown.DialogMessages);
        Assert.True(memory.DialogTextFromCode);
    }

    [Fact]
    public void Read_AVersionMismatchBehindAPointerUnderItsOwnTitle()
    {
        // The NGDP patch-failure handler shows Battle.net error 153 under "Version Mismatch".
        var client = new FakeGlueClient();
        client.AddDialogTextSites();
        using var memory = new ClientScreen();
        client.ShowScreens(LoginMask);
        long error = client.AddDialog("CBattlenetErrorDialog", Shown);
        client.SetDialogText(error, "Version Mismatch", VersionMismatch, behindPointer: true);

        ClientScreenSample sample = memory.Read(client.Module(91), client);

        Assert.Equal(ClientScreenKind.Dialog, sample.Screen);
        Assert.Equal("Version Mismatch", sample.BattlenetError?.Title);
        Assert.Equal(VersionMismatch, sample.BattlenetError?.Message);
        Assert.False(sample.OnLogin);
    }

    [Fact]
    public void Read_TheDisconnectedDialogIsABattlenetError()
    {
        var client = new FakeGlueClient();
        client.AddDialogTextSites();
        using var memory = new ClientScreen();
        client.ShowScreens(HomeMask);
        client.AddDialog("CBattlenetErrorDialog", Hidden);
        long disconnected = client.AddDialog("CDisconnectedDialog", Shown);
        client.SetDialogText(disconnected, ConnectionLost, DisconnectedMessage);

        ClientScreenSample sample = memory.Read(client.Module(92), client);

        Assert.True(sample.BattlenetErrorShown);
        Assert.Equal("CDisconnectedDialog", sample.BattlenetError?.Dialog);
        Assert.Equal(ConnectionLost, sample.BattlenetError?.Title);
        Assert.Equal(DisconnectedMessage, sample.BattlenetError?.Message);
    }

    [Fact]
    public void Read_HomeAfterBattlenetSignIn_ReadsNoBattlenetError()
    {
        // 2.57.0.98348 at home after Battle.net "launch Hero" (2026-10-08 21:42): the Battle.net
        // error and disconnect dialogs hidden with empty labels, CLoginDialog hidden with
        // "Authentication" / "Connecting...", and two purchase dialogs that keep their visible bit.
        var client = new FakeGlueClient();
        client.AddDialogTextSites();
        using var memory = new ClientScreen();
        client.ShowScreens(HomeMask);
        client.SetDialogText(client.AddDialog("CBattlenetErrorDialog", Hidden), "", "");
        client.SetDialogText(
            client.AddDialog("CLoginDialog", Hidden),
            "Authentication",
            "Connecting..."
        );
        client.SetDialogText(client.AddDialog("CDisconnectedDialog", Hidden), "", "");
        client.SetDialogText(client.AddDialog("CLootChestPurchaseDialog", 0x7B), "", "");
        client.SetDialogText(client.AddDialog("CBoostPurchaseDialog", 0x7B), "Boost Purchase", "");

        ClientScreenSample sample = memory.Read(client.Module(93), client);

        Assert.Equal(ClientScreenKind.Home, sample.Screen);
        Assert.False(sample.BattlenetErrorShown);
        Assert.Null(sample.BattlenetError);
        Assert.Equal(
            new[]
            {
                new DialogMessage("CLootChestPurchaseDialog", "", ""),
                new DialogMessage("CBoostPurchaseDialog", "Boost Purchase", ""),
            },
            sample.DialogMessages
        );
        Assert.False(sample.DialogMessages[0].HasText);
    }

    [Fact]
    public void Read_TheVersionDialogOfAGameLaunchIsNotABattlenetError()
    {
        // 2.57.0.98348, 2026-10-08 21:44: the shown CStandardDialog of the 2.57.0.98297 replay.
        var client = new FakeGlueClient();
        client.AddDialogTextSites();
        using var memory = new ClientScreen();
        client.ShowScreens(LoginMask);
        client.AddDialog("CBattlenetErrorDialog", Hidden);
        long dialog = client.AddDialog("CStandardDialog", Shown);
        client.SetDialogText(
            dialog,
            "",
            "The version of Heroes of the Storm required to play this game is not available."
        );
        client.AddLaunchManager(23);

        ClientScreenSample sample = memory.Read(client.Module(94), client);

        Assert.Equal(ClientScreenKind.Dialog, sample.Screen);
        Assert.Equal("GameLaunchUnsupportedNoData", sample.LaunchResult);
        Assert.False(sample.BattlenetErrorShown);
        DialogMessage message = Assert.Single(sample.DialogMessages);
        Assert.Equal("CStandardDialog", message.Dialog);
        Assert.Equal(
            "The version of Heroes of the Storm required to play this game is not available.",
            message.Message
        );
    }

    [Fact]
    public void Read_ABattlenetErrorWhoseLabelsDoNotReadIsShownWithoutText()
    {
        // A label slot that holds another class, or nothing, is no text: never a wrong one.
        var client = new FakeGlueClient();
        client.AddDialogTextSites();
        using var memory = new ClientScreen();
        client.ShowScreens(HomeMask);
        long error = client.AddDialog("CBattlenetErrorDialog", Shown);
        client.SetLabelSlot(error, DialogTextLayout.Default.MessageLabelOffset, "CImage");

        ClientScreenSample sample = memory.Read(client.Module(95), client);

        Assert.True(sample.BattlenetErrorShown);
        DialogMessage message = Assert.IsType<DialogMessage>(sample.BattlenetError);
        Assert.Null(message.Title);
        Assert.Null(message.Message);
        Assert.False(message.HasText);
        Assert.Equal(string.Empty, message.Text);
    }

    [Fact]
    public void Read_WithoutTheCodeSites_TheProfileSaysWhereTheTextIs()
    {
        // A build whose code has no ApplyParams/SetText site reads the profile's layout.
        var moved = new DialogTextLayout(0x260, 0x268, 0x1E0, 0x30, 0x20);
        var profiles = new BuildProfileRegistry(new BuildProfile { DialogText = moved });
        var client = new FakeGlueClient();
        using var memory = new ClientScreen(new HeroesClientOptions { Profiles = profiles });
        client.ShowScreens(HomeMask);
        long error = client.AddDialog("CBattlenetErrorDialog", Shown);
        client.SetDialogText(error, "Error", ServiceLost, text: moved);

        ClientScreenSample sample = memory.Read(client.Module(96), client);

        Assert.False(memory.DialogTextFromCode);
        Assert.Equal(moved, memory.DialogText);
        Assert.Equal(ServiceLost, sample.BattlenetError?.Message);
    }

    [Fact]
    public void Read_WhenMemoryCannotTell_TheBattlenetErrorIsUnknown()
    {
        var client = new FakeGlueClient(glueSite: false);
        using var memory = new ClientScreen();

        ClientScreenSample sample = memory.Read(client.Module(97), client);

        Assert.False(sample.Ok);
        Assert.Null(sample.BattlenetErrorShown);
        Assert.Null(sample.BattlenetError);
        Assert.Null(sample.DialogMessages);
    }

    [Theory]
    [InlineData("01 00 00 00 00 00 00 00", "")] // length 0 (1 >> 2)
    [InlineData("0C 00 00 00 00 00 00 00 4F 4B 21", "OK!")] // 3 bytes inline
    [InlineData("0C 00 00 00 00 00 00 00 C3 28 21", null)] // not UTF-8
    [InlineData("0C 00 00 00 00 00 00 00 41 00 42", null)] // a NUL inside
    [InlineData("04 40 00 00 00 00 00 00 41", null)] // 4097 bytes: longer than the cap
    public void String_ReadsTheClientsStringFormat(string block, string expected)
    {
        var client = new FakeGlueClient();
        client.AddDialogTextSites();
        using var memory = new ClientScreen();
        client.ShowScreens(HomeMask);
        long error = client.AddDialog("CBattlenetErrorDialog", Shown);
        client.AddLabelWithBlock(
            error,
            DialogTextLayout.Default.MessageLabelOffset,
            ClientScreenTests.Hex(block)
        );

        ClientScreenSample sample = memory.Read(client.Module(98), client);

        Assert.Equal(expected, sample.BattlenetError?.Message);
    }

    [Fact]
    public void Discovery_ReportsWhereTheDialogTextCameFrom()
    {
        var client = new FakeGlueClient();
        client.AddDialogTextSites();
        client.ShowScreens(HomeMask);
        using HeroesClientProcess attached = HeroesClientProcess.FromMemory(
            client,
            client.Module(99)
        );

        ClientDiscovery found = ClientDiscovery.Run(attached);

        Assert.True(found.Menus.DialogTextFromCode);
        Assert.Equal(DialogTextLayout.Default, found.Menus.DialogText);
        Assert.Contains("CBattlenetErrorDialog", ClientScreen.FrameClassNames);
        Assert.Contains("CDisconnectedDialog", ClientScreen.FrameClassNames);
        Assert.Equal(
            new[] { "CBattlenetErrorDialog", "CDisconnectedDialog" },
            ClientScreenSample.BattlenetErrorDialogs.ToArray()
        );
    }
}
