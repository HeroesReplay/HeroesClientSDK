using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace HeroesClientSDK.Tests;

/// <summary>
/// The score screen's Storm League result (HeroesClientSDK#19): the end-of-game record that
/// <c>CScreenScore</c> keeps, in the layout found in the 2.57.0.98348 code on 2026-10-09. No ranked
/// game has been captured yet, so the records here are synthetic: the made-up ranks and points of
/// a game, laid out as <c>CPlayerRewardsPanel::SetData</c> and the record's getters read them.
/// </summary>
[Trait(TestCategories.Category, TestCategories.Unit)]
public class MatchRankTests
{
    // The record's raw leagues (fn 0x1EF6A10): 0 Bronze to 6 Grandmaster.
    private const int Gold = 2;
    private const int Master = 5;
    private const int Grandmaster = 6;

    [Fact]
    public void TryDecode_ARankedStanding()
    {
        Assert.True(MatchRank.TryDecode(Bytes(Ranked(Gold, 3, 312)), out RankStanding standing));

        Assert.Equal(new RankStanding(RankLeague.Gold, 3, 312), standing);
        Assert.False(standing.Placement);
        Assert.Equal("Gold 3 (312)", standing.ToString());
    }

    [Fact]
    public void TryDecode_APlacementStanding()
    {
        Assert.True(MatchRank.TryDecode(Bytes(Placing(3)), out RankStanding standing));

        Assert.Equal(RankLeague.Placement, standing.League);
        Assert.True(standing.Placement);
        Assert.Equal(3, standing.PlacementGames);
        Assert.Equal(0, standing.Division);
        Assert.Equal("Placement (3)", standing.ToString());
    }

    [Fact]
    public void TryDecode_AGrandmasterLadderPosition_OnlyInGrandmaster()
    {
        Assert.True(
            MatchRank.TryDecode(
                Bytes(Ranked(Grandmaster, 1, 1500, ladder: 42)),
                out RankStanding gm
            )
        );
        Assert.True(
            MatchRank.TryDecode(Bytes(Ranked(Master, 1, 900, ladder: 42)), out RankStanding master)
        );

        Assert.Equal(RankLeague.Grandmaster, gm.League);
        Assert.Equal(42, gm.LadderPosition);
        Assert.Equal("Grandmaster #42 (1500)", gm.ToString());
        Assert.Equal(RankLeague.Master, master.League);
        Assert.Null(master.LadderPosition);
    }

    [Fact]
    public void TryDecode_APromotionKeepsItsValue_ADemotionDoesNot()
    {
        Assert.True(
            MatchRank.TryDecode(
                Bytes(Ranked(Gold, 1, 600, phase: 1, phaseValue: 20)),
                out RankStanding up
            )
        );
        Assert.True(
            MatchRank.TryDecode(
                Bytes(Ranked(Gold, 5, 0, phase: 2, phaseValue: 20)),
                out RankStanding down
            )
        );

        Assert.Equal(RankPhase.Promotion, up.Phase);
        Assert.Equal(20, up.PhaseValue);
        Assert.Equal(RankPhase.Demotion, down.Phase);
        Assert.Equal(0, down.PhaseValue);
    }

    [Theory]
    [InlineData(2, Gold, 0)] // an unknown variant
    [InlineData(1, 7, 0)] // a league past Grandmaster
    [InlineData(1, -1, 0)]
    [InlineData(1, Gold, 3)] // an unknown phase
    public void TryDecode_BytesThatAreNotARank(int variant, int league, int phase)
    {
        int[] values = Ranked(league, 3, 312, phase);
        values[0] = variant;

        Assert.False(MatchRank.TryDecode(Bytes(values), out RankStanding standing));
        Assert.Null(standing);
    }

    [Fact]
    public void TryDecode_TooFewBytes()
    {
        Assert.False(MatchRank.TryDecode(new byte[39], out _));
    }

    [Fact]
    public void Read_AFreshClient_HasNoResult()
    {
        // 2.57.0.98348 at the home screen (2026-10-09): CScreenScore exists, its record is null.
        var client = new FakeGlueClient();
        client.AddScoreScreen();
        using var rank = new MatchRank();

        MatchRankSample sample = rank.Read(client.Module(80), client);

        Assert.False(sample.Ok);
        Assert.Null(sample.Result);
        Assert.Equal("no-result", sample.Reason);
        Assert.Equal(HeroesClientVersion.TryParse("2.57.0.98348"), sample.ClientVersion);
        Assert.Equal(FakeGlueClient.ScoreScreen, rank.LastScoreScreen);
        Assert.Equal(0, rank.LastRecord);
    }

    [Fact]
    public void Read_WithoutTheScoreScreen()
    {
        var client = new FakeGlueClient();
        using var rank = new MatchRank();

        MatchRankSample sample = rank.Read(client.Module(81), client);

        Assert.Equal("no-score-screen", sample.Reason);
        Assert.Equal(0, rank.LastScoreScreen);
    }

    [Fact]
    public void Read_ARankedGame()
    {
        var client = new FakeGlueClient();
        client.SetRankRecord(
            Record(Ranked(Gold, 3, 312), Ranked(Gold, 2, 54), 142, new[] { 120, 22, 0, 0, 0 })
        );
        using var rank = new MatchRank();

        MatchRankSample sample = rank.Read(client.Module(82), client);

        Assert.True(sample.Ok);
        Assert.Equal("ok", sample.Reason);
        Assert.Equal(
            new RankResult(
                new RankStanding(RankLeague.Gold, 3, 312),
                new RankStanding(RankLeague.Gold, 2, 54),
                142,
                new RankPointsBreakdown(120, 22, 0, 0, 0)
            ),
            sample.Result
        );
        Assert.Equal(FakeGlueClient.RankRecord, rank.LastRecord);
        Assert.Equal((short)0, rank.LastRecordStatus);
        Assert.Equal(0x70, rank.LastRecordBytes.Length);
    }

    [Fact]
    public void Read_ALossOutOfPlacement()
    {
        var client = new FakeGlueClient();
        client.SetRankRecord(
            Record(Placing(4), Ranked(Gold, 4, 180), -35, new[] { -40, 5, 0, 0, 0 })
        );
        using var rank = new MatchRank();

        RankResult result = rank.Read(client.Module(83), client).Result;

        Assert.True(result.Before.Placement);
        Assert.Equal(new RankStanding(RankLeague.Gold, 4, 180), result.After);
        Assert.Equal(-35, result.DeltaPoints);
        Assert.Equal(-40, result.Breakdown.Match);
        Assert.Equal(5, result.Breakdown.Favored);
    }

    [Theory]
    [InlineData(0, 1, 0)] // no league data: not a ranked game
    [InlineData(1, 0, 0)] // no rank result
    [InlineData(1, 1, 1)] // a status the client does not read
    public void Read_ARecordWithoutARank(int ranked, byte hasRank, short status)
    {
        var client = new FakeGlueClient();
        client.SetRankRecord(
            Record(
                Ranked(Gold, 3, 312),
                Ranked(Gold, 2, 54),
                142,
                new[] { 142, 0, 0, 0, 0 },
                ranked,
                hasRank,
                status
            )
        );
        using var rank = new MatchRank();

        MatchRankSample sample = rank.Read(client.Module(84), client);

        Assert.False(sample.Ok);
        Assert.Equal("no-rank", sample.Reason);
        Assert.Equal(FakeGlueClient.RankRecord, rank.LastRecord);
    }

    [Fact]
    public void Read_ARecordWhoseRanksDoNotDecode()
    {
        int[] bad = Ranked(Gold, 3, 312);
        bad[0] = 9;
        var client = new FakeGlueClient();
        client.SetRankRecord(Record(bad, Ranked(Gold, 2, 54), 142, new[] { 142, 0, 0, 0, 0 }));
        using var rank = new MatchRank();

        Assert.Equal("bad-record", rank.Read(client.Module(85), client).Reason);
    }

    [Fact]
    public void Read_TheRecordGoesAway()
    {
        var client = new FakeGlueClient();
        client.SetRankRecord(Record(Ranked(Gold, 3, 312), Ranked(Gold, 2, 54), 142, new int[5]));
        using var rank = new MatchRank();
        ClientModule module = client.Module(86);

        MatchRankSample result = rank.Read(module, client);
        client.SetRankRecord(null);
        MatchRankSample gone = rank.Read(module, client);

        Assert.True(result.Ok);
        Assert.Equal("no-result", gone.Reason);
        Assert.Equal(0, rank.LastRecord);
        Assert.Null(rank.LastRecordBytes);
    }

    [Fact]
    public void Read_AnotherExpectedVersion_IsAMismatchAndStillReads()
    {
        var client = new FakeGlueClient();
        client.SetRankRecord(Record(Ranked(Gold, 3, 312), Ranked(Gold, 2, 54), 142, new int[5]));
        using var rank = new MatchRank();

        MatchRankSample sample = rank.Read(
            client.Module(87),
            client,
            HeroesClientVersion.TryParse("2.57.0.98304")
        );

        Assert.True(sample.Ok);
        Assert.True(sample.VersionMismatch);
        Assert.Equal(HeroesClientVersion.TryParse("2.57.0.98348"), sample.ClientVersion);
    }

    [Fact]
    public void Read_ABuildWithoutTheScreenSites_IsUnsupported()
    {
        var client = new FakeGlueClient(screenSites: 0);
        client.AddScoreScreen();
        using var rank = new MatchRank();

        MatchRankSample sample = rank.Read(client.Module(88), client);

        Assert.Equal("unsupported-build", sample.Reason);
        Assert.Equal(0, rank.GlobalRva);
    }

    [Fact]
    public void Read_ABuildProfileMovesTheRecord()
    {
        // A patch that moves the record needs a registry entry, not a code change.
        var moved = new MatchRankLayout(RecordOffset: 0x268, DeltaOffset: 0x16E0);
        var client = new FakeGlueClient(fileVersion: "2.57.1.99999");
        byte[] record = Record(Ranked(Gold, 3, 312), Ranked(Gold, 2, 54), 0, new int[5]);
        BitConverter.GetBytes(77).CopyTo(record, 0x16E0);
        client.SetRankRecord(record, rank: moved);
        BuildProfileRegistry profiles = BuildProfileRegistry.Default.WithBuild(
            HeroesClientVersion.TryParse("2.57.1.99999"),
            new BuildProfile { Name = "moved", MatchRank = moved }
        );
        using var rank = new MatchRank(new HeroesClientOptions { Profiles = profiles });

        MatchRankSample sample = rank.Read(client.Module(89), client);

        Assert.True(sample.Ok);
        Assert.Equal(77, sample.Result.DeltaPoints);
    }

    [Fact]
    public void Read_SharesTheCodeWalkWithTheScreenReaders()
    {
        var fake = new FakeGlueClient();
        fake.ShowScreens(0x6181);
        fake.SetRankRecord(Record(Ranked(Gold, 3, 312), Ranked(Gold, 2, 54), 142, new int[5]));
        using HeroesClientProcess client = HeroesClientProcess.FromMemory(fake, fake.Module(90));
        using var screens = new ClientScreen();
        using var rank = new MatchRank();

        ClientScreenSample screen = screens.Read(client);
        MatchRankSample sample = rank.Read(client);

        Assert.Equal(ClientScreenKind.Home, screen.Screen);
        Assert.True(sample.Ok);
        Assert.Equal(1, client.ScreenScans.Walks);
    }

    [Fact]
    public void Read_AClientThatIsNotAttached()
    {
        using var rank = new MatchRank();

        MatchRankSample sample = rank.Read((HeroesClientProcess)null);

        Assert.False(sample.Ok);
        Assert.Equal("no-process", sample.Reason);
    }

    [Fact]
    public void Poll_RaisesEachResultOnce()
    {
        DateTimeOffset now = new(2026, 10, 9, 20, 0, 0, TimeSpan.Zero);
        var client = new FakeGlueClient();
        client.AddScoreScreen();
        using var watcher = new MatchRankWatcher(options: TestTime.Options(() => now));
        var events = new List<MatchRankEventArgs>();
        watcher.ResultAvailable += (_, e) => events.Add(e);
        var running = new List<(int, Func<MatchRank, MatchRankSample>)>
        {
            (91, rank => rank.Read(client.Module(91), client)),
        };

        IReadOnlyList<MatchRankEventArgs> home = watcher.Poll(running);
        client.SetRankRecord(Record(Ranked(Gold, 3, 312), Ranked(Gold, 2, 54), 142, new int[5]));
        IReadOnlyList<MatchRankEventArgs> score = watcher.Poll(running);
        IReadOnlyList<MatchRankEventArgs> again = watcher.Poll(running);
        client.SetRankRecord(null);
        IReadOnlyList<MatchRankEventArgs> cleared = watcher.Poll(running);
        client.SetRankRecord(Record(Ranked(Gold, 3, 312), Ranked(Gold, 2, 54), 142, new int[5]));
        IReadOnlyList<MatchRankEventArgs> back = watcher.Poll(running);
        now = now.AddMinutes(25);
        client.SetRankRecord(
            Record(Ranked(Gold, 2, 54), Ranked(Gold, 2, 0), -54, new[] { -54, 0, 0, 0, 0 }),
            FakeGlueClient.RankRecord + 0x10000
        );
        IReadOnlyList<MatchRankEventArgs> next = watcher.Poll(running);

        Assert.Empty(home);
        MatchRankEventArgs first = Assert.Single(score);
        Assert.Empty(again);
        Assert.Empty(cleared);
        Assert.Empty(back);
        MatchRankEventArgs second = Assert.Single(next);
        Assert.Equal(new[] { first, second }, events);
        Assert.Equal(91, first.ProcessId);
        Assert.Equal(142, first.Result.DeltaPoints);
        Assert.Equal(HeroesClientVersion.TryParse("2.57.0.98348"), first.ClientVersion);
        Assert.Equal(new DateTimeOffset(2026, 10, 9, 20, 0, 0, TimeSpan.Zero), first.ObservedAt);
        Assert.Equal(-54, second.Result.DeltaPoints);
        Assert.Equal(now, second.ObservedAt);
    }

    [Fact]
    public void Poll_TheSameRecordWithNewValues_IsANewResult()
    {
        var client = new FakeGlueClient();
        client.SetRankRecord(Record(Ranked(Gold, 3, 312), Ranked(Gold, 2, 54), 142, new int[5]));
        using var watcher = new MatchRankWatcher();
        var running = new List<(int, Func<MatchRank, MatchRankSample>)>
        {
            (92, rank => rank.Read(client.Module(92), client)),
        };

        IReadOnlyList<MatchRankEventArgs> first = watcher.Poll(running);
        client.SetRankRecord(Record(Ranked(Gold, 2, 54), Ranked(Gold, 2, 90), 36, new int[5]));
        IReadOnlyList<MatchRankEventArgs> second = watcher.Poll(running);

        Assert.Single(first);
        Assert.Equal(36, Assert.Single(second).Result.DeltaPoints);
    }

    [Fact]
    public void Poll_EachClientOnItsOwn_AndAClientThatComesBackIsNew()
    {
        var one = new FakeGlueClient();
        var two = new FakeGlueClient();
        one.SetRankRecord(Record(Ranked(Gold, 3, 312), Ranked(Gold, 2, 54), 142, new int[5]));
        two.SetRankRecord(Record(Placing(1), Placing(2), 0, new int[5]));
        using var watcher = new MatchRankWatcher();
        (int, Func<MatchRank, MatchRankSample>) First() =>
            (93, rank => rank.Read(one.Module(93), one));
        (int, Func<MatchRank, MatchRankSample>) Second() =>
            (94, rank => rank.Read(two.Module(94), two));

        IReadOnlyList<MatchRankEventArgs> both = watcher.Poll(new[] { First(), Second() });
        IReadOnlyList<MatchRankEventArgs> onlySecond = watcher.Poll(new[] { Second() });
        IReadOnlyList<MatchRankEventArgs> firstBack = watcher.Poll(new[] { First(), Second() });

        Assert.Equal(new[] { 93, 94 }, new[] { both[0].ProcessId, both[1].ProcessId });
        Assert.Empty(onlySecond);
        Assert.Equal(93, Assert.Single(firstBack).ProcessId);
    }

    [Fact]
    public async Task RunAsync_ReturnsWhenCancelled()
    {
        using var watcher = new MatchRankWatcher(TimeSpan.FromMilliseconds(10));
        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();

        await watcher.RunAsync(cancelled.Token);

        Assert.Equal(TimeSpan.FromMilliseconds(10), watcher.Interval);
    }

    [Fact]
    public void Constructor_RejectsAnIntervalThatIsNotPositive()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new MatchRankWatcher(TimeSpan.Zero));
    }

    /// <summary>A ranked standing as the record keeps it (40 bytes, ten 32-bit values).</summary>
    internal static int[] Ranked(
        int league,
        int division,
        int points,
        int phase = 0,
        int phaseValue = 0,
        int? ladder = null
    ) =>
        new[]
        {
            1,
            league,
            division,
            points,
            phase,
            phaseValue,
            0,
            0,
            ladder is null ? 0 : 1,
            ladder ?? 0,
        };

    /// <summary>A placement standing: variant 0 and the placement value.</summary>
    internal static int[] Placing(int games) => new[] { 0, games, 0, 0, 0, 0, 0, 0, 0, 0 };

    /// <summary>
    /// An end-of-game record in <see cref="MatchRankLayout.Default"/>: the ranks, the change, the
    /// breakdown, and the flags that say it holds a rank result.
    /// </summary>
    internal static byte[] Record(
        int[] before,
        int[] after,
        int delta,
        int[] breakdown,
        int ranked = 1,
        byte hasRank = 1,
        short status = 0
    )
    {
        MatchRankLayout layout = MatchRankLayout.Default;
        byte[] record = new byte[0x2100];
        BitConverter.GetBytes(ranked).CopyTo(record, (int)layout.RankedOffset);
        record[layout.HasRankOffset] = hasRank;
        Bytes(before).CopyTo(record, (int)layout.BeforeOffset);
        Bytes(after).CopyTo(record, (int)layout.AfterOffset);
        BitConverter.GetBytes(delta).CopyTo(record, (int)layout.DeltaOffset);
        Bytes(breakdown).CopyTo(record, (int)layout.BreakdownOffset);
        BitConverter.GetBytes(status).CopyTo(record, (int)layout.StatusOffset);
        return record;
    }

    private static byte[] Bytes(int[] values)
    {
        byte[] bytes = new byte[values.Length * 4];
        Buffer.BlockCopy(values, 0, bytes, 0, bytes.Length);
        return bytes;
    }
}
