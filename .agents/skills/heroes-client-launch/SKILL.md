---
name: heroes-client-launch
description: >
  How Heroes of the Storm clients are installed, versioned, launched and downloaded on the
  HeroesReplay machines, so it does not have to be reverse engineered again. Covers the install
  layout (Versions\Base<build> and the exe file version, .build.info, HeroesSwitcher, the CASC
  HeroesData folder), the launch chains (Battle.net --exec="launch Hero" with SSO, a .StormReplay
  through HeroesSwitcher, the newest-exe handoff to an older build), the current and previous
  patch, the missing-build download Blizzard does by itself (with the proven timings), the patch
  line, retained clients in Data\Clients, the firewall rule, Variables.txt and AhliObs, the
  version-mismatch, not-available and region dialogs, Battle.net and its Agent (where they keep
  product state and logs), and which files show a download in progress. Use when a task touches
  which client runs, how it started, which build it is, several clients at once, or a client
  download, or /heroes-client-launch. Memory reads are heroes-client-re; Ghidra is ghidra.
---

# Heroes clients: install, versions, launch, download

The launch logic lives in HeroesReplay (`src/HeroesReplay.Core/GameClient`, AGENTS.md "Current patch
and previous patch"). This skill is the reference for it, plus what a read-only look at
ASA-SERVER's install showed on 2026-10-08. The SDK itself only reads clients that are already running.

## Rules

- **Launch only for live verification or RE, under the lock** (AGENTS.md rule 5). Launching clients
  and reading them with the SDK is the acceptance bar for a new read. An SDK or RE task may start a
  client only on ASA-SERVER, only while it holds `C:\heroesreplay\asa-live.lock` (taken only while
  `C:\heroesreplay\asa-release-pending` does not exist), and only the way HeroesReplay starts one:
  Battle.net `--exec="launch Hero"` (SSO) for the current patch, HeroesSwitcher with a `.StormReplay`
  for a previous patch (or without SSO to show the login form). Read it read-only, close what you
  started with `heroesreplay services stop`, then release the lock. Otherwise, work with a client the
  spectator or the owner started.
- **Never click Play, Update or Allow** in Battle.net, the client or Windows, and never type
  credentials. Never touch the stream PC (DESKTOP-8SJEK72).
- **No credentials, no account identifiers.** Never type credentials. Don't open Battle.net's account
  files (`%APPDATA%\Battle.net\*.config`, `%LOCALAPPDATA%\Battle.net\`). Never copy into a doc, test
  or commit: the `Accounts\<account id>\<toon handle>` folder names, `accountCountry` and
  `lastDeviceId` from `Variables.txt`, BattleTags or e-mail addresses.
- **Don't call the Battle.net Agent's local HTTP API.** It is listed below only so its files make sense.
- **Nothing from the client is committed** (exes, CASC files, logs). See heroes-client-re.

## Install layout

`C:\Program Files (x86)\Heroes of the Storm` (`Location:GameInstallDirectory`; the path has
parentheses, which matters for batch tools, see the ghidra skill):

| Path | What it is |
| --- | --- |
| `Versions\Base<build>\HeroesOfTheStorm_x64.exe` | One client build per folder. The folder holds only that exe. |
| `Support64\HeroesSwitcher_x64.exe` | The `.StormReplay` handler. It starts the right `Base*` exe. Its file version is the current patch (2.57.0.98348), and it is replaced with each patch. |
| `Support64\`, `Support\` | 64-bit and 32-bit runtime DLLs. `Support\` also has `HeroesWeb.exe` (CEF) and `BlizzardError.exe`. |
| `HeroesData\` | The CASC game data that all builds share: `config\` (build configs), `data\` (`data.000`... about 1 GB each, `*.idx`, `shmem`), `indices\`, `ecache\`, `hero\`. 15.6 GB on 2026-10-08. |
| `.build.info` | Pipe-separated table, one row for the active build: `Branch` (region, e.g. `eu`), `Build Key`, `CDN Key`, `CDN Path` (`tpr/Hero-Live-a`), `Tags` (platform, region, locale), `Version` (`2.57.0.98348`), `Product`. Only the current patch is listed. |
| `.product.db`, `.patch.result`, `Launcher.db` | Agent-written install state (protobuf; `0`; the locale `enUS`). |
| `Heroes of the Storm.exe` | Blizzard's launcher stub (file version 1.18.5.3107, not the game; Battle.net uses it as the icon). HeroesReplay never starts it. |

Windows registers `.StormReplay` as `Blizzard.StormReplay`, with the open command
`"...\Support64\HeroesSwitcher_x64.exe" "%1"`. The Agent registers that again on updates and
routinely afterwards (`program_associations` in its log). `battlenet:` URIs open `C:\heroesreplay\Battle.net\Battle.net.exe --uri="%1"`.

## Versions: Base folder, exe file version, replay version

- **Folder name = `Base` + the last number of the exe's file version.** `Versions\Base98348\HeroesOfTheStorm_x64.exe` has
  FileVersion `2.57.0.98348` (ProductVersion `Version 2.57.0.98348`). HeroesReplay maps a
  version to its folder with `ClientBuildArchive.BaseDirectoryName`, and the SDK reads a file's
  version with `HeroesClientVersion.FromFile(exePath)` (0.2.0).
- **Installed means the folder has the exe.** An empty `Base*` folder is not a client
  (`InstalledClientCatalog`). Battle.net leaves empty folders behind when it reclaims a build.
- **The newest installed exe is the current patch.** Every older installed exe is a previous patch.
- **The replay's version must equal the exe's version exactly** (`ReplayClientRoute.SameBuild`, after
  trimming spaces and turning `,` into `.`). The replay's version is `replay.ReplayVersion` from
  Heroes.StormReplayParser, for example `2.57.0.98285`.
- **Patch line** = the first two numbers (`2.57`, `GameVersionOrder.PatchLine`). Every build in a
  line is its own client: `2.57.0.98285`, `2.57.0.98304` and `2.57.0.98348` are not interchangeable.
- **A running client's build** is the file version of its main module path, not `.build.info`.
  Several `HeroesOfTheStorm_x64` processes can run at once, for example during a handoff (below).

ASA-SERVER on 2026-10-08:

| Folder | Exe | File version | Last written | How it got there |
| --- | --- | --- | --- | --- |
| `Base97650` | yes | 2.55.17.97650 | 2026-09-20 | previous patch line (2.55) |
| `Base97771` | yes | 2.55.17.97771 | 2026-09-20 | previous patch line (2.55) |
| `Base98025` | yes | 2.55.17.98025 | 2026-09-28 | previous patch line (2.55); the SDK's fixed-address build |
| `Base98285` | yes | 2.57.0.98285 | 2026-10-07 14:43 | Blizzard's missing-build download (below) |
| `Base98297` | **no** (empty) | | | not installed; Blizzard no longer serves it |
| `Base98304` | yes | 2.57.0.98304 | 2026-10-07 14:13 | most likely restored from `Data\Clients` (Reclaim, below) |
| `Base98348` | yes | 2.57.0.98348 | 2026-10-05 18:24 | Battle.net update; current patch |

## How a client starts

Each client process writes `Documents\Heroes of the Storm\GameLogs\<yyyy-MM-dd HH.mm.ss> Graphics.txt`. Its header names the
`Executable`, `<Parameters>`, `Parent Executable`, `Grandparent Executable` and `<Version>`. That
file is the best record of how a client was started. The chains seen on ASA-SERVER:

| Start | Process chain | Exe arguments | Signed in |
| --- | --- | --- | --- |
| **Current patch** via `Battle.net.exe --exec="launch Hero"` (`ReplayStartCommand.HeroClient`) | `Battle.net.exe` → `HeroesSwitcher_x64.exe` → `Base<newest>\HeroesOfTheStorm_x64.exe` | `-sso=1 -launch -uid heroes` | yes (SSO), reaches home |
| **Replay file** via `HeroesSwitcher_x64.exe "<file>.StormReplay"` (`ReplayStartCommand.For`, the same as an Explorer double-click) | caller → `HeroesSwitcher_x64.exe` → `Base<newest>\HeroesOfTheStorm_x64.exe` | `"<file>.StormReplay"` | no |
| **Older build** (the handoff) | `Base<newest>` exe → `HeroesSwitcher_x64.exe` → `Base<replay build>\HeroesOfTheStorm_x64.exe` | `-LocaleIdData enUS -LocaleIdAssets enUS` | no |

- **The handoff.** The switcher always starts the newest exe first. That exe reads the replay's build
  and, when it is older, asks the switcher to start that build. Then it exits by itself, within a
  few seconds of the older process appearing (the Agent log shows this for every handoff). The older process
  started 40 to 52 s after the newest one in seven handoffs on 2026-10-08 (Agent `GameProcessManager`
  times). The newest exe is part of startup: a login form, the DOWNLOADING screen or a blank window on
  it is not a failure. Don't close it, and don't open the file again.
- **The current patch signs in only through Battle.net.** A replay opened through the switcher starts
  the newest exe without SSO, so it stops on the email/password form. Open the replay from the
  signed-in home screen instead. An older build never signs in and loads the replay without it.
- **Elevation.** An elevated process that starts HeroesSwitcher can't see the medium-integrity
  Battle.net session, so the client stops at login. HeroesReplay starts every launch with a medium
  token (`MediumIntegrityProcess`, `MediumIntegrityReplayOpener`).
- `ReplayStartCommand.MatchingExe` (the exe with the replay path) exists, but the route never uses
  it. Older builds always go through the switcher.

## HeroesReplay's launch route (`ReplayClientRoute`)

`Classify(replayVersion, installedFileVersions, heldBuilds)`:

| Result | When | Launch |
| --- | --- | --- |
| `Current` | The replay build is the newest installed exe (or nothing could be read) | Battle.net `--exec="launch Hero"`, wait for home, then open the replay |
| `Previous` | An older installed exe matches | HeroesSwitcher with the replay path. The client stays open through its data download |
| `Download` | Not installed, older than the current patch, not held | HeroesSwitcher, exactly like `Previous`. Blizzard fetches the build |
| `NotInstalled` | Newer than the current patch (Battle.net hasn't updated yet), or held after a failed download | Nothing is launched. The replay stays queued |

`Decide(...)` then picks `AuthenticateCurrent`, `OpenFromHome`, `OpenInstalledBuild`,
`OpenMatchingBuild`, `Wait`, `AlreadyInMatch`, `RelaunchCurrent` or `Unavailable`. A matching client
that already shows this replay is `AlreadyInMatch`, because the report preloaded it. Limits:

| Setting or constant | Default | Meaning |
| --- | --- | --- |
| `Spectate:LaunchWaitLimit` | 3 min | A matching client that shows nothing usable gets one recovery: Battle.net again for the current patch, the switcher again for an older build |
| `Spectate:BuildDownloadLimit` | 10 min | How long a `Download` launch waits for the exe to appear |
| `Spectate:BuildDownloadHold` | 4 h | A build that failed to download is held in `Data\client-download-holds.json` |
| `ClientRelaunch.ColdBootLimit` | 4 min | A blank full-size window on the matching exe for this long is broken |
| `ClientRelaunch.GameDataStartupExtension` / cap | 3 min / 12 min | Extra wait while "Preparing game data" shows |
| `ClientInterfacePlan.GameDataDownloadExtension` / cap | 3 min / 30 min | Extra wait while the data download shows |
| `BattleNetAgents:GracePeriod` / `WarnAboveCount` | 2 min / 5 | The Agent reaper (below) |

## Missing build: Blizzard downloads it

When the replay's `Versions\Base<build>` exe is missing and the build is older than the current
patch, Blizzard downloads that client by itself when the switcher hands off to it. Reclaim comes first,
because a copy is faster and doesn't depend on Blizzard. Proven on ASA-SERVER on 2026-10-07, with
replay 65550003 (2.57.0.98285). `Base98285` was empty and there was no copy in `Data\Clients`:

| Time | Event | Source |
| --- | --- | --- |
| 14:42:35 | `HeroesSwitcher_x64.exe "<replay>.StormReplay"` | HeroesReplay AGENTS.md |
| 14:42:37 | The newest exe (`Base98348`) process starts with the replay path. Its graphics log is at 14:42:49 | Agent log, GameLogs |
| 14:43:15 | `Versions\Base98285\HeroesOfTheStorm_x64.exe` (52 MB) is written, along with `HeroesData\data\0800000019.idx` | file times |
| 14:43:17 | The 98285 process starts (parent HeroesSwitcher, grandparent the `Base98348` exe), and the `Base98348` process exits | Agent log, GameLogs |
| 14:44:31 | `heroesreplay check timer` reads the memory match clock at 00:00:34 on that exe | AGENTS.md |

That is about 2 minutes with no clicks, and the download fit inside the normal 40 s handoff. The
Battle.net Agent ran no operation in that window: its operations log has only routine `OP_VERSION`
checks, and its main log only discovers the two processes. So the newest client fetched the build
itself, which fits the client's own `@UI/GameLaunchVersionDownload*` messages. After that, the build
is an installed previous patch.

The same case ran again on 2026-10-08 (HeroesReplay#292 shadow proof). `Base98285`'s exe and its
`Data\Clients` copy were moved aside first. HeroesSwitcher opened replay 65550003 at 19:08:06. The
newest exe showed "Preparing game data", then DOWNLOADING (`CProgressBarDialog`, launch state 6,
then 8). The `Base98285` exe arrived 38 s after the open and was byte-identical to the copy set
aside. The 98285 client showed "Preparing game data" itself, then the map loading screen, and the
clock read at about 19:09:40.

**When Blizzard doesn't serve the build,** the exe never appears, or the newest exe shows "The version
of Heroes of the Storm required to play this game is not available." That happened with 2.57.0.98297
on 2026-10-07. The dialog is a top-level `CStandardDialog` over `ScreenLoginUnified` (#292).
HeroesReplay closes Heroes, defers the replay as `BuildNotInstalled`, and holds the build for 4 h.

**A missing build newer than the current patch** is a Battle.net update that hasn't happened yet.
Don't launch anything, don't click Update, and don't close a client that is already running.

## Current patch: Battle.net updates

The 2.57.0.98348 update on 2026-10-05 (Agent log times are UTC; local time was 18:24):

1. 17:24:51. The Agent logged `Build config out of date, setting playable to false - heroes`. Battle.net then shows Update.
2. 17:24:52 to 17:24:59. `OP_UPDATE for 'heroes'` ran. Battle.net polled the Agent's `GET /update/heroes` twice a second.
3. The update wrote `Versions\Base98348`, `.build.info` (`Version` 2.57.0.98348), `HeroesData\config` and a new `Support64\HeroesSwitcher_x64.exe`, then registered the file associations again.

**Reclaim.** Battle.net deletes the previous iteration's `Base*` exe while it installs the next one,
and leaves the folder empty. HeroesReplay keeps a copy of each exe on the `MinimumGameVersion` patch
line (`2.57.0.98285`) or newer in `Location:RetainedClientDirectory` (`C:\heroesreplay\Data\Clients\Base<build>\HeroesOfTheStorm_x64.exe`,
`ClientBuildArchive.Preserve`: copied to `.part` and checked by size before the rename). Before a
replay launches, `ClientBuildArchive.Restore` copies the build's exe back into `Versions\Base<build>`
when it is missing. `Base98304`'s exe is dated 14:13:01 on 2026-10-07. That is 5 s before the newest
exe started with a 98304 replay (14:13:06; the handoff to 98304 came at 14:13:50), which fits a
restore. A downloaded build is kept the same way from then on. On 2026-10-08, `Data\Clients` had
98285, 98304 and 98348. The ghidra skill imports from there because that path has no parentheses.

Don't click Update or Play to fetch anything. The owner handles patches (HeroesReplay AGENTS.md).

## What shows a download in progress

| Signal | Where | Means |
| --- | --- | --- |
| `Versions\Base<build>` exists but has no exe | install folder | Not installed: reclaimed, never served, or not arrived yet |
| The exe appears in `Versions\Base<build>` | install folder | An older-build download has arrived; the previous-patch rules apply from here |
| "DOWNLOADING — All data files must be fully downloaded to load this version of the game. Calculating... CANCEL" | client window (`ClientScreenText.GameDataDownload` = "must be fully downloaded") | The client is fetching data, seen on the newest exe during a handoff. Memory: a shown top-level `CProgressBarDialog` with no screen bit, launch state 6, then 8 before the exe exits. `ClientScreen` reads `Download` (0.4.1, read live on 98348 on 2026-10-08) |
| "Preparing game data" | A native Win32 dialog from the exe's resources: the `DLGTEMPLATEEX` "Progress", the same in 2.55.17.98025 and 2.57.0.98285, 98304 and 98348. Live it is a visible top-level `#32770` titled "Heroes of the Storm", 404x143 client pixels, with children `Static` 30101 "Preparing game data" (`DownloadProgressMessage`), `msctls_progress32` 30102, `Static` 30103 "Calculating..." and `Button` 2 "Cancel". `GetWindowText` reads the static texts across processes. Seen on 2026-10-08 on 98348 (Battle.net SSO starts and the newest exe of a handoff), 98304 and 98285. HeroesReplay reads it with `EnumWindows`/`GetClassName` (`GameDataProgressWindow`, HeroesReplay#373) | Startup of a client, on a Battle.net start too. Leave it |
| A blank full-size window on the switcher's process | client window | Still starting. Leave it until `ColdBootLimit` |
| `HeroesData\data\data.NNN` growing, `*.idx` rewritten, a new `shmem` time | CASC | Data written. This also happens on an ordinary start, so it is a hint, not proof |
| Battle.net button "Update" / "Updating" | Battle.net window (`LauncherButtonText`) | A current-patch update is pending or running. Never click it |
| `OP_UPDATE for 'heroes'`, `GET /update/heroes`, `setting playable to false` | `%ProgramData%\Battle.net\Agent\Agent.<build>\Logs\Operations-*.log` and `Agent-*.log` | The Agent is updating the current patch |
| `.build.info` `Version` changes, a new `Base*` folder appears, `product.db` time changes | install folder, Agent folder | A current-patch update finished |
| A new `GameLogs\<time> Graphics.txt` | Documents | A client process started; the header names its exe and parent |

## Battle.net and its Agent

| Path | What it is |
| --- | --- |
| `C:\heroesreplay\Battle.net\Battle.net.exe` | The Battle.net stub (2.53.4.17896), installed with `winget --location C:\heroesreplay\Battle.net` (`Location:BattlenetPath`). It runs as several `Battle.net.exe` processes (CEF children) |
| `C:\heroesreplay\Battle.net\Battle.net.<build>\` | The real app. Five builds were kept on 2026-10-08 (17821 to 17896) |
| `C:\heroesreplay\Battle.net\.battle.net\` | Battle.net's own CASC (`data`, `indices`, `config`, ...) |
| `%ProgramData%\Battle.net\Agent\Agent.<build>\Agent.exe` | The Agent (2.41.1.9824). `Agent.exe` in the folder above is a stub |
| `%ProgramData%\Battle.net\Agent\Agent.<build>\Logs\` | `Agent-*.log` (requests, `GameProcessManager` process discovery), `Operations-*.log` (`OP_VERSION`, `OP_UPDATE` per product uid), `AgentErrors-*.log`. Times are UTC |
| `%ProgramData%\Battle.net\Agent\product.db` | Hidden protobuf with one entry per product: uid `agent`, `battle.net`, `heroes`; product code `agent`, `bna`, `hero`; install path, region, locale, version and build config key. The `heroes` key equals `.build.info`'s `Build Key` |
| `%ProgramData%\Battle.net\Agent\aggregate.json` | The installed games shown in the Battle.net UI: `product_id` `hero`, `launch_uri` `battlenet://game/hero`, last played |
| `%ProgramData%\Battle.net\Agent\Agent.dat` | The port of the Agent's HTTP API on `127.0.0.1`, owned by the running `Agent.exe`. Don't call it |
| `%ProgramData%\Battle.net\Agent\Logs\Switcher-*.log` | HeroesSwitcher's own log (its `--session=` argument) |
| `%ProgramData%\Battle.net\Setup\hero_2\`, `bna_2\` | Installer logs |
| `%APPDATA%\Battle.net\`, `%LOCALAPPDATA%\Battle.net\` | Battle.net user settings and account data. **Don't read** |

- The Agent finds game processes by the path regex `Versions/Base\d+/(HeroesOfTheStorm|HeroesOfTheStorm_x64)\.exe`,
  and logs `Disconnecting Client ... due to inactivity` after one exits.
- **Leftover agents (#251).** Every `--exec="launch Hero"` leaves a new `Agent.exe` that never exits.
  HeroesReplay's `BattleNetAgentReaper` (`BattleNetAgentSelection`) runs when spectate starts and
  before each Battle.net launch. It keeps the oldest settled agent whose parent is a running
  `Battle.net.exe` (or else the oldest agent), and it terminates every other `Agent.exe` under
  `%ProgramData%\Battle.net\Agent` that is older than 2 min, plus the `conhost.exe` each one started.
  It never touches anything else.
- Uninstall: `HKLM\SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall\Heroes of the Storm` →
  `Blizzard Uninstaller.exe --uid=heroes`.

## Documents\Heroes of the Storm

| Path | What it is |
| --- | --- |
| `Variables.txt` (root) | Read by a client that isn't signed in: an older build through the switcher, or the newest exe on the login form |
| `Accounts\<account id>\Variables.txt` | Read by a signed-in client, and overrides the root file after login. It is created on the first Battle.net sign-in |
| `Accounts\<account id>\<toon handle>\Replays\` | The client's own saved replays |
| `Interfaces\AhliObs 0.75.StormInterface` | The observer interface (`heroesreplay client configure` copies it) |
| `GameLogs\` | One `<time> Graphics.txt` per client start, and `SystemInfo.txt` for the latest one |

The keys HeroesReplay writes into both `Variables.txt` files (`ClientSettings`, `StormVariablesEditor`) are only written
while Heroes is not running:

| Key | Value | Note |
| --- | --- | --- |
| `displaymode` | `0` | windowed (1 fullscreen, 2 borderless) |
| `width`, `height`, `windowwidth`, `windowheight` | `1920`, `1080` | The client may write a few pixels more into `windowwidth`; within 8 counts as equal |
| `windowstate` | `1` | normal, not maximized |
| `observerinterface`, `replayinterface` | `AhliObs 0.75.StormInterface` | The **file name**, with the extension. The dropdown label `AhliObs 0.75` loads the default HUD |
| `soundglobal` | `true` | Play in Background |

**New client.** When a newer `Base*` exe is installed, write the preset and copy the interface again
before that client starts, even if the files already name AhliObs (`ClientInterfaceSeal`,
`Data\client-interface-build.txt`, which held `2.57.0.98348` on 2026-10-08).

## Firewall

Windows asks to allow networks the first time each `Versions\Base*\HeroesOfTheStorm_x64.exe` listens.
Any enabled inbound Allow rule for that exe path counts, including Windows' own `Query User{...}`
rules. Each new `Base*` folder needs its own rule, and that includes a build Blizzard downloaded during a launch.
`heroesreplay client firewall`, run once elevated, adds `HeroesReplay inbound Base<build>` only where
no rule covers the exe. Spectate itself runs unelevated and only warns. Never click Allow.

## Dialogs on the client

| Dialog | Text (OCR today, `ClientScreenText`) | Memory lead (#292) | HeroesReplay does |
| --- | --- | --- | --- |
| Version mismatch | "version mismatch" | `@UI/GameLaunchDataBuildNumMismatch`, launch result code 15 | Close, defer (`VersionMismatch`); one launcher restart, then an operator (`LauncherRecoveryPlan`) |
| Version not available | "version of Heroes ... not available" | Top-level `CStandardDialog`; launch result 23 `GameLaunchUnsupportedNoData`, read live on 2026-10-08 (`ClientScreenKind.Dialog`, 0.4.1) | Close, defer `BuildNotInstalled`, hold the build |
| Region unavailable | "region" + "unavailable" | Not reproduced yet | Close, defer (`RegionUnavailable`) |
| Login form | "password" + "email" or "log in" | `ScreenLoginUnified` in the mask (`0x60C1`). The AUTHENTICATION "Connecting..." panel is a visible `CLoginDialog` | Current patch: close and ask Battle.net once more. On the handoff exe: ignore |
| Battle.net disconnected | `BattleNetDisconnect` phrases | | Outage handling |

The game-launch result code is the `@UI/GameLaunch*` table index. The client's game-launch manager
singleton keeps the last one at `+0x08` (98348 global RVA `0x3771BA8`). HeroesClientSDK#8 finds it per
build (`GameLaunchPattern`). The full list of codes is in the heroes-client-re skill.

## For SDK code

- Find clients with `Process.GetProcessesByName("HeroesOfTheStorm_x64")` and expect more than one.
  Identify each by its main module path (`Versions\Base<build>`) and that file's version. Use one
  reader per process. The version is optional everywhere (heroes-client-re, "Multi-client rule").
- A client started through the switcher can read as `Login` or `NoScreen` for a while during the
  handoff and the download. That is startup, not an error.
- Re-check the facts above on the machine before relying on a number. Builds, times and versions
  change with every patch. Update this skill when they do.
