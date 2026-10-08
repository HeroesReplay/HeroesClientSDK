# Changelog

Each release is a `vX.Y.Z` tag on `main`. Its `.nupkg` and SHA-256 are on the
[GitHub Release](https://github.com/HeroesReplay/HeroesClientSDK/releases) of that tag.

## 0.4.2

The follow-ups to 0.4.0 (#12). Additive: no 0.4.x name changes, and every read behaves as before.

- **Offline check of a saved module image.** `HeroesClientProcess.FromImage(path)` serves the
  image that `Save-ModuleImage.ps1` (skill `heroes-client-re`) saves from a running client, with
  its build from the image's own `FileVersion` string; it never throws (`no-image`, `bad-image`).
  `ClientDiscovery.Run(client, options, clientVersion)` runs every reader's discovery on it (or on
  any client) and reports what each found: `MatchClockDiscovery`, `LoadingScreenDiscovery` and
  `ClientScreenDiscovery` (the menu root, the screen and game-launch tables, and the frame classes
  `ClientScreen` names). `heroes-client-probe --image <file>` prints it. On the 2.57.0.98348 image:
  every reader ok in 248 ms. This replaces the exe-file scan of #1, which cannot work because the
  exe's code is encrypted on disk.
- **The frame tree's layout is profile data.** `FrameTreeLayout` (parent `0x50`, flags `0x48`,
  first child `0x40`, node `0x18`, next `0x20`, IsA slot `0x240`, visible bit 0) is
  `BuildProfile.FrameTree`, chosen like `LoadingScreenLayout` by the running exe's build (a passed
  version only when the exe has none). `ClientScreen` now uses `HeroesClientOptions.Profiles`. A
  patch that moves the tree needs a registry entry, not a code change.
- **One code scan per process for both screen readers.** A `LoadingScreen` and a `ClientScreen`
  that read the same `HeroesClientProcess` walk the client's code once between them. Each keeps
  its own 10-second retry of a failed discovery: it takes the last scan only when it has not used
  it and the scan is complete or younger than 10 seconds. `Read(Process)` gives each reader its own
  attachment, so it scans as before.
- A pattern site that starts in the overlap of two 1 MB chunks of a scan is counted once, by the
  chunk it starts in. Before, both chunks counted it.

## 0.4.1

More client states from memory for HeroesReplay#292. Additive: no 0.4.0 name changes.

- **`ClientScreenKind`** gains `Authenticating`, `Splash`, `MapLoading`, `Dialog` and `Download`.
  - `Splash` / `MapLoading`: the loading screen's `CCustomLoadingPanel` (the players) is hidden on
    the boot splash and shown on a map loading screen, with or without the screen bit and before
    any menu. `Loading` now means the panel did not read.
  - `Authenticating`: Battle.net's AUTHENTICATION "Connecting..." panel, a shown `CLoginDialog`
    over `ScreenLoginUnified`. `Login` (and `OnLogin`) is now the email and password form only.
  - `Dialog`: a shown `CStandardDialog`, `CBattlenetErrorDialog` or `CDisconnectedDialog`.
  - `Download`: the game-data DOWNLOADING dialog, a shown `CProgressBarDialog`.
- **`ClientScreenSample`** gains `Dialogs` (the shown top-level dialogs by class),
  `LaunchResultCode` and `LaunchResult` (the client's last game-launch result and its
  `@UI/GameLaunch*` key, such as `GameLaunchBaseBuildMissing`), `LaunchState`, `OnAuthenticating`,
  `OnDialog`, `OnDownload` and `DialogShown(className)`. `OnLoading` covers `Splash` and
  `MapLoading`, and `SignedIn` is false while authenticating.
- **`GameLaunchPattern`** finds the client's game-launch manager from its creator code and the
  offsets of its result and state from the code that stores and tests them. `GameLaunchTable`
  reads the message keys from the client.

## 0.4.0

A breaking release. The API follows one pattern (#7), and the rest of #1 lands. The reads behave
as before; the 0.3 names stay as `[Obsolete]` forwards for this release
(README "Moving from 0.3 to 0.4").

- **Readers.** `MatchClock`, `LoadingScreen` and `ClientScreen` replace `StableMatchClock`,
  `LoadingScreenMemory` and `ClientScreenMemory`. Each has `Read(Process, HeroesClientVersion
  clientVersion = null)` and `Read(HeroesClientProcess, HeroesClientVersion clientVersion = null)`.
- **Samples.** `MatchClockSample`, `LoadingScreenSample` and `ClientScreenSample` all have `Ok`,
  `Reason`, `ClientVersion` and `VersionMismatch`. `MatchClockSample.Time` is the match time of an
  ok read. `ClientScreenSample` has `OnHome`, `OnLogin`, `OnLoading`, `OnScore` and `OnAwards`, and
  its positional order is now `(Screen, Shown, MenuSeen, Reason, ClientVersion, VersionMismatch)`.
  The loading-screen enum is `LoadingScreenKind` (was `ClientScreen`).
- **`HeroesClientProcess`.** One read-only attachment per process (pid and start time).
  `Attach(Process)` replaces the three private attach copies and never throws.
  `FromMemory(IProcessMemory, ClientModule)` reads a fake or recorded memory. `DetectedVersion` is
  the running exe's build.
- **Attach failures, chosen on purpose.** A main module that cannot be read (a client that has only
  just started) is `no-module`, or `no-process` when the process exited. Before, the clock said
  `unsupported-build` and the screen readers `no-process`. A file version that cannot be read no
  longer fails the attach; the version is optional.
- **`IProcessMemory`** (`TryRead(address, buffer)`) replaces the internal `Func<long, byte[], bool>`.
- **`HeroesClientOptions`** (`Profiles`, `TimeProvider`) replaces the internal `UtcNow` hooks.
- **Build profiles.** `BuildProfileRegistry` looks a build up by exact build, then patch line, then
  `Fallback`, by the running exe's version (a passed version only when the exe has none). It is
  immutable and passed in options. `Default` holds the generic profile and `2.55.17.98025`'s fixed
  clock addresses, which are still tried only after the pattern. `LoadingScreenLayout` (`0x218`,
  `72`, bit 0) is profile data.
- **`ModuleScanner`** is shared by every reader. The clock keeps its scan (the whole section in
  one read, then 1 MB chunks). The dead offline exe scan (`TryResolveFile`) is gone: the exe's
  code is encrypted on disk.
- **`MatchClockTelemetry`** replaces `ClockTelemetryReport` and `ClockTelemetry`. Compare two
  reports with `!=`.
- `ReadProcessMemory` takes and returns `SIZE_T` (it was declared with `int`).
- The probe shares one `HeroesClientProcess` between the three readers of a client.

## 0.3.0

- `ClientScreenMemory` reads the MVP and awards screen at the end of a match
  (`ClientScreenKind.Awards`) from the client's UI frame tree (#6).

## 0.2.0

- `ClientScreenMemory`: the client's menu screens by their own names (login form, home, loading,
  score, another menu, a match), and whether the client is signed in (HeroesReplay#292).
- `HeroesClientVersion`, an optional version argument, and `VersionMismatch` (#1).
- `ModuleScanner`, and the read-only probe `tools/HeroesClientSDK.Probe`.
- Each release's `.nupkg` is attached to its GitHub Release.

## 0.1.0

- The memory match clock (`StableMatchClock`) and the screen state (`LoadingScreenMemory`), moved
  from HeroesReplay unchanged (HeroesReplay#274).
