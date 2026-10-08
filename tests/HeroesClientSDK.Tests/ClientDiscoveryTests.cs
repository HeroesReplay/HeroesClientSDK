using System;
using System.IO;
using System.Linq;
using System.Text;
using Xunit;

namespace HeroesClientSDK.Tests;

/// <summary>
/// Every reader's discovery offline: on the bytes derived from the 2.57.0.98348 image
/// (<see cref="Image98348"/>), and end to end through a saved image file
/// (<see cref="HeroesClientProcess.FromImage"/>) made from <see cref="FakeGlueClient"/>.
/// </summary>
[Trait(TestCategories.Category, TestCategories.Unit)]
public class ClientDiscoveryTests
{
    [Fact]
    public void Run_On98348_FindsEveryReadersGlobalsAsTheImageHasThem()
    {
        // heroes-client-probe --image on the whole 98348 image (2026-10-08) printed the same:
        // clock 0x338B5A4/0x264262C (2 sites), screen state 0x3770830 (34 sites), mask 0x1D4,
        // frames 0x1F0, launch 0x3771BA8 +0x8 +0x20, and 825 frame classes.
        RecordedImage image = Image98348.Load();
        using HeroesClientProcess client = HeroesClientProcess.FromMemory(
            image,
            image.Module(Image98348.Version)
        );

        ClientDiscovery found = ClientDiscovery.Run(client);

        Assert.True(found.Ok);
        Assert.Equal(new HeroesClientVersion(2, 57, 0, 98348), found.ClientVersion);
        Assert.Equal(new MatchClockDiscovery("pattern", 0x338B5A4, 0x264262C, 2), found.Clock);
        Assert.Equal(new LoadingScreenDiscovery("pattern", 0x3770830, 34), found.Loading);
        ClientScreenDiscovery menus = found.Menus;
        Assert.Equal("pattern", menus.Reason);
        Assert.Equal(0x3770830, menus.GlobalRva);
        Assert.Equal(0x1D4, menus.MaskOffset);
        Assert.Equal(0x1F0, menus.FramesOffset);
        Assert.Equal(ClientScreenTests.Screens, menus.Screens);
        Assert.Equal(0x3771BA8, menus.LaunchGlobalRva);
        Assert.Equal(0x08, menus.LaunchResultOffset);
        Assert.Equal(0x20, menus.LaunchStateOffset);
        Assert.Equal(new[] { string.Empty }.Concat(FakeGlueClient.LaunchKeys), menus.LaunchKeys);
        Assert.Equal(ClientScreen.FrameClassNames.Count, menus.FrameClasses);
        Assert.Empty(menus.MissingFrameClasses);
    }

    [Fact]
    public void Run_TheTwoScreenReadersShareOneWalkOfTheCode()
    {
        RecordedImage image = Image98348.Load();
        using HeroesClientProcess client = HeroesClientProcess.FromMemory(
            image,
            image.Module(Image98348.Version)
        );

        ClientDiscovery.Run(client);

        Assert.Equal(1, client.ScreenScans.Walks);
    }

    [Fact]
    public void Run_AModuleWithoutTheCode_SaysWhichReaderMisses()
    {
        // The 98348 section table and nothing else: a client that is still unpacking its code.
        var image = new RecordedImage(
            Image98348.Base,
            Image98348.Size,
            new[]
            {
                (".text", 0x1000L, 0x262C4BC, 0x60000020u),
                (".rdata", 0x262E000L, 0x900880, 0x40000040u),
            }
        );
        using HeroesClientProcess client = HeroesClientProcess.FromMemory(
            image,
            image.Module(Image98348.Version)
        );

        ClientDiscovery found = ClientDiscovery.Run(client);

        Assert.False(found.Ok);
        Assert.Equal("ok", found.Reason);
        Assert.Equal("unsupported-build", found.Clock.Reason);
        Assert.Equal("unsupported-build", found.Loading.Reason);
        Assert.Equal("unsupported-build", found.Menus.Reason);
        Assert.Equal(0, found.Menus.FrameClasses);
        Assert.Equal(ClientScreen.FrameClassNames, found.Menus.MissingFrameClasses);
    }

    [Fact]
    public void Run_AClientThatIsNotAttached_ReportsItsReason()
    {
        ClientDiscovery found = ClientDiscovery.Run(HeroesClientProcess.FromImage(null));

        Assert.False(found.Ok);
        Assert.Equal("no-image", found.Reason);
        Assert.Equal("no-image", found.Clock.Reason);
        Assert.Equal("no-image", found.Menus.Reason);
        Assert.Equal("no-process", ClientDiscovery.Run(null).Reason);
    }

    [Fact]
    public void FromImage_ServesASavedImageFileToEveryReader()
    {
        string folder = TempFolder();
        try
        {
            string path = Path.Combine(folder, "HeroesOfTheStorm_x64-2.57.0.98348-image.dmp");
            File.WriteAllBytes(path, SavedImage("2.57.0.98348"));

            using HeroesClientProcess client = HeroesClientProcess.FromImage(path);
            ClientDiscovery found = ClientDiscovery.Run(client);

            Assert.True(client.Ok);
            Assert.Equal(FakeGlueClient.Base, client.Module.BaseAddress);
            Assert.Equal(FakeGlueClient.ModuleSize, client.Module.Size);
            Assert.Equal(1, client.Module.ProcessId);
            Assert.Equal(new HeroesClientVersion(2, 57, 0, 98348), client.DetectedVersion);
            Assert.True(found.Ok);
            Assert.Equal(new MatchClockDiscovery("pattern", TickRva, SpeedRva, 2), found.Clock);
            Assert.Equal(
                new LoadingScreenDiscovery("pattern", FakeGlueClient.GlobalRva, 3),
                found.Loading
            );
            Assert.Equal(FakeGlueClient.LaunchGlobalRva, found.Menus.LaunchGlobalRva);
            Assert.Empty(found.Menus.MissingFrameClasses);
        }
        finally
        {
            Directory.Delete(folder, true);
        }
    }

    [Fact]
    public void FromImage_AnImageWithoutAVersion_StillDiscovers()
    {
        // The version is optional: an image without a FileVersion string reads as no version.
        string folder = TempFolder();
        try
        {
            string path = Path.Combine(folder, "image.dmp");
            File.WriteAllBytes(path, SavedImage(null));

            using HeroesClientProcess client = HeroesClientProcess.FromImage(path);
            ClientDiscovery found = ClientDiscovery.Run(
                client,
                clientVersion: new HeroesClientVersion(2, 57, 0, 98348)
            );

            Assert.True(client.Ok);
            Assert.Null(client.DetectedVersion);
            Assert.Null(found.ClientVersion);
            Assert.True(found.Ok);
        }
        finally
        {
            Directory.Delete(folder, true);
        }
    }

    [Fact]
    public void FromImage_NeverThrows()
    {
        string folder = TempFolder();
        try
        {
            string small = Path.Combine(folder, "small.dmp");
            File.WriteAllBytes(small, new byte[100]);
            string pe32 = Path.Combine(folder, "pe32.dmp");
            byte[] image = SavedImage("2.57.0.98348");
            BitConverter.GetBytes((ushort)0x10B).CopyTo(image, 0x98);
            File.WriteAllBytes(pe32, image);

            Assert.Equal("no-image", HeroesClientProcess.FromImage(null).Reason);
            Assert.Equal(
                "no-image",
                HeroesClientProcess.FromImage(Path.Combine(folder, "missing.dmp")).Reason
            );
            Assert.Equal("bad-image", HeroesClientProcess.FromImage(small).Reason);
            Assert.Equal("bad-image", HeroesClientProcess.FromImage(pe32).Reason);
            Assert.False(HeroesClientProcess.FromImage(small).Ok);
        }
        finally
        {
            Directory.Delete(folder, true);
        }
    }

    [Fact]
    public void FileVersion_IsTheVersionResourcesString()
    {
        // VS_VERSIONINFO String: wLength, wValueLength, wType, the key, then the value at the next
        // 4-byte boundary. The fixed file info cannot hold 98348 (16-bit fields).
        byte[] bytes = new byte[0x200];
        WriteVersion(bytes, 0x100, "2.57.0.98348");
        Encoding.Unicode.GetBytes("FileVersion\0").CopyTo(bytes, 0x40); // a key with no version

        Assert.Equal("2.57.0.98348", ModuleImage.FileVersionOf(bytes));
        Assert.Null(ModuleImage.FileVersionOf(new byte[0x200]));
    }

    private const long TickRva = 0x32000;
    private const long SpeedRva = 0x32010;

    /// <summary>
    /// What Save-ModuleImage.ps1 would save from a client like <see cref="FakeGlueClient"/>: its
    /// module page by page with the heap left out, x64 headers with the runtime base, two clock
    /// sites, and the version resource's FileVersion string (none when null).
    /// </summary>
    private static byte[] SavedImage(string version)
    {
        var client = new FakeGlueClient();
        client.AddLaunchManager(0);
        client.AddPanels();
        client.AddLoadingScreen(0x7A, 0x72);
        foreach (
            string dialog in ClientScreen.FrameClassNames.Where(name => name.EndsWith("Dialog"))
        )
        {
            client.AddDialog(dialog, 0x72);
        }

        byte[] image = new byte[FakeGlueClient.ModuleSize];
        for (int page = 0; page < image.Length; page += 0x1000)
        {
            client.TryRead(FakeGlueClient.Base + page, image.AsSpan(page, 0x1000));
        }

        BitConverter.GetBytes((ushort)0x20B).CopyTo(image, 0x98);
        BitConverter.GetBytes(FakeGlueClient.Base).CopyTo(image, 0x98 + 24);
        WriteClockSite(image, 0x8000);
        WriteClockSite(image, 0x8100);
        if (version != null)
        {
            WriteVersion(image, 0x3F000, version);
        }

        return image;
    }

    /// <summary>`test al,al; jz; movd xmm0,[tick]; cvtdq2ps; mulss xmm0,[speed]`.</summary>
    private static void WriteClockSite(byte[] image, int rva)
    {
        byte[] site = ClientScreenTests.Hex(
            "84 C0 74 19 66 0F 6E 05 00 00 00 00 0F 5B C0 F3 0F 59 05 00 00 00 00"
        );
        BitConverter.GetBytes((int)(TickRva - (rva + 12))).CopyTo(site, 8);
        BitConverter.GetBytes((int)(SpeedRva - (rva + 23))).CopyTo(site, 19);
        site.CopyTo(image, rva);
    }

    private static void WriteVersion(byte[] bytes, int at, string version)
    {
        Encoding.Unicode.GetBytes("FileVersion\0").CopyTo(bytes, at + 6);
        Encoding.Unicode.GetBytes(version + "\0").CopyTo(bytes, at + 32);
    }

    private static string TempFolder()
    {
        string folder = Path.Combine(Path.GetTempPath(), "heroesclientsdk-" + Guid.NewGuid());
        Directory.CreateDirectory(folder);
        return folder;
    }
}
