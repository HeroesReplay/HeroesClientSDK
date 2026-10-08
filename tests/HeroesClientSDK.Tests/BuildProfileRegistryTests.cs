using Xunit;

namespace HeroesClientSDK.Tests;

[Trait(TestCategories.Category, TestCategories.Unit)]
public class BuildProfileRegistryTests
{
    private static readonly HeroesClientVersion Current = new(2, 57, 0, 98348);
    private static readonly HeroesClientVersion Previous = new(2, 57, 0, 98304);

    [Fact]
    public void Resolve_ExactBuildThenPatchLineThenFallback()
    {
        var exact = new BuildProfile { Name = "exact" };
        var line = new BuildProfile { Name = "line" };
        BuildProfileRegistry profiles = new BuildProfileRegistry()
            .WithPatchLine("2.57", line)
            .WithBuild(Current, exact);

        Assert.Same(exact, profiles.Resolve(Current));
        Assert.Same(line, profiles.Resolve(Previous));
        Assert.Same(profiles.Fallback, profiles.Resolve(new HeroesClientVersion(2, 58, 0, 1)));
        Assert.Same(BuildProfile.Generic, profiles.Fallback);
    }

    [Fact]
    public void Resolve_NoVersion_IsTheFallback()
    {
        BuildProfile none = BuildProfileRegistry.Default.Resolve(null);

        Assert.Same(BuildProfile.Generic, none);
        Assert.Null(none.FixedClock);
        Assert.Equal(LoadingScreenLayout.Default, none.LoadingScreen);
    }

    [Fact]
    public void Default_HasOnly98025sFixedClock()
    {
        BuildProfile build98025 = BuildProfileRegistry.Default.Resolve(
            new HeroesClientVersion(2, 55, 17, 98025)
        );

        Assert.Equal(
            new MatchClockAddresses(MatchTickClock.MatchTickRva, MatchTickClock.GameSpeedFactorRva),
            build98025.FixedClock
        );
        Assert.Null(BuildProfileRegistry.Default.Resolve(Current).FixedClock);
        Assert.Null(
            BuildProfileRegistry
                .Default.Resolve(new HeroesClientVersion(2, 55, 17, 97771))
                .FixedClock
        );
    }

    [Fact]
    public void With_ReturnsANewRegistryAndLeavesTheOriginalAlone()
    {
        // No static or shared setting: a registry another reader holds never changes.
        BuildProfileRegistry before = BuildProfileRegistry.Default;
        BuildProfileRegistry after = before
            .WithBuild(Current, new BuildProfile { Name = "mine" })
            .WithPatchLine("2.57", new BuildProfile { Name = "line" });

        Assert.Equal("mine", after.Resolve(Current).Name);
        Assert.Equal("line", after.Resolve(Previous).Name);
        Assert.Same(BuildProfile.Generic, before.Resolve(Current));
        Assert.Same(BuildProfile.Generic, before.Resolve(Previous));
    }

    [Fact]
    public void LoadingScreenLayout_DefaultIsTheMeasuredOne()
    {
        Assert.Equal(0x218, LoadingScreenLayout.Default.ScreenOffset);
        Assert.Equal(72, LoadingScreenLayout.Default.FlagsOffset);
        Assert.Equal(0, LoadingScreenLayout.Default.LoadingBit);
    }
}
