# HeroesClientSDK

Read-only access to a running Heroes of the Storm client's memory on Windows:

- **Match clock** (`StableMatchClock`): the in-game match time, read from the client's tick
  counter. It is found per client build from the clock instruction pattern, so a new patch does
  not need new addresses. Build `2.55.17.98025` also has fixed addresses as a fallback.
- **Screen state** (`LoadingScreenMemory`): whether the client shows a menu, a loading screen
  (boot splash or map loading), or a match.

Nothing here writes to the client, injects code, or reads the screen. Every reader opens the
process with `PROCESS_QUERY_INFORMATION | PROCESS_VM_READ` only.

This code was moved out of [HeroesReplay](https://github.com/HeroesReplay/HeroesReplay), the
automated Heroes of the Storm spectator, which uses it to drive replays.

## Install

The package is on GitHub Packages. GitHub Packages needs a token for NuGet restores, even for a
public package. Use a classic token with `read:packages` (or `gh auth token` when `gh` is logged
in with that scope).

```xml
<!-- nuget.config -->
<configuration>
  <packageSources>
    <add key="nuget.org" value="https://api.nuget.org/v3/index.json" />
    <add key="github" value="https://nuget.pkg.github.com/HeroesReplay/index.json" />
  </packageSources>
  <packageSourceMapping>
    <packageSource key="nuget.org">
      <package pattern="*" />
    </packageSource>
    <packageSource key="github">
      <package pattern="HeroesClientSDK" />
    </packageSource>
  </packageSourceMapping>
</configuration>
```

Store the credential in your user-level NuGet config, not the repo file:

```powershell
dotnet nuget update source github --username <github-user> --password (gh auth token) --store-password-in-clear-text
```

In GitHub Actions, give the job `packages: read` and authenticate the source with
`GITHUB_TOKEN` before restore.

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
```

Keep one `StableMatchClock` and one `LoadingScreenMemory` for the life of your watcher. Each
reader starts over by itself when it sees a new client process (pid and start time), and
retries a failed pattern scan every 10 seconds while a fresh client is still unpacking its code.

### How the clock is trusted

- The first ok read of a newly found cell only starts confirming it. A second read that moves
  forward by no more than the wall time between reads plus 8 seconds locks it.
- A clock that stops moving for 8 seconds reads `stalled`. A clock that goes back by more than 5
  seconds is a new match in the same client.
- Zero (the menu and the loading screen) reads `near-zero`, never ok.

`LastTelemetry` reports where discovery stands (`discovering`, `memory-locked`,
`memory-unlocked`) and changes only when the state or reason does, so it is cheap to log.

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
tests, packs that version, and pushes it to GitHub Packages.

```powershell
git tag v0.1.1
git push origin v0.1.1
```

## License

MIT. See [LICENSE](LICENSE).
