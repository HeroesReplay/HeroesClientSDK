using System;
using System.Collections.Generic;
using System.Diagnostics;

namespace HeroesClientSDK;

/// <summary>Which kind of screen the client's screen-state object shows.</summary>
public enum LoadingScreenKind
{
    /// <summary>Memory cannot tell.</summary>
    Unknown,

    /// <summary>A menu, such as the home screen.</summary>
    Menu,

    /// <summary>A loading screen: the boot splash or a map loading screen.</summary>
    Loading,

    /// <summary>No screen object: the client is in a match.</summary>
    Match,
}

/// <summary>One read of the client's loading-screen state.</summary>
/// <param name="Screen">The kind of screen memory shows.</param>
/// <param name="MenuSeen">True once this client process has shown a menu.</param>
/// <param name="Reason">
/// Why, for example "menu", "loading", "match", "no-state", or "unsupported-build".
/// </param>
/// <param name="ClientVersion">The build of the running exe, or null when unknown.</param>
/// <param name="VersionMismatch">
/// True when the caller passed a version and the running exe is another build. Reading
/// continues; nothing throws.
/// </param>
public readonly record struct LoadingScreenSample(
    LoadingScreenKind Screen,
    bool MenuSeen,
    string Reason,
    HeroesClientVersion ClientVersion = null,
    bool VersionMismatch = false
)
{
    /// <summary>True when memory can tell which kind of screen this is.</summary>
    public bool Ok => Screen != LoadingScreenKind.Unknown;

    /// <summary>
    /// True on a map loading screen, false on a menu or in a match, null when memory cannot
    /// tell. The client's boot splash is a loading screen too, so a loading screen counts only
    /// after this process has shown a menu. Null means: read the screen instead.
    /// </summary>
    public bool? MapLoading =>
        !MenuSeen ? null
        : Screen == LoadingScreenKind.Loading ? true
        : Screen is LoadingScreenKind.Menu or LoadingScreenKind.Match ? false
        : null;

    /// <summary>
    /// True on a menu, false on a loading screen or in a match once this process has shown a
    /// menu, null when memory cannot tell.
    /// </summary>
    public bool? OnMenu =>
        Screen == LoadingScreenKind.Menu ? true
        : MenuSeen && Screen is LoadingScreenKind.Loading or LoadingScreenKind.Match ? false
        : null;

    /// <summary>
    /// The client is in a match: no screen object, after this process has shown a menu. A match
    /// is a replay already on screen, so the launch does not wait for a menu that cannot come
    /// (#249). Before the first menu, memory does not count it; the match clock still does.
    /// </summary>
    public bool InMatch => MenuSeen && Screen == LoadingScreenKind.Match;
}

/// <summary>
/// Read-only loading-screen state from client memory, so "WELCOME TO" does not need OCR. The
/// screen-state global G is found per build by <see cref="LoadingScreenPattern"/>; behind it,
/// <see cref="LoadingScreenLayout"/> (from the build's profile) places the screen object and its
/// loading flag. Measured on 2.57.0.98304: boot splash 1, home 0, map loading 1, match null.
/// Keep one per client process; it starts over by itself on a new process (pid and start time).
/// </summary>
public sealed class LoadingScreen : IDisposable
{
    private static readonly TimeSpan RediscoverAfter = TimeSpan.FromSeconds(10);

    private readonly ProcessAttachment attachment = new();
    private readonly BuildProfileRegistry profiles;
    private readonly TimeProvider time;
    private int pid;
    private long startedAt;
    private long moduleBase;
    private long moduleSize;
    private bool discovered;
    private long globalRva;
    private LoadingScreenLayout layout = LoadingScreenLayout.Default;
    private bool menuSeen;
    private DateTimeOffset rediscoverAt;
    private string reason = "no-process";

    /// <summary>A reader with <paramref name="options"/>, or the defaults when null.</summary>
    public LoadingScreen(HeroesClientOptions options = null)
    {
        profiles = options?.Profiles ?? BuildProfileRegistry.Default;
        time = options?.TimeProvider ?? TimeProvider.System;
    }

    internal long GlobalRva => globalRva;

    /// <summary>
    /// Reads the screen state of <paramref name="process"/>. A new process (pid and start time)
    /// starts discovery over and has not seen a menu. <paramref name="clientVersion"/> is
    /// optional: when it is given and the running exe is another build, the sample says so and
    /// reading continues.
    /// </summary>
    public LoadingScreenSample Read(Process process, HeroesClientVersion clientVersion = null) =>
        Read(attachment.For(process), clientVersion);

    /// <summary>
    /// Reads the screen state of an attached <paramref name="client"/> (one handle shared by
    /// every reader, or a client from <see cref="HeroesClientProcess.FromMemory"/>). The client
    /// stays the caller's.
    /// </summary>
    public LoadingScreenSample Read(
        HeroesClientProcess client,
        HeroesClientVersion clientVersion = null
    )
    {
        if (client is null || !client.Ok)
        {
            reason = client?.Reason ?? "no-process";
            return new LoadingScreenSample(LoadingScreenKind.Unknown, false, reason);
        }

        return Read(client.Module, client.Memory, clientVersion);
    }

    internal LoadingScreenSample Read(
        ClientModule module,
        IProcessMemory memory,
        HeroesClientVersion clientVersion = null
    )
    {
        HeroesClientVersion running = HeroesClientVersion.TryParse(module.FileVersion);
        bool mismatch =
            clientVersion is not null && running is not null && running != clientVersion;
        LoadingScreenSample Sample(LoadingScreenKind screen, string why) =>
            new(screen, menuSeen, why, running, mismatch);

        if (module.ProcessId <= 0 || module.BaseAddress <= 0 || module.Size <= 0 || memory == null)
        {
            return Sample(LoadingScreenKind.Unknown, "no-module");
        }

        UseModule(module);
        if (!discovered || (globalRva == 0 && time.GetUtcNow() >= rediscoverAt))
        {
            Discover(memory, running ?? clientVersion);
        }

        if (globalRva == 0)
        {
            return Sample(LoadingScreenKind.Unknown, reason);
        }

        if (!TryReadPointer(memory, moduleBase + globalRva, out long state))
        {
            return Sample(LoadingScreenKind.Unknown, "read-failed");
        }

        if (state == 0)
        {
            return Sample(LoadingScreenKind.Unknown, "no-state");
        }

        if (!TryReadPointer(memory, state + layout.ScreenOffset, out long screen))
        {
            return Sample(LoadingScreenKind.Unknown, "read-failed");
        }

        if (screen == 0)
        {
            return Sample(LoadingScreenKind.Match, "match");
        }

        byte[] flags = new byte[1];
        if (!TryRead(memory, screen + layout.FlagsOffset, flags))
        {
            return Sample(LoadingScreenKind.Unknown, "read-failed");
        }

        if ((flags[0] & (1 << layout.LoadingBit)) == 0)
        {
            menuSeen = true;
            return Sample(LoadingScreenKind.Menu, "menu");
        }

        return Sample(LoadingScreenKind.Loading, "loading");
    }

    /// <summary>Closes the process handle this reader opened.</summary>
    public void Dispose()
    {
        attachment.Dispose();
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

        // A relaunched client starts over: its global and its first menu are its own.
        pid = module.ProcessId;
        startedAt = module.StartedAt;
        moduleBase = module.BaseAddress;
        moduleSize = module.Size;
        discovered = false;
        globalRva = 0;
        layout = LoadingScreenLayout.Default;
        menuSeen = false;
        rediscoverAt = default;
    }

    private void Discover(IProcessMemory memory, HeroesClientVersion build)
    {
        discovered = true;
        rediscoverAt = time.GetUtcNow() + RediscoverAfter;
        layout = profiles.Resolve(build).LoadingScreen ?? LoadingScreenLayout.Default;
        if (!ModuleScanner.TrySections(memory, moduleBase, moduleSize, out var sections))
        {
            reason = "read-failed";
            return;
        }

        var found = new List<long>();
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
                LoadingScreenPattern.Width,
                (slice, rva) => found.AddRange(LoadingScreenPattern.Find(slice, rva))
            );
        }

        // Code that is still being unpacked has no sites yet; the next attempt reads it again.
        if (
            !LoadingScreenPattern.TryAgree(found, out long rva, out _)
            || rva <= 0
            || rva > moduleSize - 8
        )
        {
            reason = found.Count == 0 ? "unsupported-build" : "pattern-disagreed";
            return;
        }

        globalRva = rva;
        reason = "pattern";
    }

    private static bool TryReadPointer(IProcessMemory memory, long address, out long value) =>
        ModuleScanner.TryReadPointer(memory, address, out value);

    private static bool TryRead(IProcessMemory memory, long address, byte[] buffer)
    {
        return address > 0 && memory.TryRead(address, buffer);
    }
}
