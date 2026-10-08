using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Xunit;

namespace HeroesClientSDK.Tests;

[Trait(TestCategories.Category, TestCategories.Unit)]
public class MatchClockTests
{
    private const long ModuleBase = 0x140000000L;
    private const long SectionRva = 0x1000;
    private const int SectionSize = 0x100;
    private const long PatternTickRva = 0x9000;
    private const long PatternSpeedRva = 0xA000;
    private const long LargeModule = 0x3400000;
    private const long SmallModule = 0x20000;
    private const float Scale = 1f / 4096f;

    [Fact]
    public void Read_Non98025Version_UsesPatternPath()
    {
        MappedModule memory = MappedModule.WithPattern();
        using MatchClock clock = new MatchClock();
        ClientModule module = Module(11, SmallModule, "2.55.17.97771");
        memory.SetSeconds(PatternTickRva, PatternSpeedRva, 12);

        MatchClockSample first = clock.Read(module, memory);

        Assert.False(first.Ok);
        Assert.False(clock.IsLocked);
        Assert.Equal("confirming", first.Reason);
        memory.SetSeconds(PatternTickRva, PatternSpeedRva, 13);
        MatchClockSample locked = clock.Read(module, memory);

        Assert.True(locked.Ok);
        Assert.True(clock.IsLocked);
        Assert.Equal(13, locked.Seconds, precision: 2);
        Assert.Equal(PatternTickRva, clock.CandidateTickRva);
        Assert.NotEqual(MatchTickClock.MatchTickRva, clock.CandidateTickRva);
        Assert.Equal(0, memory.FixedTickReads);
        Assert.True(memory.WideReads > 0);
    }

    [Fact]
    public void Read_Build98025_PrefersAgreedPatternOverFixedRvas()
    {
        MappedModule memory = MappedModule.WithPattern();
        using MatchClock clock = new MatchClock();
        ClientModule module = Module(12, LargeModule, MatchTickClock.SupportedBuild);
        memory.SetSeconds(PatternTickRva, PatternSpeedRva, 12);
        memory.SetSeconds(MatchTickClock.MatchTickRva, MatchTickClock.GameSpeedFactorRva, 50);

        Assert.False(clock.Read(module, memory).Ok);
        memory.SetSeconds(PatternTickRva, PatternSpeedRva, 13);
        memory.SetSeconds(MatchTickClock.MatchTickRva, MatchTickClock.GameSpeedFactorRva, 51);
        MatchClockSample locked = clock.Read(module, memory);

        Assert.True(locked.Ok);
        Assert.Equal(13, locked.Seconds, precision: 2);
        Assert.Equal(PatternTickRva, clock.CandidateTickRva);
        Assert.Equal(0, memory.FixedTickReads);
    }

    [Fact]
    public void Read_FixedRva_IsValidatedBeforeLock()
    {
        MappedModule memory = MappedModule.Empty();
        using MatchClock clock = new MatchClock();
        ClientModule module = Module(13, LargeModule, MatchTickClock.SupportedBuild);
        memory.SetSeconds(MatchTickClock.MatchTickRva, MatchTickClock.GameSpeedFactorRva, 15);

        MatchClockSample first = clock.Read(module, memory);

        Assert.False(first.Ok);
        Assert.False(clock.IsLocked);
        Assert.Equal("confirming", first.Reason);
        Assert.Equal(15 * 4096, first.Ticks);
        Assert.Equal(MatchTickClock.MatchTickRva, clock.CandidateTickRva);
        Assert.True(memory.WideReads > 0);
        Assert.True(memory.FixedTickReads > 0);
        int scans = memory.WideReads;
        memory.SetSeconds(MatchTickClock.MatchTickRva, MatchTickClock.GameSpeedFactorRva, 16);
        MatchClockSample locked = clock.Read(module, memory);

        Assert.True(locked.Ok);
        Assert.True(clock.IsLocked);
        Assert.Equal(16, locked.Seconds, precision: 2);
        Assert.Equal(scans, memory.WideReads);
    }

    [Fact]
    public void Read_FixedRva_OutsideModule_DoesNotLock()
    {
        MappedModule memory = MappedModule.Empty();
        using MatchClock clock = new MatchClock();
        ClientModule module = Module(14, SmallModule, MatchTickClock.SupportedBuild);
        memory.SetSeconds(MatchTickClock.MatchTickRva, MatchTickClock.GameSpeedFactorRva, 15);
        MatchClockSample first = clock.Read(module, memory);
        int scans = memory.WideReads;
        memory.SetSeconds(MatchTickClock.MatchTickRva, MatchTickClock.GameSpeedFactorRva, 16);
        MatchClockSample second = clock.Read(module, memory);

        Assert.False(first.Ok);
        Assert.False(second.Ok);
        Assert.False(clock.IsLocked);
        Assert.Equal("out-of-range", first.Reason);
        Assert.Equal("out-of-range", second.Reason);
        Assert.Equal(0, memory.FixedTickReads);
        Assert.True(scans > 0);
        Assert.Equal(scans, memory.WideReads);
    }

    [Fact]
    public void Read_IncoherentSample_DoesNotLock()
    {
        MappedModule memory = MappedModule.WithPattern();
        using MatchClock clock = new MatchClock();
        ClientModule module = Module(15, SmallModule, "2.55.17.97771");
        memory.SetSeconds(PatternTickRva, PatternSpeedRva, 30);
        MatchClockSample first = clock.Read(module, memory);
        int scans = memory.WideReads;
        memory.SetSeconds(PatternTickRva, PatternSpeedRva, 10);
        MatchClockSample second = clock.Read(module, memory);

        Assert.False(first.Ok);
        Assert.False(second.Ok);
        Assert.Equal("incoherent", second.Reason);
        Assert.False(clock.IsLocked);
        Assert.True(scans > 0);
        Assert.Equal(scans, memory.WideReads);
    }

    [Fact]
    public void Read_InvalidSample_DoesNotLock()
    {
        MappedModule memory = MappedModule.WithPattern();
        using MatchClock clock = new MatchClock();
        ClientModule module = Module(16, SmallModule, "2.55.17.97771");
        memory.SetSeconds(PatternTickRva, PatternSpeedRva, 12);
        memory.SetSingle(PatternSpeedRva, float.NaN);
        MatchClockSample first = clock.Read(module, memory);
        int scans = memory.WideReads;
        MatchClockSample second = clock.Read(module, memory);

        Assert.False(first.Ok);
        Assert.False(second.Ok);
        Assert.Equal("bad-scale", first.Reason);
        Assert.Equal("bad-scale", second.Reason);
        Assert.False(clock.IsLocked);
        Assert.True(scans > 0);
        Assert.Equal(scans, memory.WideReads);
    }

    [Fact]
    public void Read_CachedFingerprint_DoesNotScanAgain()
    {
        MappedModule memory = MappedModule.WithPattern();
        using MatchClock clock = new MatchClock();
        ClientModule module = Module(17, SmallModule, "2.55.17.97771");
        memory.SetSeconds(PatternTickRva, PatternSpeedRva, 20);
        Assert.False(clock.Read(module, memory).Ok);
        memory.SetSeconds(PatternTickRva, PatternSpeedRva, 21);
        MatchClockSample locked = clock.Read(module, memory);
        Assert.True(locked.Ok);
        int scans = memory.WideReads;

        memory.SetSeconds(PatternTickRva, PatternSpeedRva, 22);
        MatchClockSample again = clock.Read(module, memory);

        Assert.True(again.Ok);
        Assert.True(clock.IsLocked);
        Assert.Equal(22, again.Seconds, precision: 2);
        Assert.Equal(scans, memory.WideReads);
    }

    [Fact]
    public void Read_FailedDiscovery_DoesNotScanAgain()
    {
        MappedModule memory = MappedModule.Empty();
        using MatchClock clock = new MatchClock();
        ClientModule module = Module(18, SmallModule, "2.55.17.97771");
        MatchClockSample first = clock.Read(module, memory);
        int scans = memory.WideReads;
        MatchClockSample second = clock.Read(module, memory);

        Assert.False(first.Ok);
        Assert.False(second.Ok);
        Assert.False(clock.IsLocked);
        Assert.Equal("unsupported-build", first.Reason);
        Assert.Equal("unsupported-build", second.Reason);
        Assert.True(scans > 0);
        Assert.Equal(scans, memory.WideReads);
    }

    [Fact]
    public void Read_ProcessChange_ResetsDiscovery()
    {
        MappedModule memory = MappedModule.WithPattern();
        using MatchClock clock = new MatchClock();
        ClientModule firstProcess = Module(19, SmallModule, "2.55.17.97771");
        memory.SetSeconds(PatternTickRva, PatternSpeedRva, 20);
        Assert.False(clock.Read(firstProcess, memory).Ok);
        memory.SetSeconds(PatternTickRva, PatternSpeedRva, 21);
        Assert.True(clock.Read(firstProcess, memory).Ok);
        Assert.True(clock.IsLocked);
        int scans = memory.WideReads;

        ClientModule nextProcess = Module(20, SmallModule, "2.55.17.97771");
        MatchClockSample restarted = clock.Read(nextProcess, memory);

        Assert.False(restarted.Ok);
        Assert.False(clock.IsLocked);
        Assert.True(memory.WideReads > scans);
    }

    [Fact]
    public void Read_NextMatchInTheSameClient_IsNotStalledByThePreviousMatch()
    {
        // 2026-10-02: after a 24 minute match, every read of the next replay was "stalled"
        // because the previous match's last second stayed the baseline.
        MappedModule memory = MappedModule.WithPattern();
        DateTimeOffset now = new(2026, 10, 2, 19, 0, 0, TimeSpan.Zero);
        using MatchClock clock = new MatchClock(TestTime.Options(() => now));
        ClientModule module = Module(21, SmallModule, "2.57.0.98304");
        memory.SetSeconds(PatternTickRva, PatternSpeedRva, 1440);
        clock.Read(module, memory);
        memory.SetSeconds(PatternTickRva, PatternSpeedRva, 1441);
        Assert.True(clock.Read(module, memory).Ok);

        for (int second = 10; second <= 30; second++)
        {
            now = now.AddSeconds(1);
            memory.SetSeconds(PatternTickRva, PatternSpeedRva, second);
            MatchClockSample sample = clock.Read(module, memory);
            Assert.True(sample.Ok, $"second {second}: {sample.Reason}");
        }
    }

    [Fact]
    public void BeginMatch_ForgetsTheStallBaselineButKeepsTheLock()
    {
        MappedModule memory = MappedModule.WithPattern();
        DateTimeOffset now = new(2026, 10, 2, 19, 0, 0, TimeSpan.Zero);
        using MatchClock clock = new MatchClock(TestTime.Options(() => now));
        ClientModule module = Module(22, SmallModule, "2.57.0.98304");
        memory.SetSeconds(PatternTickRva, PatternSpeedRva, 600);
        clock.Read(module, memory);
        memory.SetSeconds(PatternTickRva, PatternSpeedRva, 601);
        Assert.True(clock.Read(module, memory).Ok);

        clock.BeginMatch();
        now = now.AddSeconds(30);
        memory.SetSeconds(PatternTickRva, PatternSpeedRva, 599);

        Assert.True(clock.Read(module, memory).Ok);
        Assert.True(clock.IsLocked);
    }

    [Fact]
    public void IsRunning_OnlyWhenBothReadsSucceedAndTheSecondIsAhead()
    {
        TimeSpan at = TimeSpan.FromSeconds(42);

        Assert.True(MatchClock.IsRunning(at, at + TimeSpan.FromMilliseconds(250)));
        Assert.False(MatchClock.IsRunning(at, at));
        Assert.False(MatchClock.IsRunning(at, at - TimeSpan.FromSeconds(1)));
        Assert.False(MatchClock.IsRunning(null, at));
        Assert.False(MatchClock.IsRunning(at, null));
        Assert.False(MatchClock.IsRunning(null, null));
    }

    [Fact]
    public void Read_FrozenClockFromTheLastMatch_ReadsButIsNotRunning()
    {
        // The last match's clock can sit at its final second until the next one starts.
        // Each read is ok until the stall window passes, so one read must not start a match.
        MappedModule memory = MappedModule.WithPattern();
        DateTimeOffset now = new(2026, 10, 2, 19, 0, 0, TimeSpan.Zero);
        using MatchClock clock = new MatchClock(TestTime.Options(() => now));
        ClientModule module = Module(23, SmallModule, "2.57.0.98304");
        memory.SetSeconds(PatternTickRva, PatternSpeedRva, 1439);
        clock.Read(module, memory);
        memory.SetSeconds(PatternTickRva, PatternSpeedRva, 1440);
        clock.Read(module, memory);

        clock.BeginMatch();
        now = now.AddSeconds(1);
        MatchClockSample first = clock.Read(module, memory);
        now = now.AddMilliseconds(250);
        MatchClockSample second = clock.Read(module, memory);

        Assert.True(first.Ok);
        Assert.True(second.Ok);
        Assert.False(
            MatchClock.IsRunning(
                TimeSpan.FromSeconds(first.Seconds),
                TimeSpan.FromSeconds(second.Seconds)
            )
        );
    }

    [Fact]
    public void Read_CodeStillUnpacking_IsScannedAgainLater()
    {
        // The client's code is encrypted on disk. Read too early, it has no clock pattern yet.
        MappedModule memory = MappedModule.Empty();
        DateTimeOffset now = new(2026, 10, 2, 22, 0, 0, TimeSpan.Zero);
        using MatchClock clock = new MatchClock(TestTime.Options(() => now));
        ClientModule module = Module(25, SmallModule, "2.57.0.98304");

        Assert.Equal("unsupported-build", clock.Read(module, memory).Reason);
        memory.AddPattern();
        memory.SetSeconds(PatternTickRva, PatternSpeedRva, 30);
        Assert.Equal("unsupported-build", clock.Read(module, memory).Reason);

        now = now.AddSeconds(11);
        clock.Read(module, memory);
        now = now.AddSeconds(1);
        memory.SetSeconds(PatternTickRva, PatternSpeedRva, 31);

        Assert.True(clock.Read(module, memory).Ok);
        Assert.Equal(PatternTickRva, clock.CandidateTickRva);
    }

    [Fact]
    public void Read_MenuZero_IsNotAMatchClock()
    {
        MappedModule memory = MappedModule.WithPattern();
        using MatchClock clock = new MatchClock();
        ClientModule module = Module(24, SmallModule, "2.57.0.98304");
        memory.SetSeconds(PatternTickRva, PatternSpeedRva, 0);

        Assert.Equal("near-zero", clock.Read(module, memory).Reason);
        Assert.Equal("near-zero", clock.Read(module, memory).Reason);
    }

    [Fact]
    public void SameCellIsStale_EightSecondsWithoutAStep_IsStale()
    {
        DateTimeOffset changed = new DateTimeOffset(2026, 9, 30, 17, 49, 39, TimeSpan.Zero);
        DateTimeOffset later = changed.AddSeconds(9);

        Assert.False(MatchClock.SameCellIsStale(double.NaN, 339.6, changed, later));
        Assert.False(MatchClock.SameCellIsStale(339.6, 339.6, default, later));
        Assert.False(MatchClock.SameCellIsStale(339.6, 339.86, changed, later));
        Assert.False(
            MatchClock.SameCellIsStale(
                339.6,
                339.6,
                changed,
                changed.AddSeconds(8) - TimeSpan.FromMilliseconds(1)
            )
        );
        Assert.True(MatchClock.SameCellIsStale(339.6, 339.6, changed, changed.AddSeconds(8)));
        Assert.True(MatchClock.SameCellIsStale(339.6, 339.85, changed, changed.AddSeconds(8)));
    }

    [Fact]
    public void Read_LockedCellStopsMoving_ReportsStalledUntilItMoves()
    {
        MappedModule memory = MappedModule.WithPattern();
        DateTimeOffset now = new DateTimeOffset(2026, 9, 30, 17, 49, 39, TimeSpan.Zero);
        using MatchClock clock = new MatchClock(TestTime.Options(() => now));
        ClientModule module = Module(21, SmallModule, "2.55.17.97771");
        memory.SetSeconds(PatternTickRva, PatternSpeedRva, 339);
        Assert.Equal("confirming", clock.Read(module, memory).Reason);
        memory.SetSeconds(PatternTickRva, PatternSpeedRva, 340);
        MatchClockSample locked = clock.Read(module, memory);
        Assert.True(locked.Ok);
        Assert.Equal("ok", locked.Reason);
        Assert.True(clock.IsLocked);
        int scans = memory.WideReads;

        now = now.AddSeconds(7);
        MatchClockSample holding = clock.Read(module, memory);
        Assert.True(holding.Ok);
        Assert.Equal("ok", holding.Reason);

        now = now.AddSeconds(1);
        MatchClockSample stalled = clock.Read(module, memory);
        Assert.False(stalled.Ok);
        Assert.Equal("stalled", stalled.Reason);
        Assert.Equal(340, stalled.Seconds, precision: 2);
        Assert.True(clock.IsLocked);
        Assert.Equal(scans, memory.WideReads);

        memory.SetSeconds(PatternTickRva, PatternSpeedRva, 341);
        MatchClockSample moved = clock.Read(module, memory);
        Assert.True(moved.Ok);
        Assert.Equal("ok", moved.Reason);
        Assert.Equal(341, moved.Seconds, precision: 2);
    }

    [Fact]
    public void Read_SlowCaller_StillLocksTheClockOfARelaunchedClient()
    {
        // #249: the launch wait read once per pass, and a pass with OCR on a hung window took
        // 30 s. A fixed 8 s step made every pass "incoherent", so the clock never locked.
        MappedModule memory = MappedModule.WithPattern();
        DateTimeOffset now = new(2026, 10, 7, 13, 13, 0, TimeSpan.Zero);
        using MatchClock clock = new MatchClock(TestTime.Options(() => now));
        ClientModule module = Module(31, SmallModule, "2.57.0.98348");
        memory.SetSeconds(PatternTickRva, PatternSpeedRva, 95);
        Assert.Equal("confirming", clock.Read(module, memory).Reason);

        now = now.AddSeconds(30);
        memory.SetSeconds(PatternTickRva, PatternSpeedRva, 125);
        MatchClockSample later = clock.Read(module, memory);

        Assert.True(later.Ok, later.Reason);
        Assert.True(clock.IsLocked);
        Assert.Equal(125, later.Seconds, precision: 2);
    }

    [Fact]
    public void Read_CellThatJumpsFasterThanWallTime_IsStillIncoherent()
    {
        MappedModule memory = MappedModule.WithPattern();
        DateTimeOffset now = new(2026, 10, 7, 13, 13, 0, TimeSpan.Zero);
        using MatchClock clock = new MatchClock(TestTime.Options(() => now));
        ClientModule module = Module(32, SmallModule, "2.57.0.98348");
        memory.SetSeconds(PatternTickRva, PatternSpeedRva, 100);
        clock.Read(module, memory);

        now = now.AddSeconds(1);
        memory.SetSeconds(PatternTickRva, PatternSpeedRva, 400);
        MatchClockSample jumped = clock.Read(module, memory);

        Assert.False(jumped.Ok);
        Assert.Equal("incoherent", jumped.Reason);
        Assert.False(clock.IsLocked);
    }

    [Theory]
    [InlineData(0.25, 0.25, true)]
    [InlineData(8, 0, true)]
    [InlineData(8.5, 0, false)]
    [InlineData(30, 30, true)]
    [InlineData(38, 30, true)]
    [InlineData(39, 30, false)]
    [InlineData(-1, 30, false)]
    public void CoherentStep_AllowsTheWallTimeBetweenReadsPlusEightSeconds(
        double delta,
        double wallSeconds,
        bool expected
    )
    {
        Assert.Equal(expected, MatchClock.CoherentStep(delta, TimeSpan.FromSeconds(wallSeconds)));
    }

    [Fact]
    public void Read_RelaunchWithTheSamePidAndImage_StartsOverFromTheFrozenClock()
    {
        // #249: the last value read was the previous match's frozen 18:45. A relaunched client
        // can get the same pid and image base, so the start time is part of the process.
        MappedModule memory = MappedModule.WithPattern();
        DateTimeOffset now = new(2026, 10, 7, 13, 1, 0, TimeSpan.Zero);
        using MatchClock clock = new MatchClock(TestTime.Options(() => now));
        var before = new ClientModule(
            33,
            ModuleBase,
            SmallModule,
            "2.57.0.98348",
            now.AddMinutes(-20).Ticks
        );
        memory.SetSeconds(PatternTickRva, PatternSpeedRva, 1124);
        clock.Read(before, memory);
        memory.SetSeconds(PatternTickRva, PatternSpeedRva, 1125);
        Assert.True(clock.Read(before, memory).Ok);
        Assert.True(clock.IsLocked);
        int scans = memory.WideReads;

        now = now.AddMinutes(12);
        ClientModule relaunched = before with { StartedAt = now.AddMinutes(-8).Ticks };
        memory.SetSeconds(PatternTickRva, PatternSpeedRva, 95);
        MatchClockSample first = clock.Read(relaunched, memory);

        Assert.False(clock.IsLocked);
        Assert.Equal("confirming", first.Reason);
        Assert.True(memory.WideReads > scans);
        now = now.AddSeconds(1);
        memory.SetSeconds(PatternTickRva, PatternSpeedRva, 96);
        MatchClockSample locked = clock.Read(relaunched, memory);
        Assert.True(locked.Ok, locked.Reason);
        Assert.Equal(96, locked.Seconds, precision: 2);
    }

    [Fact]
    public async Task ReadRunningAsync_ConfirmsAFreshCellInOneProbe()
    {
        MappedModule memory = MappedModule.WithPattern();
        DateTimeOffset now = new(2026, 10, 7, 13, 13, 0, TimeSpan.Zero);
        using MatchClock clock = new MatchClock(TestTime.Options(() => now));
        ClientModule module = Module(34, SmallModule, "2.57.0.98348");
        int seconds = 95;
        memory.SetSeconds(PatternTickRva, PatternSpeedRva, seconds);
        int pauses = 0;

        TimeSpan? running = await MatchClock.ReadRunningAsync(
            () => clock.Read(module, memory),
            () =>
            {
                pauses++;
                now = now.AddSeconds(1);
                memory.SetSeconds(PatternTickRva, PatternSpeedRva, ++seconds);
                return Task.CompletedTask;
            }
        );

        Assert.Equal(TimeSpan.FromSeconds(97), running);
        Assert.Equal(2, pauses);
    }

    [Fact]
    public async Task ReadRunningAsync_MenuZeroAnswersAtOnce()
    {
        MappedModule memory = MappedModule.WithPattern();
        using MatchClock clock = new MatchClock();
        ClientModule module = Module(35, SmallModule, "2.57.0.98348");
        memory.SetSeconds(PatternTickRva, PatternSpeedRva, 0);
        int pauses = 0;

        TimeSpan? running = await MatchClock.ReadRunningAsync(
            () => clock.Read(module, memory),
            () =>
            {
                pauses++;
                return Task.CompletedTask;
            }
        );

        Assert.Null(running);
        Assert.Equal(0, pauses);
    }

    [Fact]
    public async Task ReadRunningAsync_FrozenClockIsNotRunning()
    {
        MappedModule memory = MappedModule.WithPattern();
        DateTimeOffset now = new(2026, 10, 7, 13, 1, 0, TimeSpan.Zero);
        using MatchClock clock = new MatchClock(TestTime.Options(() => now));
        ClientModule module = Module(36, SmallModule, "2.57.0.98348");
        memory.SetSeconds(PatternTickRva, PatternSpeedRva, 1124);
        clock.Read(module, memory);
        memory.SetSeconds(PatternTickRva, PatternSpeedRva, 1125);
        Assert.True(clock.Read(module, memory).Ok);

        TimeSpan? running = await MatchClock.ReadRunningAsync(
            () => clock.Read(module, memory),
            () =>
            {
                now = now.AddMilliseconds(250);
                return Task.CompletedTask;
            }
        );

        Assert.Null(running);
    }

    [Fact]
    public void Read_NoVersionAnywhere_TakesTheGenericPathAndFindsThePattern()
    {
        MappedModule memory = MappedModule.WithPattern();
        using MatchClock clock = new MatchClock();
        ClientModule module = Module(40, LargeModule, null);
        memory.SetSeconds(PatternTickRva, PatternSpeedRva, 12);
        memory.SetSeconds(MatchTickClock.MatchTickRva, MatchTickClock.GameSpeedFactorRva, 50);

        Assert.Equal("confirming", clock.Read(module, memory).Reason);
        memory.SetSeconds(PatternTickRva, PatternSpeedRva, 13);
        MatchClockSample locked = clock.Read(module, memory);

        Assert.True(locked.Ok);
        Assert.Null(locked.ClientVersion);
        Assert.False(locked.VersionMismatch);
        Assert.Equal(TimeSpan.FromSeconds(13), locked.Time);
        Assert.Equal(PatternTickRva, clock.CandidateTickRva);
    }

    [Fact]
    public void Read_NoPatternAndNoProfile_IsUnsupportedNotAnException()
    {
        MappedModule memory = MappedModule.Empty();
        using MatchClock clock = new MatchClock();
        memory.SetSeconds(MatchTickClock.MatchTickRva, MatchTickClock.GameSpeedFactorRva, 15);

        MatchClockSample sample = clock.Read(Module(41, LargeModule, "2.99.0.123456"), memory);

        Assert.False(sample.Ok);
        Assert.Equal("unsupported-build", sample.Reason);
        Assert.Null(sample.Time);
        Assert.Equal(0, memory.FixedTickReads);
    }

    [Fact]
    public void Read_APassedVersionThatDiffers_IsReportedAndReadingContinues()
    {
        MappedModule memory = MappedModule.WithPattern();
        using MatchClock clock = new MatchClock();
        ClientModule module = Module(42, SmallModule, "2.57.0.98348");
        var expected = new HeroesClientVersion(2, 57, 0, 98304);
        memory.SetSeconds(PatternTickRva, PatternSpeedRva, 20);

        MatchClockSample first = clock.Read(module, memory, expected);
        memory.SetSeconds(PatternTickRva, PatternSpeedRva, 21);
        MatchClockSample locked = clock.Read(module, memory, expected);
        MatchClockSample same = clock.Read(
            module,
            memory,
            new HeroesClientVersion(2, 57, 0, 98348)
        );

        Assert.True(first.VersionMismatch);
        Assert.Equal("confirming", first.Reason);
        Assert.True(locked.Ok);
        Assert.True(locked.VersionMismatch);
        Assert.Equal(new HeroesClientVersion(2, 57, 0, 98348), locked.ClientVersion);
        Assert.True(same.Ok);
        Assert.False(same.VersionMismatch);
    }

    [Fact]
    public void Read_ABuildProfileWithFixedAddresses_IsTriedAfterThePattern()
    {
        // A registry entry, not new code, gives a build its fixed addresses.
        BuildProfileRegistry profiles = BuildProfileRegistry.Default.WithBuild(
            new HeroesClientVersion(2, 57, 0, 98348),
            new BuildProfile
            {
                Name = "test",
                FixedClock = new MatchClockAddresses(PatternTickRva, PatternSpeedRva),
            }
        );
        MappedModule memory = MappedModule.Empty();
        using MatchClock clock = new MatchClock(new HeroesClientOptions { Profiles = profiles });
        ClientModule module = Module(43, SmallModule, "2.57.0.98348");
        memory.SetSeconds(PatternTickRva, PatternSpeedRva, 30);

        Assert.Equal("confirming", clock.Read(module, memory).Reason);
        memory.SetSeconds(PatternTickRva, PatternSpeedRva, 31);

        Assert.True(clock.Read(module, memory).Ok);
        Assert.Equal(PatternTickRva, clock.CandidateTickRva);
    }

    [Fact]
    public void Read_ThePassedVersionPicksTheProfileOnlyWhenTheExeHasNone()
    {
        var build98025 = new HeroesClientVersion(2, 55, 17, 98025);
        MappedModule memory = MappedModule.Empty();
        memory.SetSeconds(MatchTickClock.MatchTickRva, MatchTickClock.GameSpeedFactorRva, 15);
        using MatchClock running = new MatchClock();
        using MatchClock unversioned = new MatchClock();

        // The running exe is another build: its own (generic) profile wins over the passed one.
        MatchClockSample other = running.Read(
            Module(44, LargeModule, "2.57.0.98348"),
            memory,
            build98025
        );
        // The exe has no version: the passed version picks the 98025 profile.
        MatchClockSample fallback = unversioned.Read(
            Module(45, LargeModule, null),
            memory,
            build98025
        );

        Assert.Equal("unsupported-build", other.Reason);
        Assert.True(other.VersionMismatch);
        Assert.Equal("confirming", fallback.Reason);
        Assert.False(fallback.VersionMismatch);
        Assert.Equal(MatchTickClock.MatchTickRva, unversioned.CandidateTickRva);
    }

    [Fact]
    public void Read_TwoClientsAtOnce_EachClockKeepsItsOwn()
    {
        MappedModule current = MappedModule.WithPattern();
        MappedModule previous = MappedModule.WithPattern();
        using MatchClock first = new MatchClock();
        using MatchClock second = new MatchClock();
        ClientModule currentModule = Module(46, SmallModule, "2.57.0.98348");
        ClientModule previousModule = Module(47, SmallModule, "2.57.0.98304");

        current.SetSeconds(PatternTickRva, PatternSpeedRva, 100);
        previous.SetSeconds(PatternTickRva, PatternSpeedRva, 600);
        first.Read(currentModule, current);
        second.Read(previousModule, previous, new HeroesClientVersion(2, 57, 0, 98304));
        current.SetSeconds(PatternTickRva, PatternSpeedRva, 101);
        previous.SetSeconds(PatternTickRva, PatternSpeedRva, 601);
        MatchClockSample a = first.Read(currentModule, current);
        MatchClockSample b = second.Read(
            previousModule,
            previous,
            new HeroesClientVersion(2, 57, 0, 98304)
        );

        Assert.True(a.Ok);
        Assert.True(b.Ok);
        Assert.Equal(101, a.Seconds, precision: 2);
        Assert.Equal(601, b.Seconds, precision: 2);
        Assert.Equal(98348, a.ClientVersion.Build);
        Assert.Equal(98304, b.ClientVersion.Build);
        Assert.False(b.VersionMismatch);
    }

    [Fact]
    public void Read_AClientFromMemory_ReadsLikeAProcess()
    {
        MappedModule memory = MappedModule.WithPattern();
        using MatchClock clock = new MatchClock();
        using HeroesClientProcess client = HeroesClientProcess.FromMemory(
            memory,
            Module(48, SmallModule, "2.57.0.98348")
        );
        memory.SetSeconds(PatternTickRva, PatternSpeedRva, 40);
        clock.Read(client);
        memory.SetSeconds(PatternTickRva, PatternSpeedRva, 41);

        MatchClockSample sample = clock.Read(client);

        Assert.True(client.Ok);
        Assert.Equal(new HeroesClientVersion(2, 57, 0, 98348), client.DetectedVersion);
        Assert.True(sample.Ok);
        Assert.Equal(41, sample.Seconds, precision: 2);
    }

    private static ClientModule Module(int pid, long size, string version)
    {
        return new ClientModule(pid, ModuleBase, size, version);
    }

    private sealed class MappedModule : IProcessMemory
    {
        private readonly Dictionary<long, byte> bytes = new Dictionary<long, byte>();

        public int WideReads { get; private set; }

        public int FixedTickReads { get; private set; }

        public static MappedModule WithPattern()
        {
            MappedModule module = Empty();
            module.AddPattern();
            return module;
        }

        public void AddPattern()
        {
            int tickDisp = checked((int)(PatternTickRva - SectionRva - MatchClockPattern.MovdEnd));
            int speedDisp = checked(
                (int)(PatternSpeedRva - SectionRva - MatchClockPattern.MulssEnd)
            );
            Write(SectionRva, Pattern(tickDisp, speedDisp));
        }

        public static MappedModule Empty()
        {
            MappedModule module = new MappedModule();
            module.Write(0, new byte[] { (byte)'M', (byte)'Z' });
            module.WriteInt32(0x3C, 0x80);
            module.Write(0x80, new byte[] { (byte)'P', (byte)'E', 0, 0 });
            module.WriteUInt16(0x80 + 6, 1);
            module.WriteUInt16(0x80 + 20, 0);
            long section = 0x80 + 24;
            module.WriteInt32(section + 8, SectionSize);
            module.WriteUInt32(section + 12, (uint)SectionRva);
            module.WriteUInt32(section + 36, 0x20000000);
            return module;
        }

        public void SetSeconds(long tickRva, long speedRva, int seconds)
        {
            WriteInt32(tickRva, seconds * 4096);
            SetSingle(speedRva, Scale);
        }

        public void SetSingle(long rva, float value)
        {
            Write(rva, BitConverter.GetBytes(value));
        }

        public bool TryRead(long address, Span<byte> buffer)
        {
            if (address <= 0 || buffer.IsEmpty)
            {
                return false;
            }

            if (buffer.Length > 16)
            {
                WideReads++;
            }

            long fixedTick = ModuleBase + MatchTickClock.MatchTickRva;
            if (address < fixedTick + 4 && address + buffer.Length > fixedTick)
            {
                FixedTickReads++;
            }

            for (int i = 0; i < buffer.Length; i++)
            {
                byte value;
                buffer[i] = bytes.TryGetValue(address + i, out value) ? value : (byte)0;
            }

            return true;
        }

        private void WriteInt32(long rva, int value)
        {
            Write(rva, BitConverter.GetBytes(value));
        }

        private void WriteUInt16(long rva, ushort value)
        {
            Write(rva, BitConverter.GetBytes(value));
        }

        private void WriteUInt32(long rva, uint value)
        {
            Write(rva, BitConverter.GetBytes(value));
        }

        private void Write(long rva, byte[] value)
        {
            for (int i = 0; i < value.Length; i++)
            {
                bytes[ModuleBase + rva + i] = value[i];
            }
        }

        private static byte[] Pattern(int tickDisplacement, int speedDisplacement)
        {
            byte[] bytes = new byte[MatchClockPattern.MulssEnd];
            bytes[0] = 0x84;
            bytes[1] = 0xC0;
            bytes[2] = 0x74;
            bytes[3] = 0x0A;
            bytes[4] = 0x66;
            bytes[5] = 0x0F;
            bytes[6] = 0x6E;
            bytes[7] = 0x05;
            BitConverter
                .GetBytes(tickDisplacement)
                .CopyTo(bytes, MatchClockPattern.TickDisplacement);
            bytes[12] = 0x0F;
            bytes[13] = 0x5B;
            bytes[14] = 0xC0;
            bytes[15] = 0xF3;
            bytes[16] = 0x0F;
            bytes[17] = 0x59;
            bytes[18] = 0x05;
            BitConverter
                .GetBytes(speedDisplacement)
                .CopyTo(bytes, MatchClockPattern.SpeedDisplacement);
            return bytes;
        }
    }
}
