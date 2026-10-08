# HeroesClientSDK

Read-only access to a running Heroes of the Storm client's memory on Windows:

- **Match clock** (`MatchClock`): the in-game match time, read from the client's tick counter.
  It is found per client build from the clock instruction pattern, so a new patch does not need
  new addresses. Build `2.55.17.98025` also has fixed addresses as a fallback (its build profile).
- **Loading screen** (`LoadingScreen`): whether the client shows a menu, a loading screen (boot
  splash or map loading), or a match.
- **Menu screens** (`ClientScreen`): which screen the client shows, by the client's own screen
  names and UI frame classes: the email and password form, Battle.net authentication, home, the
  boot splash, a map loading screen, the score screen, another menu, a message dialog, the
  game-data DOWNLOADING dialog, a match, or the MVP and awards screen at its end. It also says
  whether the client is signed in (false on the login screen, true on home), which dialogs are
  shown, and the client's last game-launch result by its message key (for example
  `GameLaunchBaseBuildMissing`).

Nothing here writes to the client, injects code, or reads the screen. Every reader opens the
process with `PROCESS_QUERY_INFORMATION | PROCESS_VM_READ` only.

This code was moved out of [HeroesReplay](https://github.com/HeroesReplay/HeroesReplay), the
automated Heroes of the Storm spectator, which uses it to drive replays.

## Install

Every release attaches `HeroesClientSDK.<version>.nupkg` to its
[GitHub Release](https://github.com/HeroesReplay/HeroesClientSDK/releases). That file downloads
without credentials, and the release notes give its SHA-256. The same file is also pushed to
GitHub Packages.

### From the release asset (no credentials)

Download the file into a local folder, check its SHA-256, and map the package to that folder:

```powershell
$version = '0.4.0'
New-Item -ItemType Directory -Force .packages | Out-Null
Invoke-WebRequest "https://github.com/HeroesReplay/HeroesClientSDK/releases/download/v$version/HeroesClientSDK.$version.nupkg" -OutFile ".packages/HeroesClientSDK.$version.nupkg"
(Get-FileHash ".packages/HeroesClientSDK.$version.nupkg" -Algorithm SHA256).Hash   # compare with the release notes
```

```xml
<!-- nuget.config -->
<configuration>
  <packageSources>
    <clear />
    <add key="nuget.org" value="https://api.nuget.org/v3/index.json" />
    <add key="heroesclientsdk" value=".packages" />
  </packageSources>
  <packageSourceMapping>
    <packageSource key="nuget.org">
      <package pattern="*" />
    </packageSource>
    <packageSource key="heroesclientsdk">
      <package pattern="HeroesClientSDK" />
    </packageSource>
  </packageSourceMapping>
</configuration>
```

HeroesReplay does this with `tools/restore-sdk-package.ps1`. It pins the version and the SHA-256
together in `Directory.Packages.props`.

### From GitHub Packages

GitHub Packages needs a token with `read:packages` for any NuGet restore, even of a public
package. Add `https://nuget.pkg.github.com/HeroesReplay/index.json` as a source, map
`HeroesClientSDK` to it, and keep the credential in your user-level NuGet config, not in the
repo. In GitHub Actions, the job needs `packages: read`, and the package must grant that
repository read access.

```powershell
dotnet add package HeroesClientSDK --version 0.4.0
```

## Usage

The package targets `net10.0` and is Windows only (`[SupportedOSPlatform("windows")]`).

```csharp
using System.Diagnostics;
using HeroesClientSDK;

Process client = Process.GetProcessesByName("HeroesOfTheStorm_x64")[0];

using var clock = new MatchClock();
MatchClockSample sample = clock.Read(client);
if (sample.Time is TimeSpan time)
{
    Console.WriteLine($"Match time {time:mm\\:ss}");
}
else
{
    // "near-zero" (menu or loading screen), "confirming", "stalled", "unsupported-build", ...
    Console.WriteLine($"No match clock: {sample.Reason}");
}

// A match is running only when two reads 250 ms apart move forward.
TimeSpan? running = await MatchClock.ReadRunningAsync(
    () => clock.Read(client),
    () => Task.Delay(MatchClock.RunningProbe)
);

using var screens = new LoadingScreen();
LoadingScreenSample screen = screens.Read(client);
Console.WriteLine($"{screen.Screen} (menu seen: {screen.MenuSeen}, map loading: {screen.MapLoading})");

// The client version is optional. Pass one only to be told when the running exe is another build.
using var menus = new ClientScreen();
ClientScreenSample menu = menus.Read(client, HeroesClientVersion.TryParse("2.57.0.98348"));
// Home, Login, Authenticating, Splash, MapLoading, Score, Awards, Menu, Dialog, Download, Match,
// NoScreen or Unknown, plus the screens and dialogs shown and the last game-launch result
Console.WriteLine($"{menu.Screen} [{string.Join(", ", menu.Shown)}] signed in: {menu.SignedIn}");
if (menu.VersionMismatch)
{
    Console.WriteLine($"The running exe is {menu.ClientVersion}");
}
```

Keep one `MatchClock`, one `LoadingScreen` and one `ClientScreen` per client for the life of your
watcher. Each reader starts over by itself when it sees a new client process (pid and start time),
and retries a failed pattern scan every 10 seconds while a fresh client is still unpacking its
code.

### The API in one table

| Type | What it is |
| --- | --- |
| `MatchClock`, `LoadingScreen`, `ClientScreen` | The readers. Each has `Read(Process process, HeroesClientVersion clientVersion = null)` and `Read(HeroesClientProcess client, HeroesClientVersion clientVersion = null)`. |
| `MatchClockSample`, `LoadingScreenSample`, `ClientScreenSample` | One read each. Every sample has `Ok`, `Reason`, `ClientVersion` (the running exe, or null) and `VersionMismatch`. |
| `HeroesClientProcess` | One client attached read-only. `Attach(Process)` never throws and says `Ok` and `Reason` (`no-process`, `open-failed`, `no-module`), with `Module` and `DetectedVersion`. Pass it to every reader to share one handle. `FromMemory(IProcessMemory, ClientModule)` serves a fake or recorded memory instead of a process. |
| `IProcessMemory` | `TryRead(address, buffer)`: the only thing a reader needs from a client. Implement it for tests. |
| `HeroesClientOptions` | Optional reader settings: `Profiles` and `TimeProvider`. |
| `BuildProfileRegistry`, `BuildProfile` | Per-build data, looked up by the running exe's build: exact build, then patch line (`2.57`), then `Fallback`. Immutable. `Default` holds the generic profile and the fixed clock of `2.55.17.98025`. |
| `HeroesClientVersion` | A client build: `TryParse`, `FromFile`, `PatchLine`, comparable. |
| `MatchClockTelemetry` | Where clock discovery stands (`discovering`, `memory-locked`, `memory-unlocked`), from `MatchClock.LastTelemetry`. |

To read one client with all three readers on one handle, or a client served from memory in a test:

```csharp
using HeroesClientProcess attached = HeroesClientProcess.Attach(client);
if (!attached.Ok)
{
    Console.WriteLine($"Not attached: {attached.Reason}"); // no-process, open-failed, no-module
}

MatchClockSample now = clock.Read(attached);
ClientScreenSample shown = menus.Read(attached);

// In a test: any IProcessMemory, with the module it serves.
using HeroesClientProcess fake = HeroesClientProcess.FromMemory(
    myFakeMemory,
    new ClientModule(ProcessId: 1, BaseAddress: 0x140000000, Size: 0x4000000, FileVersion: "2.57.0.98348")
);
```

### Any client build, any number of clients

The SDK must work with whatever Heroes of the Storm builds are installed, side by side. These
rules hold for every release:

- **No version is required.** No API needs the client version. Every read takes an optional one
  (`HeroesClientVersion? clientVersion = null`). It is an expectation: when the running exe is
  another build, the sample says `VersionMismatch`, and per-build data still follows the running
  exe. A passed version picks the build profile only when the exe has no readable version.
- **An unknown or different build never throws.** Reads find the clock and the screen state
  from instruction patterns, so a new build works without new addresses. A build the patterns do
  not match reads as not ok with a reason (`unsupported-build`, `pattern-disagreed`), never an
  exception. Per-build data (today only the fixed addresses of `2.55.17.98025`) is used only when
  the running exe is that build, and only after the pattern scan.
- **A version mismatch is reported, not thrown.**
- **Several clients at once.** Readers keep no static or global client state, and a
  `BuildProfileRegistry` is an immutable instance passed in options. Use one reader per client
  process to read several processes, or several builds, at the same time. A single reader pointed
  at a different process starts over for that process.

### Moving from 0.3 to 0.4

0.4 renames the public API so that every reader, sample and option follows one pattern
(HeroesClientSDK#7). The 0.3 names marked obsolete below still compile for one release and forward
to the 0.4 types.

| 0.3 | 0.4 |
| --- | --- |
| `StableMatchClock` (obsolete) | `MatchClock` |
| `StableClockSample` (obsolete) | `MatchClockSample`, plus `Time`, `ClientVersion` and `VersionMismatch` |
| `ClockTelemetryReport`, `ClockTelemetry` (obsolete) | `MatchClockTelemetry` (`Discovering`, `Locked`, `Unlocked`, with the same strings). `ClockTelemetry.Changed(a, b)` is `a != b`. |
| `LoadingScreenMemory` (obsolete) | `LoadingScreen` |
| `ClientScreen` (the enum) | `LoadingScreenKind`. The name `ClientScreen` now belongs to the menu-screen reader, so the enum has no shim. |
| `LoadingScreenSample` | The same name, plus `Ok`, `ClientVersion` and `VersionMismatch` |
| `ClientScreenMemory` (obsolete) | `ClientScreen` |
| `ClientScreenSample(Screen, Shown, Reason, ClientVersion, VersionMismatch, MenuSeen)` | `ClientScreenSample(Screen, Shown, MenuSeen, Reason, ClientVersion = null, VersionMismatch = false)` |
| `ClientScreenSample.Known`, `.Home`, `.LoginForm`, `.Loading`, `.ScoreScreen`, `.AwardsScreen` (obsolete) | `.Ok`, `.OnHome`, `.OnLogin`, `.OnLoading`, `.OnScore`, `.OnAwards` |
| `Read(Process)` | `Read(Process, HeroesClientVersion clientVersion = null)` and `Read(HeroesClientProcess, ...)` |

A main module that cannot be read yet now reads `no-module` on every reader, or `no-process` when
the process has exited. Before 0.4 the clock said `unsupported-build` there and the screen readers
said `no-process`.

### How the clock is trusted

- The first ok read of a newly found cell only starts confirming it. A second read that moves
  forward by no more than the wall time between reads plus 8 seconds locks it.
- A clock that stops moving for 8 seconds reads `stalled`. A clock that goes back by more than 5
  seconds is a new match in the same client.
- Zero (the menu and the loading screen) reads `near-zero`, never ok.

`MatchClock.LastTelemetry` reports where discovery stands (`discovering`, `memory-locked`,
`memory-unlocked`) with the reason of the last read. Two reports are equal when the state and
the reason are, so a caller that logs on `!=` logs each change once.

### How the menu screens are read

The object at the screen-state global is the client's menu root. It holds a 64-bit mask with one
bit per screen that is shown and one frame per screen; `GlueScreenPattern` finds both offsets
from the code that tests a screen (`0x1D4` and `0x1F0` on 2.57.0.98304 and 2.57.0.98348). The
bit of each screen comes from the client's own template table (`ScreenHome/ScreenHome`, ...), so a
build that reorders its screens still reads right. When the menus are torn down for a match the
loading screen's frame is gone, and the sample reads `Match`. Measured on 2.57.0.98348: home
`0x6181`, the login screen `0x60C1`, the boot splash `0x20`.

Some screens need the UI frames as well (see the awards screen below for how frames and their
class names are read):

- **Boot splash or map.** The boot splash and a map loading screen are the same `ScreenLoading`
  frame. Its `CCustomLoadingPanel` child (the players) shows only on a map loading screen
  (`MapLoading`); on the boot splash it is hidden (`Splash`). A previous-patch client that loads a
  replay straight from the file can show its map loading screen with no screen bit, and the panel
  still names it. When the panel cannot be read the sample says `Loading`, and `MapLoading` falls
  back to "after a menu or a match".
- **Authentication or the login form.** Battle.net's AUTHENTICATION "Connecting..." panel is a
  `CLoginDialog` shown at the top of the UI over `ScreenLoginUnified` (`Authenticating`). The
  email and password form is the same screen with no dialog over it (`Login`).
- **Dialogs.** A shown `CStandardDialog`, `CBattlenetErrorDialog` or `CDisconnectedDialog` puts a
  message with an OK button over the menus (`Dialog`), and a shown `CProgressBarDialog` is the
  game-data DOWNLOADING dialog (`Download`). The client's game-launch manager (a singleton found
  from its creator code, `GameLaunchPattern`) keeps the last launch result, and the client's own
  `@UI/GameLaunch*` table names it (`LaunchResult`), so the message is known without reading its
  text in any language.

### How the awards screen is read

The MVP and awards screen is in-game UI, not a menu screen. The client's UI frames form one tree
above the menu root (`CRoot`, then a `CLayer` per UI, then `CGlueUI` or `CGameUI`). Each frame keeps
its parent at `+0x50`, its visible bit in the byte at `+0x48`, and its children as an intrusive list
(first child node at `+0x40`, the child's node at `+0x18`, the next sibling's node at `+0x20`, a
tagged end). The frame classes carry no RTTI locator, but each one's vtable slot `0x240` is
`IsA(type)`, which calls the class's static type accessor, which loads the class name
(`FrameClass`). So the reader walks the tree once, names each distinct vtable once, and keeps the
`CEndOfGameAwardsPanel` frame. It exists for the whole match (flags `0x7A`) and turns visible on the
MVP screen (`0x7B`): measured on 2.57.0.98348, replay 65823392, 2026-10-08. The tree has about
115,000 frames in a match; a full walk takes about 0.6 s and is repeated at most every 5 s while the
panel is missing. Because the panel exists only in a match, a reader that starts mid-match reads
`Match` rather than `Unknown`.

## Probe

`tools/HeroesClientSDK.Probe` prints what the SDK reads from every running client, read-only:

```powershell
dotnet run --project tools/HeroesClientSDK.Probe -c Release -- --watch 250
```

Each line has the build, the menu screen and the screens shown, the dialogs shown, the last
game-launch result and the launch state, the map loading and signed-in states, the loading screen
reader, and the match clock. The three readers share one `HeroesClientProcess` per client.
`--version 2.57.0.98304` reports a client that is another build.

## Build

```powershell
dotnet tool restore
dotnet csharpier check .
dotnet build HeroesClientSDK.slnx -c Release
dotnet test HeroesClientSDK.slnx -c Release
dotnet pack src/HeroesClientSDK -c Release -o artifacts
```

## Releases

What changed in each release is in [CHANGELOG.md](CHANGELOG.md).

Versions come from git tags (`vX.Y.Z`, MinVer). Pushing a tag runs `publish.yml`, which builds,
tests, and packs that version, pushes it to GitHub Packages, and attaches the same `.nupkg` to the
tag's GitHub Release with its SHA-256 in the notes. An asset already on a release is never
replaced, because consumers pin its hash.

```powershell
git tag v0.1.1
git push origin v0.1.1
```

## Reverse engineering and agent skills

New reads come from reverse engineering the client. [AGENTS.md](AGENTS.md) has the repo rules
(read-only, several clients at once, an optional version, nothing from the client committed) and
lists every skill in `.agents/skills/`:

- [`heroes-client-re`](.agents/skills/heroes-client-re/SKILL.md): the workflow for a new read. It
  covers the rules, a read-only memory image of a running client, the UI frame tree and how to name
  a frame's class, finding and confirming a global, a pattern that holds across builds, tests, and
  where each HeroesReplay#292 state stands.
- [`ghidra`](.agents/skills/ghidra/SKILL.md): Ghidra 12.1 headless, per-build projects, and the
  GhidraScripts in `.agents/skills/ghidra/scripts/`.
- [`heroes-client-launch`](.agents/skills/heroes-client-launch/SKILL.md): how clients are
  installed, versioned, started and downloaded. It covers `Versions\Base*`, HeroesSwitcher,
  Battle.net and its Agent, the current and previous patch, and missing builds.
- [`dotnet-10-csharpier`](.agents/skills/dotnet-10-csharpier/SKILL.md): this repo's .NET 10
  build, format, test and package setup.
- Official .NET skills from [dotnet/skills](https://github.com/dotnet/skills) (MIT): refactoring,
  MSBuild, NuGet, tests and performance, pinned in `.agents/skills/vendored.json`.

The client exe is encrypted on disk and its loaded image is not, so code work uses a read-only
memory image of a running client. Nothing from the client (exes, images, Ghidra projects) is
committed.

## License

All rights reserved. The source is published for reference only; see [LICENSE](LICENSE).
