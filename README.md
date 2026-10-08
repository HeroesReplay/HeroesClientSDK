# HeroesClientSDK

Read-only access to a running Heroes of the Storm client's memory on Windows:

- **Match clock** (`StableMatchClock`): the in-game match time, read from the client's tick
  counter. It is found per client build from the clock instruction pattern, so a new patch does
  not need new addresses. Build `2.55.17.98025` also has fixed addresses as a fallback.
- **Screen state** (`LoadingScreenMemory`): whether the client shows a menu, a loading screen
  (boot splash or map loading), or a match.
- **Menu screens** (`ClientScreenMemory`): which screen the client shows, by the client's own
  screen names: the login form, home, the loading screen, the score screen, another menu, or a
  match. It also says whether the client is signed in (false on the login form, true on home),
  and reads two UI panels from the client's frame tree: the MVP and awards screen at the end of a
  match (`Awards`) and the game data download panel (`Download`).

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
$version = '0.1.0'
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
dotnet add package HeroesClientSDK --version 0.1.0
```

## Usage

The package targets `net10.0` and is Windows only (`[SupportedOSPlatform("windows")]`).

```csharp
using System.Diagnostics;
using HeroesClientSDK;

Process client = Process.GetProcessesByName("HeroesOfTheStorm_x64")[0];

using var clock = new StableMatchClock();
StableClockSample sample = clock.Read(client);
if (sample.Ok)
{
    Console.WriteLine($"Match time {TimeSpan.FromSeconds(sample.Seconds):mm\\:ss}");
}
else
{
    // "near-zero" (menu or loading screen), "confirming", "stalled", "unsupported-build", ...
    Console.WriteLine($"No match clock: {sample.Reason}");
}

// A match is running only when two reads 250 ms apart move forward.
TimeSpan? running = await StableMatchClock.ReadRunningAsync(
    () => clock.Read(client),
    () => Task.Delay(StableMatchClock.RunningProbe)
);

using var screens = new LoadingScreenMemory();
LoadingScreenSample screen = screens.Read(client);
Console.WriteLine($"{screen.Screen} (menu seen: {screen.MenuSeen}, map loading: {screen.MapLoading})");

// The client version is optional. Pass one only to be told when the running exe is another build.
using var menus = new ClientScreenMemory();
ClientScreenSample menu = menus.Read(client);
// Home, Login, Loading, Score, Menu, Match, NoScreen or Unknown, plus the screens shown
Console.WriteLine($"{menu.Screen} [{string.Join(", ", menu.Shown)}] signed in: {menu.SignedIn}");
```

Keep one `StableMatchClock`, one `LoadingScreenMemory` and one `ClientScreenMemory` per client for the life of your watcher. Each
reader starts over by itself when it sees a new client process (pid and start time), and
retries a failed pattern scan every 10 seconds while a fresh client is still unpacking its code.

### Any client build, any number of clients

The SDK must work with whatever Heroes of the Storm builds are installed, side by side. These
rules hold for every release:

- **No version is required.** No API needs the client version. When a later API accepts one, it
  is optional (`HeroesClientVersion? clientVersion = null`, or an options type with a nullable
  `ClientVersion`); null means detect it from the process, or use the generic path.
- **An unknown or different build never throws.** Reads find the clock and the screen state
  from instruction patterns, so a new build works without new addresses. A build the patterns do
  not match reads as not ok with a reason (`unsupported-build`, `pattern-disagreed`), never an
  exception. Per-build data (today only the fixed addresses of `2.55.17.98025`) is used only when
  the running exe is that build, and only after the pattern scan.
- **A version mismatch is reported, not thrown.**
- **Several clients at once.** Readers keep no static or global client state. Use one reader per
  client process to read several processes, or several builds, at the same time. A single reader
  pointed at a different process starts over for that process.

### How the clock is trusted

- The first ok read of a newly found cell only starts confirming it. A second read that moves
  forward by no more than the wall time between reads plus 8 seconds locks it.
- A clock that stops moving for 8 seconds reads `stalled`. A clock that goes back by more than 5
  seconds is a new match in the same client.
- Zero (the menu and the loading screen) reads `near-zero`, never ok.

`LastTelemetry` reports where discovery stands (`discovering`, `memory-locked`,
`memory-unlocked`) and changes only when the state or reason does, so it is cheap to log.

### How the menu screens are read

The object at the screen-state global is the client's menu root. It holds a 64-bit mask with one
bit per screen that is shown and one frame per screen; `GlueScreenPattern` finds both offsets
from the code that tests a screen (`0x1D4` and `0x1F0` on 2.57.0.98304 and 2.57.0.98348). The
bit of each screen comes from the client's own template table (`ScreenHome/ScreenHome`, ...), so a
build that reorders its screens still reads right. When the menus are torn down for a match the
loading screen's frame is gone, and the sample reads `Match`. A loading screen counts as a map
(`MapLoading`) only after that process has shown a menu or a match, because the boot splash is the
same screen. Measured on 2.57.0.98348: home `0x6181`, the login form `0x60C1`, the boot splash
`0x20`.

### How the awards and download panels are read

The client's UI frames form one tree above the menu root. Each frame keeps its parent at `+0x50`,
its visible bit in the byte at `+0x48`, and its children as an intrusive list (first child node at
`+0x40`, the child's node at `+0x18`, the next sibling's node at `+0x20`, a tagged end). A panel is
found by its frame type: the client registers `EndOfGameAwardsPanel` and `DownloadPanel` with a
factory whose constructor stores the class's vtable, so `FrameTypeLocator` follows the
registration to the vtable and `FrameTree` finds the frame with it (the awards panel is about
6,600 frames into an in-game tree of 86,000, 17 ms). A panel shows when it and every frame above it
are visible. The awards panel exists for the whole match, so a reader that starts mid-match reads
`Match` rather than `Unknown`.

## Probe

`tools/HeroesClientSDK.Probe` prints what the SDK reads from every running client, read-only:

```powershell
dotnet run --project tools/HeroesClientSDK.Probe -c Release -- --watch 250
```

Each line has the build, the menu screen and the screens shown, the signed-in state, the loading
screen reader, and the match clock. `--version 2.57.0.98304` reports a client that is another build.

## Build

```powershell
dotnet tool restore
dotnet csharpier check .
dotnet build HeroesClientSDK.slnx -c Release
dotnet test HeroesClientSDK.slnx -c Release
dotnet pack src/HeroesClientSDK -c Release -o artifacts
```

## Releases

Versions come from git tags (`vX.Y.Z`, MinVer). Pushing a tag runs `publish.yml`, which builds,
tests, and packs that version, pushes it to GitHub Packages, and attaches the same `.nupkg` to the
tag's GitHub Release with its SHA-256 in the notes. An asset already on a release is never
replaced, because consumers pin its hash.

```powershell
git tag v0.1.1
git push origin v0.1.1
```

## Reverse engineering

New reads come from reverse engineering the client. The agent skills in `.agents/skills/` cover
it:

- `ghidra`: Ghidra 12.1 headless, per-build projects, and the GhidraScripts in
  `.agents/skills/ghidra/scripts/`.
- `heroes-client-re`: the workflow for a new read. It covers a read-only memory image of a
  running client, finding and confirming a global, a pattern that holds across builds, tests,
  and the rules.

The client exe is encrypted on disk, so code work uses a memory image. Nothing from the client
(exes, images, Ghidra projects) is committed.

## License

MIT. See [LICENSE](LICENSE).
