namespace HeroesClientSDK;

/// <summary>
/// The client's main module as the readers see it. <paramref name="StartedAt"/> (the process start
/// time in UTC ticks, 0 when Windows does not give it) tells a relaunch apart from the same process
/// even when Windows hands the new client the old pid and the same image base, so every
/// per-process cache starts over (HeroesReplay#249).
/// </summary>
/// <param name="ProcessId">The process id.</param>
/// <param name="BaseAddress">Where the main module is loaded.</param>
/// <param name="Size">The main module's size in memory.</param>
/// <param name="FileVersion">
/// The exe's file version as Windows reports it, for example <c>2.57.0.98348</c>, or null.
/// </param>
/// <param name="StartedAt">The process start time in UTC ticks, or 0 when unknown.</param>
public readonly record struct ClientModule(
    int ProcessId,
    long BaseAddress,
    long Size,
    string FileVersion,
    long StartedAt = 0
);
