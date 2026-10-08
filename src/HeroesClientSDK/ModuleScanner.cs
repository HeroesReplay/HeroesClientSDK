using System;
using System.Collections.Generic;
using System.Text;

namespace HeroesClientSDK;

/// <summary>One PE section of a client module, by RVA.</summary>
internal readonly record struct ModuleSection(
    string Name,
    long VirtualAddress,
    int VirtualSize,
    uint Characteristics
)
{
    private const uint ExecuteFlag = 0x20000000;

    public bool Executable => (Characteristics & ExecuteFlag) != 0;
}

/// <summary>
/// Reads the section table of a client module from its in-memory PE headers and walks a section
/// in chunks. The on-disk exe has its code encrypted, so every pattern is matched against the
/// running client's memory, read-only.
/// </summary>
internal static class ModuleScanner
{
    public const int HeaderSize = 0x1000;
    public const int Chunk = 1 << 20;
    public const int MaxSectionSize = 64 * 1024 * 1024;

    public static bool TrySections(
        Func<long, byte[], bool> read,
        long moduleBase,
        long moduleSize,
        out List<ModuleSection> sections
    )
    {
        sections = new List<ModuleSection>();
        byte[] headers = new byte[HeaderSize];
        if (read == null || moduleBase <= 0 || !read(moduleBase, headers))
        {
            return false;
        }

        if (!TryParseSections(headers, out List<ModuleSection> all))
        {
            return false;
        }

        foreach (ModuleSection section in all)
        {
            if (
                section.VirtualSize > 0
                && section.VirtualAddress > 0
                && section.VirtualAddress + section.VirtualSize <= moduleSize
            )
            {
                sections.Add(section);
            }
        }

        return sections.Count > 0;
    }

    public static bool TryParseSections(
        ReadOnlySpan<byte> headers,
        out List<ModuleSection> sections
    )
    {
        sections = new List<ModuleSection>();
        if (headers.Length < 0x40 || headers[0] != (byte)'M' || headers[1] != (byte)'Z')
        {
            return false;
        }

        int lfanew = BitConverter.ToInt32(headers.Slice(0x3C, 4));
        if (lfanew <= 0 || lfanew + 24 > headers.Length)
        {
            return false;
        }

        if (
            headers[lfanew] != (byte)'P'
            || headers[lfanew + 1] != (byte)'E'
            || headers[lfanew + 2] != 0
            || headers[lfanew + 3] != 0
        )
        {
            return false;
        }

        int count = BitConverter.ToUInt16(headers.Slice(lfanew + 6, 2));
        int optionalSize = BitConverter.ToUInt16(headers.Slice(lfanew + 20, 2));
        int table = lfanew + 24 + optionalSize;
        if (count <= 0 || table + count * 40 > headers.Length)
        {
            return false;
        }

        for (int i = 0; i < count; i++)
        {
            int at = table + i * 40;
            string name = Encoding.ASCII.GetString(headers.Slice(at, 8)).TrimEnd('\0');
            sections.Add(
                new ModuleSection(
                    name,
                    BitConverter.ToUInt32(headers.Slice(at + 12, 4)),
                    BitConverter.ToInt32(headers.Slice(at + 8, 4)),
                    BitConverter.ToUInt32(headers.Slice(at + 36, 4))
                )
            );
        }

        return sections.Count > 0;
    }

    /// <summary>
    /// Calls <paramref name="visit"/> with each readable chunk of a section and the chunk's RVA.
    /// Chunks overlap by <paramref name="overlap"/> bytes so a pattern across a boundary is seen.
    /// </summary>
    public static void Walk(
        Func<long, byte[], bool> read,
        long moduleBase,
        ModuleSection section,
        int overlap,
        Action<byte[], long> visit
    )
    {
        if (section.VirtualSize <= 0 || section.VirtualSize > MaxSectionSize)
        {
            return;
        }

        for (int offset = 0; offset < section.VirtualSize; offset += Chunk)
        {
            int count = Math.Min(section.VirtualSize - offset, Chunk + overlap);
            byte[] slice = new byte[count];
            long rva = section.VirtualAddress + offset;
            if (read(moduleBase + rva, slice))
            {
                visit(slice, rva);
            }
        }
    }

    /// <summary>
    /// The whole section, page by page. Pages that cannot be read stay zero.
    /// </summary>
    public static byte[] ReadSection(
        Func<long, byte[], bool> read,
        long moduleBase,
        ModuleSection section
    )
    {
        if (section.VirtualSize <= 0 || section.VirtualSize > MaxSectionSize)
        {
            return Array.Empty<byte>();
        }

        byte[] bytes = new byte[section.VirtualSize];
        byte[] chunk = new byte[Chunk];
        for (int offset = 0; offset < bytes.Length; offset += Chunk)
        {
            int count = Math.Min(bytes.Length - offset, Chunk);
            byte[] buffer = count == Chunk ? chunk : new byte[count];
            if (read(moduleBase + section.VirtualAddress + offset, buffer))
            {
                Array.Copy(buffer, 0, bytes, offset, count);
                continue;
            }

            for (int page = 0; page < count; page += 0x1000)
            {
                byte[] one = new byte[Math.Min(0x1000, count - page)];
                if (read(moduleBase + section.VirtualAddress + offset + page, one))
                {
                    Array.Copy(one, 0, bytes, offset + page, one.Length);
                }
            }
        }

        return bytes;
    }
}
