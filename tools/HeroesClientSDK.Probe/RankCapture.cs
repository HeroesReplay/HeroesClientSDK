using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using HeroesClientSDK;

/// <summary>
/// <c>heroes-client-probe --rank</c> (HeroesClientSDK#19): the score screen's Storm League result
/// of one client, read-only, as one JSON object. It exists to confirm, on a real ranked game, the
/// layout found in the 2.57.0.98348 image before it becomes an SDK reader, so next to the decoded
/// fields it keeps the raw bytes they come from.
/// </summary>
/// <remarks>
/// Found in the 2.57.0.98348 image and at the home screen of a live client (2026-10-09):
/// <list type="bullet">
/// <item><c>CScreenScore</c> (a child of <c>CGlueUI</c>, alive from start-up) keeps the local
/// player's end-of-game record at <c>+0x260</c> (null until a game ends) and its
/// <c>CPlayerRewardsPanel</c> at <c>+0x280</c>. <c>CPlayerRewardsPanel::SetData</c> (fn
/// <c>0x10119D0</c>) keeps the record again at <c>+0x1F8</c>.</item>
/// <item>The record: <c>+0x1680</c> is 1 and <c>+0x1684</c> non-zero when it has a rank result,
/// <c>+0x205C</c> is 0 when it reads; the rank before at <c>+0x1688</c> and after at
/// <c>+0x16B0</c> (40 bytes each: variant 0 placement or 1 ranked, league, division byte, points,
/// phase, phase value, ..., the Grandmaster position), the total points change at
/// <c>+0x16D8</c>, and five ints from <c>+0x16DC</c>: match points, the favored-team points,
/// the catch-up bonus, performance points and the deserter penalty.</item>
/// <item>The panel's own copy: the ranks at <c>+0x230</c> and <c>+0x248</c> (0x18 bytes: league 2
/// placement, 3 to 9 Bronze to Grandmaster; phase; division; points; phase value), placement
/// games at <c>+0x260</c>, the total at <c>+0x390</c>, the breakdown list at <c>+0x2E8</c>
/// (count, then the items at <c>+0x10</c>: 12 bytes of float points, 1, and the
/// <c>@UI/RewardItem</c> kind), and the rank label (a <c>CCountdownLabel</c>) at
/// <c>+0x168</c>.</item>
/// </list>
/// </remarks>
internal sealed class RankCapture : IDisposable
{
    private const int ScreenRecord = 0x260;
    private const int ScreenPanel = 0x280;
    private const int PanelRecord = 0x1F8;
    private const int PanelRankLabel = 0x168;
    private const int PanelBefore = 0x230;
    private const int PanelAfter = 0x248;
    private const int PanelPlacement = 0x260;
    private const int PanelTotal = 0x390;
    private const int PanelBreakdown = 0x2E8;
    private const int RecordFlags = 0x898;
    private const int RecordRanked = 0x1680;
    private const int RecordBefore = 0x1688;
    private const int RecordAfter = 0x16B0;
    private const int RecordDelta = 0x16D8;
    private const int RecordBreakdown = 0x16DC;
    private const int RecordStatus = 0x205C;
    private const int MaxBreakdown = 16;

    private static readonly string[] Leagues =
    {
        "none",
        "none",
        "placement",
        "Bronze",
        "Silver",
        "Gold",
        "Platinum",
        "Diamond",
        "Master",
        "Grandmaster",
    };

    private static readonly string[] Kinds =
    {
        "Game",
        "Completion",
        "Win",
        "BrawlWinRoundLoseGame",
        "GameLength",
        "FirstWinOfDay",
        "TimePeriod",
        "IGRTimePeriod",
        "RepeatingTimePeriod",
        "RankMatchPoints",
        "ReservedPoints",
        "RankPromotionPoints",
        "RankDemotionPoints",
        "OpponentFavoredPoints",
        "YouFavoredPoints",
        "RankCatchUpBonus",
        "PerformancePoints",
        "RankDeserterPenaltyPoints",
    };

    private readonly ClientScreen screens = new();
    private readonly Dictionary<long, string> classes = new();
    private HeroesClientProcess attached;
    private long menuGlobalRva;

    public object Read(Process client)
    {
        if (attached is not { Ok: true })
        {
            attached?.Dispose();
            attached = HeroesClientProcess.Attach(client);
            menuGlobalRva = 0;
            classes.Clear();
        }

        if (!attached.Ok)
        {
            return new { attached = attached.Reason };
        }

        string build = attached.DetectedVersion?.ToString();
        ClientScreenSample screen = screens.Read(attached);
        if (menuGlobalRva == 0)
        {
            ClientScreenDiscovery menus = ClientDiscovery.Run(attached).Menus;
            menuGlobalRva = menus.GlobalRva;
            if (menuGlobalRva == 0)
            {
                return new
                {
                    build,
                    screen = screen.Screen.ToString(),
                    error = $"menu root not found ({menus.Reason})",
                };
            }
        }

        IProcessMemory memory = attached.Memory;
        long moduleBase = attached.Module.BaseAddress;
        long scoreScreen = FindScoreScreen(memory, Pointer(memory, moduleBase + menuGlobalRva));
        if (scoreScreen == 0)
        {
            return new
            {
                build,
                screen = screen.Screen.ToString(),
                error = "no CScreenScore",
            };
        }

        long record = Pointer(memory, scoreScreen + ScreenRecord);
        long panel = Pointer(memory, scoreScreen + ScreenPanel);
        bool isPanel = ClassOf(memory, panel) == "CPlayerRewardsPanel";
        return new
        {
            build,
            screen = screen.Screen.ToString(),
            scoreScreen = Hex(scoreScreen),
            record = record == 0 ? null : ReadRecord(memory, record),
            panel = isPanel ? ReadPanel(memory, panel) : null,
            panelClass = isPanel ? null : ClassOf(memory, panel),
        };
    }

    /// <summary>A one-line summary of a capture for the console.</summary>
    public static string Summary(JsonElement capture)
    {
        string Get(JsonElement element, string name) =>
            element.ValueKind == JsonValueKind.Object
            && element.TryGetProperty(name, out JsonElement value)
                ? value.ToString()
                : null;

        string text = $"{Get(capture, "build")} screen {Get(capture, "screen")}";
        if (Get(capture, "error") is string error)
        {
            return $"{text} {error}";
        }

        if (
            !capture.TryGetProperty("record", out JsonElement record)
            || record.ValueKind != JsonValueKind.Object
        )
        {
            return $"{text} no end-of-game record";
        }

        text +=
            $" record {Get(record, "address")} ranked {Get(record, "ranked")} has-rank {Get(record, "hasRank")} status {Get(record, "status")} delta {Get(record, "deltaPoints")} breakdown {Get(record, "breakdown")}";
        if (
            capture.TryGetProperty("panel", out JsonElement panel)
            && panel.ValueKind == JsonValueKind.Object
        )
        {
            text +=
                $" | panel {Rank(panel, "before")} -> {Rank(panel, "after")} total {Get(panel, "total")} label \"{Get(panel, "rankLabelText")}\"";
        }

        return text;

        string Rank(JsonElement owner, string name) =>
            owner.TryGetProperty(name, out JsonElement rank)
                ? $"{Get(rank, "league")} {Get(rank, "division")} ({Get(rank, "points")} pts, phase {Get(rank, "phase")})"
                : "?";
    }

    private object ReadRecord(IProcessMemory memory, long record)
    {
        byte[] rank = Bytes(memory, record + RecordRanked, 0xA0);
        byte[] flags = Bytes(memory, record + RecordFlags, 0x10);
        byte[] tail = Bytes(memory, record + 0x2040, 0x30);
        if (rank == null)
        {
            return new { address = Hex(record), error = "does not read" };
        }

        int At(int offset) =>
            BinaryPrimitives.ReadInt32LittleEndian(rank.AsSpan(offset - RecordRanked));
        int[] Ints(int offset, int count) =>
            Enumerable.Range(0, count).Select(i => At(offset + 4 * i)).ToArray();

        return new
        {
            address = Hex(record),
            ranked = At(RecordRanked),
            hasRank = rank[0x1684 - RecordRanked],
            status = tail == null
                ? (int?)null
                : BinaryPrimitives.ReadInt16LittleEndian(tail.AsSpan(RecordStatus - 0x2040)),
            flags = flags == null ? null : Hex(BitConverter.ToUInt32(flags, 0)),
            before = Ints(RecordBefore, 10),
            after = Ints(RecordAfter, 10),
            deltaPoints = At(RecordDelta),
            breakdown = Ints(RecordBreakdown, 5),
            raw1680 = Convert.ToHexString(rank),
            raw0898 = flags == null ? null : Convert.ToHexString(flags),
            raw2040 = tail == null ? null : Convert.ToHexString(tail),
        };
    }

    private object ReadPanel(IProcessMemory memory, long panel)
    {
        byte[] ranks = Bytes(memory, panel + PanelBefore, 0x40);
        byte[] totals = Bytes(memory, panel + 0x380, 0x20);
        byte[] list = Bytes(memory, panel + PanelBreakdown, 0x18);
        int Int(byte[] bytes, int offset) =>
            bytes == null ? 0 : BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(offset));

        var breakdown = new List<object>();
        int count = Int(list, 0);
        long items = list == null ? 0 : BitConverter.ToInt64(list, 0x10);
        if (count is > 0 and <= MaxBreakdown && Bytes(memory, items, 12 * count) is byte[] bytes)
        {
            for (int i = 0; i < count; i++)
            {
                int kind = Int(bytes, 12 * i + 8);
                breakdown.Add(
                    new
                    {
                        kind,
                        name = kind >= 0 && kind < Kinds.Length ? Kinds[kind] : null,
                        points = BitConverter.ToSingle(bytes, 12 * i),
                        flag = Int(bytes, 12 * i + 4),
                    }
                );
            }
        }

        long label = Pointer(memory, panel + PanelRankLabel);
        byte[] counter = Bytes(memory, label + 0x200, 0x18);
        return new
        {
            address = Hex(panel),
            record = Hex(Pointer(memory, panel + PanelRecord)),
            before = Rank(ranks, 0),
            after = Rank(ranks, PanelAfter - PanelBefore),
            placementGames = Int(ranks, PanelPlacement - PanelBefore),
            word264 = Int(ranks, 0x264 - PanelBefore),
            total = Int(totals, PanelTotal - 0x380),
            flags = Hex((uint)Int(totals, 0x398 - 0x380)),
            breakdownCount = count,
            breakdown,
            rankLabelClass = ClassOf(memory, label),
            rankLabelText = DialogText.Label(
                memory,
                DialogTextLayout.Default,
                panel + PanelRankLabel,
                vtable => ClassOfVtable(memory, vtable)
            ),
            rankLabelTo = Int(counter, 0),
            rankLabelFrom = Int(counter, 4),
            rankLabelValue = Int(counter, 8),
            raw230 = ranks == null ? null : Convert.ToHexString(ranks),
            raw380 = totals == null ? null : Convert.ToHexString(totals),
            raw2E8 = list == null ? null : Convert.ToHexString(list),
            rawLabel200 = counter == null ? null : Convert.ToHexString(counter),
        };
    }

    private static object Rank(byte[] bytes, int offset)
    {
        if (bytes == null)
        {
            return null;
        }

        int Int(int at) => BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(offset + at));
        int league = Int(0);
        return new
        {
            league = league >= 0 && league < Leagues.Length
                ? Leagues[league]
                : league.ToString(CultureInfo.InvariantCulture),
            leagueValue = league,
            phase = Int(4),
            division = Int(8),
            points = Int(12),
            phaseValue = Int(16),
            word14 = Int(20),
        };
    }

    // CScreenScore is a child of CGlueUI, which is under a CLayer under the top frame. Looked up
    // on every read: the glue UI can be rebuilt after a match.
    private long FindScoreScreen(IProcessMemory memory, long menuRoot)
    {
        FrameTreeLayout layout = FrameTreeLayout.Default;
        long top = FrameTree.Top(memory, layout, menuRoot);
        var level = new List<long> { top };
        for (int depth = 0; depth < 3 && level.Count > 0; depth++)
        {
            var next = new List<long>();
            foreach (long frame in level)
            {
                foreach ((long child, long vtable) in FrameTree.Children(memory, layout, frame))
                {
                    if (ClassOfVtable(memory, vtable) == "CScreenScore")
                    {
                        return child;
                    }

                    next.Add(child);
                }
            }

            level = next;
        }

        return 0;
    }

    private string ClassOf(IProcessMemory memory, long frame) =>
        frame == 0 ? null : ClassOfVtable(memory, Pointer(memory, frame));

    private string ClassOfVtable(IProcessMemory memory, long vtable)
    {
        if (vtable == 0)
        {
            return null;
        }

        if (!classes.TryGetValue(vtable, out string name))
        {
            name = FrameClass.Name(
                memory,
                FrameTreeLayout.Default,
                vtable,
                attached.Module.BaseAddress,
                attached.Module.Size
            );
            classes[vtable] = name;
        }

        return name;
    }

    private static long Pointer(IProcessMemory memory, long address)
    {
        byte[] bytes = Bytes(memory, address, 8);
        return bytes == null ? 0 : BitConverter.ToInt64(bytes, 0);
    }

    private static byte[] Bytes(IProcessMemory memory, long address, int length)
    {
        byte[] bytes = new byte[length];
        return address > 0 && memory.TryRead(address, bytes) ? bytes : null;
    }

    private static string Hex(long value) =>
        "0x" + value.ToString("X", CultureInfo.InvariantCulture);

    public void Dispose()
    {
        screens.Dispose();
        attached?.Dispose();
    }
}
