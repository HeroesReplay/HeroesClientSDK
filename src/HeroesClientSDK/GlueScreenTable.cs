using System;
using System.Collections.Generic;
using System.Text;

namespace HeroesClientSDK;

/// <summary>
/// The client's table of menu screen templates, in screen-index order: entries of
/// <c>{ char* path, long length }</c> such as <c>"ScreenHome/ScreenHome", 21</c>. The index of an
/// entry is the screen's bit in the shown-screens mask and its slot in the frame array, so the
/// names come from the client itself and no build needs its own index list. On 2.57.0.98304 there
/// are 30 screens: <c>ScreenLoading</c> is 5, <c>ScreenLoginUnified</c> 6, <c>ScreenHome</c> 8,
/// <c>ScreenScore</c> 15.
/// </summary>
internal static class GlueScreenTable
{
    public const string Anchor = "ScreenHome/ScreenHome";
    public const int MaxScreens = 64;
    private const int EntrySize = 16;
    private const int MaxNameLength = 127;

    /// <summary>
    /// Finds the table in a read-only data section (<paramref name="data"/> at
    /// <paramref name="dataRva"/>) of a module loaded at <paramref name="moduleBase"/>. Returns
    /// the screen names by index (the part before the slash), or an empty list.
    /// </summary>
    public static List<string> Find(byte[] data, long dataRva, long moduleBase, long moduleSize)
    {
        var names = new List<string>();
        if (data == null || data.Length < EntrySize)
        {
            return names;
        }

        byte[] anchor = Encoding.ASCII.GetBytes(Anchor + "\0");
        int at = data.AsSpan().IndexOf(anchor);
        while (at >= 0)
        {
            // The string must start there, not end a longer one.
            if (at == 0 || data[at - 1] == 0)
            {
                long stringVa = moduleBase + dataRva + at;
                int entry = FindEntry(data, stringVa, Anchor.Length);
                if (entry >= 0)
                {
                    return Read(data, dataRva, moduleBase, moduleSize, entry);
                }
            }

            int next = data.AsSpan(at + 1).IndexOf(anchor);
            at = next < 0 ? -1 : at + 1 + next;
        }

        return names;
    }

    private static int FindEntry(byte[] data, long stringVa, int length)
    {
        for (int i = 0; i + EntrySize <= data.Length; i += 8)
        {
            if (
                BitConverter.ToInt64(data, i) == stringVa
                && BitConverter.ToInt64(data, i + 8) == length
            )
            {
                return i;
            }
        }

        return -1;
    }

    private static List<string> Read(
        byte[] data,
        long dataRva,
        long moduleBase,
        long moduleSize,
        int anchorEntry
    )
    {
        int start = anchorEntry;
        while (
            start - EntrySize >= 0
            && TryName(data, dataRva, moduleBase, moduleSize, start - EntrySize, out _)
        )
        {
            start -= EntrySize;
        }

        var names = new List<string>();
        for (
            int entry = start;
            entry + EntrySize <= data.Length && names.Count < MaxScreens;
            entry += EntrySize
        )
        {
            if (!TryName(data, dataRva, moduleBase, moduleSize, entry, out string name))
            {
                break;
            }

            names.Add(name);
        }

        return names;
    }

    /// <summary>A screen entry is a template path with a slash: "ScreenX/ScreenX".</summary>
    private static bool TryName(
        byte[] data,
        long dataRva,
        long moduleBase,
        long moduleSize,
        int entry,
        out string name
    )
    {
        name = null;
        long pointer = BitConverter.ToInt64(data, entry);
        long length = BitConverter.ToInt64(data, entry + 8);
        if (length <= 0 || length > MaxNameLength)
        {
            return false;
        }

        long rva = pointer - moduleBase;
        long offset = rva - dataRva;
        if (rva <= 0 || rva >= moduleSize || offset < 0 || offset + length >= data.Length)
        {
            return false;
        }

        int at = (int)offset;
        if (data[at + (int)length] != 0)
        {
            return false;
        }

        for (int i = 0; i < length; i++)
        {
            byte c = data[at + i];
            if (c < 0x20 || c >= 0x7F)
            {
                return false;
            }
        }

        string path = Encoding.ASCII.GetString(data, at, (int)length);
        int slash = path.IndexOf('/', StringComparison.Ordinal);
        if (slash <= 0)
        {
            return false;
        }

        name = path.Substring(0, slash);
        return true;
    }
}
