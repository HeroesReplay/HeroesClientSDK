using System;
using System.Collections.Generic;
using System.Text;

namespace HeroesClientSDK;

/// <summary>
/// Finds the client's game-launch manager and where it keeps the last game-launch result. The
/// manager is a singleton that its accessor creates on first use:
/// <c>sub rsp,28h; cmp qword ptr [G],0; jne; mov ecx,SIZE; call alloc; test rax,rax; je;
/// mov rcx,rax; call ctor; mov [G],rax</c>. Both loads of <c>G</c> must agree, and <c>SIZE</c> is
/// large (0x36958 on 2.57). When a launch fails, the manager stores the result code (1 to 24) and
/// shows the message with that index in the <c>@UI/GameLaunch*</c> table (see
/// <see cref="GameLaunchTable"/>): <c>cmp eax,2; jne; movsxd rdi,[rdx+4]; lea eax,[rdi-1];
/// cmp eax,17h; ja; mov [rcx+RESULT],edi</c>. A new launch clears it. On 2.57.0.98348 the global
/// is RVA 0x3771BA8 and on 2.57.0.98304 RVA 0x3772BA8; both store the result at +0x08, and each
/// pattern has one site.
/// </summary>
internal static class GameLaunchPattern
{
    public const int CreatorWidth = 44;
    public const int ResultWidth = 24;
    private const int MinSize = 0x10000;
    private const int MaxSize = 0x100000;

    private static readonly int[] Creator =
    {
        0x48,
        0x83,
        0xEC,
        0x28,
        0x48,
        0x83,
        0x3D,
        -1,
        -1,
        -1,
        -1,
        0x00,
        0x75,
        -1,
        0xB9,
        -1,
        -1,
        -1,
        -1,
        0xE8,
        -1,
        -1,
        -1,
        -1,
        0x48,
        0x85,
        0xC0,
        0x74,
        -1,
        0x48,
        0x8B,
        0xC8,
        0xE8,
        -1,
        -1,
        -1,
        -1,
        0x48,
        0x89,
        0x05,
        -1,
        -1,
        -1,
        -1,
    };

    private static readonly int[] Result =
    {
        0x83,
        0xF8,
        0x02,
        0x0F,
        0x85,
        -1,
        -1,
        -1,
        -1,
        0x48,
        0x63,
        0x7A,
        0x04,
        0x8D,
        0x47,
        0xFF,
        0x83,
        0xF8,
        -1,
        0x77,
        -1,
        0x89,
        0x79,
        -1,
    };

    public const int StateWidth = 23;

    // mov rax,[G]; mov r64,rcx; cmp dword ptr [rax+STATE],0; jne short; cmp dword ptr [rax+disp32],0
    private static readonly int[] State =
    {
        0x48,
        0x8B,
        0x05,
        -1,
        -1,
        -1,
        -1,
        0x48,
        0x8B,
        -1,
        0x83,
        0x78,
        -1,
        0x00,
        0x75,
        -1,
        0x83,
        0xB8,
        -1,
        -1,
        -1,
        -1,
        0x00,
    };

    /// <summary>
    /// The launch state's offset at each site that tests it, with the global the site loads
    /// (<c>mov rax,[G]; mov rbx,rcx; cmp dword ptr [rax+20h],0; jne; cmp dword ptr [rax+100E8h],0</c>,
    /// two sites on 2.57). The caller keeps the sites that load the manager's global.
    /// </summary>
    public static List<(long Global, int Offset)> FindStateOffsets(
        ReadOnlySpan<byte> bytes,
        long rva
    )
    {
        var found = new List<(long Global, int Offset)>();
        for (int i = 0; i + StateWidth <= bytes.Length; i++)
        {
            if (Matches(bytes, i, State))
            {
                long global = rva + i + 7 + BitConverter.ToInt32(bytes.Slice(i + 3, 4));
                found.Add((global, bytes[i + 12]));
            }
        }

        return found;
    }

    /// <summary>The global RVA of each creator site in <paramref name="bytes"/> at <paramref name="rva"/>.</summary>
    public static List<long> FindGlobals(ReadOnlySpan<byte> bytes, long rva)
    {
        var found = new List<long>();
        for (int i = 0; i + CreatorWidth <= bytes.Length; i++)
        {
            if (!Matches(bytes, i, Creator))
            {
                continue;
            }

            int size = BitConverter.ToInt32(bytes.Slice(i + 15, 4));
            if (size < MinSize || size > MaxSize)
            {
                continue;
            }

            long site = rva + i;
            long first = site + 12 + BitConverter.ToInt32(bytes.Slice(i + 7, 4));
            long second = site + CreatorWidth + BitConverter.ToInt32(bytes.Slice(i + 40, 4));
            if (first == second)
            {
                found.Add(first);
            }
        }

        return found;
    }

    /// <summary>The result field's offset at each store site in <paramref name="bytes"/>.</summary>
    public static List<int> FindResultOffsets(ReadOnlySpan<byte> bytes)
    {
        var found = new List<int>();
        for (int i = 0; i + ResultWidth <= bytes.Length; i++)
        {
            if (Matches(bytes, i, Result))
            {
                found.Add(bytes[i + ResultWidth - 1]);
            }
        }

        return found;
    }

    /// <summary>Exactly one value, or every site naming the same one.</summary>
    public static bool TryAgree<T>(IReadOnlyList<T> sites, out T value)
    {
        value = default;
        if (sites == null || sites.Count == 0)
        {
            return false;
        }

        for (int i = 1; i < sites.Count; i++)
        {
            if (!EqualityComparer<T>.Default.Equals(sites[i], sites[0]))
            {
                return false;
            }
        }

        value = sites[0];
        return true;
    }

    private static bool Matches(ReadOnlySpan<byte> b, int i, int[] pattern)
    {
        for (int k = 0; k < pattern.Length; k++)
        {
            if (pattern[k] >= 0 && b[i + k] != pattern[k])
            {
                return false;
            }
        }

        return true;
    }
}

/// <summary>
/// The client's table of game-launch messages: <c>{ char* key, long length }</c> entries indexed
/// by the launch result code, starting with an empty entry 0 and then
/// <c>"@UI/GameLaunchGenericLaunchFailure"</c>. The names come from the client, so no build needs
/// a code list. On 2.57 there are 24: 10 <c>GameLaunchBaseBuildMissing</c>, 13
/// <c>GameLaunchVersionDownloadFailure</c>, 15 <c>GameLaunchDataBuildNumMismatch</c>.
/// </summary>
internal static class GameLaunchTable
{
    public const string Anchor = "@UI/GameLaunchGenericLaunchFailure";
    public const string Prefix = "@UI/";
    private const int EntrySize = 16;
    private const int MaxEntries = 64;

    /// <summary>The keys by result code without <c>@UI/</c> (entry 0 is empty), or an empty list.</summary>
    public static List<string> Find(byte[] data, long dataRva, long moduleBase, long moduleSize)
    {
        var keys = new List<string>();
        if (data == null || data.Length < EntrySize * 2)
        {
            return keys;
        }

        byte[] anchor = Encoding.ASCII.GetBytes(Anchor + "\0");
        int at = data.AsSpan().IndexOf(anchor);
        if (at < 0 || (at > 0 && data[at - 1] != 0))
        {
            return keys;
        }

        long stringVa = moduleBase + dataRva + at;
        int first = -1;
        for (int i = EntrySize; i + EntrySize <= data.Length; i += 8)
        {
            if (
                BitConverter.ToInt64(data, i) == stringVa
                && BitConverter.ToInt64(data, i + 8) == Anchor.Length
                && BitConverter.ToInt64(data, i - EntrySize + 8) == 0
            )
            {
                first = i - EntrySize;
                break;
            }
        }

        if (first < 0)
        {
            return keys;
        }

        keys.Add(string.Empty);
        for (int entry = first + EntrySize; entry + EntrySize <= data.Length; entry += EntrySize)
        {
            long pointer = BitConverter.ToInt64(data, entry);
            long length = BitConverter.ToInt64(data, entry + 8);
            long offset = pointer - moduleBase - dataRva;
            if (
                length <= Prefix.Length
                || length > 120
                || pointer < moduleBase
                || pointer >= moduleBase + moduleSize
                || offset < 0
                || offset + length > data.Length
                || keys.Count >= MaxEntries
            )
            {
                break;
            }

            string key = Encoding.ASCII.GetString(data, (int)offset, (int)length);
            if (!key.StartsWith(Prefix, StringComparison.Ordinal))
            {
                break;
            }

            keys.Add(key.Substring(Prefix.Length));
        }

        return keys;
    }
}
