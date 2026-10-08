using System;
using System.Collections.Generic;
using System.Linq;

namespace HeroesClientSDK;

/// <summary>What <see cref="MatchClock"/>'s discovery found.</summary>
/// <param name="Reason">
/// "pattern" (the clock pattern's sites agree), "fixed" (the build profile's addresses), or why
/// there is no clock: "unsupported-build", "pattern-disagreed", "out-of-range", or the client's
/// own reason.
/// </param>
/// <param name="TickRva">The tick counter's RVA, or 0.</param>
/// <param name="SpeedRva">The seconds-per-tick global's RVA, or 0.</param>
/// <param name="Sites">How many clock pattern sites the code has.</param>
public sealed record MatchClockDiscovery(string Reason, long TickRva, long SpeedRva, int Sites)
{
    /// <summary>True when the clock's two globals are known.</summary>
    public bool Ok => Reason is "pattern" or "fixed";
}

/// <summary>What <see cref="LoadingScreen"/>'s discovery found.</summary>
/// <param name="Reason">
/// "pattern", or why the screen-state global is not known: "unsupported-build",
/// "pattern-disagreed", "read-failed", or the client's own reason.
/// </param>
/// <param name="GlobalRva">The screen-state global's RVA, or 0.</param>
/// <param name="Sites">How many sites name that global.</param>
public sealed record LoadingScreenDiscovery(string Reason, long GlobalRva, int Sites)
{
    /// <summary>True when the screen-state global is known.</summary>
    public bool Ok => Reason == "pattern";
}

/// <summary>What <see cref="ClientScreen"/>'s discovery found.</summary>
/// <param name="Reason">
/// "pattern", or why the menu screens cannot be read: "unsupported-build", "pattern-disagreed",
/// "no-screen-table", "read-failed", or the client's own reason.
/// </param>
/// <param name="GlobalRva">The menu root's global (the screen-state global), or 0.</param>
/// <param name="MaskOffset">The shown-screens mask's offset in the menu root, or 0.</param>
/// <param name="FramesOffset">The screen frames' offset in the menu root, or 0.</param>
/// <param name="Screens">The client's screen names by index.</param>
/// <param name="LaunchGlobalRva">The game-launch manager's global, or 0.</param>
/// <param name="LaunchResultOffset">The launch result's offset in the manager, or 0.</param>
/// <param name="LaunchStateOffset">The launch state's offset in the manager, or 0.</param>
/// <param name="LaunchKeys">The client's game-launch message keys by result code.</param>
/// <param name="FrameClasses">
/// How many UI frame classes the client's <c>IsA</c> functions name (827 on 2.57).
/// </param>
/// <param name="MissingFrameClasses">
/// The frame classes this reader looks for by name that no <c>IsA</c> function names.
/// </param>
public sealed record ClientScreenDiscovery(
    string Reason,
    long GlobalRva,
    int MaskOffset,
    int FramesOffset,
    IReadOnlyList<string> Screens,
    long LaunchGlobalRva,
    int LaunchResultOffset,
    int LaunchStateOffset,
    IReadOnlyList<string> LaunchKeys,
    int FrameClasses,
    IReadOnlyList<string> MissingFrameClasses
)
{
    /// <summary>
    /// True when the menu root, the screen table, the game-launch manager and every frame class
    /// the reader names are found.
    /// </summary>
    public bool Ok =>
        Reason == "pattern"
        && LaunchGlobalRva != 0
        && LaunchKeys.Count > 0
        && FrameClasses > 0
        && MissingFrameClasses.Count == 0;
}

/// <summary>
/// Every reader's discovery on one client: the patterns, the globals and offsets they name, the
/// client's own tables, and the UI frame classes. It needs only the client's module, not its
/// heap, so it checks a saved module image of a new build offline
/// (<see cref="HeroesClientProcess.FromImage"/>, <c>heroes-client-probe --image</c>) as well as a
/// running client. Match and screen state are not read.
/// </summary>
/// <param name="Reason">"ok", or why the client is not attached (its <see cref="HeroesClientProcess.Reason"/>).</param>
/// <param name="ClientVersion">The client's build, or null when unknown.</param>
/// <param name="Clock">The match clock's discovery.</param>
/// <param name="Loading">The loading screen's discovery.</param>
/// <param name="Menus">The menu screens' discovery.</param>
public sealed record ClientDiscovery(
    string Reason,
    HeroesClientVersion ClientVersion,
    MatchClockDiscovery Clock,
    LoadingScreenDiscovery Loading,
    ClientScreenDiscovery Menus
)
{
    /// <summary>True when every reader found everything it needs.</summary>
    public bool Ok => Reason == "ok" && Clock.Ok && Loading.Ok && Menus.Ok;

    /// <summary>
    /// Runs each reader's discovery on <paramref name="client"/> with <paramref name="options"/>.
    /// <paramref name="clientVersion"/> is optional, as on every read: it picks the build profile
    /// only when the client's module has no version. Never throws. The client stays the caller's.
    /// </summary>
    public static ClientDiscovery Run(
        HeroesClientProcess client,
        HeroesClientOptions options = null,
        HeroesClientVersion clientVersion = null
    )
    {
        if (client is null || !client.Ok)
        {
            string reason = client?.Reason ?? "no-process";
            return new ClientDiscovery(
                reason,
                null,
                new MatchClockDiscovery(reason, 0, 0, 0),
                new LoadingScreenDiscovery(reason, 0, 0),
                new ClientScreenDiscovery(
                    reason,
                    0,
                    0,
                    0,
                    Array.Empty<string>(),
                    0,
                    0,
                    0,
                    Array.Empty<string>(),
                    0,
                    Array.Empty<string>()
                )
            );
        }

        using var clock = new MatchClock(options);
        using var loading = new LoadingScreen(options);
        using var menus = new ClientScreen(options);
        clock.Read(client, clientVersion);
        loading.Read(client, clientVersion);
        menus.Read(client, clientVersion);

        IReadOnlyCollection<string> classes = FrameClassMap.Find(
            client.Memory,
            menus.FrameLayout,
            client.Module.BaseAddress,
            client.Module.Size
        );
        string[] missing = ClientScreen
            .FrameClassNames.Where(name => !classes.Contains(name))
            .ToArray();
        return new ClientDiscovery(
            "ok",
            client.DetectedVersion,
            new MatchClockDiscovery(
                clock.DiscoveryReason,
                clock.CandidateTickRva,
                clock.CandidateSpeedRva,
                clock.PatternSites
            ),
            new LoadingScreenDiscovery(loading.DiscoveryReason, loading.GlobalRva, loading.Sites),
            new ClientScreenDiscovery(
                menus.DiscoveryReason,
                menus.GlobalRva,
                menus.MaskOffset,
                menus.FramesOffset,
                menus.ScreenNames.ToArray(),
                menus.LaunchGlobalRva,
                menus.LaunchResultOffset,
                menus.LaunchStateOffset,
                menus.LaunchKeys.ToArray(),
                classes.Count,
                missing
            )
        );
    }
}

/// <summary>
/// The client's UI frame classes from its module alone: every qword in <c>.rdata</c> that points
/// at an <c>IsA</c> function (<see cref="FrameClass.IsAPrologue"/>) is a vtable slot, and the
/// class name is the one that IsA's StaticType loads (heroes-client-re, "Class map"). On the
/// 2.57 images this names 827 classes.
/// </summary>
internal static class FrameClassMap
{
    public static IReadOnlyCollection<string> Find(
        IProcessMemory memory,
        FrameTreeLayout layout,
        long moduleBase,
        long moduleSize
    )
    {
        var names = new HashSet<string>(StringComparer.Ordinal);
        if (
            layout == null
            || !ModuleScanner.TrySections(
                memory,
                moduleBase,
                moduleSize,
                out List<ModuleSection> sections
            )
        )
        {
            return names;
        }

        var code = new List<(ModuleSection Section, byte[] Bytes)>();
        foreach (ModuleSection section in sections.Where(section => section.Executable))
        {
            code.Add((section, ModuleScanner.ReadSection(memory, moduleBase, section)));
        }

        ReadOnlySpan<byte> prologue = FrameClass.IsAPrologue;
        var tried = new HashSet<long>();
        foreach (ModuleSection data in sections.Where(section => section.Name == ".rdata"))
        {
            byte[] bytes = ModuleScanner.ReadSection(memory, moduleBase, data);
            for (int i = 0; i + 8 <= bytes.Length; i += 8)
            {
                long rva = BitConverter.ToInt64(bytes, i) - moduleBase;
                foreach ((ModuleSection section, byte[] text) in code)
                {
                    long at = rva - section.VirtualAddress;
                    if (
                        at < 0
                        || at > text.Length - prologue.Length
                        || !text.AsSpan((int)at, prologue.Length).SequenceEqual(prologue)
                        || !tried.Add(rva)
                    )
                    {
                        continue;
                    }

                    // The slot is IsA; the vtable starts IsASlot before it, as a read finds it.
                    long vtable = moduleBase + data.VirtualAddress + i - layout.IsASlot;
                    string name = FrameClass.Name(memory, layout, vtable, moduleBase, moduleSize);
                    if (name != null)
                    {
                        names.Add(name);
                    }
                }
            }
        }

        return names;
    }
}
