using System;
using System.Collections.Generic;
using System.Diagnostics;

namespace HeroesClientSDK;

/// <summary>Which client screen memory shows, by the screen's own name in the client.</summary>
public enum ClientScreenKind
{
    /// <summary>Memory cannot tell (no process, pattern not found yet, read failed).</summary>
    Unknown,

    /// <summary>The menus are torn down: the client is in a match it opened from a menu.</summary>
    Match,

    /// <summary>
    /// The menus exist but show no screen: a match that a client loaded straight from a replay
    /// file (a previous-patch client), or a moment between two screens.
    /// </summary>
    NoScreen,

    /// <summary><c>ScreenLoading</c>: the boot splash or a map loading screen.</summary>
    Loading,

    /// <summary>
    /// <c>ScreenLoginUnified</c>: the login screen. Not signed in yet: either Battle.net
    /// authentication is still connecting (a client Battle.net started, for a few seconds) or it
    /// shows the email and password form (a client started without SSO).
    /// </summary>
    Login,

    /// <summary><c>ScreenHome</c>: the signed-in home screen.</summary>
    Home,

    /// <summary><c>ScreenScore</c>: the score screen after a match.</summary>
    Score,

    /// <summary>Another menu screen (collection, replays, a hero, the store...).</summary>
    Menu,

    /// <summary>
    /// The MVP and awards screen at the end of a match (`EndOfGameAwardsPanel`, in-game UI).
    /// </summary>
    Awards,
}

/// <summary>One read of the client's menu screens.</summary>
/// <param name="Screen">The main screen shown (see <see cref="ClientScreenKind"/>).</param>
/// <param name="Shown">
/// The names of every screen the client shows, such as <c>ScreenHome</c> with the hero
/// backgrounds around it. Empty when none is shown or memory cannot tell.
/// </param>
/// <param name="Reason">
/// Why, for example "screens", "match", "no-screen", "no-state", "unsupported-build".
/// </param>
/// <param name="ClientVersion">The version of the running exe, or null when unknown.</param>
/// <param name="VersionMismatch">
/// True when the caller passed a version and the running exe is a different build. Reading
/// continues; nothing throws.
/// </param>
/// <param name="MenuSeen">
/// True once this client process has shown a screen other than the loading screen (the login
/// form, home, or another menu), or a match.
/// </param>
public readonly record struct ClientScreenSample(
    ClientScreenKind Screen,
    IReadOnlyList<string> Shown,
    string Reason,
    HeroesClientVersion ClientVersion,
    bool VersionMismatch,
    bool MenuSeen = false
)
{
    /// <summary>
    /// True on a map loading screen, false on any other known screen, null when memory cannot
    /// tell. The boot splash is the same loading screen, so a loading screen counts as a map
    /// only after this process has shown a menu or a match.
    /// </summary>
    public bool? MapLoading =>
        Screen == ClientScreenKind.Loading ? (MenuSeen ? true : null)
        : Known ? false
        : null;

    /// <summary>True when memory can tell which screen this is.</summary>
    public bool Known => Screen != ClientScreenKind.Unknown;

    /// <summary>True on the home screen, false on any other known screen, null when unknown.</summary>
    public bool? Home => Known ? Screen == ClientScreenKind.Home : null;

    /// <summary>
    /// True on the login screen (authenticating or the email and password form), false on any
    /// other known screen, null when unknown.
    /// </summary>
    public bool? LoginForm => Known ? Screen == ClientScreenKind.Login : null;

    /// <summary>True on the loading screen, false on any other known screen, null when unknown.</summary>
    public bool? Loading => Known ? Screen == ClientScreenKind.Loading : null;

    /// <summary>True on the score screen, false on any other known screen, null when unknown.</summary>
    public bool? ScoreScreen => Known ? Screen == ClientScreenKind.Score : null;

    /// <summary>
    /// True on the MVP and awards screen, false on any other known screen, null when unknown.
    /// </summary>
    public bool? AwardsScreen => Known ? Screen == ClientScreenKind.Awards : null;

    /// <summary>
    /// False on the login form, true on the home screen (which only a signed-in client reaches),
    /// null otherwise.
    /// </summary>
    public bool? SignedIn =>
        Screen == ClientScreenKind.Login ? false
        : Screen == ClientScreenKind.Home ? true
        : null;
}

/// <summary>
/// Read-only menu screens from client memory, so the home screen, the login form, the loading
/// screen and the score screen need no screen capture. The menu root is the object at the
/// screen-state global (<see cref="LoadingScreenPattern"/>, null in a match on some paths). It
/// holds a 64-bit mask with one bit per screen that is shown, and a frame per screen
/// (<see cref="GlueScreenPattern"/> finds both offsets). The screen names come from the client's
/// own template table (<see cref="GlueScreenTable"/>), so no build needs an index list. Measured
/// on 2.57.0.98348: home shows <c>ScreenHome</c> with <c>ScreenBackgroundHero</c>,
/// <c>ScreenHeroCutscene</c>, <c>ScreenNavigationHero</c> and <c>ScreenForegroundHero</c>
/// (mask 0x6181).
/// </summary>
public sealed class ClientScreenMemory : IDisposable
{
    private static readonly TimeSpan RediscoverAfter = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan PanelWalkInterval = TimeSpan.FromSeconds(5);

    private IntPtr handle;
    private int attachedPid;
    private int pid;
    private long startedAt;
    private long moduleBase;
    private long moduleSize;
    private string fileVersion;
    private bool discovered;
    private long globalRva;
    private GlueScreenPattern.Offsets offsets;
    private List<string> names = new();
    private bool screenSeen;
    private bool menuSeen;
    private readonly Panel awardsPanel = new("CEndOfGameAwardsPanel");
    private readonly Dictionary<long, string> classes = new();
    private long panelTop;
    private DateTimeOffset nextPanelWalk;
    private DateTimeOffset rediscoverAt;
    private string reason = "no-process";

    internal Func<DateTimeOffset> UtcNow { get; set; } = () => DateTimeOffset.UtcNow;

    internal long GlobalRva => globalRva;

    internal int MaskOffset => offsets.Mask;

    internal int FramesOffset => offsets.Frames;

    internal IReadOnlyList<string> ScreenNames => names;

    internal long AwardsVtable => awardsPanel.Vtable;

    /// <summary>A UI panel the read looks for by its class name, with its vtable and frame.</summary>
    private sealed class Panel
    {
        public Panel(string name) => Name = name;

        public string Name { get; }

        public long Vtable { get; set; }

        public long Frame { get; set; }

        public void Reset()
        {
            Vtable = 0;
            Frame = 0;
        }
    }

    /// <summary>
    /// Finds the awards panel in the frame tree by its class name, at most every 5 s while it is
    /// missing, and drops a cached frame whose vtable changed (destroyed and reused). Class names
    /// are resolved once per vtable (<see cref="FrameClass"/>).
    /// </summary>
    private void LocatePanels(Func<long, byte[], bool> read, long root)
    {
        if (
            awardsPanel.Frame != 0
            && (!TryReadPointer(read, awardsPanel.Frame, out long vt) || vt != awardsPanel.Vtable)
        )
        {
            awardsPanel.Frame = 0;
        }

        if (awardsPanel.Frame != 0 || UtcNow() < nextPanelWalk)
        {
            return;
        }

        nextPanelWalk = UtcNow() + PanelWalkInterval;
        panelTop = FrameTree.Top(read, root);
        (long frame, long vtable) = FrameTree.FindFirst(
            read,
            panelTop,
            candidate => ClassOf(read, candidate) == awardsPanel.Name
        );
        awardsPanel.Frame = frame;
        awardsPanel.Vtable = vtable;
    }

    private string ClassOf(Func<long, byte[], bool> read, long vtable)
    {
        if (!classes.TryGetValue(vtable, out string name))
        {
            name = FrameClass.Name(read, vtable, moduleBase, moduleSize);
            classes[vtable] = name;
        }

        return name;
    }

    /// <summary>Null when the panel is not known or not found; else whether it shows.</summary>
    private bool? PanelShown(Func<long, byte[], bool> read, Panel panel)
    {
        if (panel.Vtable == 0 || panel.Frame == 0)
        {
            return null;
        }

        return FrameTree.Shown(read, panel.Frame, panelTop);
    }

    /// <summary>
    /// Reads the menu screens of <paramref name="process"/>. A new process (pid and start time)
    /// starts discovery over. <paramref name="clientVersion"/> is optional: when it is given and
    /// the running exe is another build, the sample says so and reading continues.
    /// </summary>
    public ClientScreenSample Read(Process process, HeroesClientVersion clientVersion = null)
    {
        if (!TryAttach(process, out StableClockModule module))
        {
            return new ClientScreenSample(
                ClientScreenKind.Unknown,
                Array.Empty<string>(),
                reason,
                null,
                false
            );
        }

        return Read(module, ReadProcess, clientVersion);
    }

    internal ClientScreenSample Read(
        StableClockModule module,
        Func<long, byte[], bool> read,
        HeroesClientVersion clientVersion = null
    )
    {
        if (module.ProcessId <= 0 || module.BaseAddress <= 0 || module.Size <= 0 || read == null)
        {
            return Sample(ClientScreenKind.Unknown, "no-module", clientVersion);
        }

        UseModule(module);
        if (!discovered || (!Ready && UtcNow() >= rediscoverAt))
        {
            Discover(read);
        }

        if (!Ready)
        {
            return Sample(ClientScreenKind.Unknown, reason, clientVersion);
        }

        if (!TryReadPointer(read, moduleBase + globalRva, out long root))
        {
            return Sample(ClientScreenKind.Unknown, "read-failed", clientVersion);
        }

        if (root == 0)
        {
            return Sample(ClientScreenKind.Unknown, "no-state", clientVersion);
        }

        int loadingIndex = names.IndexOf("ScreenLoading");
        if (loadingIndex >= 0)
        {
            if (!TryReadPointer(read, root + offsets.Frames + 8L * loadingIndex, out long frame))
            {
                return Sample(ClientScreenKind.Unknown, "read-failed", clientVersion);
            }

            if (frame == 0)
            {
                // The awards panel exists only in a match, and shows at its end.
                LocatePanels(read, root);
                if (PanelShown(read, awardsPanel) == true)
                {
                    menuSeen = true;
                    return Sample(ClientScreenKind.Awards, "awards", clientVersion);
                }

                // A client that is still starting has no frames yet either. Only after it has
                // shown a screen, or while the in-game awards panel exists, do missing frames
                // mean the menus were torn down for a match.
                if (!screenSeen && awardsPanel.Frame == 0)
                {
                    return Sample(ClientScreenKind.Unknown, "starting", clientVersion);
                }

                menuSeen = true;
                return Sample(ClientScreenKind.Match, "match", clientVersion);
            }
        }

        byte[] maskBytes = new byte[8];
        if (!TryRead(read, root + offsets.Mask, maskBytes))
        {
            return Sample(ClientScreenKind.Unknown, "read-failed", clientVersion);
        }

        ulong mask = BitConverter.ToUInt64(maskBytes, 0);
        if (names.Count < 64 && (mask >> names.Count) != 0)
        {
            return Sample(ClientScreenKind.Unknown, "mask-out-of-range", clientVersion);
        }

        var shown = new List<string>();
        for (int bit = 0; bit < names.Count; bit++)
        {
            if ((mask & (1UL << bit)) != 0)
            {
                shown.Add(names[bit]);
            }
        }

        ClientScreenKind kind = Classify(shown);
        if (kind is not (ClientScreenKind.Loading or ClientScreenKind.Login))
        {
            LocatePanels(read, root);
            if (PanelShown(read, awardsPanel) == true)
            {
                kind = ClientScreenKind.Awards;
            }
        }

        if (shown.Count > 0)
        {
            screenSeen = true;
        }

        if (kind is not (ClientScreenKind.Loading or ClientScreenKind.NoScreen))
        {
            menuSeen = true;
        }

        return new ClientScreenSample(
            kind,
            shown,
            kind switch
            {
                ClientScreenKind.NoScreen => "no-screen",
                ClientScreenKind.Awards => "awards",
                _ => "screens",
            },
            Version(),
            Mismatch(clientVersion),
            menuSeen
        );
    }

    /// <summary>
    /// The main screen among those shown. A loading screen or the login form covers everything;
    /// the score and home screens come next; any other shown screen is a menu.
    /// </summary>
    internal static ClientScreenKind Classify(IReadOnlyCollection<string> shown)
    {
        if (shown == null || shown.Count == 0)
        {
            return ClientScreenKind.NoScreen;
        }

        bool Has(string name)
        {
            foreach (string screen in shown)
            {
                if (string.Equals(screen, name, StringComparison.Ordinal))
                {
                    return true;
                }
            }

            return false;
        }

        if (Has("ScreenLoading"))
        {
            return ClientScreenKind.Loading;
        }

        if (Has("ScreenLoginUnified"))
        {
            return ClientScreenKind.Login;
        }

        if (Has("ScreenScore"))
        {
            return ClientScreenKind.Score;
        }

        if (Has("ScreenHome"))
        {
            return ClientScreenKind.Home;
        }

        return ClientScreenKind.Menu;
    }

    /// <summary>Closes the process handle.</summary>
    public void Dispose()
    {
        ReleaseHandle();
    }

    private bool Ready => globalRva != 0 && offsets.Mask != 0 && names.Count > 0;

    private ClientScreenSample Sample(
        ClientScreenKind kind,
        string why,
        HeroesClientVersion clientVersion
    ) => new(kind, Array.Empty<string>(), why, Version(), Mismatch(clientVersion), menuSeen);

    private HeroesClientVersion Version() => HeroesClientVersion.TryParse(fileVersion);

    private bool Mismatch(HeroesClientVersion clientVersion)
    {
        HeroesClientVersion running = Version();
        return clientVersion is not null && running is not null && running != clientVersion;
    }

    private void UseModule(StableClockModule module)
    {
        if (
            pid == module.ProcessId
            && startedAt == module.StartedAt
            && moduleBase == module.BaseAddress
            && moduleSize == module.Size
        )
        {
            return;
        }

        // A relaunched client starts over: its globals and its table are its own.
        pid = module.ProcessId;
        startedAt = module.StartedAt;
        moduleBase = module.BaseAddress;
        moduleSize = module.Size;
        fileVersion = module.FileVersion;
        discovered = false;
        globalRva = 0;
        offsets = default;
        names = new List<string>();
        screenSeen = false;
        menuSeen = false;
        awardsPanel.Reset();
        classes.Clear();
        panelTop = 0;
        nextPanelWalk = default;
        rediscoverAt = default;
    }

    private void Discover(Func<long, byte[], bool> read)
    {
        discovered = true;
        rediscoverAt = UtcNow() + RediscoverAfter;
        if (!ModuleScanner.TrySections(read, moduleBase, moduleSize, out var sections))
        {
            reason = "read-failed";
            return;
        }

        var globals = new List<long>();
        var sites = new List<GlueScreenPattern.Offsets>();
        int overlap = Math.Max(LoadingScreenPattern.Width, GlueScreenPattern.Width);
        foreach (ModuleSection section in sections)
        {
            if (!section.Executable)
            {
                continue;
            }

            ModuleScanner.Walk(
                read,
                moduleBase,
                section,
                overlap,
                (slice, rva) =>
                {
                    globals.AddRange(LoadingScreenPattern.Find(slice, rva));
                    sites.AddRange(GlueScreenPattern.Find(slice));
                }
            );
        }

        // Code that is still being unpacked has no sites yet; the next attempt reads it again.
        if (
            !LoadingScreenPattern.TryAgree(globals, out long rva, out _)
            || rva <= 0
            || rva > moduleSize - 8
        )
        {
            reason = globals.Count == 0 ? "unsupported-build" : "pattern-disagreed";
            return;
        }

        if (!GlueScreenPattern.TryAgree(sites, out GlueScreenPattern.Offsets found))
        {
            reason = sites.Count == 0 ? "unsupported-build" : "pattern-disagreed";
            return;
        }

        List<string> table = new();
        foreach (ModuleSection section in sections)
        {
            if (section.Executable || section.Name != ".rdata")
            {
                continue;
            }

            byte[] data = ModuleScanner.ReadSection(read, moduleBase, section);
            table = GlueScreenTable.Find(data, section.VirtualAddress, moduleBase, moduleSize);
            if (table.Count > 0)
            {
                break;
            }
        }

        if (table.Count == 0)
        {
            reason = "no-screen-table";
            return;
        }

        globalRva = rva;
        offsets = found;
        names = table;
        reason = "pattern";
    }

    /// <summary>
    /// A pointer is zero or a user-mode address. Anything else is not this structure.
    /// </summary>
    private static bool TryReadPointer(Func<long, byte[], bool> read, long address, out long value)
    {
        value = 0;
        byte[] buffer = new byte[8];
        if (!TryRead(read, address, buffer))
        {
            return false;
        }

        value = BitConverter.ToInt64(buffer, 0);
        return value == 0 || (value >= 0x10000 && value <= 0x7FFF_FFFF_FFFF);
    }

    private static bool TryRead(Func<long, byte[], bool> read, long address, byte[] buffer)
    {
        return address > 0 && read(address, buffer);
    }

    private bool TryAttach(Process process, out StableClockModule module)
    {
        module = default;
        try
        {
            if (process == null || process.HasExited)
            {
                reason = "no-process";
                return false;
            }

            if (handle == IntPtr.Zero || attachedPid != process.Id)
            {
                ReleaseHandle();
                handle = NativeMethods.OpenProcess(
                    NativeMethods.ProcessQueryInformation | NativeMethods.ProcessVmRead,
                    false,
                    process.Id
                );
                if (handle == IntPtr.Zero)
                {
                    reason = "open-failed";
                    return false;
                }

                attachedPid = process.Id;
            }

            ProcessModule main = process.MainModule;
            if (main == null || main.BaseAddress == IntPtr.Zero || main.ModuleMemorySize <= 0)
            {
                reason = "no-module";
                return false;
            }

            module = new StableClockModule(
                process.Id,
                main.BaseAddress.ToInt64(),
                main.ModuleMemorySize,
                main.FileVersionInfo.FileVersion,
                StableClockModule.StartTicks(process)
            );
            return true;
        }
        catch
        {
            reason = "no-process";
            return false;
        }
    }

    private bool ReadProcess(long address, byte[] buffer)
    {
        return handle != IntPtr.Zero
            && NativeMethods.ReadProcessMemory(
                handle,
                (IntPtr)address,
                buffer,
                buffer.Length,
                out int read
            )
            && read == buffer.Length;
    }

    private void ReleaseHandle()
    {
        if (handle != IntPtr.Zero)
        {
            NativeMethods.CloseHandle(handle);
            handle = IntPtr.Zero;
        }

        attachedPid = 0;
    }
}
