using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace HeroesClientSDK.Tests;

[Trait(TestCategories.Category, TestCategories.Unit)]
public class ReplaySeekerTests
{
    [Fact]
    public async Task SeekAsync_Forward_RunsAtX8ThenStopsOnTheSecondAtNormal()
    {
        var replay = new FakeReplay { Time = TimeSpan.FromSeconds(30) };

        ReplaySeekResult result = await replay.Seeker().SeekAsync(TimeSpan.FromMinutes(10));

        Assert.True(result.Ok);
        Assert.Equal("reached", result.Reason);
        Assert.Equal(TimeSpan.FromSeconds(30), result.From);
        Assert.InRange(replay.Time.TotalSeconds, 600, 601);
        Assert.Equal(8, replay.TopSpeed);
        Assert.Equal(1, replay.Speed);
        Assert.False(replay.Paused);
        // 9:30 of match time at x8, then 6 s at Normal: about 77 s, far below real time.
        Assert.InRange(replay.Elapsed.TotalSeconds, 70, 90);
    }

    [Fact]
    public async Task SeekAsync_Pause_PressesPOnArrival()
    {
        var replay = new FakeReplay { Time = TimeSpan.FromMinutes(5) };

        ReplaySeekResult result = await replay
            .Seeker()
            .SeekAsync(TimeSpan.FromMinutes(6), pause: true);

        Assert.True(result.Ok);
        Assert.True(replay.Paused);
        Assert.InRange(replay.Time.TotalSeconds, 360, 361);
    }

    [Fact]
    public async Task SeekAsync_CloseTarget_StaysAtNormal()
    {
        var replay = new FakeReplay { Time = TimeSpan.FromSeconds(100) };

        ReplaySeekResult result = await replay.Seeker().SeekAsync(TimeSpan.FromSeconds(104));

        Assert.True(result.Ok);
        Assert.Equal(1, replay.TopSpeed);
        Assert.InRange(replay.Time.TotalSeconds, 104, 105);
    }

    [Fact]
    public async Task SeekAsync_TargetBehindTheMatch_IsBehindTargetAndSendsNoKeys()
    {
        var replay = new FakeReplay { Time = TimeSpan.FromMinutes(13) };

        ReplaySeekResult result = await replay.Seeker().SeekAsync(TimeSpan.FromMinutes(5));

        Assert.False(result.Ok);
        Assert.Equal("behind-target", result.Reason);
        Assert.Equal(TimeSpan.FromMinutes(13), result.From);
        Assert.Empty(replay.Sent);
    }

    [Fact]
    public async Task SeekAsync_WithinTolerance_IsReachedAtOnce()
    {
        var replay = new FakeReplay { Time = TimeSpan.FromSeconds(301) };

        ReplaySeekResult result = await replay.Seeker().SeekAsync(TimeSpan.FromSeconds(300));

        Assert.True(result.Ok);
        Assert.Empty(replay.Sent);
    }

    [Fact]
    public async Task SeekAsync_PausedMatch_PressesPlayOnceThenSeeks()
    {
        var replay = new FakeReplay { Time = TimeSpan.FromSeconds(48), Paused = true };

        ReplaySeekResult result = await replay.Seeker().SeekAsync(TimeSpan.FromMinutes(2));

        Assert.True(result.Ok);
        Assert.Equal(ReplayKey.Pause, replay.Sent[0]);
        Assert.Equal(1, replay.Sent.FindAll(key => key == ReplayKey.Pause).Count);
        Assert.InRange(replay.Time.TotalSeconds, 120, 121);
    }

    [Fact]
    public async Task SeekAsync_PastTheReplaysEnd_IsLeftMatchAtNormalSpeed()
    {
        var replay = new FakeReplay { Time = TimeSpan.Zero, Length = TimeSpan.FromSeconds(866) };

        ReplaySeekResult result = await replay.Seeker().SeekAsync(TimeSpan.FromMinutes(20));

        Assert.False(result.Ok);
        Assert.Equal("left-match", result.Reason);
        Assert.Equal(1, replay.Speed);
    }

    [Fact]
    public async Task SeekAsync_NoMatch_GivesUpAfterTheMatchTimeout()
    {
        var replay = new FakeReplay { Screen = ClientScreenKind.Home };

        ReplaySeekResult result = await replay.Seeker().SeekAsync(TimeSpan.FromMinutes(1));

        Assert.False(result.Ok);
        Assert.Equal("no-match", result.Reason);
        Assert.Null(result.From);
        Assert.InRange(replay.Elapsed.TotalMinutes, 3, 3.01);
    }

    [Fact]
    public async Task SeekAsync_NoWindow_IsNoWindow()
    {
        var replay = new FakeReplay { Time = TimeSpan.Zero, HasWindow = false };

        ReplaySeekResult result = await replay.Seeker().SeekAsync(TimeSpan.FromMinutes(1));

        Assert.False(result.Ok);
        Assert.Equal("no-window", result.Reason);
    }

    [Theory]
    [InlineData(ReplayKey.SpeedUp, new ushort[] { 0x6B })]
    [InlineData(ReplayKey.SpeedDown, new ushort[] { 0x6D })]
    [InlineData(ReplayKey.Pause, new ushort[] { 0x50 })]
    [InlineData(ReplayKey.Escape, new ushort[] { 0x1B })]
    [InlineData(ReplayKey.QuitReplay, new ushort[] { 0x51 })]
    [InlineData(ReplayKey.ToggleObserverTools, new ushort[] { 0x11, 0x10, 0x4F })]
    [InlineData(ReplayKey.Player1, new ushort[] { 0x31 })]
    [InlineData(ReplayKey.Player9, new ushort[] { 0x39 })]
    [InlineData(ReplayKey.Player10, new ushort[] { 0x30 })]
    public void Chord_IsTheClientsHotkey(ReplayKey key, ushort[] chord)
    {
        Assert.Equal(chord, WindowReplayKeys.Chord(key));
    }

    [Theory]
    [InlineData(1, ReplayKey.Player1)]
    [InlineData(6, ReplayKey.Player6)]
    [InlineData(10, ReplayKey.Player10)]
    public void Player_MapsOneToTen(int player, ReplayKey key)
    {
        Assert.Equal(key, WindowReplayKeys.Player(player));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(11)]
    public void Player_OutOfRange_Throws(int player)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => WindowReplayKeys.Player(player));
    }

    /// <summary>
    /// A replay on a fake clock: every wait moves real time, and the match by real time times the
    /// speed. The speed keys walk the client's ladder (Normal, Faster, x2, x4, x8), P pauses, and
    /// a paused clock reads "stalled" as <see cref="MatchClock"/> does.
    /// </summary>
    private sealed class FakeReplay : IReplayKeys
    {
        private static readonly double[] Ladder = [1, 1.4, 2, 4, 8];
        private int step;
        private DateTimeOffset now = new(2026, 10, 26, 20, 0, 0, TimeSpan.Zero);
        private readonly DateTimeOffset start;

        public FakeReplay() => start = now;

        public TimeSpan Time { get; set; }
        public TimeSpan Length { get; set; } = TimeSpan.FromMinutes(30);
        public bool Paused { get; set; }
        public bool HasWindow { get; set; } = true;
        public ClientScreenKind Screen { get; set; } = ClientScreenKind.Match;
        public double Speed => Ladder[step];
        public double TopSpeed { get; private set; } = 1;
        public List<ReplayKey> Sent { get; } = [];
        public TimeSpan Elapsed => now - start;

        public ReplaySeeker Seeker() =>
            new(
                this,
                Read,
                new ReplaySeekOptions
                {
                    TimeProvider = new TestTime(() => now),
                    Delay = Wait,
                }
            );

        public bool Send(ReplayKey key)
        {
            if (!HasWindow)
            {
                return false;
            }

            Sent.Add(key);
            switch (key)
            {
                case ReplayKey.SpeedUp:
                    step = Math.Min(step + 1, Ladder.Length - 1);
                    TopSpeed = Math.Max(TopSpeed, Speed);
                    break;
                case ReplayKey.SpeedDown:
                    step = Math.Max(step - 1, 0);
                    break;
                case ReplayKey.Pause:
                    Paused = !Paused;
                    break;
            }

            return true;
        }

        private ReplayPosition Read() =>
            Screen != ClientScreenKind.Match ? new ReplayPosition(Screen, null, "near-zero")
            : Paused ? new ReplayPosition(Screen, null, "stalled")
            : new ReplayPosition(Screen, Time, "ok");

        private Task Wait(TimeSpan wait, CancellationToken cancellationToken)
        {
            now += wait;
            if (Screen == ClientScreenKind.Match && !Paused)
            {
                Time += wait * Speed;
                if (Time >= Length)
                {
                    Time = Length;
                    Screen = ClientScreenKind.Awards;
                }
            }

            return Task.CompletedTask;
        }
    }
}
