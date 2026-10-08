using System;
using System.Collections.Generic;
using System.Text;

namespace HeroesClientSDK.Tests;

/// <summary>
/// A client module as <see cref="ClientScreen"/> reads it: PE headers, a code section with
/// the screen-state global's sites, the mask/frames site and the game-launch sites, a .rdata with
/// the screen template table and the game-launch message table, the globals, the menu root with
/// its mask and frames, and a UI frame tree whose frames name their class through the recorded
/// IsA/StaticType code shape.
/// </summary>
internal sealed class FakeGlueClient : IProcessMemory
{
    public const long Base = 0x140000000L;
    public const long ModuleSize = 0x40000;
    public const long GlobalRva = 0x30000;
    public const long LaunchGlobalRva = 0x31378;
    private const long TextRva = 0x1000;
    private const int TextSize = 0x8000;
    private const long RdataRva = 0x10000;
    private const int RdataSize = 0x10000;
    private const long Root = 0x2_0000_0000L;
    private const int MaskOffset = 0x1D4;
    private const int FramesOffset = 0x1F0;
    private const long FrameBase = 0x3_0000_0000L;
    private const long LaunchManager = 0x4_0000_0000L;
    private const int LoadingIndex = 5;

    // The frame tree (2.57 layout): parent +0x50, flags +0x48 (bit 0 visible), first child
    // node +0x40, a child's node at +0x18 and its next sibling node at +0x20, and a tagged end.
    public const long Top = 0x5_0000_0000L;
    public const long MenuContainer = 0x5_0000_1000L;
    public const long GameUi = 0x5_0000_2000L;
    public const long AwardsPanel = 0x5_0000_3000L;
    private const long DialogBase = 0x5_0001_0000L;
    private const long LoadingChildren = 0x5_0002_0000L;

    private readonly byte[] headers = new byte[0x1000];
    private readonly byte[] text = new byte[TextSize];
    private readonly byte[] rdata = new byte[RdataSize];
    private readonly byte[] global = new byte[8];
    private readonly byte[] launchGlobal = new byte[8];
    private readonly byte[] root = new byte[0x400];
    private readonly Dictionary<long, byte[]> heap = new();
    private readonly Dictionary<long, List<long>> children = new();
    private readonly Dictionary<string, long> vtables = new();
    private readonly string fileVersion;
    private int nameAt = 0x6000;
    private int dialogs;

    public FakeGlueClient(
        bool glueSite = true,
        bool table = true,
        string fileVersion = "2.57.0.98348"
    )
    {
        this.fileVersion = fileVersion;
        WriteHeaders();
        for (int i = 0; i < 3; i++)
        {
            WriteScreenGlobalSite(0x40 + i * 0x40);
        }

        if (glueSite)
        {
            AddGlueSite();
        }

        if (table)
        {
            WriteTable();
        }

        BitConverter.GetBytes(Root).CopyTo(global, 0);
    }

    public ClientModule Module(int processId) =>
        new(processId, Base, ModuleSize, fileVersion, StartedAt: processId);

    public void AddGlueSite()
    {
        byte[] site = ClientScreenTests.Hex(
            "49 8B 81 D4 01 00 00 8B 51 08 48 0F A3 D0 73 1D 49 8B 8C D1 F0 01 00 00"
        );
        Array.Copy(site, 0, text, 0x400, site.Length);
    }

    /// <summary>The menus exist (a frame per screen) and show the screens in the mask.</summary>
    public void ShowScreens(ulong mask)
    {
        for (int i = 0; i < ClientScreenTests.Screens.Length; i++)
        {
            BitConverter.GetBytes(FrameBase + i * 0x1000L).CopyTo(root, FramesOffset + i * 8);
        }

        BitConverter.GetBytes(mask).CopyTo(root, MaskOffset);
    }

    /// <summary>A match: the frames are gone.</summary>
    public void TearDownMenus()
    {
        Array.Clear(root, FramesOffset, ClientScreenTests.Screens.Length * 8);
        BitConverter.GetBytes(0UL).CopyTo(root, MaskOffset);
    }

    public bool TryRead(long address, Span<byte> buffer)
    {
        return Copy(Base, headers, address, buffer)
            || Copy(Base + TextRva, text, address, buffer)
            || Copy(Base + RdataRva, rdata, address, buffer)
            || Copy(Base + GlobalRva, global, address, buffer)
            || Copy(Base + LaunchGlobalRva, launchGlobal, address, buffer)
            || Copy(Root, root, address, buffer)
            || CopyHeap(address, buffer);
    }

    /// <summary>The vtable of a class this client defined, as an address.</summary>
    public long VtableOf(string className) => vtables[className];

    /// <summary>
    /// The frame tree above the menus with the in-game awards panel: the top frame holds the menu
    /// container (the menu root's parent) and <c>CGameUI</c>, which holds the awards panel.
    /// </summary>
    public void AddPanels()
    {
        AddTree();
        AddFrame(AwardsPanel, "CEndOfGameAwardsPanel", 0x7A, GameUi);
    }

    /// <summary>
    /// 2.57.0.98348, 2026-10-08: the awards panel is 0x7A in the match and 0x7B on the MVP screen.
    /// </summary>
    public void ShowAwards(bool shown) => SetFlags(AwardsPanel, (byte)(shown ? 0x7B : 0x7A));

    /// <summary>
    /// The <c>CScreenLoading</c> frame (the screen frame of index 5) under the top frame, with its
    /// <c>CLoadingBar</c> and <c>CCustomLoadingPanel</c> children. Recorded on 2026-10-08: on the
    /// boot splash of 2.57.0.98348 and 2.57.0.98304 the bar is 0x7A and the panel 0x72; on the
    /// map loading screen of 2.57.0.98304 both are 0x7B.
    /// </summary>
    public void AddLoadingScreen(byte bar, byte panel)
    {
        AddTree();
        long loading = FrameBase + LoadingIndex * 0x1000L;
        if (!heap.ContainsKey(loading))
        {
            AddFrame(loading, "CScreenLoading", 0x7B, Top);
            AddFrame(LoadingChildren, "CLabel", 0x73, loading);
            AddFrame(LoadingChildren + 0x1000, "CLoadingBar", bar, loading);
            AddFrame(LoadingChildren + 0x2000, "CCustomLoadingPanel", panel, loading);
        }

        SetFlags(LoadingChildren + 0x1000, bar);
        SetFlags(LoadingChildren + 0x2000, panel);
    }

    /// <summary>A dialog at the top of the UI (a child of the top frame).</summary>
    public long AddDialog(string className, byte flags)
    {
        AddTree();
        long frame = DialogBase + dialogs++ * 0x1000L;
        AddFrame(frame, className, flags, Top);
        return frame;
    }

    public void SetFlags(long frame, byte flags) => heap[frame][0x48] = flags;

    /// <summary>
    /// The game-launch manager: its creator and result-store code (recorded from 2.57.0.98348,
    /// RVA 0xCFB530 and 0xCFB024), the global, the object, and the client's message table.
    /// </summary>
    public void AddLaunchManager(int result)
    {
        long creator = TextRva + 0x600;
        byte[] create = ClientScreenTests.Hex(
            "48 83 EC 28 48 83 3D 00 00 00 00 00 75 2A B9 58 69 03 00 E8 E8 6F 67 00 48 85 C0 74 14 48 8B C8 E8 0B B0 FF FF 48 89 05 00 00 00 00"
        );
        BitConverter.GetBytes((int)(LaunchGlobalRva - (creator + 12))).CopyTo(create, 7);
        BitConverter.GetBytes((int)(LaunchGlobalRva - (creator + 44))).CopyTo(create, 40);
        Array.Copy(create, 0, text, 0x600, create.Length);
        byte[] store = ClientScreenTests.Hex(
            "83 F8 02 0F 85 80 00 00 00 48 63 7A 04 8D 47 FF 83 F8 17 77 6D 89 79 08"
        );
        Array.Copy(store, 0, text, 0x680, store.Length);
        // The state test at 2.57.0.98348 RVA 0x6C41A6: mov rax,[G]; mov rbx,rcx;
        // cmp dword ptr [rax+20h],0; jne; cmp dword ptr [rax+100E8h],0.
        long stateSite = TextRva + 0x6C0;
        byte[] state = ClientScreenTests.Hex(
            "48 8B 05 00 00 00 00 48 8B D9 83 78 20 00 75 12 83 B8 E8 00 01 00 00"
        );
        BitConverter.GetBytes((int)(LaunchGlobalRva - (stateSite + 7))).CopyTo(state, 3);
        Array.Copy(state, 0, text, 0x6C0, state.Length);

        heap[LaunchManager] = new byte[0x40];
        BitConverter.GetBytes(LaunchManager).CopyTo(launchGlobal, 0);
        SetLaunchResult(result);

        int entry = 0x4000;
        int stringAt = 0x4800;
        entry += 16; // entry 0: { "", 0 }
        foreach (string key in LaunchKeys)
        {
            byte[] bytes = Encoding.ASCII.GetBytes("@UI/" + key);
            Array.Copy(bytes, 0, rdata, stringAt, bytes.Length);
            BitConverter.GetBytes(Base + RdataRva + stringAt).CopyTo(rdata, entry);
            BitConverter.GetBytes((long)bytes.Length).CopyTo(rdata, entry + 8);
            entry += 16;
            stringAt += bytes.Length + 1;
        }
    }

    public void SetLaunchResult(int result) =>
        BitConverter.GetBytes(result).CopyTo(heap[LaunchManager], 8);

    public void SetLaunchState(int state) =>
        BitConverter.GetBytes(state).CopyTo(heap[LaunchManager], 0x20);

    // 2.57.0.98348 and 2.57.0.98304: the @UI/GameLaunch* keys by result code, from 1.
    internal static readonly string[] LaunchKeys =
    {
        "GameLaunchGenericLaunchFailure",
        "GameLaunchReplayOpenFailure",
        "GameLaunchSaveOpenFailure",
        "GameLaunchMapOpenFailure",
        "GameLaunchTrialDisallowedMap",
        "GameLaunchMapPrefetchFailure",
        "GameLaunchInvalidFiles",
        "GameLaunchTooManyDependencies",
        "GameLaunchGameBusy",
        "GameLaunchBaseBuildMissing",
        "GameLaunchPTRLauncherMissing",
        "GameLaunchVersionDownloadMessage",
        "GameLaunchVersionDownloadFailure",
        "GameLaunchVersionLaunchError",
        "GameLaunchDataBuildNumMismatch",
        "GameLaunchModDataMismatch",
        "GameLaunchMapDataMismatch",
        "GameLaunchNotLicensed",
        "GameLaunchLicenseNotValidated",
        "GameLaunchUnsupportedInCN",
        "GameLaunchUnsupportedInRC",
        "GameLaunchUnsupportedInTrial",
        "GameLaunchUnsupportedNoData",
        "GameLaunchUnsupportedTooOld",
    };

    private void AddTree()
    {
        if (heap.ContainsKey(Top))
        {
            return;
        }

        AddFrame(Top, "CRoot", 0x6B, 0);
        AddFrame(MenuContainer, "CLayer", 0x6B, Top);
        AddFrame(GameUi, "CGameUI", 0x7B, Top);
        BitConverter.GetBytes(MenuContainer).CopyTo(root, 0x50);
    }

    private void AddFrame(long frame, string className, byte flags, long parent)
    {
        heap[frame] = new byte[0x100];
        BitConverter.GetBytes(Class(className)).CopyTo(heap[frame], 0);
        heap[frame][0x48] = flags;
        children[frame] = new List<long>();
        if (parent != 0)
        {
            children[parent].Add(frame);
            Link(parent);
        }
    }

    private void Link(long parent)
    {
        List<long> list = children[parent];
        long end = (parent + 0x38) | 1;
        BitConverter.GetBytes(list.Count == 0 ? end : list[0] + 0x18).CopyTo(heap[parent], 0x40);
        for (int i = 0; i < list.Count; i++)
        {
            long next = i + 1 < list.Count ? list[i + 1] + 0x18 : end;
            BitConverter.GetBytes(next).CopyTo(heap[list[i]], 0x20);
            BitConverter.GetBytes(parent).CopyTo(heap[list[i]], 0x50);
        }
    }

    /// <summary>
    /// A class: its name in .rdata, a vtable whose slot 0x240 is IsA, IsA's
    /// <c>push rbx; sub rsp,20h; mov rbx,rdx; call StaticType</c>, and StaticType's
    /// <c>lea rcx,[name]</c> (recorded shapes from 2.57.0.98348).
    /// </summary>
    private long Class(string name)
    {
        if (vtables.TryGetValue(name, out long existing))
        {
            return existing;
        }

        int n = vtables.Count;
        int nameRva = nameAt;
        byte[] bytes = Encoding.ASCII.GetBytes(name + "\0");
        Array.Copy(bytes, 0, rdata, nameRva, bytes.Length);
        nameAt += bytes.Length;

        int vtable = 0x8000 + n * 0x300;
        int isA = 0x1000 + n * 0x80;
        int staticType = isA + 0x20;
        BitConverter.GetBytes(Base + TextRva + isA).CopyTo(rdata, vtable + 0x240);
        byte[] isACode = ClientScreenTests.Hex("40 53 48 83 EC 20 48 8B DA E8 00 00 00 00");
        BitConverter.GetBytes(staticType - (isA + 9 + 5)).CopyTo(isACode, 10);
        Array.Copy(isACode, 0, text, isA, isACode.Length);
        byte[] staticCode = ClientScreenTests.Hex(
            "40 53 48 83 EC 30 8B 05 3C 3F A9 02 A8 01 75 65 83 C8 01 48 C7 44 24 28 0E 00 00 00 48 8D 0D 00 00 00 00"
        );
        BitConverter
            .GetBytes((int)(RdataRva + nameRva - (TextRva + staticType + 0x1C + 7)))
            .CopyTo(staticCode, 0x1F);
        Array.Copy(staticCode, 0, text, staticType, staticCode.Length);

        long address = Base + RdataRva + vtable;
        vtables[name] = address;
        return address;
    }

    private bool CopyHeap(long address, Span<byte> buffer)
    {
        foreach (KeyValuePair<long, byte[]> frame in heap)
        {
            if (Copy(frame.Key, frame.Value, address, buffer))
            {
                return true;
            }
        }

        return false;
    }

    private static bool Copy(long start, byte[] source, long address, Span<byte> buffer)
    {
        long offset = address - start;
        if (offset < 0 || offset + buffer.Length > source.Length)
        {
            return false;
        }

        source.AsSpan((int)offset, buffer.Length).CopyTo(buffer);
        return true;
    }

    private void WriteHeaders()
    {
        headers[0] = (byte)'M';
        headers[1] = (byte)'Z';
        const int pe = 0x80;
        BitConverter.GetBytes(pe).CopyTo(headers, 0x3C);
        headers[pe] = (byte)'P';
        headers[pe + 1] = (byte)'E';
        BitConverter.GetBytes((ushort)2).CopyTo(headers, pe + 6);
        BitConverter.GetBytes((ushort)0xF0).CopyTo(headers, pe + 20);
        int table = pe + 24 + 0xF0;
        WriteSection(table, ".text", TextRva, TextSize, 0x60000020);
        WriteSection(table + 40, ".rdata", RdataRva, RdataSize, 0x40000040);
    }

    private void WriteSection(int at, string name, long rva, int size, uint characteristics)
    {
        Encoding.ASCII.GetBytes(name).CopyTo(headers, at);
        BitConverter.GetBytes(size).CopyTo(headers, at + 8);
        BitConverter.GetBytes((uint)rva).CopyTo(headers, at + 12);
        BitConverter.GetBytes(characteristics).CopyTo(headers, at + 36);
    }

    /// <summary>`mov rcx,[G]; test; jz; xor edx,edx; call; test al,al; jz; mov rcx,[G]; call`.</summary>
    private void WriteScreenGlobalSite(int at)
    {
        long site = TextRva + at;
        byte[] bytes = new byte[LoadingScreenPattern.Width];
        bytes[0] = 0x48;
        bytes[1] = 0x8B;
        bytes[2] = 0x0D;
        BitConverter.GetBytes((int)(GlobalRva - (site + 7))).CopyTo(bytes, 3);
        bytes[7] = 0x48;
        bytes[8] = 0x85;
        bytes[9] = 0xC9;
        bytes[10] = 0x74;
        bytes[12] = 0x33;
        bytes[13] = 0xD2;
        bytes[14] = 0xE8;
        bytes[19] = 0x84;
        bytes[20] = 0xC0;
        bytes[21] = 0x74;
        bytes[23] = 0x48;
        bytes[24] = 0x8B;
        bytes[25] = 0x0D;
        BitConverter.GetBytes((int)(GlobalRva - (site + 30))).CopyTo(bytes, 26);
        bytes[30] = 0xE8;
        Array.Copy(bytes, 0, text, at, bytes.Length);
    }

    private void WriteTable()
    {
        // Something that is not an entry before the table, as in the client.
        BitConverter.GetBytes(0x3BA700432FBB3E0FL).CopyTo(rdata, 0x0F0);
        int entry = 0x100;
        int stringAt = 0x1000;
        foreach (string name in ClientScreenTests.Screens)
        {
            byte[] path = Encoding.ASCII.GetBytes(name + "/" + name);
            Array.Copy(path, 0, rdata, stringAt, path.Length);
            BitConverter.GetBytes(Base + RdataRva + stringAt).CopyTo(rdata, entry);
            BitConverter.GetBytes((long)path.Length).CopyTo(rdata, entry + 8);
            entry += 16;
            stringAt += path.Length + 1;
        }
    }
}
