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

    /// <summary>
    /// <c>ScreenLoading</c> when memory cannot tell the boot splash from a map loading screen
    /// (the loading screen's panels did not read). See <see cref="Splash"/> and
    /// <see cref="MapLoading"/>.
    /// </summary>
    Loading,

    /// <summary>
    /// <c>ScreenLoginUnified</c> with no dialog over it: the email and password form of a client
    /// started without SSO. Not signed in.
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

    /// <summary>
    /// <c>ScreenLoginUnified</c> under Battle.net's AUTHENTICATION "Connecting..." panel (a
    /// shown <c>CLoginDialog</c>): a client Battle.net started, still signing in. Not signed in
    /// yet, and not the email and password form.
    /// </summary>
    Authenticating,

    /// <summary>
    /// <c>ScreenLoading</c> as the boot splash: the loading screen's map panels
    /// (<c>CCustomLoadingPanel</c>, <c>CLoadingBar</c>) are hidden.
    /// </summary>
    Splash,

    /// <summary>
    /// A map loading screen: the <c>ScreenLoading</c> frame shows its map panel
    /// (<c>CCustomLoadingPanel</c> with the players). A previous-patch client that loads a replay
    /// straight from the file can show it with no screen bit in the mask.
    /// </summary>
    MapLoading,

    /// <summary>
    /// A message dialog with an OK button covers the menus (<c>CStandardDialog</c>,
    /// <c>CBattlenetErrorDialog</c> or <c>CDisconnectedDialog</c> shown at the top of the UI).
    /// <see cref="ClientScreenSample.LaunchResult"/> names the message when it is a game-launch
    /// result, such as "the version required to play this game is not available".
    /// </summary>
    Dialog,

    /// <summary>
    /// The game-data DOWNLOADING dialog ("All data files must be fully downloaded to load this
    /// version of the game.", a shown <c>CProgressBarDialog</c>): the client fetches data before
    /// it can load a replay, for example the newest client while HeroesSwitcher hands a replay to
    /// an older build. Leave it running.
    /// </summary>
    Download,
}

/// <summary>One read of the client's menu screens.</summary>
/// <param name="Screen">The main screen shown (see <see cref="ClientScreenKind"/>).</param>
/// <param name="Shown">
/// The names of every screen the client shows, such as <c>ScreenHome</c> with the hero
/// backgrounds around it. Empty when none is shown or memory cannot tell.
/// </param>
/// <param name="MenuSeen">
/// True once this client process has shown a screen other than the loading screen (the login
/// form, home, or another menu), or a match.
/// </param>
/// <param name="Reason">
/// Why, for example "screens", "match", "no-screen", "no-state", "unsupported-build".
/// </param>
/// <param name="ClientVersion">The build of the running exe, or null when unknown.</param>
/// <param name="VersionMismatch">
/// True when the caller passed a version and the running exe is another build. Reading
/// continues; nothing throws.
/// </param>
/// <param name="Dialogs">
/// The class names of the dialogs shown at the top of the client's UI, such as
/// <c>CLoginDialog</c> (Battle.net authentication), <c>CStandardDialog</c> (a message with an OK
/// button) or <c>CProgressBarDialog</c> (the DOWNLOADING dialog). Empty when none is shown or
/// memory cannot tell. Some purchase dialogs keep their visible bit while they are off screen, so
/// match the class you need (<see cref="DialogShown"/>).
/// </param>
/// <param name="LaunchResultCode">
/// The client's last game-launch result (1 to 24 on 2.57), 0 when the last launch has no
/// result, null when memory cannot tell. A new launch clears it.
/// </param>
/// <param name="LaunchResult">
/// The message key of <paramref name="LaunchResultCode"/> without <c>@UI/</c>, from the client's
/// own table, for example <c>GameLaunchBaseBuildMissing</c> or
/// <c>GameLaunchDataBuildNumMismatch</c>. Null when there is none.
/// </param>
/// <param name="LaunchState">
/// The client's game-launch state: 0 when no launch is under way, other values while one is (on
/// 2.57.0.98348, 6 on the DOWNLOADING dialog and 8 while a replay starts loading). Null when
/// memory cannot tell. For diagnostics; the values are not a contract.
/// </param>
public readonly record struct ClientScreenSample(
    ClientScreenKind Screen,
    IReadOnlyList<string> Shown,
    bool MenuSeen,
    string Reason,
    HeroesClientVersion ClientVersion = null,
    bool VersionMismatch = false,
    IReadOnlyList<string> Dialogs = null,
    int? LaunchResultCode = null,
    string LaunchResult = null,
    int? LaunchState = null
)
{
    /// <summary>True when memory can tell which screen this is.</summary>
    public bool Ok => Screen != ClientScreenKind.Unknown;

    /// <summary>
    /// True on a map loading screen, false on any other known screen (the boot splash included),
    /// null when memory cannot tell. When the loading screen's panels do not read
    /// (<see cref="ClientScreenKind.Loading"/>), a loading screen counts as a map only after this
    /// process has shown a menu or a match.
    /// </summary>
    public bool? MapLoading =>
        Screen == ClientScreenKind.MapLoading ? true
        : Screen == ClientScreenKind.Loading ? (MenuSeen ? true : null)
        : Ok ? false
        : null;

    /// <summary>True on the home screen, false on any other known screen, null when unknown.</summary>
    public bool? OnHome => Is(ClientScreenKind.Home);

    /// <summary>
    /// True on the email and password form, false on any other known screen (Battle.net
    /// authentication included), null when unknown.
    /// </summary>
    public bool? OnLogin => Is(ClientScreenKind.Login);

    /// <summary>
    /// True while Battle.net authentication connects, false on any other known screen, null when
    /// unknown.
    /// </summary>
    public bool? OnAuthenticating => Is(ClientScreenKind.Authenticating);

    /// <summary>
    /// True on any loading screen (the boot splash or a map), false on any other known screen,
    /// null when unknown.
    /// </summary>
    public bool? OnLoading =>
        Ok
            ? Screen
                is ClientScreenKind.Loading
                    or ClientScreenKind.Splash
                    or ClientScreenKind.MapLoading
            : null;

    /// <summary>
    /// True on a game-launch message dialog, false on any other known screen, null when unknown.
    /// </summary>
    public bool? OnDialog => Is(ClientScreenKind.Dialog);

    /// <summary>
    /// True on the game-data DOWNLOADING dialog, false on any other known screen, null when
    /// unknown.
    /// </summary>
    public bool? OnDownload => Is(ClientScreenKind.Download);

    /// <summary>True when a dialog of exactly this class is shown at the top of the UI.</summary>
    public bool DialogShown(string className)
    {
        if (Dialogs == null || className == null)
        {
            return false;
        }

        foreach (string dialog in Dialogs)
        {
            if (string.Equals(dialog, className, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>True on the score screen, false on any other known screen, null when unknown.</summary>
    public bool? OnScore => Is(ClientScreenKind.Score);

    /// <summary>
    /// True on the MVP and awards screen, false on any other known screen, null when unknown.
    /// </summary>
    public bool? OnAwards => Is(ClientScreenKind.Awards);

    /// <summary>
    /// False on the login form and while Battle.net authentication connects, true on the home
    /// screen (which only a signed-in client reaches), null otherwise.
    /// </summary>
    public bool? SignedIn =>
        Screen is ClientScreenKind.Login or ClientScreenKind.Authenticating ? false
        : Screen == ClientScreenKind.Home ? true
        : null;

    /// <summary>Obsolete: use <see cref="Ok"/>.</summary>
    [Obsolete("Use Ok. Removed after 0.4.")]
    public bool Known => Ok;

    /// <summary>Obsolete: use <see cref="OnHome"/>.</summary>
    [Obsolete("Use OnHome. Removed after 0.4.")]
    public bool? Home => OnHome;

    /// <summary>Obsolete: use <see cref="OnLogin"/>.</summary>
    [Obsolete("Use OnLogin. Removed after 0.4.")]
    public bool? LoginForm => OnLogin;

    /// <summary>Obsolete: use <see cref="OnLoading"/>.</summary>
    [Obsolete("Use OnLoading. Removed after 0.4.")]
    public bool? Loading => OnLoading;

    /// <summary>Obsolete: use <see cref="OnScore"/>.</summary>
    [Obsolete("Use OnScore. Removed after 0.4.")]
    public bool? ScoreScreen => OnScore;

    /// <summary>Obsolete: use <see cref="OnAwards"/>.</summary>
    [Obsolete("Use OnAwards. Removed after 0.4.")]
    public bool? AwardsScreen => OnAwards;

    private bool? Is(ClientScreenKind kind) => Ok ? Screen == kind : null;
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
/// (mask 0x6181). Keep one per client process; it starts over by itself on a new process (pid
/// and start time).
/// </summary>
public sealed class ClientScreen : IDisposable
{
    private static readonly TimeSpan RediscoverAfter = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan PanelWalkInterval = TimeSpan.FromSeconds(5);

    private readonly ProcessAttachment attachment = new();
    private readonly TimeProvider time;
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
    private long launchGlobalRva;
    private int launchResultOffset;
    private int launchStateOffset;
    private List<string> launchKeys = new();

    /// <summary>
    /// A reader with <paramref name="options"/>, or the defaults when null. The menu screens need
    /// no per-build data: the offsets and the screen names come from the client itself.
    /// </summary>
    public ClientScreen(HeroesClientOptions options = null)
    {
        time = options?.TimeProvider ?? TimeProvider.System;
    }

    internal long GlobalRva => globalRva;

    internal int MaskOffset => offsets.Mask;

    internal int FramesOffset => offsets.Frames;

    internal IReadOnlyList<string> ScreenNames => names;

    internal long AwardsVtable => awardsPanel.Vtable;

    internal long LaunchGlobalRva => launchGlobalRva;

    internal int LaunchResultOffset => launchResultOffset;

    internal int LaunchStateOffset => launchStateOffset;

    internal IReadOnlyList<string> LaunchKeys => launchKeys;

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
    private void LocatePanels(IProcessMemory memory, long root)
    {
        if (
            awardsPanel.Frame != 0
            && (!TryReadPointer(memory, awardsPanel.Frame, out long vt) || vt != awardsPanel.Vtable)
        )
        {
            awardsPanel.Frame = 0;
        }

        if (awardsPanel.Frame != 0 || time.GetUtcNow() < nextPanelWalk)
        {
            return;
        }

        nextPanelWalk = time.GetUtcNow() + PanelWalkInterval;
        panelTop = FrameTree.Top(memory, root);
        (long frame, long vtable) = FrameTree.FindFirst(
            memory,
            panelTop,
            candidate => ClassOf(memory, candidate) == awardsPanel.Name
        );
        awardsPanel.Frame = frame;
        awardsPanel.Vtable = vtable;
    }

    private string ClassOf(IProcessMemory memory, long vtable)
    {
        if (!classes.TryGetValue(vtable, out string name))
        {
            name = FrameClass.Name(memory, vtable, moduleBase, moduleSize);
            classes[vtable] = name;
        }

        return name;
    }

    /// <summary>Null when the panel is not known or not found; else whether it shows.</summary>
    private bool? PanelShown(IProcessMemory memory, Panel panel)
    {
        if (panel.Vtable == 0 || panel.Frame == 0)
        {
            return null;
        }

        return FrameTree.Shown(memory, panel.Frame, panelTop);
    }

    /// <summary>
    /// Reads the menu screens of <paramref name="process"/>. A new process (pid and start time)
    /// starts discovery over. <paramref name="clientVersion"/> is optional: when it is given and
    /// the running exe is another build, the sample says so and reading continues.
    /// </summary>
    public ClientScreenSample Read(Process process, HeroesClientVersion clientVersion = null) =>
        Read(attachment.For(process), clientVersion);

    /// <summary>
    /// Reads the menu screens of an attached <paramref name="client"/> (one handle shared by
    /// every reader, or a client from <see cref="HeroesClientProcess.FromMemory"/>). The client
    /// stays the caller's.
    /// </summary>
    public ClientScreenSample Read(
        HeroesClientProcess client,
        HeroesClientVersion clientVersion = null
    )
    {
        if (client is null || !client.Ok)
        {
            reason = client?.Reason ?? "no-process";
            return new ClientScreenSample(
                ClientScreenKind.Unknown,
                Array.Empty<string>(),
                false,
                reason
            );
        }

        return Read(client.Module, client.Memory, clientVersion);
    }

    internal ClientScreenSample Read(
        ClientModule module,
        IProcessMemory memory,
        HeroesClientVersion clientVersion = null
    )
    {
        if (module.ProcessId <= 0 || module.BaseAddress <= 0 || module.Size <= 0 || memory == null)
        {
            return Sample(ClientScreenKind.Unknown, "no-module", clientVersion);
        }

        UseModule(module);
        if (!discovered || (!Ready && time.GetUtcNow() >= rediscoverAt))
        {
            Discover(memory);
        }

        if (!Ready)
        {
            return Sample(ClientScreenKind.Unknown, reason, clientVersion);
        }

        if (!TryReadPointer(memory, moduleBase + globalRva, out long root))
        {
            return Sample(ClientScreenKind.Unknown, "read-failed", clientVersion);
        }

        if (root == 0)
        {
            return Sample(ClientScreenKind.Unknown, "no-state", clientVersion);
        }

        long top = FrameTree.Top(memory, root);
        IReadOnlyList<string> dialogs = ShownDialogs(memory, top);
        (int? launchCode, string launchResult, int? launchState) = ReadLaunch(memory);
        ClientScreenSample Sampled(
            ClientScreenKind kind,
            IReadOnlyList<string> shown,
            string why
        ) =>
            new(
                kind,
                shown,
                menuSeen,
                why,
                Version(),
                Mismatch(clientVersion),
                dialogs,
                launchCode,
                launchResult,
                launchState
            );

        long loadingFrame = 0;
        int loadingIndex = names.IndexOf("ScreenLoading");
        if (loadingIndex >= 0)
        {
            if (
                !TryReadPointer(memory, root + offsets.Frames + 8L * loadingIndex, out loadingFrame)
            )
            {
                return Sample(ClientScreenKind.Unknown, "read-failed", clientVersion);
            }

            if (loadingFrame == 0)
            {
                // The awards panel exists only in a match, and shows at its end.
                LocatePanels(memory, root);
                if (PanelShown(memory, awardsPanel) == true)
                {
                    menuSeen = true;
                    return Sampled(ClientScreenKind.Awards, Array.Empty<string>(), "awards");
                }

                // A client that is still starting has no frames yet either. Only after it has
                // shown a screen, or while the in-game awards panel exists, do missing frames
                // mean the menus were torn down for a match.
                if (!screenSeen && awardsPanel.Frame == 0)
                {
                    return Sampled(ClientScreenKind.Unknown, Array.Empty<string>(), "starting");
                }

                menuSeen = true;
                return Sampled(ClientScreenKind.Match, Array.Empty<string>(), "match");
            }
        }

        byte[] maskBytes = new byte[8];
        if (!TryRead(memory, root + offsets.Mask, maskBytes))
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
        bool? mapPanel = MapPanelShown(memory, loadingFrame, top);
        if (kind == ClientScreenKind.Loading)
        {
            kind = mapPanel switch
            {
                true => ClientScreenKind.MapLoading,
                false => ClientScreenKind.Splash,
                _ => ClientScreenKind.Loading,
            };
        }
        else if (kind == ClientScreenKind.NoScreen && mapPanel == true)
        {
            // A previous-patch client that loads a replay straight from the file can show the map
            // loading screen without its screen bit (2.57.0.98304, 2026-10-08).
            kind = ClientScreenKind.MapLoading;
        }
        else if (kind == ClientScreenKind.Login && Contains(dialogs, LoginDialog))
        {
            kind = ClientScreenKind.Authenticating;
        }

        if (
            kind
            is ClientScreenKind.Login
                or ClientScreenKind.Home
                or ClientScreenKind.Menu
                or ClientScreenKind.Score
                or ClientScreenKind.NoScreen
        )
        {
            if (Contains(dialogs, DownloadDialog))
            {
                kind = ClientScreenKind.Download;
            }
            else if (MessageDialogShown(dialogs))
            {
                kind = ClientScreenKind.Dialog;
            }
        }

        if (
            kind
            is not (
                ClientScreenKind.Loading
                or ClientScreenKind.Splash
                or ClientScreenKind.MapLoading
                or ClientScreenKind.Login
                or ClientScreenKind.Authenticating
            )
        )
        {
            LocatePanels(memory, root);
            if (PanelShown(memory, awardsPanel) == true)
            {
                kind = ClientScreenKind.Awards;
            }
        }

        if (shown.Count > 0)
        {
            screenSeen = true;
        }

        if (
            kind
            is not (
                ClientScreenKind.Loading
                or ClientScreenKind.Splash
                or ClientScreenKind.MapLoading
                or ClientScreenKind.NoScreen
            )
        )
        {
            menuSeen = true;
        }

        return Sampled(
            kind,
            shown,
            kind switch
            {
                ClientScreenKind.NoScreen => "no-screen",
                ClientScreenKind.Awards => "awards",
                ClientScreenKind.MapLoading when shown.Count == 0 => "map-panel",
                _ => "screens",
            }
        );
    }

    private const string LoginDialog = "CLoginDialog";
    private const string DownloadDialog = "CProgressBarDialog";
    private const string MapPanel = "CCustomLoadingPanel";
    private const string DialogSuffix = "Dialog";

    /// <summary>The dialogs that put a message with an OK button over the menus.</summary>
    internal static readonly string[] MessageDialogs =
    {
        "CStandardDialog",
        "CBattlenetErrorDialog",
        "CDisconnectedDialog",
    };

    /// <summary>
    /// Whether the loading screen shows its map panel (<c>CCustomLoadingPanel</c>, which holds
    /// the players): true on a map loading screen, false on the boot splash, null when the panel
    /// is not found. Measured on 2026-10-08: hidden (0x72) on the boot splash of 2.57.0.98348 and
    /// 2.57.0.98304, shown (0x7B) on the map loading screens of both.
    /// </summary>
    private bool? MapPanelShown(IProcessMemory memory, long loadingFrame, long top)
    {
        if (loadingFrame == 0)
        {
            return null;
        }

        foreach ((long child, long vtable) in FrameTree.Children(memory, loadingFrame))
        {
            if (ClassOf(memory, vtable) == MapPanel)
            {
                return top != 0
                    ? FrameTree.Shown(memory, child, top)
                    : FrameTree.Visible(memory, loadingFrame) == true
                        && FrameTree.Visible(memory, child) == true;
            }
        }

        return null;
    }

    /// <summary>
    /// The class names of the top frame's children whose name ends with "Dialog" and whose
    /// visible bit is set: the dialogs the client shows over every screen. The list is read on
    /// every call (about 45 children on 2.57): the client creates <c>CLoginDialog</c> when the
    /// login screen first shows, and a cached list made the AUTHENTICATION panel read as the
    /// login form for a moment (2.57.0.98348, 2026-10-08 15:56:22).
    /// </summary>
    private IReadOnlyList<string> ShownDialogs(IProcessMemory memory, long top)
    {
        if (top == 0)
        {
            return Array.Empty<string>();
        }

        var shown = new List<string>();
        foreach ((long child, long vtable) in FrameTree.Children(memory, top))
        {
            string name = ClassOf(memory, vtable);
            if (
                name != null
                && name.EndsWith(DialogSuffix, StringComparison.Ordinal)
                && FrameTree.Visible(memory, child) == true
            )
            {
                shown.Add(name);
            }
        }

        return shown;
    }

    /// <summary>
    /// The last game-launch result and the launch state of the client's launch manager, when
    /// found.
    /// </summary>
    private (int? Code, string Key, int? State) ReadLaunch(IProcessMemory memory)
    {
        if (launchGlobalRva == 0)
        {
            return (null, null, null);
        }

        if (!TryReadPointer(memory, moduleBase + launchGlobalRva, out long manager) || manager == 0)
        {
            return (null, null, null);
        }

        byte[] value = new byte[4];
        if (!TryRead(memory, manager + launchResultOffset, value))
        {
            return (null, null, null);
        }

        int code = BitConverter.ToInt32(value, 0);
        string key = code > 0 && code < launchKeys.Count ? launchKeys[code] : null;
        int? state = null;
        if (launchStateOffset > 0 && TryRead(memory, manager + launchStateOffset, value))
        {
            state = BitConverter.ToInt32(value, 0);
        }

        return (code, key, state);
    }

    private static bool MessageDialogShown(IReadOnlyList<string> dialogs)
    {
        foreach (string dialog in MessageDialogs)
        {
            if (Contains(dialogs, dialog))
            {
                return true;
            }
        }

        return false;
    }

    private static bool Contains(IReadOnlyList<string> values, string value)
    {
        foreach (string item in values)
        {
            if (string.Equals(item, value, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
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

    /// <summary>Closes the process handle this reader opened.</summary>
    public void Dispose()
    {
        attachment.Dispose();
    }

    private bool Ready => globalRva != 0 && offsets.Mask != 0 && names.Count > 0;

    private ClientScreenSample Sample(
        ClientScreenKind kind,
        string why,
        HeroesClientVersion clientVersion
    ) => new(kind, Array.Empty<string>(), menuSeen, why, Version(), Mismatch(clientVersion));

    private HeroesClientVersion Version() => HeroesClientVersion.TryParse(fileVersion);

    private bool Mismatch(HeroesClientVersion clientVersion)
    {
        HeroesClientVersion running = Version();
        return clientVersion is not null && running is not null && running != clientVersion;
    }

    private void UseModule(ClientModule module)
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
        launchGlobalRva = 0;
        launchResultOffset = 0;
        launchStateOffset = 0;
        launchKeys = new List<string>();
    }

    private void Discover(IProcessMemory memory)
    {
        discovered = true;
        rediscoverAt = time.GetUtcNow() + RediscoverAfter;
        if (!ModuleScanner.TrySections(memory, moduleBase, moduleSize, out var sections))
        {
            reason = "read-failed";
            return;
        }

        var globals = new List<long>();
        var sites = new List<GlueScreenPattern.Offsets>();
        var launchGlobals = new List<long>();
        var resultOffsets = new List<int>();
        var stateSites = new List<(long Global, int Offset)>();
        int overlap = Math.Max(
            Math.Max(LoadingScreenPattern.Width, GlueScreenPattern.Width),
            GameLaunchPattern.CreatorWidth
        );
        foreach (ModuleSection section in sections)
        {
            if (!section.Executable)
            {
                continue;
            }

            ModuleScanner.Walk(
                memory,
                moduleBase,
                section,
                overlap,
                (slice, rva) =>
                {
                    globals.AddRange(LoadingScreenPattern.Find(slice, rva));
                    sites.AddRange(GlueScreenPattern.Find(slice));
                    launchGlobals.AddRange(GameLaunchPattern.FindGlobals(slice, rva));
                    resultOffsets.AddRange(GameLaunchPattern.FindResultOffsets(slice));
                    stateSites.AddRange(GameLaunchPattern.FindStateOffsets(slice, rva));
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
        List<string> keys = new();
        foreach (ModuleSection section in sections)
        {
            if (section.Executable || section.Name != ".rdata")
            {
                continue;
            }

            byte[] data = ModuleScanner.ReadSection(memory, moduleBase, section);
            table = GlueScreenTable.Find(data, section.VirtualAddress, moduleBase, moduleSize);
            keys = GameLaunchTable.Find(data, section.VirtualAddress, moduleBase, moduleSize);
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

        // The launch result is extra: a build without these sites still reads its screens.
        if (
            GameLaunchPattern.TryAgree(launchGlobals, out long launchRva)
            && launchRva > 0
            && launchRva <= moduleSize - 8
            && GameLaunchPattern.TryAgree(resultOffsets, out int resultOffset)
        )
        {
            launchGlobalRva = launchRva;
            launchResultOffset = resultOffset;
            launchKeys = keys;
            var states = new List<int>();
            foreach ((long site, int offset) in stateSites)
            {
                if (site == launchRva)
                {
                    states.Add(offset);
                }
            }

            launchStateOffset = GameLaunchPattern.TryAgree(states, out int stateOffset)
                ? stateOffset
                : 0;
        }
    }

    /// <summary>
    /// A pointer is zero or a user-mode address. Anything else is not this structure.
    /// </summary>
    private static bool TryReadPointer(IProcessMemory memory, long address, out long value)
    {
        value = 0;
        byte[] buffer = new byte[8];
        if (!TryRead(memory, address, buffer))
        {
            return false;
        }

        value = BitConverter.ToInt64(buffer, 0);
        return value == 0 || (value >= 0x10000 && value <= 0x7FFF_FFFF_FFFF);
    }

    private static bool TryRead(IProcessMemory memory, long address, byte[] buffer)
    {
        return address > 0 && memory.TryRead(address, buffer);
    }
}
