using System;
using System.Collections.Generic;

namespace HeroesClientSDK;

/// <summary>The match clock's two globals, as RVAs in the client's main module.</summary>
/// <param name="TickRva">The live tick counter (an int32).</param>
/// <param name="SpeedRva">Seconds per tick (a float, 1/4096).</param>
public readonly record struct MatchClockAddresses(long TickRva, long SpeedRva);

/// <summary>
/// Where the loading-screen state sits behind the screen-state global G: <c>[[G]+ScreenOffset]</c>
/// is the current screen object (null in a match), and bit <see cref="LoadingBit"/> of its byte at
/// <see cref="FlagsOffset"/> is set on a loading screen and clear on a menu. Measured on
/// 2.57.0.98304 and 2.57.0.98348.
/// </summary>
/// <param name="ScreenOffset">The screen object's offset in the object at G.</param>
/// <param name="FlagsOffset">The flags byte's offset in the screen object.</param>
/// <param name="LoadingBit">The bit that is set on a loading screen.</param>
public sealed record LoadingScreenLayout(
    long ScreenOffset = 0x218,
    long FlagsOffset = 72,
    int LoadingBit = 0
)
{
    /// <summary>The layout of every known build.</summary>
    public static LoadingScreenLayout Default { get; } = new();
}

/// <summary>
/// How the client's UI frames form one tree, which <see cref="ClientScreen"/> walks for the awards
/// screen, the loading screen's map panel and the dialogs. Every frame keeps its parent at
/// <see cref="ParentOffset"/>, its own visible bit (<see cref="VisibleBit"/>) in the byte at
/// <see cref="FlagsOffset"/>, and its children as an intrusive list: the parent's
/// <see cref="FirstChildOffset"/> points at the first child's list node (child +
/// <see cref="NodeOffset"/>), each child's <see cref="NextOffset"/> points at the next node, and
/// the list ends at a tagged pointer. A frame's class is named by the <c>IsA</c> function in its
/// vtable slot at <see cref="IsASlot"/>. Measured on 2.57.0.98304 and 2.57.0.98348.
/// </summary>
/// <param name="ParentOffset">The parent frame's offset in a frame.</param>
/// <param name="FlagsOffset">The flags byte's offset in a frame.</param>
/// <param name="FirstChildOffset">The offset of the pointer to the first child's list node.</param>
/// <param name="NodeOffset">The list node's offset in a child frame.</param>
/// <param name="NextOffset">The offset of the pointer to the next sibling's list node.</param>
/// <param name="IsASlot">The vtable offset of the frame class's <c>IsA(type)</c>.</param>
/// <param name="VisibleBit">The bit of the flags byte that is set while the frame is visible.</param>
public sealed record FrameTreeLayout(
    long ParentOffset = 0x50,
    long FlagsOffset = 0x48,
    long FirstChildOffset = 0x40,
    long NodeOffset = 0x18,
    long NextOffset = 0x20,
    long IsASlot = 0x240,
    int VisibleBit = 0
)
{
    /// <summary>The layout of every known build.</summary>
    public static FrameTreeLayout Default { get; } = new();
}

/// <summary>
/// Where a message dialog keeps its text. A <c>CStandardDialog</c> (and every dialog built on it:
/// <c>CBattlenetErrorDialog</c>, <c>CDisconnectedDialog</c>, <c>CLoginDialog</c>...) holds its
/// title label at <see cref="TitleLabelOffset"/> and its message label at
/// <see cref="MessageLabelOffset"/>; a <c>CLabel</c> holds a text object at
/// <see cref="LabelTextOffset"/>, whose <see cref="TextStringOffset"/> points at a block with the
/// string at <see cref="StringOffset"/>: a 32-bit length times 4, 32-bit flags (bit 1: the bytes
/// are behind a pointer), then the UTF-8 bytes or that pointer. <see cref="DialogTextPattern"/>
/// finds these in the client's code; this is the fallback when it does not. Measured on
/// 2.57.0.98304 and 2.57.0.98348 (<c>CStandardDialog::ApplyParams</c> and <c>CLabel::SetText</c>).
/// </summary>
/// <param name="TitleLabelOffset">The title label's offset in a standard dialog.</param>
/// <param name="MessageLabelOffset">The message label's offset in a standard dialog.</param>
/// <param name="LabelTextOffset">The text object's offset in a label.</param>
/// <param name="TextStringOffset">The string block's pointer offset in the text object.</param>
/// <param name="StringOffset">The string's offset in that block.</param>
public sealed record DialogTextLayout(
    long TitleLabelOffset = 0x248,
    long MessageLabelOffset = 0x250,
    long LabelTextOffset = 0x1D8,
    long TextStringOffset = 0x28,
    long StringOffset = 0x18
)
{
    /// <summary>The layout of every known build.</summary>
    public static DialogTextLayout Default { get; } = new();
}

/// <summary>
/// Per-build data. The readers find everything by instruction pattern first; a profile adds what
/// a pattern cannot give for that build. It is chosen by the running exe's build (exact build,
/// then patch line, then <see cref="BuildProfileRegistry.Fallback"/>), never by a static setting.
/// </summary>
public sealed record BuildProfile
{
    /// <summary>The profile of any build without its own: patterns only, the known layout.</summary>
    public static BuildProfile Generic { get; } = new();

    /// <summary>A name for logs, such as the build.</summary>
    public string Name { get; init; } = "generic";

    /// <summary>
    /// Fixed clock addresses, tried only after the clock pattern does not resolve, and only
    /// locked after a confirming read. Null for builds without them.
    /// </summary>
    public MatchClockAddresses? FixedClock { get; init; }

    /// <summary>The loading-screen layout.</summary>
    public LoadingScreenLayout LoadingScreen { get; init; } = LoadingScreenLayout.Default;

    /// <summary>The UI frame tree's layout, which <see cref="ClientScreen"/> walks.</summary>
    public FrameTreeLayout FrameTree { get; init; } = FrameTreeLayout.Default;

    /// <summary>
    /// Where a message dialog keeps its title and message text, used when
    /// <see cref="DialogTextPattern"/> does not find it in the client's code.
    /// </summary>
    public DialogTextLayout DialogText { get; init; } = DialogTextLayout.Default;
}

/// <summary>
/// The per-build profiles a reader may use. It is immutable: <see cref="WithBuild"/> and
/// <see cref="WithPatchLine"/> return a new registry, and a reader gets its registry from
/// <see cref="HeroesClientOptions.Profiles"/>, so two readers never share a mutable setting.
/// </summary>
public sealed class BuildProfileRegistry
{
    private readonly Dictionary<HeroesClientVersion, BuildProfile> builds;
    private readonly Dictionary<string, BuildProfile> patchLines;

    /// <summary>A registry with only <paramref name="fallback"/> (generic when null).</summary>
    public BuildProfileRegistry(BuildProfile fallback = null)
        : this(fallback ?? BuildProfile.Generic, new(), new(StringComparer.Ordinal)) { }

    private BuildProfileRegistry(
        BuildProfile fallback,
        Dictionary<HeroesClientVersion, BuildProfile> builds,
        Dictionary<string, BuildProfile> patchLines
    )
    {
        Fallback = fallback;
        this.builds = builds;
        this.patchLines = patchLines;
    }

    /// <summary>
    /// The SDK's known builds: the generic profile, and 2.55.17.98025 with its fixed clock
    /// addresses (Ghidra, HeroesReplay#249) after the pattern.
    /// </summary>
    public static BuildProfileRegistry Default { get; } =
        new BuildProfileRegistry().WithBuild(
            new HeroesClientVersion(2, 55, 17, 98025),
            new BuildProfile
            {
                Name = MatchTickClock.SupportedBuild,
                FixedClock = new MatchClockAddresses(
                    MatchTickClock.MatchTickRva,
                    MatchTickClock.GameSpeedFactorRva
                ),
            }
        );

    /// <summary>The profile of a build that has no entry, and of an unknown build.</summary>
    public BuildProfile Fallback { get; }

    /// <summary>A copy of this registry with <paramref name="profile"/> for exactly <paramref name="build"/>.</summary>
    public BuildProfileRegistry WithBuild(HeroesClientVersion build, BuildProfile profile)
    {
        ArgumentNullException.ThrowIfNull(build);
        ArgumentNullException.ThrowIfNull(profile);
        var copy = new Dictionary<HeroesClientVersion, BuildProfile>(builds) { [build] = profile };
        return new BuildProfileRegistry(Fallback, copy, patchLines);
    }

    /// <summary>
    /// A copy of this registry with <paramref name="profile"/> for every build of
    /// <paramref name="patchLine"/> (<see cref="HeroesClientVersion.PatchLine"/>, such as
    /// <c>2.57</c>) that has no exact entry.
    /// </summary>
    public BuildProfileRegistry WithPatchLine(string patchLine, BuildProfile profile)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(patchLine);
        ArgumentNullException.ThrowIfNull(profile);
        var copy = new Dictionary<string, BuildProfile>(patchLines, StringComparer.Ordinal)
        {
            [patchLine.Trim()] = profile,
        };
        return new BuildProfileRegistry(Fallback, builds, copy);
    }

    /// <summary>
    /// The profile for <paramref name="version"/>: its exact build, then its patch line, then
    /// <see cref="Fallback"/>. A null version gets <see cref="Fallback"/>. Never throws.
    /// </summary>
    public BuildProfile Resolve(HeroesClientVersion version)
    {
        if (version is null)
        {
            return Fallback;
        }

        if (builds.TryGetValue(version, out BuildProfile exact))
        {
            return exact;
        }

        return patchLines.TryGetValue(version.PatchLine, out BuildProfile line) ? line : Fallback;
    }
}
