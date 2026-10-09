using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Diagnostics;

namespace HeroesClientSDK;

/// <summary>A Storm League league, as the client shows it.</summary>
public enum RankLeague
{
    /// <summary>Still in placement games.</summary>
    Placement,

    /// <summary>Bronze.</summary>
    Bronze,

    /// <summary>Silver.</summary>
    Silver,

    /// <summary>Gold.</summary>
    Gold,

    /// <summary>Platinum.</summary>
    Platinum,

    /// <summary>Diamond.</summary>
    Diamond,

    /// <summary>Master.</summary>
    Master,

    /// <summary>Grandmaster.</summary>
    Grandmaster,
}

/// <summary>Whether a rank is in a promotion or demotion series.</summary>
public enum RankPhase
{
    /// <summary>Neither.</summary>
    None,

    /// <summary>A promotion series.</summary>
    Promotion,

    /// <summary>A demotion series.</summary>
    Demotion,
}

/// <summary>
/// One Storm League rank, before or after a game.
/// </summary>
/// <param name="League">The league, or <see cref="RankLeague.Placement"/> while placing.</param>
/// <param name="Division">The division in the league (5 lowest, 1 highest); 0 while placing.</param>
/// <param name="Points">The rank points in the division; 0 while placing.</param>
/// <param name="Phase">Whether the rank is in a promotion or demotion series.</param>
/// <param name="PhaseValue">
/// The value the client keeps with a promotion series (shown as reserved points); 0 otherwise.
/// </param>
/// <param name="LadderPosition">The Grandmaster ladder position, when the client has one.</param>
/// <param name="PlacementGames">The placement value the client keeps while placing; 0 otherwise.</param>
public sealed record RankStanding(
    RankLeague League,
    int Division,
    int Points,
    RankPhase Phase = RankPhase.None,
    int PhaseValue = 0,
    int? LadderPosition = null,
    int PlacementGames = 0
)
{
    /// <summary>True while the player is still in placement games.</summary>
    public bool Placement => League == RankLeague.Placement;

    /// <summary>Like "Gold 3 (312)", "Grandmaster #42 (1500)" or "Placement (3)".</summary>
    public override string ToString() =>
        League switch
        {
            RankLeague.Placement => $"Placement ({PlacementGames})",
            RankLeague.Grandmaster when LadderPosition is int position =>
                $"Grandmaster #{position} ({Points})",
            _ => $"{League} {Division} ({Points})",
        };
}

/// <summary>
/// Where a game's rank points came from, as the score screen's rank tooltip lists them
/// (<c>@UI/RewardItem/*</c>).
/// </summary>
/// <param name="Match">
/// The points for the result. The tooltip calls them promotion or demotion points during a series.
/// </param>
/// <param name="Favored">
/// The adjustment for the teams' odds: positive when the opponent was favored, negative when the
/// player's team was.
/// </param>
/// <param name="CatchUpBonus">The catch-up bonus.</param>
/// <param name="Performance">The performance points.</param>
/// <param name="DeserterPenalty">The deserter penalty.</param>
public readonly record struct RankPointsBreakdown(
    int Match,
    int Favored,
    int CatchUpBonus,
    int Performance,
    int DeserterPenalty
);

/// <summary>
/// The local player's Storm League result of one game, as the score screen shows it: the rank
/// before and after, and the points change.
/// </summary>
/// <param name="Before">The rank before the game.</param>
/// <param name="After">The rank after the game.</param>
/// <param name="DeltaPoints">The total rank points change that the client received.</param>
/// <param name="Breakdown">Where the points came from.</param>
public sealed record RankResult(
    RankStanding Before,
    RankStanding After,
    int DeltaPoints,
    RankPointsBreakdown Breakdown
);

/// <summary>
/// One read of the score screen's Storm League result.
/// </summary>
/// <param name="Result">The result, or null when there is none.</param>
/// <param name="Reason">
/// "ok", or why there is no result. "no-result" means no game has ended in this client yet, and
/// "no-rank" that the last game's record has no rank result (not a ranked game). Other reasons:
/// "no-score-screen", "bad-record", "no-state", "read-failed", "unsupported-build",
/// "pattern-disagreed", "no-module", "no-process", "open-failed".
/// </param>
/// <param name="ClientVersion">The running exe's build, or null when unknown.</param>
/// <param name="VersionMismatch">True when an expected version was passed and the running exe is another build.</param>
public readonly record struct MatchRankSample(
    RankResult Result,
    string Reason,
    HeroesClientVersion ClientVersion = null,
    bool VersionMismatch = false
)
{
    /// <summary>True when the read has a result.</summary>
    public bool Ok => Result is not null;
}

/// <summary>
/// Read-only Storm League result from client memory: the rank before and after the local player's
/// last game and the points change, from the end-of-game record that the score screen shows
/// (HeroesClientSDK#19). The menu root is found per build by <see cref="LoadingScreenPattern"/>;
/// its <c>CScreenScore</c> child is found by class name, and <see cref="MatchRankLayout"/> (from
/// the build's profile) places the record and its fields.
/// <para>
/// The record stays null until a game ends in the client, so a read at the home screen of a fresh
/// client is "no-result". Keep one per client process; it starts over by itself on a new process
/// (pid and start time). <see cref="MatchRankWatcher"/> raises an event once per new result.
/// </para>
/// </summary>
public sealed class MatchRank : IDisposable
{
    private const string ScoreScreen = "CScreenScore";
    private const int RecordBytes = 40;
    private const int BreakdownValues = 5;
    private static readonly TimeSpan RediscoverAfter = TimeSpan.FromSeconds(10);

    // The rank label on the score screen, for diagnostics only (heroes-client-probe --rank):
    // CScreenScore keeps its CPlayerRewardsPanel at +0x280, which keeps the
    // RankCountingFrame/RankEarnedLabel at +0x168 (2.57.0.98348).
    private const int RewardsPanelOffset = 0x280;
    private const int RankLabelOffset = 0x168;

    private readonly ProcessAttachment attachment = new();
    private readonly BuildProfileRegistry profiles;
    private readonly TimeProvider time;
    private readonly ScreenScans ownScans = new();
    private readonly Dictionary<long, string> classes = new();
    private ScreenScan lastScan;
    private FrameTreeLayout frameLayout = FrameTreeLayout.Default;
    private MatchRankLayout layout = MatchRankLayout.Default;
    private int pid;
    private long startedAt;
    private long moduleBase;
    private long moduleSize;
    private bool discovered;
    private long globalRva;
    private DateTimeOffset rediscoverAt;
    private string reason = "no-process";
    private long scoreScreen;
    private long scoreScreenVtable;

    /// <summary>
    /// A reader with <paramref name="options"/>, or the defaults when null. The menu root comes
    /// from the client's code; the record's layout comes from the running build's profile
    /// (<see cref="BuildProfile.MatchRank"/>).
    /// </summary>
    public MatchRank(HeroesClientOptions options = null)
    {
        profiles = options?.Profiles ?? BuildProfileRegistry.Default;
        time = options?.TimeProvider ?? TimeProvider.System;
    }

    internal string DiscoveryReason => reason;

    internal long GlobalRva => globalRva;

    /// <summary>The process start of the last module read, so a reused pid reads as a new client.</summary>
    internal long ProcessStart => startedAt;

    /// <summary>The address of the end-of-game record of the last read; 0 when there was none.</summary>
    internal long LastRecord { get; private set; }

    /// <summary>The <c>CScreenScore</c> frame of the last read; 0 when not found.</summary>
    internal long LastScoreScreen => scoreScreen;

    /// <summary>
    /// The record's bytes from <see cref="MatchRankLayout.RankedOffset"/> to the end of the
    /// breakdown, as the last read saw them; null when there was no record.
    /// </summary>
    internal byte[] LastRecordBytes { get; private set; }

    /// <summary>The 16-bit status of the last record read.</summary>
    internal short? LastRecordStatus { get; private set; }

    /// <summary>The score screen's rank label text at the last read, for diagnostics.</summary>
    internal string LastRankLabel { get; private set; }

    /// <summary>
    /// Reads the score screen's Storm League result of <paramref name="process"/>. A new process
    /// (pid and start time) starts discovery over. <paramref name="clientVersion"/> is optional:
    /// when it is given and the running exe is another build, the sample says so and reading
    /// continues.
    /// </summary>
    public MatchRankSample Read(Process process, HeroesClientVersion clientVersion = null) =>
        Read(attachment.For(process), clientVersion);

    /// <summary>
    /// Reads the score screen's Storm League result of an attached <paramref name="client"/>
    /// (one handle shared by every reader, or a client from
    /// <see cref="HeroesClientProcess.FromMemory"/>). The client stays the caller's.
    /// </summary>
    public MatchRankSample Read(
        HeroesClientProcess client,
        HeroesClientVersion clientVersion = null
    )
    {
        if (client is null || !client.Ok)
        {
            reason = client?.Reason ?? "no-process";
            Forget();
            return new MatchRankSample(null, reason);
        }

        return Read(client.Module, client.Memory, clientVersion, client.ScreenScans);
    }

    internal MatchRankSample Read(
        ClientModule module,
        IProcessMemory memory,
        HeroesClientVersion clientVersion = null,
        ScreenScans scans = null
    )
    {
        HeroesClientVersion running = HeroesClientVersion.TryParse(module.FileVersion);
        bool mismatch =
            clientVersion is not null && running is not null && running != clientVersion;
        MatchRankSample Sample(RankResult result, string why) =>
            new(result, why, running, mismatch);

        Forget();
        if (module.ProcessId <= 0 || module.BaseAddress <= 0 || module.Size <= 0 || memory == null)
        {
            return Sample(null, "no-module");
        }

        UseModule(module);
        if (!discovered || (globalRva == 0 && time.GetUtcNow() >= rediscoverAt))
        {
            Discover(memory, module, running ?? clientVersion, scans ?? ownScans);
        }

        if (globalRva == 0)
        {
            return Sample(null, reason);
        }

        if (!ModuleScanner.TryReadPointer(memory, moduleBase + globalRva, out long root))
        {
            return Sample(null, "read-failed");
        }

        if (root == 0)
        {
            return Sample(null, "no-state");
        }

        long screen = FindScoreScreen(memory, root);
        if (screen == 0)
        {
            return Sample(null, "no-score-screen");
        }

        long panel = ReadPointer(memory, screen + RewardsPanelOffset);
        LastRankLabel =
            panel == 0
                ? null
                : HeroesClientSDK.DialogText.Label(
                    memory,
                    DialogTextLayout.Default,
                    panel + RankLabelOffset,
                    vtable => ClassOf(memory, vtable)
                );
        if (!ModuleScanner.TryReadPointer(memory, screen + layout.RecordOffset, out long record))
        {
            return Sample(null, "read-failed");
        }

        if (record == 0)
        {
            return Sample(null, "no-result");
        }

        LastRecord = record;
        long length = layout.BreakdownOffset + 4 * BreakdownValues - layout.RankedOffset;
        byte[] bytes = new byte[length];
        byte[] status = new byte[2];
        if (
            length <= 0
            || !TryRead(memory, record + layout.RankedOffset, bytes)
            || !TryRead(memory, record + layout.StatusOffset, status)
        )
        {
            return Sample(null, "read-failed");
        }

        LastRecordBytes = bytes;
        LastRecordStatus = BinaryPrimitives.ReadInt16LittleEndian(status);
        ReadOnlySpan<byte> Field(long offset, int size) =>
            bytes.AsSpan((int)(offset - layout.RankedOffset), size);
        int Int(long offset) => BinaryPrimitives.ReadInt32LittleEndian(Field(offset, 4));

        if (
            Int(layout.RankedOffset) != 1
            || Field(layout.HasRankOffset, 1)[0] == 0
            || LastRecordStatus != 0
        )
        {
            return Sample(null, "no-rank");
        }

        if (
            !TryDecode(Field(layout.BeforeOffset, RecordBytes), out RankStanding before)
            || !TryDecode(Field(layout.AfterOffset, RecordBytes), out RankStanding after)
        )
        {
            return Sample(null, "bad-record");
        }

        var breakdown = new RankPointsBreakdown(
            Int(layout.BreakdownOffset),
            Int(layout.BreakdownOffset + 4),
            Int(layout.BreakdownOffset + 8),
            Int(layout.BreakdownOffset + 12),
            Int(layout.BreakdownOffset + 16)
        );
        return Sample(new RankResult(before, after, Int(layout.DeltaOffset), breakdown), "ok");
    }

    /// <summary>
    /// One rank as the record keeps it (40 bytes; the client converts it with 2.57.0.98348 fn
    /// <c>0x1DED180</c>): a 32-bit variant, 0 while placing (then the placement value) or 1 when
    /// ranked; for a ranked one the league (0 Bronze to 6 Grandmaster, fn <c>0x1EF6A10</c>), the
    /// division byte at +8, the points at +12, the phase at +16 (0 none, 1 promotion with its
    /// value at +20, 2 demotion), and for Grandmaster a flag byte at +32 and the ladder position at
    /// +36. False when the bytes are not a rank.
    /// </summary>
    internal static bool TryDecode(ReadOnlySpan<byte> bytes, out RankStanding standing)
    {
        standing = null;
        if (bytes.Length < RecordBytes)
        {
            return false;
        }

        switch (Int32(bytes, 0))
        {
            case 0:
                int placement = Int32(bytes, 4);
                if (placement < 0)
                {
                    return false;
                }

                standing = new RankStanding(RankLeague.Placement, 0, 0, PlacementGames: placement);
                return true;
            case 1:
                int league = Int32(bytes, 4);
                int phase = Int32(bytes, 16);
                if (league is < 0 or > 6 || phase is < 0 or > 2)
                {
                    return false;
                }

                bool grandmaster = league == 6;
                standing = new RankStanding(
                    (RankLeague)(league + 1),
                    bytes[8],
                    Int32(bytes, 12),
                    (RankPhase)phase,
                    phase == 1 ? Int32(bytes, 20) : 0,
                    grandmaster && bytes[32] != 0 ? Int32(bytes, 36) : null
                );
                return true;
            default:
                return false;
        }
    }

    private static int Int32(ReadOnlySpan<byte> bytes, int offset) =>
        BinaryPrimitives.ReadInt32LittleEndian(bytes.Slice(offset, 4));

    /// <summary>Closes the process handle this reader opened.</summary>
    public void Dispose()
    {
        attachment.Dispose();
    }

    private void Forget()
    {
        LastRecord = 0;
        LastRecordBytes = null;
        LastRecordStatus = null;
        LastRankLabel = null;
    }

    /// <summary>
    /// The <c>CScreenScore</c> frame: the cached one while its vtable holds, else a child of the
    /// menu root named by its class (2.57.0.98348: <c>CScreenScore</c> under <c>CGlueUI</c>, the
    /// menu root, under <c>CLayer</c> under <c>CRoot</c>).
    /// </summary>
    private long FindScoreScreen(IProcessMemory memory, long root)
    {
        if (
            scoreScreen != 0
            && ModuleScanner.TryReadPointer(memory, scoreScreen, out long vtable)
            && vtable == scoreScreenVtable
            && ModuleScanner.TryReadPointer(
                memory,
                scoreScreen + frameLayout.ParentOffset,
                out long parent
            )
            && parent == root
        )
        {
            return scoreScreen;
        }

        scoreScreen = 0;
        scoreScreenVtable = 0;
        foreach ((long child, long childVtable) in FrameTree.Children(memory, frameLayout, root))
        {
            if (ClassOf(memory, childVtable) == ScoreScreen)
            {
                scoreScreen = child;
                scoreScreenVtable = childVtable;
                break;
            }
        }

        return scoreScreen;
    }

    private string ClassOf(IProcessMemory memory, long vtable)
    {
        if (!classes.TryGetValue(vtable, out string name))
        {
            name = FrameClass.Name(memory, frameLayout, vtable, moduleBase, moduleSize);
            classes[vtable] = name;
        }

        return name;
    }

    private void UseModule(ClientModule module)
    {
        if (
            pid == module.ProcessId
            && startedAt == module.StartedAt
            && moduleBase == module.BaseAddress
            && moduleSize == module.Size
        )
        {
            return;
        }

        // A relaunched client starts over: its global, frames and record are its own.
        pid = module.ProcessId;
        startedAt = module.StartedAt;
        moduleBase = module.BaseAddress;
        moduleSize = module.Size;
        discovered = false;
        globalRva = 0;
        rediscoverAt = default;
        lastScan = null;
        classes.Clear();
        scoreScreen = 0;
        scoreScreenVtable = 0;
        frameLayout = FrameTreeLayout.Default;
        layout = MatchRankLayout.Default;
    }

    /// <summary>
    /// Finds the screen-state global (the menu root) in the client's code scan, which this reader
    /// shares with the screen readers that read the same <see cref="HeroesClientProcess"/>.
    /// </summary>
    private void Discover(
        IProcessMemory memory,
        ClientModule module,
        HeroesClientVersion build,
        ScreenScans scans
    )
    {
        discovered = true;
        DateTimeOffset now = time.GetUtcNow();
        rediscoverAt = now + RediscoverAfter;
        BuildProfile profile = profiles.Resolve(build);
        frameLayout = profile.FrameTree ?? FrameTreeLayout.Default;
        layout = profile.MatchRank ?? MatchRankLayout.Default;
        ScreenScan scan = scans.For(memory, module, lastScan, now);
        lastScan = scan;
        if (scan.Sections == null)
        {
            reason = "read-failed";
            return;
        }

        List<long> found = scan.ScreenGlobals;
        if (
            !LoadingScreenPattern.TryAgree(found, out long rva, out _)
            || rva <= 0
            || rva > moduleSize - 8
        )
        {
            reason = found.Count == 0 ? "unsupported-build" : "pattern-disagreed";
            return;
        }

        globalRva = rva;
        reason = "pattern";
    }

    private static long ReadPointer(IProcessMemory memory, long address) =>
        ModuleScanner.TryReadPointer(memory, address, out long value) ? value : 0;

    private static bool TryRead(IProcessMemory memory, long address, byte[] buffer) =>
        address > 0 && memory.TryRead(address, buffer);
}
