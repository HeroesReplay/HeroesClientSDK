using System;
using System.Collections.Generic;
using System.Text;

namespace HeroesClientSDK;

/// <summary>
/// The client's UI frames form one tree. Every frame keeps its parent at
/// <see cref="ParentOffset"/>, its own visible bit (bit 0) in the byte at
/// <see cref="FlagsOffset"/>, and its children as an intrusive list: the parent's
/// <see cref="FirstChildOffset"/> points at the first child's list node (child +
/// <see cref="NodeOffset"/>), each child's <see cref="NextOffset"/> points at the next node, and
/// the list ends at a tagged pointer (low bit set). Measured on 2.57.0.98304 and 2.57.0.98348:
/// the in-game tree has about 86,000 frames, and the awards panel is found after about 6,600 of
/// them (17 ms). Every child's parent pointer must name the frame it was listed under, so a
/// layout that moved reads as not found, never as a wrong frame.
/// </summary>
internal static class FrameTree
{
    public const long ParentOffset = 0x50;
    public const long FlagsOffset = 0x48;
    public const long FirstChildOffset = 0x40;
    public const long NodeOffset = 0x18;
    public const long NextOffset = 0x20;
    public const int MaxFrames = 300_000;
    public const int MaxDepth = 64;

    /// <summary>
    /// The top of the tree: the menu root's grandparent (the menu root sits under a menu
    /// container under the top frame). Zero when the chain does not read.
    /// </summary>
    public static long Top(Func<long, byte[], bool> read, long menuRoot)
    {
        if (!TryPointer(read, menuRoot + ParentOffset, out long container) || container == 0)
        {
            return 0;
        }

        return TryPointer(read, container + ParentOffset, out long top) ? top : 0;
    }

    /// <summary>
    /// Depth-first search from <paramref name="top"/> for frames whose vtable is one of
    /// <paramref name="vtables"/>. Returns the first frame found for each vtable.
    /// </summary>
    public static Dictionary<long, long> Find(
        Func<long, byte[], bool> read,
        long top,
        IReadOnlyCollection<long> vtables
    )
    {
        var found = new Dictionary<long, long>();
        if (top == 0 || vtables == null || vtables.Count == 0)
        {
            return found;
        }

        var stack = new Stack<(long Frame, int Depth)>();
        stack.Push((top, 0));
        int visited = 0;
        while (stack.Count > 0 && visited < MaxFrames && found.Count < vtables.Count)
        {
            (long frame, int depth) = stack.Pop();
            visited++;
            if (!TryPointer(read, frame, out long vtable))
            {
                continue;
            }

            foreach (long wanted in vtables)
            {
                if (vtable == wanted && !found.ContainsKey(wanted))
                {
                    found[wanted] = frame;
                }
            }

            if (depth >= MaxDepth || !TryPointer(read, frame + FirstChildOffset, out long node))
            {
                continue;
            }

            int siblings = 0;
            while (node != 0 && (node & 7) == 0 && siblings++ < MaxFrames)
            {
                long child = node - NodeOffset;
                if (
                    !TryPointer(read, child + ParentOffset, out long parent)
                    || parent != frame
                    || !TryPointer(read, child + NextOffset, out long next)
                )
                {
                    break;
                }

                stack.Push((child, depth + 1));
                node = next;
            }
        }

        return found;
    }

    /// <summary>
    /// True when the frame and every frame above it up to <paramref name="top"/> have their
    /// visible bit set; null when the chain does not read.
    /// </summary>
    public static bool? Shown(Func<long, byte[], bool> read, long frame, long top)
    {
        byte[] flags = new byte[1];
        long current = frame;
        for (int depth = 0; depth < MaxDepth && current != 0; depth++)
        {
            if (!read(current + FlagsOffset, flags))
            {
                return null;
            }

            if ((flags[0] & 1) == 0)
            {
                return false;
            }

            if (current == top)
            {
                return true;
            }

            if (!TryPointer(read, current + ParentOffset, out current))
            {
                return null;
            }
        }

        return current == 0 ? true : null;
    }

    private static bool TryPointer(Func<long, byte[], bool> read, long address, out long value)
    {
        value = 0;
        byte[] buffer = new byte[8];
        if (address <= 0 || !read(address, buffer))
        {
            return false;
        }

        value = BitConverter.ToInt64(buffer, 0);
        return value == 0 || (value >= 0x10000 && value <= 0x7FFF_FFFF_FFFF);
    }
}

/// <summary>
/// Finds the vtable of a UI frame class from the client's frame-type registration, so no build
/// needs it listed. The client registers each frame type as
/// <c>lea rcx,[name]; lea rax,[factory]</c>; the factory allocates and tail-jumps to the
/// constructor (<c>jmp ctor</c>), and the constructor stores the vtable right after the base
/// constructor (<c>call base; lea rax,[vtable]</c>). Measured for <c>EndOfGameAwardsPanel</c>
/// (98348 vtable RVA 0x2674F68, 98304 0x267BF68) and <c>DownloadPanel</c> (98348 0x2740510,
/// 98304 0x2747510).
/// </summary>
internal static class FrameTypeLocator
{
    public const int Width = 14;
    private const int FactoryScan = 64;
    private const int ConstructorScan = 64;

    /// <summary>The RVA of each "name\0" that starts a string in the data section.</summary>
    public static List<long> NameRvas(byte[] data, long dataRva, string name)
    {
        var rvas = new List<long>();
        byte[] needle = Encoding.ASCII.GetBytes(name + "\0");
        int at = data.AsSpan().IndexOf(needle);
        while (at >= 0)
        {
            if (at == 0 || data[at - 1] == 0)
            {
                rvas.Add(dataRva + at);
            }

            int next = data.AsSpan(at + 1).IndexOf(needle);
            at = next < 0 ? -1 : at + 1 + next;
        }

        return rvas;
    }

    /// <summary>
    /// Registration sites in a code chunk: <c>48 8D 0D [name] 48 8D 05 [factory]</c> whose name is
    /// one of <paramref name="names"/>. Returns (name RVA, factory RVA) pairs.
    /// </summary>
    public static List<(long Name, long Factory)> FindRegistrations(
        ReadOnlySpan<byte> code,
        long codeRva,
        IReadOnlyCollection<long> names
    )
    {
        var found = new List<(long, long)>();
        for (int i = 0; i + Width <= code.Length; i++)
        {
            if (
                code[i] != 0x48
                || code[i + 1] != 0x8D
                || code[i + 2] != 0x0D
                || code[i + 7] != 0x48
                || code[i + 8] != 0x8D
                || code[i + 9] != 0x05
            )
            {
                continue;
            }

            long name = codeRva + i + 7 + BitConverter.ToInt32(code.Slice(i + 3, 4));
            foreach (long wanted in names)
            {
                if (name == wanted)
                {
                    long factory = codeRva + i + 14 + BitConverter.ToInt32(code.Slice(i + 10, 4));
                    found.Add((name, factory));
                }
            }
        }

        return found;
    }

    /// <summary>The constructor the factory tail-jumps to (<c>E9 rel32</c>), or 0.</summary>
    public static long Constructor(ReadOnlySpan<byte> factory, long factoryRva)
    {
        for (int i = 0; i + 5 <= factory.Length && i < FactoryScan; i++)
        {
            if (factory[i] == 0xE9)
            {
                return factoryRva + i + 5 + BitConverter.ToInt32(factory.Slice(i + 1, 4));
            }
        }

        return 0;
    }

    /// <summary>
    /// The vtable the constructor stores first: the first <c>lea rax,[rip+x]</c> after the first
    /// <c>call</c>, or 0.
    /// </summary>
    public static long Vtable(ReadOnlySpan<byte> constructor, long constructorRva)
    {
        bool called = false;
        for (int i = 0; i + 7 <= constructor.Length && i < ConstructorScan; i++)
        {
            if (!called && constructor[i] == 0xE8)
            {
                called = true;
                i += 4;
                continue;
            }

            if (
                called
                && constructor[i] == 0x48
                && constructor[i + 1] == 0x8D
                && constructor[i + 2] == 0x05
            )
            {
                return constructorRva + i + 7 + BitConverter.ToInt32(constructor.Slice(i + 3, 4));
            }
        }

        return 0;
    }
}
