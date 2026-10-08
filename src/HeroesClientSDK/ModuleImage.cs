using System;
using System.IO;
using System.Text;

namespace HeroesClientSDK;

/// <summary>
/// A saved image of a client's main module, served read-only like a live client's memory. The
/// file is what <c>Save-ModuleImage.ps1</c> (skill <c>heroes-client-re</c>) writes: the running
/// client's module read with <c>ReadProcessMemory</c>, laid out by RVA (each section's raw data at
/// its virtual address) with the exe's headers and the runtime base as <c>ImageBase</c>, so the
/// absolute pointers in its tables still point into it. Offset <c>n</c> of the file is address
/// <c>ImageBase + n</c>; nothing outside the module reads.
/// </summary>
internal sealed class ModuleImage : IProcessMemory
{
    private const ushort Pe32Plus = 0x20B;
    private const string VersionKey = "FileVersion";

    private readonly byte[] image;

    private ModuleImage(byte[] image, long baseAddress, string fileVersion, long writtenAt)
    {
        this.image = image;
        Module = new ClientModule(1, baseAddress, image.Length, fileVersion, writtenAt);
    }

    /// <summary>
    /// The module as the readers see it: process 1, the image's base and length, its
    /// <c>FileVersion</c> (or null), and the file's write time as the process start time.
    /// </summary>
    public ClientModule Module { get; }

    /// <summary>
    /// "ok" with the image, "no-image" when the file is missing or unreadable, or "bad-image"
    /// when it is not an x64 module image. Never throws.
    /// </summary>
    public static string TryLoad(string path, out ModuleImage image)
    {
        image = null;
        byte[] bytes;
        long writtenAt;
        try
        {
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            {
                return "no-image";
            }

            bytes = File.ReadAllBytes(path);
            writtenAt = File.GetLastWriteTimeUtc(path).Ticks;
        }
        catch (Exception)
        {
            return "no-image";
        }

        return TryParse(bytes, writtenAt, out image);
    }

    /// <summary>The image in <paramref name="bytes"/>; see <see cref="TryLoad"/>.</summary>
    internal static string TryParse(byte[] bytes, long writtenAt, out ModuleImage image)
    {
        image = null;
        if (
            bytes == null
            || bytes.Length < ModuleScanner.HeaderSize
            || !ModuleScanner.TryParseSections(bytes.AsSpan(0, ModuleScanner.HeaderSize), out _)
        )
        {
            return "bad-image";
        }

        int optional = BitConverter.ToInt32(bytes, 0x3C) + 24;
        if (BitConverter.ToUInt16(bytes, optional) != Pe32Plus)
        {
            return "bad-image";
        }

        long imageBase = BitConverter.ToInt64(bytes, optional + 24);
        if (imageBase <= 0 || imageBase > 0x7FFF_FFFF_FFFF - bytes.Length)
        {
            return "bad-image";
        }

        image = new ModuleImage(bytes, imageBase, FileVersionOf(bytes), writtenAt);
        return "ok";
    }

    /// <inheritdoc />
    public bool TryRead(long address, Span<byte> buffer)
    {
        long offset = address - Module.BaseAddress;
        if (buffer.IsEmpty || offset < 0 || offset > image.Length - buffer.Length)
        {
            return false;
        }

        image.AsSpan((int)offset, buffer.Length).CopyTo(buffer);
        return true;
    }

    /// <summary>
    /// The <c>FileVersion</c> string of the image's version resource, such as
    /// <c>2.57.0.98348</c>, or null. The fixed file info cannot hold a build above 65535, so the
    /// string is read: a UTF-16 <c>FileVersion</c> key, then its value at the next 4-byte boundary.
    /// </summary>
    internal static string FileVersionOf(byte[] bytes)
    {
        byte[] key = Encoding.Unicode.GetBytes(VersionKey + "\0");
        int from = 0;
        while (from < bytes.Length)
        {
            int found = bytes.AsSpan(from).IndexOf(key);
            if (found < 0)
            {
                return null;
            }

            int at = from + found;
            int value = (at + key.Length + 3) & ~3;
            string text = Utf16At(bytes, value);
            if (HeroesClientVersion.TryParse(text) is not null)
            {
                return text.Trim();
            }

            from = at + 2;
        }

        return null;
    }

    private static string Utf16At(byte[] bytes, int at)
    {
        var text = new StringBuilder();
        for (int i = at; i + 1 < bytes.Length && text.Length < 64; i += 2)
        {
            char c = (char)BitConverter.ToUInt16(bytes, i);
            if (c == '\0')
            {
                return text.ToString();
            }

            text.Append(c);
        }

        return null;
    }
}
