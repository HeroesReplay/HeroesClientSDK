using System;
using System.Collections.Generic;

namespace HeroesClientSDK;

/// <summary>
/// The 98025 match clock is `movd` of the tick global, `cvtdq2ps`, then `mulss` by the 1/4096
/// speed global. The opcodes stay put when a patch moves those globals. Two sites must agree.
/// </summary>
internal static class MatchClockPattern
{
    private static readonly byte[] Pattern =
    {
        0x84,
        0xC0,
        0x74,
        0x00,
        0x66,
        0x0F,
        0x6E,
        0x05,
        0x00,
        0x00,
        0x00,
        0x00,
        0x0F,
        0x5B,
        0xC0,
        0xF3,
        0x0F,
        0x59,
        0x05,
        0x00,
        0x00,
        0x00,
        0x00,
    };

    private static readonly byte[] Mask =
    {
        1,
        1,
        1,
        0,
        1,
        1,
        1,
        1,
        0,
        0,
        0,
        0,
        1,
        1,
        1,
        1,
        1,
        1,
        1,
        0,
        0,
        0,
        0,
    };

    public const int TickDisplacement = 8;
    public const int SpeedDisplacement = 19;
    public const int MovdEnd = 12;
    public const int MulssEnd = 23;

    public readonly record struct Site(long TickRva, long SpeedRva);

    public static List<Site> Find(ReadOnlySpan<byte> bytes, long byteRva)
    {
        var sites = new List<Site>();
        int width = Pattern.Length;
        if (bytes.Length < width)
        {
            return sites;
        }

        for (int i = 0; i + width <= bytes.Length; i++)
        {
            if (!Matches(bytes, i))
            {
                continue;
            }

            int tickDisp = BitConverter.ToInt32(bytes.Slice(i + TickDisplacement, 4));
            int speedDisp = BitConverter.ToInt32(bytes.Slice(i + SpeedDisplacement, 4));
            long site = byteRva + i;
            sites.Add(new Site(site + MovdEnd + tickDisp, site + MulssEnd + speedDisp));
        }

        return sites;
    }

    public static bool TryAgree(IReadOnlyList<Site> sites, out long tickRva, out long speedRva)
    {
        tickRva = 0;
        speedRva = 0;
        if (sites == null || sites.Count == 0)
        {
            return false;
        }

        tickRva = sites[0].TickRva;
        speedRva = sites[0].SpeedRva;
        if (tickRva <= 0 || speedRva <= 0)
        {
            return false;
        }

        for (int i = 1; i < sites.Count; i++)
        {
            if (sites[i].TickRva != tickRva || sites[i].SpeedRva != speedRva)
            {
                tickRva = 0;
                speedRva = 0;
                return false;
            }
        }

        return true;
    }

    private static bool Matches(ReadOnlySpan<byte> bytes, int at)
    {
        for (int j = 0; j < Pattern.Length; j++)
        {
            if (Mask[j] != 0 && bytes[at + j] != Pattern[j])
            {
                return false;
            }
        }

        return true;
    }
}
