using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Text;

namespace HeroesClientSDK;

/// <summary>One PNG of a client's game window: where it went, its size, and whether it was black.</summary>
/// <param name="Ok">True when the PNG was written.</param>
/// <param name="Reason">"ok", "no-window", "empty-window" or "print-failed".</param>
/// <param name="Path">The full path of the PNG, or null.</param>
/// <param name="Width">The client area's width in pixels.</param>
/// <param name="Height">The client area's height in pixels.</param>
/// <param name="Black">
/// True when almost every pixel is black. PrintWindow draws menu screens black; a match draws.
/// </param>
public readonly record struct GameWindowCapture(
    bool Ok,
    string Reason,
    string Path = null,
    int Width = 0,
    int Height = 0,
    bool Black = false
);

/// <summary>
/// A client's game window: found by process id, and copied to a PNG with PrintWindow, which draws
/// the window's own frame (DirectX included) even when it is covered or minimised. Nothing here
/// reads the desktop or the client's memory.
/// </summary>
public static class GameWindow
{
    private const int MinWidth = 640;
    private const int MinHeight = 480;

    /// <summary>
    /// The client's game window: the largest visible window of the process whose title names
    /// Heroes of the Storm, else its largest visible window. Zero when it has none.
    /// </summary>
    public static IntPtr Find(int processId)
    {
        IntPtr best = IntPtr.Zero;
        long bestArea = 0;
        IntPtr titled = IntPtr.Zero;
        long titledArea = 0;
        WindowNativeMethods.EnumWindows(
            (hwnd, _) =>
            {
                WindowNativeMethods.GetWindowThreadProcessId(hwnd, out int owner);
                if (owner != processId || !WindowNativeMethods.IsWindowVisible(hwnd))
                {
                    return true;
                }

                WindowNativeMethods.GetClientRect(hwnd, out WindowNativeMethods.Rect client);
                long width = client.Right - client.Left;
                long height = client.Bottom - client.Top;
                long area = width * height;
                if (area <= 0)
                {
                    return true;
                }

                if (area > bestArea)
                {
                    bestArea = area;
                    best = hwnd;
                }

                if (
                    width >= MinWidth
                    && height >= MinHeight
                    && area > titledArea
                    && Title(hwnd)
                        .Contains("Heroes of the Storm", StringComparison.OrdinalIgnoreCase)
                )
                {
                    titledArea = area;
                    titled = hwnd;
                }

                return true;
            },
            IntPtr.Zero
        );
        return titled != IntPtr.Zero ? titled : best;
    }

    /// <summary>The game window of this client process, or zero.</summary>
    public static IntPtr Find(Process process) =>
        Find((process ?? throw new ArgumentNullException(nameof(process))).Id);

    /// <summary>Writes the client area of the process's game window to a PNG file.</summary>
    public static GameWindowCapture CapturePng(Process process, string path) =>
        CapturePng(Find(process), path);

    /// <summary>Writes the client area of this window to a PNG file.</summary>
    public static GameWindowCapture CapturePng(IntPtr window, string path)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        if (window == IntPtr.Zero)
        {
            return new GameWindowCapture(false, "no-window");
        }

        WindowNativeMethods.GetClientRect(window, out WindowNativeMethods.Rect client);
        int width = client.Right - client.Left;
        int height = client.Bottom - client.Top;
        if (width <= 0 || height <= 0)
        {
            return new GameWindowCapture(false, "empty-window", Width: width, Height: height);
        }

        byte[] bgra = new byte[width * height * 4];
        IntPtr screen = WindowNativeMethods.GetDC(IntPtr.Zero);
        IntPtr memory = WindowNativeMethods.CreateCompatibleDC(screen);
        IntPtr bitmap = WindowNativeMethods.CreateCompatibleBitmap(screen, width, height);
        IntPtr previous = WindowNativeMethods.SelectObject(memory, bitmap);
        try
        {
            // PW_CLIENTONLY (1) with PW_RENDERFULLCONTENT (2): the client area, DirectX included.
            if (
                !WindowNativeMethods.PrintWindow(
                    window,
                    memory,
                    1 | WindowNativeMethods.PwRenderFullContent
                )
            )
            {
                return new GameWindowCapture(false, "print-failed", Width: width, Height: height);
            }

            WindowNativeMethods.SelectObject(memory, previous);
            var header = new WindowNativeMethods.BitmapInfoHeader
            {
                Size = 40,
                Width = width,
                Height = -height,
                Planes = 1,
                BitCount = 32,
            };
            if (
                WindowNativeMethods.GetDIBits(
                    memory,
                    bitmap,
                    0,
                    (uint)height,
                    bgra,
                    ref header,
                    WindowNativeMethods.DibRgbColors
                ) == 0
            )
            {
                return new GameWindowCapture(false, "print-failed", Width: width, Height: height);
            }
        }
        finally
        {
            WindowNativeMethods.SelectObject(memory, previous);
            WindowNativeMethods.DeleteObject(bitmap);
            WindowNativeMethods.DeleteDC(memory);
            WindowNativeMethods.ReleaseDC(IntPtr.Zero, screen);
        }

        string full = System.IO.Path.GetFullPath(path);
        string directory = System.IO.Path.GetDirectoryName(full);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        File.WriteAllBytes(full, Png.Encode(bgra, width, height));
        return new GameWindowCapture(true, "ok", full, width, height, Png.IsMostlyBlack(bgra));
    }

    internal static List<IntPtr> WithVisibleChildren(IntPtr window)
    {
        var windows = new List<IntPtr> { window };
        WindowNativeMethods.EnumChildWindows(
            window,
            (child, _) =>
            {
                if (WindowNativeMethods.IsWindowVisible(child))
                {
                    windows.Add(child);
                }

                return true;
            },
            IntPtr.Zero
        );
        return windows;
    }

    private static string Title(IntPtr hwnd)
    {
        var text = new StringBuilder(256);
        WindowNativeMethods.GetWindowText(hwnd, text, text.Capacity);
        return text.ToString();
    }
}

/// <summary>A minimal PNG writer for top-down 32-bit BGRA pixels (8-bit RGB, no alpha).</summary>
internal static class Png
{
    private static readonly uint[] CrcTable = BuildCrcTable();

    public static byte[] Encode(ReadOnlySpan<byte> bgra, int width, int height)
    {
        byte[] raw = new byte[height * (1 + width * 3)];
        int at = 0;
        for (int y = 0; y < height; y++)
        {
            raw[at++] = 0;
            int row = y * width * 4;
            for (int x = 0; x < width; x++)
            {
                int pixel = row + x * 4;
                raw[at++] = bgra[pixel + 2];
                raw[at++] = bgra[pixel + 1];
                raw[at++] = bgra[pixel];
            }
        }

        using var compressed = new MemoryStream();
        using (var zlib = new ZLibStream(compressed, CompressionLevel.Fastest, leaveOpen: true))
        {
            zlib.Write(raw);
        }

        using var png = new MemoryStream();
        png.Write([0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A]);
        byte[] header = new byte[13];
        BinaryPrimitives.WriteInt32BigEndian(header, width);
        BinaryPrimitives.WriteInt32BigEndian(header.AsSpan(4), height);
        header[8] = 8;
        header[9] = 2;
        WriteChunk(png, "IHDR", header);
        WriteChunk(png, "IDAT", compressed.ToArray());
        WriteChunk(png, "IEND", []);
        return png.ToArray();
    }

    /// <summary>True when at least 99% of pixels have every channel at 8 or below.</summary>
    public static bool IsMostlyBlack(ReadOnlySpan<byte> bgra)
    {
        int pixels = bgra.Length / 4;
        if (pixels == 0)
        {
            return true;
        }

        int lit = 0;
        for (int i = 0; i < pixels; i++)
        {
            int p = i * 4;
            if (bgra[p] > 8 || bgra[p + 1] > 8 || bgra[p + 2] > 8)
            {
                lit++;
            }
        }

        return lit * 100L < pixels;
    }

    private static void WriteChunk(Stream png, string type, byte[] data)
    {
        Span<byte> length = stackalloc byte[4];
        BinaryPrimitives.WriteInt32BigEndian(length, data.Length);
        png.Write(length);
        byte[] typeBytes = Encoding.ASCII.GetBytes(type);
        png.Write(typeBytes);
        png.Write(data);
        uint crc = Crc(Crc(0xFFFFFFFFu, typeBytes), data) ^ 0xFFFFFFFFu;
        Span<byte> crcBytes = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(crcBytes, crc);
        png.Write(crcBytes);
    }

    private static uint Crc(uint crc, byte[] bytes)
    {
        foreach (byte b in bytes)
        {
            crc = CrcTable[(crc ^ b) & 0xFF] ^ (crc >> 8);
        }

        return crc;
    }

    private static uint[] BuildCrcTable()
    {
        var table = new uint[256];
        for (uint n = 0; n < 256; n++)
        {
            uint c = n;
            for (int k = 0; k < 8; k++)
            {
                c = (c & 1) != 0 ? 0xEDB88320u ^ (c >> 1) : c >> 1;
            }

            table[n] = c;
        }

        return table;
    }
}
