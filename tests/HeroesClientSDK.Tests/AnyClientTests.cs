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
        using var clock = new MatchClock();
        using var screens = new LoadingScreen();
        using var menus = new ClientScreen();

        MatchClockSample sample = clock.Read((Process)null);
        LoadingScreenSample screen = screens.Read((Process)null);
        ClientScreenSample menu = menus.Read((Process)null);

        Assert.False(sample.Ok);
        Assert.Equal("no-process", sample.Reason);
        Assert.Equal(LoadingScreenKind.Unknown, screen.Screen);
        Assert.False(screen.Ok);
        Assert.Equal("no-process", screen.Reason);
        Assert.False(menu.Ok);
        Assert.Equal("no-process", menu.Reason);
    }

    [Fact]
    public void Read_ANullClient_ReportsNoProcess()
    {
        using var clock = new MatchClock();
        using var screens = new LoadingScreen();
        using var menus = new ClientScreen();

        Assert.Equal("no-process", clock.Read((HeroesClientProcess)null).Reason);
        Assert.Equal("no-process", screens.Read((HeroesClientProcess)null).Reason);
        Assert.Equal("no-process", menus.Read((HeroesClientProcess)null).Reason);
    }

    [Fact]
    public void Read_AProcessThatIsNotAHeroesBuild_ReportsWhyAndDoesNotThrow()
    {
        // The test host is a real process with a real module, but not a Heroes build.
        using Process self = Process.GetCurrentProcess();
        using var clock = new MatchClock();
        using var screens = new LoadingScreen();
        using var menus = new ClientScreen();

        MatchClockSample sample = clock.Read(self);
        LoadingScreenSample screen = screens.Read(self);
        ClientScreenSample menu = menus.Read(self, new HeroesClientVersion(2, 57, 0, 98348));

        Assert.False(sample.Ok);
        Assert.False(string.IsNullOrEmpty(sample.Reason));
        Assert.Equal(LoadingScreenKind.Unknown, screen.Screen);
        Assert.False(string.IsNullOrEmpty(screen.Reason));
        Assert.False(menu.Ok);
        Assert.False(string.IsNullOrEmpty(menu.Reason));
    }

    [Fact]
    public void Attach_TheTestHost_IsOkWithItsModule()
    {
        using Process self = Process.GetCurrentProcess();

        using HeroesClientProcess client = HeroesClientProcess.Attach(self);

        Assert.True(client.Ok, client.Reason);
        Assert.Equal("ok", client.Reason);
        Assert.Equal(self.Id, client.Module.ProcessId);
        Assert.True(client.Module.BaseAddress > 0);
        Assert.True(client.Module.Size > 0);
        Assert.NotEqual(0, client.Module.StartedAt);
        Assert.True(client.IsProcess(self));
    }

    [Fact]
    public void Attach_NoProcess_ReportsNoProcess()
    {
        using HeroesClientProcess client = HeroesClientProcess.Attach(null);

        Assert.False(client.Ok);
        Assert.Equal("no-process", client.Reason);
        Assert.Null(client.DetectedVersion);
    }

    [Fact]
    public void Attach_AnExitedProcess_ReportsNoProcess()
    {
        using Process exited = Process.Start(
            new ProcessStartInfo("cmd.exe", "/c exit 0")
            {
                CreateNoWindow = true,
                UseShellExecute = false,
            }
        );
        exited.WaitForExit();

        using HeroesClientProcess client = HeroesClientProcess.Attach(exited);

        Assert.False(client.Ok);
        Assert.Equal("no-process", client.Reason);
    }

    [Fact]
    public void Attach_AModuleThatCannotBeReadYet_ReportsNoModule()
    {
        // HeroesClientSDK#1: StableMatchClock said "unsupported-build" here and the screen readers
        // said "no-process". The process is running; only its module list cannot be read yet,
        // as on a client that has just started.
        using Process self = Process.GetCurrentProcess();

        using HeroesClientProcess client = HeroesClientProcess.Attach(
            self,
            _ => throw new System.ComponentModel.Win32Exception(299)
        );

        Assert.False(client.Ok);
        Assert.Equal("no-module", client.Reason);
    }

    [Fact]
    public void Attach_AModuleWithoutABase_ReportsNoModule()
    {
        using Process self = Process.GetCurrentProcess();

        using HeroesClientProcess client = HeroesClientProcess.Attach(self, _ => default);

        Assert.False(client.Ok);
        Assert.Equal("no-module", client.Reason);
    }

    [Fact]
    public void Attach_AnUnreadableFileVersion_StillAttachesWithoutAVersion()
    {
        // The version is optional: the readers find everything by pattern without it.
        using Process self = Process.GetCurrentProcess();

        using HeroesClientProcess client = HeroesClientProcess.Attach(
            self,
            _ => new HeroesClientProcess.MainModule(0x140000000, 0x1000, null)
        );

        Assert.True(client.Ok);
        Assert.Null(client.DetectedVersion);
    }
}
