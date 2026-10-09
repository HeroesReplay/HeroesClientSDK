using System;
using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using HeroesClientSDK;

/// <summary>
/// <c>heroes-client-probe --rank</c> (HeroesClientSDK#19): one client's <see cref="MatchRank"/>
/// read, with the screen it shows (<see cref="ClientScreen"/>, on the same attachment) and, until a
/// ranked game confirms the layout, the evidence behind the read: the end-of-game record's address
/// and raw bytes, and the rank label's text as the score screen shows it.
/// </summary>
internal sealed class RankCapture : IDisposable
{
    internal static readonly JsonSerializerOptions Json = new()
    {
        Converters = { new JsonStringEnumConverter() },
    };

    private readonly ClientScreen screens = new();
    private readonly MatchRank rank = new();
    private HeroesClientProcess attached;

    public object Read(Process client)
    {
        if (attached is not { Ok: true })
        {
            attached?.Dispose();
            attached = HeroesClientProcess.Attach(client);
        }

        ClientScreenSample screen = screens.Read(attached);
        MatchRankSample sample = rank.Read(attached);
        return new
        {
            build = sample.ClientVersion?.ToString(),
            screen = screen.Screen.ToString(),
            reason = sample.Reason,
            result = sample.Result,
            scoreScreen = Hex(rank.LastScoreScreen),
            record = Hex(rank.LastRecord),
            recordStatus = rank.LastRecordStatus,
            rankLabel = rank.LastRankLabel,
            recordBytes = rank.LastRecordBytes is byte[] bytes ? Convert.ToHexString(bytes) : null,
        };
    }

    /// <summary>A one-line summary of a capture for the console.</summary>
    public static string Summary(JsonElement capture)
    {
        string Get(JsonElement element, string name) =>
            element.ValueKind == JsonValueKind.Object
            && element.TryGetProperty(name, out JsonElement value)
            && value.ValueKind != JsonValueKind.Null
                ? value.ToString()
                : null;

        string text =
            $"{Get(capture, "build")} screen {Get(capture, "screen")} rank {Get(capture, "reason")}";
        if (Get(capture, "rankLabel") is string label && label.Length > 0)
        {
            text += $" label \"{label}\"";
        }

        if (
            !capture.TryGetProperty("result", out JsonElement result)
            || result.ValueKind != JsonValueKind.Object
        )
        {
            return text;
        }

        return text
            + $" | {Rank(result, "Before")} -> {Rank(result, "After")} delta {Get(result, "DeltaPoints")} breakdown {Get(result, "Breakdown")}";

        string Rank(JsonElement owner, string name) =>
            owner.TryGetProperty(name, out JsonElement standing)
                ? $"{Get(standing, "League")} {Get(standing, "Division")} ({Get(standing, "Points")} pts, {Get(standing, "Phase")})"
                : "?";
    }

    private static string Hex(long value) =>
        value == 0 ? null : "0x" + value.ToString("X", CultureInfo.InvariantCulture);

    public void Dispose()
    {
        screens.Dispose();
        rank.Dispose();
        attached?.Dispose();
    }
}
