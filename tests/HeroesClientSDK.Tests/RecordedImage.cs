using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace HeroesClientSDK.Tests;

/// <summary>
/// A client module served from a few recorded bytes, the way a saved module image serves it:
/// PE headers made from the recorded section table (x64, with the runtime base as the image
/// base), the recorded bytes and strings at their RVAs, and zero everywhere else in the module.
/// Nothing outside the module reads, as the heap does not in an image.
/// </summary>
internal sealed class RecordedImage : IProcessMemory
{
    private const int Pe = 0x80;
    private const int Optional = Pe + 24;
    private const int OptionalSize = 0xF0;

    private readonly List<(long Rva, byte[] Bytes)> chunks = new();

    public RecordedImage(
        long baseAddress,
        long size,
        IReadOnlyList<(string Name, long Rva, int Size, uint Characteristics)> sections,
        IEnumerable<(long Rva, string Hex)> bytes = null,
        IEnumerable<(long Rva, string Text)> strings = null
    )
    {
        BaseAddress = baseAddress;
        Size = size;
        Write(0, Headers(baseAddress, size, sections));
        foreach ((long rva, string hex) in bytes ?? Array.Empty<(long, string)>())
        {
            Write(rva, ClientScreenTests.Hex(hex));
        }

        foreach ((long rva, string text) in strings ?? Array.Empty<(long, string)>())
        {
            Write(rva, Encoding.ASCII.GetBytes(text + "\0"));
        }
    }

    public long BaseAddress { get; }

    public long Size { get; }

    /// <summary>The module as an image serves it: process 1, with this file version.</summary>
    public ClientModule Module(string fileVersion) => new(1, BaseAddress, Size, fileVersion, 1);

    public void Write(long rva, byte[] bytes) => chunks.Add((rva, bytes));

    public bool TryRead(long address, Span<byte> buffer)
    {
        long rva = address - BaseAddress;
        if (buffer.IsEmpty || rva < 0 || rva > Size - buffer.Length)
        {
            return false;
        }

        buffer.Clear();
        foreach ((long at, byte[] bytes) in chunks)
        {
            long from = Math.Max(at, rva);
            long to = Math.Min(at + bytes.Length, rva + buffer.Length);
            if (from < to)
            {
                bytes
                    .AsSpan((int)(from - at), (int)(to - from))
                    .CopyTo(buffer.Slice((int)(from - rva)));
            }
        }

        return true;
    }

    /// <summary>The whole module as a saved image file holds it.</summary>
    public byte[] ToArray()
    {
        byte[] image = new byte[Size];
        TryRead(BaseAddress, image);
        return image;
    }

    private static byte[] Headers(
        long baseAddress,
        long size,
        IReadOnlyList<(string Name, long Rva, int Size, uint Characteristics)> sections
    )
    {
        byte[] headers = new byte[0x1000];
        headers[0] = (byte)'M';
        headers[1] = (byte)'Z';
        BitConverter.GetBytes(Pe).CopyTo(headers, 0x3C);
        headers[Pe] = (byte)'P';
        headers[Pe + 1] = (byte)'E';
        BitConverter.GetBytes((ushort)0x8664).CopyTo(headers, Pe + 4);
        BitConverter.GetBytes((ushort)sections.Count).CopyTo(headers, Pe + 6);
        BitConverter.GetBytes((ushort)OptionalSize).CopyTo(headers, Pe + 20);
        BitConverter.GetBytes((ushort)0x20B).CopyTo(headers, Optional);
        BitConverter.GetBytes(baseAddress).CopyTo(headers, Optional + 24);
        BitConverter.GetBytes((int)size).CopyTo(headers, Optional + 56);
        int table = Optional + OptionalSize;
        foreach (var (section, i) in sections.Select((section, i) => (section, i)))
        {
            int at = table + i * 40;
            Encoding.ASCII.GetBytes(section.Name).CopyTo(headers, at);
            BitConverter.GetBytes(section.Size).CopyTo(headers, at + 8);
            BitConverter.GetBytes((uint)section.Rva).CopyTo(headers, at + 12);
            BitConverter.GetBytes(section.Characteristics).CopyTo(headers, at + 36);
        }

        return headers;
    }
}
