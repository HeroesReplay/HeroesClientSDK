using System;
using System.Collections.Generic;
using System.Text;

namespace HeroesClientSDK;

/// <summary>
/// The client's UI frames form one tree, laid out as the build's <see cref="FrameTreeLayout"/>
/// says (on 2.57: parent +0x50, flags +0x48, first child node +0x40, a child's node at +0x18 and
/// its next sibling node at +0x20, and a tagged end). Measured on 2.57.0.98304 and 2.57.0.98348:
/// the in-game tree has about 86,000 frames, and the awards panel is found after about 6,600 of
/// them (17 ms). Every child's parent pointer must name the frame it was listed under, so a
/// layout that moved reads as not found, never as a wrong frame.
/// </summary>
internal static class FrameTree
{
    public const int MaxFrames = 300_000;
    public const int MaxDepth = 64;
    public const int MaxChildren = 4096;

    /// <summary>
    /// The direct children of <paramref name="frame"/> with their vtables, in list order. Stops
    /// at the first child whose parent pointer does not name <paramref name="frame"/>.
    /// </summary>
    public static List<(long Frame, long Vtable)> Children(
        IProcessMemory memory,
        FrameTreeLayout layout,
        long frame
    )
    {
        var children = new List<(long Frame, long Vtable)>();
        if (frame == 0 || !TryPointer(memory, frame + layout.FirstChildOffset, out long node))
        {
            return children;
        }

        while (node != 0 && (node & 7) == 0 && children.Count < MaxChildren)
        {
            long child = node - layout.NodeOffset;
            if (
                !TryPointer(memory, child + layout.ParentOffset, out long parent)
                || parent != frame
                || !TryPointer(memory, child, out long vtable)
                || !TryPointer(memory, child + layout.NextOffset, out long next)
            )
            {
                break;
            }

            children.Add((child, vtable));
            node = next;
        }

        return children;
    }

    /// <summary>The frame's own visible bit; null when unreadable.</summary>
    public static bool? Visible(IProcessMemory memory, FrameTreeLayout layout, long frame)
    {
        byte[] flags = new byte[1];
        return frame != 0 && memory.TryRead(frame + layout.FlagsOffset, flags)
            ? IsVisible(flags[0], layout)
            : null;
    }

    /// <summary>
    /// The top of the tree: the menu root's grandparent (the menu root sits under a menu
    /// container under the top frame). Zero when the chain does not read.
    /// </summary>
    public static long Top(IProcessMemory memory, FrameTreeLayout layout, long menuRoot)
    {
        if (
            !TryPointer(memory, menuRoot + layout.ParentOffset, out long container)
            || container == 0
        )
        {
            return 0;
        }

        return TryPointer(memory, container + layout.ParentOffset, out long top) ? top : 0;
    }

    /// <summary>
    /// Depth-first search from <paramref name="top"/> for the first frame whose vtable
    /// <paramref name="match"/> accepts. Returns the frame and its vtable, or zeros.
    /// </summary>
    public static (long Frame, long Vtable) FindFirst(
        IProcessMemory memory,
        FrameTreeLayout layout,
        long top,
        Func<long, bool> match
    )
    {
        if (top == 0 || match == null)
        {
            return (0, 0);
        }

        var stack = new Stack<(long Frame, int Depth)>();
        stack.Push((top, 0));
        int visited = 0;
        while (stack.Count > 0 && visited < MaxFrames)
        {
            (long frame, int depth) = stack.Pop();
            visited++;
            if (!TryPointer(memory, frame, out long vtable))
            {
                continue;
            }

            if (vtable != 0 && match(vtable))
            {
                return (frame, vtable);
            }

            if (
                depth >= MaxDepth
                || !TryPointer(memory, frame + layout.FirstChildOffset, out long node)
            )
            {
                continue;
            }

            int siblings = 0;
            while (node != 0 && (node & 7) == 0 && siblings++ < MaxFrames)
            {
                long child = node - layout.NodeOffset;
                if (
                    !TryPointer(memory, child + layout.ParentOffset, out long parent)
                    || parent != frame
                    || !TryPointer(memory, child + layout.NextOffset, out long next)
                )
                {
                    break;
                }

                stack.Push((child, depth + 1));
                node = next;
            }
        }

        return (0, 0);
    }

    /// <summary>
    /// True when the frame and every frame above it up to <paramref name="top"/> have their
    /// visible bit set; null when the chain does not read.
    /// </summary>
    public static bool? Shown(IProcessMemory memory, FrameTreeLayout layout, long frame, long top)
    {
        byte[] flags = new byte[1];
        long current = frame;
        for (int depth = 0; depth < MaxDepth && current != 0; depth++)
        {
            if (!memory.TryRead(current + layout.FlagsOffset, flags))
            {
                return null;
            }

            if (!IsVisible(flags[0], layout))
            {
                return false;
            }

            if (current == top)
            {
                return true;
            }

            if (!TryPointer(memory, current + layout.ParentOffset, out current))
            {
                return null;
            }
        }

        return current == 0 ? true : null;
    }

    private static bool IsVisible(byte flags, FrameTreeLayout layout) =>
        (flags & (1 << layout.VisibleBit)) != 0;

    private static bool TryPointer(IProcessMemory memory, long address, out long value)
    {
        value = 0;
        byte[] buffer = new byte[8];
        if (address <= 0 || !memory.TryRead(address, buffer))
        {
            return false;
        }

        value = BitConverter.ToInt64(buffer, 0);
        return value == 0 || (value >= 0x10000 && value <= 0x7FFF_FFFF_FFFF);
    }
}

/// <summary>
/// Names a UI frame's C++ class from its vtable, so no build needs class or vtable lists. The
/// frame classes carry no RTTI locator, but every one implements <c>IsA(type)</c> in the vtable
/// slot at <see cref="FrameTreeLayout.IsASlot"/>, and that function first calls the class's static
/// type accessor, which loads the class name: <c>call StaticType</c>, then in StaticType
/// <c>lea rcx,[name]</c>. Measured on 2.57.0.98348 and 2.57.0.98304: <c>CGlueUI</c>,
/// <c>CRoot</c>, <c>CScreenLoading</c>, <c>CStandardDialog</c>, <c>CLoginDialog</c>,
/// <c>CGameMenuDialog</c>, <c>CLabel</c>, <c>CImage</c>.
/// </summary>
internal static class FrameClass
{
    private const int CodeScan = 64;
    private const int StaticTypeScan = 96;
    private const int MaxName = 80;

    /// <summary>
    /// How an <c>IsA</c> function starts on 2.57 (<c>push rbx; sub rsp,20h; mov rbx,rdx;
    /// call StaticType</c>). Only the offline class map needs it; a read names any vtable.
    /// </summary>
    internal static ReadOnlySpan<byte> IsAPrologue =>
        new byte[] { 0x40, 0x53, 0x48, 0x83, 0xEC, 0x20, 0x48, 0x8B, 0xDA, 0xE8 };

    /// <summary>The class name, or null when the code does not have this shape.</summary>
    public static string Name(
        IProcessMemory memory,
        FrameTreeLayout layout,
        long vtable,
        long moduleBase,
        long moduleSize
    )
    {
        if (vtable < moduleBase || vtable >= moduleBase + moduleSize)
        {
            return null;
        }

        byte[] slot = new byte[8];
        if (!memory.TryRead(vtable + layout.IsASlot, slot))
        {
            return null;
        }

        return NameOfIsA(memory, BitConverter.ToInt64(slot, 0), moduleBase, moduleSize);
    }

    /// <summary>
    /// The class name that the <c>IsA</c> function at <paramref name="isA"/> loads through its
    /// StaticType, or null when the code does not have this shape.
    /// </summary>
    public static string NameOfIsA(
        IProcessMemory memory,
        long isA,
        long moduleBase,
        long moduleSize
    )
    {
        bool InModule(long address) => address >= moduleBase && address < moduleBase + moduleSize;
        byte[] code = new byte[CodeScan];
        if (!InModule(isA) || !memory.TryRead(isA, code))
        {
            return null;
        }

        long staticType = FirstCall(code, isA);
        byte[] accessor = new byte[StaticTypeScan];
        if (!InModule(staticType) || !memory.TryRead(staticType, accessor))
        {
            return null;
        }

        long name = FirstLeaRcx(accessor, staticType);
        byte[] text = new byte[MaxName];
        if (!InModule(name) || !memory.TryRead(name, text))
        {
            return null;
        }

        int length = Array.IndexOf(text, (byte)0);
        if (length < 2)
        {
            return null;
        }

        for (int i = 0; i < length; i++)
        {
            if (text[i] < 0x20 || text[i] >= 0x7F)
            {
                return null;
            }
        }

        return Encoding.ASCII.GetString(text, 0, length);
    }

    /// <summary>The target of the first <c>call rel32</c>, or 0.</summary>
    public static long FirstCall(ReadOnlySpan<byte> code, long address)
    {
        for (int i = 0; i + 5 <= code.Length; i++)
        {
            if (code[i] == 0xE8)
            {
                return address + i + 5 + BitConverter.ToInt32(code.Slice(i + 1, 4));
            }
        }

        return 0;
    }

    /// <summary>The target of the first <c>lea rcx,[rip+x]</c>, or 0.</summary>
    public static long FirstLeaRcx(ReadOnlySpan<byte> code, long address)
    {
        for (int i = 0; i + 7 <= code.Length; i++)
        {
            if (code[i] == 0x48 && code[i + 1] == 0x8D && code[i + 2] == 0x0D)
            {
                return address + i + 7 + BitConverter.ToInt32(code.Slice(i + 3, 4));
            }
        }

        return 0;
    }
}
