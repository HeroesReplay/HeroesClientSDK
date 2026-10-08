using System;
using System.Collections.Generic;

namespace HeroesClientSDK;

/// <summary>
/// Finds where a message dialog keeps its text (<see cref="DialogTextLayout"/>) in the client's
/// own code, so a patch that moves a field moves the read with it. Two sites, each unique on
/// 2.57.0.98348 and 2.57.0.98304 (HeroesReplay#292, read-only module images, 2026-10-08):
/// <list type="bullet">
/// <item><c>CStandardDialog::ApplyParams</c> (98348 RVA 0x1468441, 98304 0x146F0E1) sets the
/// title, then the message:
/// <c>mov rcx,[rdi+TITLE]; shr rax,2; mov [rbp-8],rax; mov rax,[rcx]; call [rax+SETTEXT];
/// mov eax,[rsi+34h]; lea rcx,[rsi+38h]; shr eax,1; test al,1; je; mov rcx,[rcx];
/// mov eax,[rsi+30h]; lea rdx,[rbp-10h]; mov [rbp-10h],rcx; mov rcx,[rdi+MESSAGE]</c>.</item>
/// <item><c>CLabel::SetText</c> (98348 RVA 0x14B19F9, 98304 0x14B8699) reads the label's
/// current string: <c>mov rbx,[rdi+TEXT]; lea rsi,[empty]; mov rcx,[rbx+STRING]; test rcx,rcx;
/// je; add rcx,HEADER; jmp; mov rcx,rsi</c>.</item>
/// </list>
/// Every displacement and stack offset is a wildcard; only the shape is fixed.
/// </summary>
internal static class DialogTextPattern
{
    public const int LabelsWidth = 58;
    public const int TextWidth = 32;

    /// <summary>The title and message label offsets one ApplyParams site names.</summary>
    public readonly record struct Labels(int Title, int Message);

    /// <summary>The text, string and header offsets one SetText site names.</summary>
    public readonly record struct Text(int TextOffset, int StringOffset, int HeaderOffset);

    public static int Width => Math.Max(LabelsWidth, TextWidth);

    public static List<Labels> FindLabels(ReadOnlySpan<byte> bytes)
    {
        var found = new List<Labels>();
        for (int i = 0; i + LabelsWidth <= bytes.Length; i++)
        {
            if (!MatchesLabels(bytes, i))
            {
                continue;
            }

            int title = BitConverter.ToInt32(bytes.Slice(i + 3, 4));
            int message = BitConverter.ToInt32(bytes.Slice(i + 54, 4));
            if (title > 0 && message > 0 && title < 0x10000 && message < 0x10000)
            {
                found.Add(new Labels(title, message));
            }
        }

        return found;
    }

    public static List<Text> FindText(ReadOnlySpan<byte> bytes)
    {
        var found = new List<Text>();
        for (int i = 0; i + TextWidth <= bytes.Length; i++)
        {
            if (!MatchesText(bytes, i))
            {
                continue;
            }

            int text = BitConverter.ToInt32(bytes.Slice(i + 3, 4));
            int pointer = bytes[i + 17];
            int header = bytes[i + 26];
            if (text > 0 && text < 0x10000)
            {
                found.Add(new Text(text, pointer, header));
            }
        }

        return found;
    }

    /// <summary>
    /// The layout both kinds of site agree on. Every site of a kind must name the same offsets,
    /// and each kind needs at least one site; otherwise false.
    /// </summary>
    public static bool TryAgree(
        IReadOnlyList<Labels> labels,
        IReadOnlyList<Text> text,
        out DialogTextLayout layout
    )
    {
        layout = null;
        if (!TryOne(labels, out Labels label) || !TryOne(text, out Text field))
        {
            return false;
        }

        layout = new DialogTextLayout(
            label.Title,
            label.Message,
            field.TextOffset,
            field.StringOffset,
            field.HeaderOffset
        );
        return true;
    }

    private static bool TryOne<T>(IReadOnlyList<T> sites, out T value)
        where T : struct
    {
        value = default;
        if (sites == null || sites.Count == 0)
        {
            return false;
        }

        for (int i = 1; i < sites.Count; i++)
        {
            if (!sites[i].Equals(sites[0]))
            {
                return false;
            }
        }

        value = sites[0];
        return true;
    }

    private static bool MatchesLabels(ReadOnlySpan<byte> b, int i)
    {
        // 48 8B 8F [title]  48 C1 E8 02  48 89 45 ??  48 8B 01  FF 90 [settext]
        // 8B 46 ??  48 8D 4E ??  D1 E8  A8 01  74 03  48 8B 09  8B 46 ??  48 8D 55 ??
        // 48 89 4D ??  48 8B 8F [message]
        return b[i] == 0x48
            && b[i + 1] == 0x8B
            && b[i + 2] == 0x8F
            && b[i + 7] == 0x48
            && b[i + 8] == 0xC1
            && b[i + 9] == 0xE8
            && b[i + 10] == 0x02
            && b[i + 11] == 0x48
            && b[i + 12] == 0x89
            && b[i + 13] == 0x45
            && b[i + 15] == 0x48
            && b[i + 16] == 0x8B
            && b[i + 17] == 0x01
            && b[i + 18] == 0xFF
            && b[i + 19] == 0x90
            && b[i + 24] == 0x8B
            && b[i + 25] == 0x46
            && b[i + 27] == 0x48
            && b[i + 28] == 0x8D
            && b[i + 29] == 0x4E
            && b[i + 31] == 0xD1
            && b[i + 32] == 0xE8
            && b[i + 33] == 0xA8
            && b[i + 34] == 0x01
            && b[i + 35] == 0x74
            && b[i + 36] == 0x03
            && b[i + 37] == 0x48
            && b[i + 38] == 0x8B
            && b[i + 39] == 0x09
            && b[i + 40] == 0x8B
            && b[i + 41] == 0x46
            && b[i + 43] == 0x48
            && b[i + 44] == 0x8D
            && b[i + 45] == 0x55
            && b[i + 47] == 0x48
            && b[i + 48] == 0x89
            && b[i + 49] == 0x4D
            && b[i + 51] == 0x48
            && b[i + 52] == 0x8B
            && b[i + 53] == 0x8F;
    }

    private static bool MatchesText(ReadOnlySpan<byte> b, int i)
    {
        // 48 8B 9F [text]  48 8D 35 [rel32]  48 8B 4B [string]  48 85 C9  74 06
        // 48 83 C1 [header]  EB 03  48 8B CE
        return b[i] == 0x48
            && b[i + 1] == 0x8B
            && b[i + 2] == 0x9F
            && b[i + 7] == 0x48
            && b[i + 8] == 0x8D
            && b[i + 9] == 0x35
            && b[i + 14] == 0x48
            && b[i + 15] == 0x8B
            && b[i + 16] == 0x4B
            && b[i + 18] == 0x48
            && b[i + 19] == 0x85
            && b[i + 20] == 0xC9
            && b[i + 21] == 0x74
            && b[i + 22] == 0x06
            && b[i + 23] == 0x48
            && b[i + 24] == 0x83
            && b[i + 25] == 0xC1
            && b[i + 27] == 0xEB
            && b[i + 28] == 0x03
            && b[i + 29] == 0x48
            && b[i + 30] == 0x8B
            && b[i + 31] == 0xCE;
    }
}
