using System;

namespace HeroesClientSDK;

/// <summary>
/// Settings a reader is constructed with. Every property is optional. The client version is not
/// here: it is an optional argument of each read, so one reader can follow a client across
/// replays of different builds.
/// </summary>
public sealed record HeroesClientOptions
{
    /// <summary>
    /// The per-build data (<see cref="BuildProfileRegistry.Default"/> when null): fixed clock
    /// addresses and the loading-screen layout, chosen by the running exe's build.
    /// </summary>
    public BuildProfileRegistry Profiles { get; init; } = BuildProfileRegistry.Default;

    /// <summary>
    /// The clock for rediscovery, clock confirmation and stall timing
    /// (<see cref="TimeProvider.System"/> when null).
    /// </summary>
    public TimeProvider TimeProvider { get; init; } = TimeProvider.System;
}
