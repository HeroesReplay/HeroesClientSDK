using System;
using System.Collections.Generic;

namespace HeroesClientSDK;

/// <summary>
/// One walk of a client's code for every pattern the two screen readers need: the screen-state
/// global (<see cref="LoadingScreenPattern"/>, both readers), the menu root's mask and frames
/// (<see cref="GlueScreenPattern"/>) and the game-launch manager (<see cref="GameLaunchPattern"/>).
/// Each reader decides from the sites what it found, with its own reasons. Each site is counted
/// once, by the chunk it starts in.
/// </summary>
internal sealed class ScreenScan
{
    /// <summary>The widest pattern: the overlap between two chunks of a walk.</summary>
    internal static readonly int Overlap = Math.Max(
        Math.Max(LoadingScreenPattern.Width, GlueScreenPattern.Width),
        Math.Max(
            GameLaunchPattern.CreatorWidth,
            Math.Max(GameLaunchPattern.ResultWidth, GameLaunchPattern.StateWidth)
        )
    );

    private ScreenScan(ClientModule module, DateTimeOffset at, List<ModuleSection> sections)
    {
        Module = module;
        At = at;
        Sections = sections;
    }

    /// <summary>The module this scan read.</summary>
    public ClientModule Module { get; }

    /// <summary>When the scan ran, by the clock of the reader that asked for it.</summary>
    public DateTimeOffset At { get; }

    /// <summary>The module's sections; null when its headers did not read.</summary>
    public IReadOnlyList<ModuleSection> Sections { get; }

    /// <summary>The global each screen-state site names.</summary>
    public List<long> ScreenGlobals { get; } = new();

    /// <summary>The mask and frames offsets each menu-root site names.</summary>
    public List<GlueScreenPattern.Offsets> GlueSites { get; } = new();

    /// <summary>The global each game-launch creator site names.</summary>
    public List<long> LaunchGlobals { get; } = new();

    /// <summary>The result offset each game-launch store site names.</summary>
    public List<int> ResultOffsets { get; } = new();

    /// <summary>The global and launch-state offset each state test names.</summary>
    public List<(long Global, int Offset)> StateSites { get; } = new();

    /// <summary>
    /// True when every pattern agreed: the code is unpacked and another walk of this process would
    /// find the same sites.
    /// </summary>
    public bool Complete =>
        Sections != null
        && LoadingScreenPattern.TryAgree(ScreenGlobals, out _, out _)
        && GlueScreenPattern.TryAgree(GlueSites, out _)
        && GameLaunchPattern.TryAgree(LaunchGlobals, out _)
        && GameLaunchPattern.TryAgree(ResultOffsets, out _);

    /// <summary>Walks every executable section of <paramref name="module"/> once.</summary>
    public static ScreenScan Run(IProcessMemory memory, ClientModule module, DateTimeOffset at)
    {
        if (
            !ModuleScanner.TrySections(
                memory,
                module.BaseAddress,
                module.Size,
                out List<ModuleSection> sections
            )
        )
        {
            return new ScreenScan(module, at, null);
        }

        var scan = new ScreenScan(module, at, sections);
        foreach (ModuleSection section in sections)
        {
            if (!section.Executable)
            {
                continue;
            }

            long end = section.VirtualAddress + section.VirtualSize;
            ModuleScanner.Walk(
                memory,
                module.BaseAddress,
                section,
                Overlap,
                (slice, rva) =>
                    scan.Visit(slice, rva, (int)Math.Min(ModuleScanner.Chunk, end - rva))
            );
        }

        return scan;
    }

    /// <summary>
    /// One chunk. A pattern of width <c>w</c> only sees the sites that start in the chunk's own
    /// <paramref name="owned"/> bytes, so a site in the overlap is counted by the next chunk only.
    /// </summary>
    private void Visit(byte[] slice, long rva, int owned)
    {
        ReadOnlySpan<byte> Own(int width) =>
            slice.AsSpan(0, Math.Min(slice.Length, owned + width - 1));

        ScreenGlobals.AddRange(LoadingScreenPattern.Find(Own(LoadingScreenPattern.Width), rva));
        GlueSites.AddRange(GlueScreenPattern.Find(Own(GlueScreenPattern.Width)));
        LaunchGlobals.AddRange(
            GameLaunchPattern.FindGlobals(Own(GameLaunchPattern.CreatorWidth), rva)
        );
        ResultOffsets.AddRange(
            GameLaunchPattern.FindResultOffsets(Own(GameLaunchPattern.ResultWidth))
        );
        StateSites.AddRange(
            GameLaunchPattern.FindStateOffsets(Own(GameLaunchPattern.StateWidth), rva)
        );
    }
}

/// <summary>
/// The screen readers' code scans of one client process. <see cref="LoadingScreen"/> and
/// <see cref="ClientScreen"/> that read the same <see cref="HeroesClientProcess"/> share them, so
/// the client's code is walked once for both. Each reader keeps its own discovery timing (a
/// failed discovery is retried every 10 s): when a reader discovers, it takes the last scan if it
/// has not used that scan yet and the scan is complete or younger than 10 s, and otherwise walks
/// the code again.
/// </summary>
internal sealed class ScreenScans
{
    /// <summary>How long an incomplete scan may serve another reader's discovery.</summary>
    internal static readonly TimeSpan Fresh = TimeSpan.FromSeconds(10);

    private readonly object gate = new();
    private ScreenScan last;

    /// <summary>How many times the code was walked.</summary>
    internal int Walks { get; private set; }

    /// <summary>
    /// A scan of <paramref name="module"/> for a reader whose last scan was
    /// <paramref name="seen"/> (null on its first discovery). Two readers on two threads get one
    /// walk: the second waits for the first's.
    /// </summary>
    public ScreenScan For(
        IProcessMemory memory,
        ClientModule module,
        ScreenScan seen,
        DateTimeOffset now
    )
    {
        lock (gate)
        {
            if (
                last != null
                && last.Module == module
                && !ReferenceEquals(last, seen)
                && (last.Complete || now - last.At < Fresh)
            )
            {
                return last;
            }

            Walks++;
            last = ScreenScan.Run(memory, module, now);
            return last;
        }
    }
}
