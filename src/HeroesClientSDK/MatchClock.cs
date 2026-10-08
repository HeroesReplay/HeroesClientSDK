using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading.Tasks;

namespace HeroesClientSDK;

/// <summary>
/// One read of the match clock. Only an <paramref name="Ok"/> read is a running match.
/// </summary>
/// <param name="Ok">True when the read is the confirmed, moving match clock.</param>
/// <param name="Reason">
/// "ok", or why there is no clock, for example "no-process", "no-module", "open-failed",
/// "read-failed", "unsupported-build", "pattern-disagreed", "out-of-range", "bad-scale",
/// "near-zero", "confirming", "incoherent", or "stalled".
/// </param>
/// <param name="Ticks">The raw tick counter.</param>
/// <param name="Scale">Seconds per tick (1/4096).</param>
/// <param name="Seconds">
/// Match time in seconds: <paramref name="Ticks"/> times <paramref name="Scale"/>.
/// </param>
/// <param name="ClientVersion">The build of the running exe, or null when unknown.</param>
/// <param name="VersionMismatch">
/// True when the caller passed a version and the running exe is another build. Reading
/// continues; nothing throws.
/// </param>
public readonly record struct MatchClockSample(
    bool Ok,
    string Reason,
    int Ticks = 0,
    float Scale = 0,
    double Seconds = 0,
    HeroesClientVersion ClientVersion = null,
    bool VersionMismatch = false
)
{
    /// <summary>The match time of an ok read, null otherwise.</summary>
    public TimeSpan? Time => Ok ? TimeSpan.FromSeconds(Seconds) : null;
}

/// <summary>
/// Read-only match clock. Pattern discovery runs once per process module on every client build.
/// A build's fixed addresses (<see cref="BuildProfile.FixedClock"/>, today only 2.55.17.98025)
/// are only a candidate after the pattern; a failed check stays unlocked and reports no clock.
/// This is the only match clock. The HUD timer is never cropped or OCR'd. Keep one per client
/// process; it starts over by itself on a new process (pid and start time).
/// </summary>
public sealed class MatchClock : IDisposable
{
    private const double MaxCoherentStepSeconds = 8;
    private static readonly TimeSpan RediscoverAfter = TimeSpan.FromSeconds(10);

    private readonly ProcessAttachment attachment = new();
    private readonly BuildProfileRegistry profiles;
    private readonly TimeProvider time;
    private bool fingerprintSet;
    private int pid;
    private long startedAt;
    private long moduleBase;
    private long moduleSize;
    private string version = "";
    private long tickRva;
    private long speedRva;
    private bool discovered;
    private bool located;
    private bool hasSample;
    private int lastTicks;
    private float lastScale;
    private DateTimeOffset lastSampleAt;
    private double lastOkSeconds = double.NaN;
    private DateTimeOffset lastOkChange;
    private DateTimeOffset rediscoverAt;
    private string discoveryReason = "no-process";
    private MatchClockTelemetry lastReport;
    private int telemetryEmissions;
    private HeroesClientVersion readVersion;
    private bool readMismatch;

    /// <summary>A clock with <paramref name="options"/>, or the defaults when null.</summary>
    public MatchClock(HeroesClientOptions options = null)
    {
        profiles = options?.Profiles ?? BuildProfileRegistry.Default;
        time = options?.TimeProvider ?? TimeProvider.System;
    }

    /// <summary>
    /// Gap between the two reads that prove the clock is moving. The clock counts ticks / 4096
    /// per second, so a running match is about a quarter second ahead on the second read.
    /// </summary>
    public static readonly TimeSpan RunningProbe = TimeSpan.FromMilliseconds(250);

    /// <summary>
    /// The discovery state and reason of the last read (see <see cref="MatchClockTelemetry"/>).
    /// </summary>
    public MatchClockTelemetry LastTelemetry { get; private set; }

    internal MatchClockTelemetry DiscoveryTelemetry { get; private set; }

    internal int TelemetryEmissions => telemetryEmissions;

    internal bool IsLocked => located;

    internal long CandidateTickRva => tickRva;

    /// <summary>
    /// Reads the match clock of <paramref name="process"/>. A new process (pid and start time)
    /// starts discovery over. <paramref name="clientVersion"/> is optional: when it is given and
    /// the running exe is another build, the sample says so and reading continues.
    /// </summary>
    public MatchClockSample Read(Process process, HeroesClientVersion clientVersion = null) =>
        Read(attachment.For(process), clientVersion);

    /// <summary>
    /// Reads the match clock of an attached <paramref name="client"/> (one handle shared by every
    /// reader, or a client from <see cref="HeroesClientProcess.FromMemory"/>). The client stays
    /// the caller's.
    /// </summary>
    public MatchClockSample Read(
        HeroesClientProcess client,
        HeroesClientVersion clientVersion = null
    )
    {
        if (client is null || !client.Ok)
        {
            discoveryReason = client?.Reason ?? "no-process";
            return new MatchClockSample(false, discoveryReason);
        }

        return Read(client.Module, client.Memory, clientVersion);
    }

    internal MatchClockSample Read(
        ClientModule module,
        IProcessMemory memory,
        HeroesClientVersion clientVersion = null
    )
    {
        readVersion = HeroesClientVersion.TryParse(module.FileVersion);
        readMismatch =
            clientVersion is not null && readVersion is not null && readVersion != clientVersion;
        if (module.ProcessId <= 0)
        {
            return Finish(new MatchClockSample(false, "no-process"));
        }

        if (module.BaseAddress <= 0 || module.Size <= 0)
        {
            return Finish(new MatchClockSample(false, "no-module"));
        }

        if (memory == null)
        {
            return Finish(new MatchClockSample(false, "read-failed"));
        }

        UseModule(module);
        if (!discovered || (tickRva == 0 && time.GetUtcNow() >= rediscoverAt))
        {
            ReportTelemetry(discoveryReason);
            Discover(memory, clientVersion);
        }

        if (tickRva == 0 || speedRva == 0)
        {
            return Finish(new MatchClockSample(false, discoveryReason));
        }

        int ticks = 0;
        float speed = 0;
        if (
            !TryReadInt32(memory, moduleBase + tickRva, out ticks)
            || !TryReadSingle(memory, moduleBase + speedRva, out speed)
        )
        {
            return Finish(new MatchClockSample(false, "read-failed", ticks, speed));
        }

        if (!MatchTickClock.TrySeconds(ticks, speed, out double seconds))
        {
            hasSample = false;
            return Finish(new MatchClockSample(false, "bad-scale", ticks, speed, seconds));
        }

        // Zero is also the menu and the loading screen, so it is not a started match.
        if (Math.Abs(seconds) < 0.5)
        {
            return Finish(new MatchClockSample(false, "near-zero", ticks, speed, seconds));
        }

        if (!located && !TryConfirm(ticks, speed, seconds, out string reason))
        {
            return Finish(new MatchClockSample(false, reason, ticks, speed, seconds));
        }

        // A clock that went back is a new match in the same client, not a frozen cell. Without
        // this, the previous match's last second stayed the baseline and every read was stalled.
        if (StartedOver(lastOkSeconds, seconds))
        {
            BeginMatch();
        }

        if (SameCellIsStale(lastOkSeconds, seconds, lastOkChange, time.GetUtcNow()))
        {
            return Finish(new MatchClockSample(false, "stalled", ticks, speed, seconds));
        }

        if (double.IsNaN(lastOkSeconds) || seconds > lastOkSeconds + 0.25)
        {
            lastOkSeconds = seconds;
            lastOkChange = time.GetUtcNow();
        }

        return Finish(new MatchClockSample(true, "ok", ticks, speed, seconds));
    }

    private MatchClockSample Finish(MatchClockSample sample)
    {
        ReportTelemetry(sample.Reason);
        return sample with { ClientVersion = readVersion, VersionMismatch = readMismatch };
    }

    private void ReportTelemetry(string reason)
    {
        MatchClockTelemetry report = MatchClockTelemetry.Describe(discovered, located, reason);
        if (!discovered)
        {
            DiscoveryTelemetry = report;
        }

        if (lastReport != report)
        {
            lastReport = report;
            telemetryEmissions++;
        }

        LastTelemetry = report;
    }

    /// <summary>
    /// Forgets the stall baseline for a new replay. The located clock address is kept.
    /// </summary>
    public void BeginMatch()
    {
        lastOkSeconds = double.NaN;
        lastOkChange = default;
    }

    /// <summary>
    /// A match is running only when both reads succeed and the second is ahead of the first.
    /// The menu's zero does not read, and a frozen clock from the last match does not move.
    /// </summary>
    public static bool IsRunning(TimeSpan? first, TimeSpan? second)
    {
        return first.HasValue && second.HasValue && second.Value > first.Value;
    }

    /// <summary>
    /// The reasons a read gives while a found cell is not confirmed yet. One more read
    /// <see cref="RunningProbe"/> later confirms a cell that is really the clock.
    /// </summary>
    internal static bool StillConfirming(string reason) => reason is "confirming" or "incoherent";

    /// <summary>
    /// The running match clock from one probe: two reads <see cref="RunningProbe"/> apart that
    /// move forward. A read that is still confirming a fresh cell (a relaunched client) gets one
    /// more read first, so a caller that probes only every 30 s still locks the clock. Any other
    /// miss (the menu's zero, a stalled or unsupported clock) answers at once.
    /// </summary>
    public static async Task<TimeSpan?> ReadRunningAsync(
        Func<MatchClockSample> read,
        Func<Task> pause
    )
    {
        ArgumentNullException.ThrowIfNull(read);
        ArgumentNullException.ThrowIfNull(pause);
        MatchClockSample first = read();
        if (!first.Ok && StillConfirming(first.Reason))
        {
            await pause().ConfigureAwait(false);
            first = read();
        }

        if (!first.Ok)
        {
            return null;
        }

        await pause().ConfigureAwait(false);
        MatchClockSample second = read();
        TimeSpan? running = second.Ok ? TimeSpan.FromSeconds(second.Seconds) : null;
        return IsRunning(TimeSpan.FromSeconds(first.Seconds), running) ? running : null;
    }

    internal static bool StartedOver(double previousSeconds, double seconds)
    {
        return !double.IsNaN(previousSeconds) && seconds < previousSeconds - 5;
    }

    /// <summary>
    /// A locked cell that stops moving is not a running match: it is paused, over, or not the clock.
    /// </summary>
    internal static bool SameCellIsStale(
        double previousSeconds,
        double seconds,
        DateTimeOffset changedAt,
        DateTimeOffset now
    )
    {
        if (double.IsNaN(previousSeconds) || changedAt == default)
        {
            return false;
        }

        if (seconds > previousSeconds + 0.25)
        {
            return false;
        }

        return now - changedAt >= TimeSpan.FromSeconds(8);
    }

    /// <summary>Closes the process handle this clock opened and forgets the located clock.</summary>
    public void Dispose()
    {
        attachment.Dispose();
        ResetState();
    }

    private void UseModule(ClientModule module)
    {
        string fileVersion = module.FileVersion ?? "";
        if (
            fingerprintSet
            && pid == module.ProcessId
            && startedAt == module.StartedAt
            && moduleBase == module.BaseAddress
            && moduleSize == module.Size
            && string.Equals(version, fileVersion, StringComparison.Ordinal)
        )
        {
            return;
        }

        // A new client process: the located cell, the confirmation sample, and the stall
        // baseline all belong to the old one (#249: the last match's frozen clock).
        fingerprintSet = true;
        pid = module.ProcessId;
        startedAt = module.StartedAt;
        moduleBase = module.BaseAddress;
        moduleSize = module.Size;
        version = fileVersion;
        discovered = false;
        rediscoverAt = default;
        located = false;
        lastReport = default;
        DiscoveryTelemetry = default;
        tickRva = 0;
        speedRva = 0;
        hasSample = false;
        lastTicks = 0;
        lastScale = 0;
        lastSampleAt = default;
        BeginMatch();
    }

    private void Discover(IProcessMemory memory, HeroesClientVersion clientVersion)
    {
        discovered = true;
        // A client still unpacking its code has no pattern yet. A miss is scanned again later.
        rediscoverAt = time.GetUtcNow() + RediscoverAfter;
        located = false;
        tickRva = 0;
        speedRva = 0;
        hasSample = false;
        lastTicks = 0;
        lastScale = 0;
        lastSampleAt = default;

        bool agreed = TryLocateByPattern(
            memory,
            out int sites,
            out long patternTick,
            out long patternSpeed
        );
        if (agreed && InRange(patternTick) && InRange(patternSpeed))
        {
            tickRva = patternTick;
            speedRva = patternSpeed;
            discoveryReason = "pattern";
            return;
        }

        // Per-build data follows the running exe; a passed version counts only when the exe has
        // none. A build without fixed addresses has nothing after the pattern.
        MatchClockAddresses? fixedClock = profiles.Resolve(readVersion ?? clientVersion).FixedClock;
        if (
            fixedClock is MatchClockAddresses known
            && InRange(known.TickRva)
            && InRange(known.SpeedRva)
        )
        {
            tickRva = known.TickRva;
            speedRva = known.SpeedRva;
            discoveryReason = "fixed";
            return;
        }

        if (patternTick != 0 || patternSpeed != 0 || fixedClock.HasValue)
        {
            discoveryReason = "out-of-range";
            return;
        }

        discoveryReason = sites == 0 ? "unsupported-build" : "pattern-disagreed";
    }

    private bool InRange(long rva)
    {
        return rva > 0 && rva <= moduleSize - 4;
    }

    private bool TryConfirm(int ticks, float scale, double seconds, out string reason)
    {
        DateTimeOffset now = time.GetUtcNow();
        if (!hasSample)
        {
            hasSample = true;
            lastTicks = ticks;
            lastScale = scale;
            lastSampleAt = now;
            reason = "confirming";
            return false;
        }

        double delta = seconds - (lastTicks * (double)lastScale);
        bool sameScale = SameScale(scale, lastScale);
        TimeSpan sinceSample = now - lastSampleAt;
        lastTicks = ticks;
        lastScale = scale;
        lastSampleAt = now;
        if (!sameScale || !CoherentStep(delta, sinceSample))
        {
            reason = "incoherent";
            return false;
        }

        if (delta == 0)
        {
            reason = "confirming";
            return false;
        }

        located = true;
        reason = "ok";
        return true;
    }

    /// <summary>
    /// A real match clock moves forward by about the wall time between two reads, never more.
    /// The step allowed is <see cref="MaxCoherentStepSeconds"/> on top of that wall time. A fixed
    /// eight seconds broke when the caller read slowly: the launch wait reads once per pass, and
    /// a pass with OCR on a hung window took 30 s or more, so every pass was "incoherent" and the
    /// clock never locked on a relaunched client (#249).
    /// </summary>
    internal static bool CoherentStep(double deltaSeconds, TimeSpan sinceLastSample)
    {
        if (double.IsNaN(deltaSeconds) || deltaSeconds < 0)
        {
            return false;
        }

        double wall = Math.Max(0, sinceLastSample.TotalSeconds);
        return deltaSeconds <= MaxCoherentStepSeconds + wall;
    }

    private static bool SameScale(float left, float right)
    {
        return Math.Abs(left - right) <= 0.000001f;
    }

    private bool TryLocateByPattern(
        IProcessMemory memory,
        out int sites,
        out long patternTick,
        out long patternSpeed
    )
    {
        sites = 0;
        patternTick = 0;
        patternSpeed = 0;
        if (!ModuleScanner.TrySections(memory, moduleBase, moduleSize, out var sections))
        {
            return false;
        }

        var found = new List<MatchClockPattern.Site>();
        foreach (ModuleSection section in sections)
        {
            if (!section.Executable)
            {
                continue;
            }

            ModuleScanner.WalkWhole(
                memory,
                moduleBase,
                section,
                MatchClockPattern.MulssEnd,
                (slice, rva) => found.AddRange(MatchClockPattern.Find(slice, rva))
            );
        }

        sites = found.Count;
        return MatchClockPattern.TryAgree(found, out patternTick, out patternSpeed);
    }

    private static bool TryReadInt32(IProcessMemory memory, long address, out int value)
    {
        value = 0;
        Span<byte> buffer = stackalloc byte[4];
        if (!TryRead(memory, address, buffer))
        {
            return false;
        }

        value = BitConverter.ToInt32(buffer);
        return true;
    }

    private static bool TryReadSingle(IProcessMemory memory, long address, out float value)
    {
        value = 0;
        Span<byte> buffer = stackalloc byte[4];
        if (!TryRead(memory, address, buffer))
        {
            return false;
        }

        value = BitConverter.ToSingle(buffer);
        return true;
    }

    private static bool TryRead(IProcessMemory memory, long address, Span<byte> buffer)
    {
        return memory != null && address > 0 && !buffer.IsEmpty && memory.TryRead(address, buffer);
    }

    private void ResetState()
    {
        fingerprintSet = false;
        pid = 0;
        startedAt = 0;
        moduleBase = 0;
        moduleSize = 0;
        version = "";
        tickRva = 0;
        speedRva = 0;
        discovered = false;
        rediscoverAt = default;
        located = false;
        hasSample = false;
        lastTicks = 0;
        lastScale = 0;
        lastSampleAt = default;
        lastOkSeconds = double.NaN;
        lastOkChange = default;
        discoveryReason = "no-process";
        lastReport = default;
        DiscoveryTelemetry = default;
        LastTelemetry = default;
        telemetryEmissions = 0;
        readVersion = null;
        readMismatch = false;
    }
}
