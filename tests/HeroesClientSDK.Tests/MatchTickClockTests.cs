using Xunit;

namespace HeroesClientSDK.Tests;

[Trait(TestCategories.Category, TestCategories.Unit)]
public class MatchTickClockTests
{
    private const float Scale = 1f / 4096f;

    [Fact]
    public void TrySeconds_OneSecondIs4096Ticks()
    {
        Assert.True(MatchTickClock.TrySeconds(4096, Scale, out double seconds));
        Assert.Equal(1, seconds, precision: 2);
    }

    [Fact]
    public void TrySeconds_KeepsThePreGameSign()
    {
        Assert.True(MatchTickClock.TrySeconds(-4096 * 7, Scale, out double seconds));
        Assert.Equal(-7, seconds, precision: 2);
    }

    [Theory]
    [InlineData(0f)]
    [InlineData(-1f)]
    public void TrySeconds_RejectsANonPositiveScale(float speed)
    {
        Assert.False(MatchTickClock.TrySeconds(4096, speed, out _));
    }

    [Fact]
    public void TrySeconds_RejectsAValuePastNinetyMinutes()
    {
        int ticks = 4096 * (90 * 60 + 1);
        Assert.False(MatchTickClock.TrySeconds(ticks, Scale, out _));
    }

    [Theory]
    [InlineData("2.55.17.98025", true)]
    [InlineData("2.55.17.97771", false)]
    [InlineData("", false)]
    public void FixedClock_IsOnlyBuild98025(string version, bool supported)
    {
        BuildProfile profile = BuildProfileRegistry.Default.Resolve(
            HeroesClientVersion.TryParse(version)
        );
        Assert.Equal(supported, profile.FixedClock.HasValue);
    }

    [Fact]
    public void Rvas_MatchThe98025GhidraNotes()
    {
        Assert.Equal(
            new MatchClockAddresses(0x338D4A4, 0x264862C),
            BuildProfileRegistry
                .Default.Resolve(new HeroesClientVersion(2, 55, 17, 98025))
                .FixedClock
        );
        Assert.Equal(0x338D4A4, MatchTickClock.MatchTickRva);
        Assert.Equal(0x264862C, MatchTickClock.GameSpeedFactorRva);
    }
}
