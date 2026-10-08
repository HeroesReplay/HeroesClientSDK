using System;
using System.Collections.Generic;

namespace HeroesClientSDK;

/// <summary>
/// Finds where the client's menu root (the object at the screen-state global that
/// <see cref="LoadingScreenPattern"/> finds) keeps its shown-screens mask and its screen frames.
/// The client tests a screen like this when it lays out the menus:
/// <c>mov rax,[r9+MASK]; mov edx,[rcx+8]; bt rax,rdx; jae; mov rcx,[r9+rdx*8+FRAMES]</c>.
/// <c>MASK</c> is a 64-bit mask with one bit per screen index (set while that screen is shown)
/// and <c>FRAMES</c> is the array of screen frames, one per index. On 2.57.0.98304 and
/// 2.57.0.98348 they are 0x1D4 and 0x1F0, and this site is unique.
/// </summary>
internal static class GlueScreenPattern
{
    public const int Width = 24;
    private const int MaskDisplacement = 3;
    private const int FramesDisplacement = 20;

    public readonly record struct Offsets(int Mask, int Frames);

    public static List<Offsets> Find(ReadOnlySpan<byte> bytes)
    {
        var found = new List<Offsets>();
        for (int i = 0; i + Width <= bytes.Length; i++)
        {
            if (!Matches(bytes, i))
            {
                continue;
            }

            int mask = BitConverter.ToInt32(bytes.Slice(i + MaskDisplacement, 4));
            int frames = BitConverter.ToInt32(bytes.Slice(i + FramesDisplacement, 4));
            if (mask > 0 && frames > mask && frames < 0x10000)
            {
                found.Add(new Offsets(mask, frames));
            }
        }

        return found;
    }

    /// <summary>Every site must name the same two offsets.</summary>
    public static bool TryAgree(IReadOnlyList<Offsets> sites, out Offsets offsets)
    {
        offsets = default;
        if (sites == null || sites.Count == 0)
        {
            return false;
        }

        for (int i = 1; i < sites.Count; i++)
        {
            if (sites[i] != sites[0])
            {
                return false;
            }
        }

        offsets = sites[0];
        return true;
    }

    private static bool Matches(ReadOnlySpan<byte> b, int i)
    {
        // 49 8B 81 [mask]  8B 51 08  48 0F A3 D0  73 ??  49 8B 8C D1 [frames]
        return b[i] == 0x49
            && b[i + 1] == 0x8B
            && b[i + 2] == 0x81
            && b[i + 7] == 0x8B
            && b[i + 8] == 0x51
            && b[i + 9] == 0x08
            && b[i + 10] == 0x48
            && b[i + 11] == 0x0F
            && b[i + 12] == 0xA3
            && b[i + 13] == 0xD0
            && b[i + 14] == 0x73
            && b[i + 16] == 0x49
            && b[i + 17] == 0x8B
            && b[i + 18] == 0x8C
            && b[i + 19] == 0xD1;
    }
}
