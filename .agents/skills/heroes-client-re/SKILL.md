---
name: heroes-client-re
description: >
  Workflow for adding a new read-only memory read of the Heroes of the Storm client to
  HeroesClientSDK (screen ids, login, game-data download, version and region dialogs, end screen:
  HeroesReplay#292). Covers the rules (read-only, nothing committed), taking a memory image of a
  running client (owner-approved, read-only, kept under C:\heroesreplay\re), finding candidate
  globals and objects with Ghidra (strings, RTTI vftables, the screen-state global [[G]+0x218], the
  UI frame tree and the IsA vtable slot that names frame classes, the game-launch result code),
  confirming them live, writing a pattern that survives builds and checking it on the current and a
  previous patch, the profile entry and snapshot tests, and the multi-client version rule. Use when
  reverse engineering the client or adding an SDK read, or /heroes-client-re. Ghidra itself is the
  ghidra skill; how clients are installed, started and downloaded is heroes-client-launch.
---

# Adding a client memory read

## Rules (read first)

- **Read-only, always.** Open the process with `PROCESS_QUERY_INFORMATION | PROCESS_VM_READ` only, as the SDK readers and `scripts/` do. No `WriteProcessMemory`, no injected DLL or thread, no debugger attached (not x64dbg, not Cheat Engine's debugger, not `DebugActiveProcess`), no suspended threads. `MiniDumpWriteDump` suspends the process, so don't use it on a client the spectator is using. Don't click, focus, move or close that client's window.
- **Never start the game for RE.** Work with a client that the spectator or the owner already started. ASA-SERVER's live proofs own the client (`C:\heroesreplay\asa-live.lock`). A read-only image or read doesn't disturb a proof, but anything that drives the client does. Never touch the stream PC (DESKTOP-8SJEK72). How clients start, and which build is which, is in the heroes-client-launch skill.
- **Memory images of a running client are approved.** The owner approved read-only memory dumps of a running client (HeroesClientSDK#2, 2026-10-08). That covers `scripts/Save-ModuleImage.ps1`, `scripts/Read-ClientMemory.ps1`, the probe, and whole-tree walks of the UI frames: `ReadProcessMemory` and `VirtualQueryEx` only, on a client that is already running. Keep the output under `C:\heroesreplay\re\` (`dumps\<version>\`, tree captures) or the session scratchpad, and never commit it. The approval does not cover anything that writes, attaches or suspends (above).
- **Commit nothing from Blizzard.** That covers exes, DLLs, memory images, Ghidra projects, `.jsonl` snapshots and decompiled listings. They live under `C:\heroesreplay\re\`, and the SDK `.gitignore` ignores `re/`, `*.gpr`, `*.rep` and `*.dmp`. A test keeps only the few bytes it needs, as hex in the test source, with the build and date in a comment.
- **Don't decrypt the exe offline.** Its code is encrypted on disk (below). The running client's own decrypted image is what we read.

## What is already known

- `HeroesOfTheStorm_x64.exe` keeps `.text` encrypted on disk (entropy 8.0 in 98025, 98304 and 98348). The SDK scans the live process's code, and a fresh client is retried every 10 s while it decrypts. **Every code question needs a memory image** (`scripts/Save-ModuleImage.ps1`). The exe files in `Versions\Base*` and `C:\heroesreplay\Data\Clients\Base*` are useful only for strings, RTTI names and headers.
- **Screen state** (`LoadingScreenMemory`, `LoadingScreenPattern`): global G, found per build where both loads in `mov rcx,[G]; test rcx,rcx; jz; xor edx,edx; call; test al,al; jz; mov rcx,[G]; call` agree. On 2.57.0.98304 it is RVA `0x3771830` with 34 sites. `[[G]+0x218]` is the current screen object: null in a match, otherwise bit 0 of byte 72 is 1 on a loading screen (boot splash or map) and 0 on a menu.
- **Menu screens** (`ClientScreenMemory`, HeroesReplay#292): `[G]` is the menu root. `root+0x1D4` is a u64 mask of the screens shown (one bit per screen index; the client `or`s the bit when it shows a screen and clears it when it hides one) and `root+0x1F0 + 8*i` the screen frames. `GlueScreenPattern` (`49 8B 81 [mask] 8B 51 08 48 0F A3 D0 73 ?? 49 8B 8C D1 [frames]`) finds both offsets: one site in 98348 (RVA `0xDD6AD4`) and 98304 (`0xDE3624`). The index of each screen comes from the client's template table of `{char* "ScreenX/ScreenX", len}` entries (98348 RVA `0x26F6C00`, 98304 `0x26FDC00`; Loading 5, LoginUnified 6, Home 8, Score 15). Live on 98348: home `0x6181`, login form `0x60C1`, boot splash `0x20`; in a match the ScreenLoading frame is null. The MVP/awards screen after a replay is in-game UI (`CEndOfGameAwardsPanel`, frame type registered by 98304 fn `0x7A0270`), not a menu screen. The `CScreen*` objects carry no RTTI locator, so the vftable route does not name them; the mask does.
- **UI frame tree and class names** (HeroesReplay#292): every frame keeps its parent at `+0x50`, its visible bit at bit 0 of `+0x48` (`CFrame::SetVisible`), and its children as an intrusive list (first child node `+0x40`, child node `+0x18`, next sibling `+0x20`, tagged end). The top is `CRoot` = the menu root's grandparent; `CLayer` children hold `CGlueUI` and `CGameUI` (98348 global RVA `0x338B2E8` points at `CGameUI`). **Name any frame's class from its vtable:** slot `+0x240` is `IsA(type)`, whose first `call` is the class's StaticType, whose first `lea rcx` is the class name (works on 98348 live and the 98304 image: `CGlueUI`, `CRoot`, `CScreenLoading`, `CGameMenuDialog`). Do **not** pair names with factories from the frame-type registration code (`lea rcx,[name]; lea rax,[factory]`): the name in that pair belongs to the next descriptor, so it maps `EndOfGameAwardsPanel` to `CDifficultyPulldown`. Live (98348, 2026-10-08): the MVP screen is `CEndOfGameAwardsPanel` (vtable RVA `0x2688628`) under `CFrame`/`CFrame`/`CGameUI`, flags `0x7A` in the match and `0x7B` on the MVP screen. The Battle.net AUTHENTICATION "Connecting..." panel is a visible top-level `CLoginDialog` (`0x2767168`) over `ScreenLoginUnified`; the "version ... is not available" message is a visible top-level `CStandardDialog` (`0x265C1D8`) over `ScreenLoginUnified`. The game-data "DOWNLOADING ... must be fully downloaded" screen shows no menu screen in the mask and no `CDownloadPanel`; its frame is not identified yet. Method that found them: walk the whole tree read-only, print each frame's class name and effective visibility (own bit AND every parent's), and diff two states (mid-match vs MVP screen, connecting vs dialog); a `FrameTree`/`FrameClass` loop in a scratch console does it in under a second.
- **Match clock** (`MatchClockPattern`): `test al,al; jz; movd xmm0,[tick]; cvtdq2ps; mulss xmm0,[1/4096]`. Every site must name the same tick and speed globals.
- **RTTI in 2.57.0.98348** (file, data-only analysis, `C:\heroesreplay\re\ghidra\98348\out\`):
  - Screen classes: `CGlueScreen`, `CScreenHome`, `CScreenLoginUnified`, `CScreenLoading`, `CScreenScore`, `CScreenReplay`, `CScreenPlay`, plus about 30 more `CScreen*`. Also `CEnumFieldT<EScreen>`, which means a screen enum exists.
  - Dialogs and panels: `CDialog`, `CStandardDialog`, `CConfirmationDialog`, `CTimedMessageDialog`, `CBattlenetErrorDialog`, `CLoginDialog`, `CWaitingForServerDialog`, `CDownloadPanel`.
  - Other: `CGlueUI`, `BGS::AccountHandler`.
  - Strings include `@UI/GameLaunchDataBuildNumMismatch`, `DownloadProgressMessage=Preparing game data` and the `ScreenHome/ScreenHome`, `ScreenScore/ScreenScore` and `ScreenLoginUnified/ScreenLoginUnified` layout names.
  - The `CScreen*` type descriptors have no complete object locator in the plain sections of the file, so their vftables come from an image.

## Findings from the live and image work (HeroesReplay#292, 2026-10-08)

All of this came from read-only images and reads of 2.57.0.98348 (current patch) and 2.57.0.98304 (previous patch). The images are in `C:\heroesreplay\re\dumps\<version>\`.

- **The file is encrypted and the loaded image is not.** In the file, `.text` has entropy 8.0. In the images Save-ModuleImage took from running clients, it has entropy 6.58 on both builds, with `textLooksPlain: true`, no unreadable ranges, `SizeOfImage` about 73 MB (`0x45AE000` on 98348), and a read of 48 ms (98348) or 78 ms (98304). An image taken right after start can still be partly encrypted; the sidecar says so.
- **Screen-state global on both builds.** `LoadingScreenPattern` ranks one global first with 34 sites: RVA `0x3770830` on 98348 and `0x3771830` on 98304.
- **Frame tree size.** The layout is in the UI frame tree bullet above. A match has 86,000 to 115,000 frames, and a full walk takes 0.25 to 0.6 s, so walk rarely and cache what you find. `FrameTree` checks that each child's parent points back, so a moved layout reads as not found.
- **The whole class map from the IsA slot.** The frame classes carry no RTTI locator, so `FindVftables` can't name them; the IsA slot `+0x240` (above) can. Its shape is `push rbx; sub rsp,20h; mov rbx,rdx; call StaticType; mov rcx,[rbx]; cmp [rax],rcx; je; call <base>::StaticType; ...`. Each `StaticType` call in turn names the class and then each base, so following the chain gives the class and all its bases. Both images give 827 vtables with the same class set. Examples on 98348 (vtable RVAs):

  | Class | 98348 vtable | Bases |
  | --- | --- | --- |
  | `CLoginDialog` | `0x2767168` | |
  | `CStandardDialog` | `0x265C1D8` | `CDialog` < `CLayer` < `CControl` < `CFrame` |
  | `CScreenLoading` | `0x26FD828` | |
  | `CDownloadPanel` | `0x27409B0` | |
  | `CEndOfGameAwardsPanel` | `0x2688628` | |

  The registration-pairing trap (above) also produced an earlier "`DownloadPanel` `0x2740510`", which is really `CDisconnectedDialog`.
- **Boot splash vs map loading** (found by diffing tree captures). The `CScreenLoading` frame has two children that show only on a map loading screen: `CLoadingBar`, and `CCustomLoadingPanel`, which holds `CHeroLoadingPanel` with 10 `CHeroLoadingPanelPlayerFrame`. On the boot splash they are hidden (flags `7A` and `72`); on map loading they are shown (`7B`). On the previous-patch path, where a replay loads straight from the file, map loading sets **no** bit in the shown-screens mask (0.2.0 reads `NoScreen`), but the `ScreenLoading` frame (`root+0x1F0+8*5`) is the one shown.
- **Which dialog: the game-launch result code.** The 98348 function at RVA `0xCFB000` shows a game-launch error. It handles `{2, code}`, checks `1 <= code <= 0x18`, stores the code at `+0x08` of its object, and looks the text up in the `@UI/GameLaunch*` table (RVA `0x26FC600`, 16-byte `{char*, len}` entries). A new launch clears `+0x08`. The object is a singleton of `0x36958` bytes at global RVA `0x3771BA8` (98348) or `0x37729A8` (98304), and its launch state is at `+0x20`. The codes are 1 GenericLaunchFailure, 2 ReplayOpenFailure, 3 SaveOpenFailure, 4 MapOpenFailure, 5 TrialDisallowedMap, 6 MapPrefetchFailure, 7 InvalidFiles, 8 TooManyDependencies, 9 GameBusy, 10 BaseBuildMissing, 11 PTRLauncherMissing, 12 VersionDownloadMessage, 13 VersionDownloadFailure, 14 VersionLaunchError, 15 DataBuildNumMismatch, 16 ModDataMismatch, 17 MapDataMismatch, 18 NotLicensed, 19 LicenseNotValidated, 20 UnsupportedInCN, 21 UnsupportedInRC, 22 UnsupportedInTrial, 23 UnsupportedNoData and 24 UnsupportedTooOld. The key doesn't depend on the client language. These patterns hit once on each image:
  - singleton creator: `48 83 EC 28 48 83 3D [G] 00 75 ?? B9 [size] E8 ?? ?? ?? ?? 48 85 C0 74 ?? 48 8B C8 E8 ?? ?? ?? ?? 48 89 05 [G]` (both `[G]` agree);
  - error store: `83 F8 02 0F 85 ?? ?? ?? ?? 48 63 7A 04 8D 47 FF 83 F8 ?? 77 ?? 89 79 [err]` (98348 `0xCFB024`, 98304 `0xD07A04`).

  Which code the "version ... is not available" dialog sets (10, 13 or 14) still needs a live read with the 2.57.0.98297 replay in `C:\heroesreplay\Data\Proof\missing2`.
- **Login, Connecting, dialogs and the download screen: the flags.** `ScreenLoginUnified` (mask `0x60C1`) covers both the email/password form and the Connecting panel. On Connecting, `CLoginDialog` is shown (`73`). On the "version ... not available" message, `CLoginDialog` is hidden (`72`) and `CStandardDialog` is shown (`73`). No capture of the email/password form exists yet. The DOWNLOADING capture (on the newest exe during a switcher handoff) has only 71 frames under `CRoot`, all of them hidden except `CGlueUI`. The next step is to find `CDownloadPanel` instances by vtable in private memory. "Preparing game data" is a native Win32 resource string (`DownloadProgressMessage` in `.rsrc`), not UI.
- **Signed in.** `BattlenetAPI_IsAuthenticated` reads `ctx->[+8]->[+0xDD8]->[+0x10]->byte[+0x2D]`. The global context isn't resolved yet, so `SignedIn` comes from the screens.

## Tools

| Tool | Use |
| --- | --- |
| `scripts/Save-ModuleImage.ps1` | Read-only copy of a running client's main module as a PE file Ghidra imports. Output: `C:\heroesreplay\re\dumps\<version>\HeroesOfTheStorm_x64-<version>-image.dmp`, plus a `.json` with the base, unreadable ranges and per-section entropy. `textLooksPlain: false` means the client hadn't finished decrypting yet; take it again at the home screen with `-Force`. |
| `scripts/Read-ClientMemory.ps1` | Read-only live reader. It follows a pointer chain from an RVA (`-Rva 0x3771830 -Offsets 0x218,0x48 -Size 1` is the screen flags byte on 98304), annotates qwords that point into the module with their RVA (a vftable names the class), watches for changes (`-Watch 1 -Count 120`), and appends labelled snapshots (`-Label home -OutFile ...jsonl`) for diffs and test data. |
| Ghidra skill `scripts/` | FindBytePattern, FindRipRelativeLoads, XrefsToAddress, MakeSignature, ExportFunction, FindStringRefs, FindSymbols, FindVftables (see `.agents/skills/ghidra/SKILL.md`). |
| `heroesreplay check timer` (HeroesReplay) | The SDK match clock against the running client. |
| `tools/HeroesClientSDK.Probe` | Read-only: every running client's build, `ClientScreenMemory` screen and shown screens, signed-in state, `LoadingScreenMemory` and the match clock (`--watch 250` prints a line on each change). The `ResolveFile(exePath)` plan of HeroesClientSDK#1 cannot work on encrypted exe files, so the probe reads live clients. |

Both PowerShell scripts were first tested on 2026-10-08 against a self-started native process (`decompile.exe` from Ghidra). The image's `.text` was byte-identical to the file's, and Ghidra gave the same functions and RVAs for both. The same day, Save-ModuleImage took images of running 2.57.0.98348 and 2.57.0.98304 clients (findings above).

## The loop

1. **Take an image of the build in front of you.** When a client is up (a proof, the spectator, or the owner), run `pwsh -NoProfile -File scripts\Save-ModuleImage.ps1`, adding `-ProcessId` if there are two. Keep one image per build in `C:\heroesreplay\re\dumps`, and save one every time a previous-patch build runs. Those images are the only offline way to check a pattern on more than one build.
2. **Import the image** into `C:\heroesreplay\re\ghidra\<build>` with `preset:large-x64` and a full analysis in the background (ghidra skill). For a quick look first, import it `-noanalysis` (seconds). FindBytePattern, FindRipRelativeLoads, FindVftables, MakeSignature and ExportFunction work on that.
3. **Prove the setup on a known fact** before hunting. On a 98304 image, FindBytePattern with the LoadingScreenPattern bytes, `rip:3:7 rip:26:30 same block:.text`, must rank RVA `0x3771830` first with 34 matches (on 98348, `0x3770830` with 34). On another build it must rank one global far ahead of the rest. If it doesn't, the image is still encrypted or the code moved.
4. **Find candidates:**
   - *Screen object classes.* Read `[[G]+0x218]` live and note its first qword (the vftable RVA, annotated by Read-ClientMemory) on the home screen, the login form, a loading screen and the score screen. Name each RVA from its IsA slot (`+0x240`, above). `FindVftables` works only for classes with RTTI, and the UI frame classes have none. Then the screen is a class check, `vftable == CScreenHome's vftable`, resolved per build by class name. No code pattern is needed, which is the most build-proof option.
   - *Fields.* Snapshot an object in two states (`-Label home` vs `-Label login`, `-Size 0x400`) and diff the hex lines. Look for a field that changes with the state (an enum, a flag, a pointer that becomes non-null).
   - *Code.* Use `FindStringRefs.java <text> scan` on the image for UI and error ids, `FindRipRelativeLoads.java rva:<type descriptor>` for `dynamic_cast`/`typeid` sites that test a class, `XrefsToAddress` and `FindRipRelativeLoads rva:<G> context:6` to see how the client reads a global, and `ExportFunction` to decompile the function that switches on a screen or dialog id.
5. **Confirm live** with Read-ClientMemory in each state the read must tell apart. Do it at least twice per state, after a client restart, because heap pointers change and the chain must still hold.
6. **Make it survive builds.**
   - Prefer, in this order: (a) vftables found by class name at run time (RTTI where the class has it, the IsA slot for UI frames); (b) an instruction-shape pattern whose sites agree on one global (LoadingScreenPattern style; MakeSignature prints "matches naming the same global N of M"); (c) a unique signature from MakeSignature, with the global's displacement offset and instruction end from its "rip disp at +d, instruction end at +e" line.
   - Wildcard every rip displacement and branch target. Wildcard struct offsets in a pattern (`wildoffsets`), and keep them as profile data instead, because offsets like `0x218` and `72` can change with a patch.
   - Check the pattern with FindBytePattern on the current-patch image **and** at least one previous-patch image (the newest `Versions\Base*` build is the current patch). It must give the same answer, or the same shape with a moved global, on both. Record the build, site count and RVA in the PR.
7. **Add it to the SDK.**
   - Put the pattern in an `internal static` pattern class next to `LoadingScreenPattern`, and the read in the reader (or the profile registry of HeroesClientSDK#1 once it lands): the offsets, the bit, and the vftable names to match.
   - Tests follow `LoadingScreenMemoryTests`: synthetic code bytes for the pattern (`Find`, `TryAgree`), and a fake `read` function (`Func<long, byte[], bool>` in 0.1.0, `IProcessMemory` after #1) that serves a recorded snapshot. That snapshot is the few bytes Read-ClientMemory captured per state, as hex, with `// 2.57.0.98348, 2026-10-xx: home` comments. A state that can't be reproduced live (the version-mismatch or region dialog) may be proven by such a snapshot test (#292 "Done when").
   - Run `dotnet csharpier check .`, then build and test as in the README.
8. **HeroesReplay.** Run it in shadow mode next to OCR, log disagreements at Warning, and switch over once a long proof agrees (#292 Approach steps 3 and 4).

## Multi-client rule (README "Any client build, any number of clients")

- A client version is only ever an optional argument (`HeroesClientVersion? clientVersion = null`, or an options type with a nullable `ClientVersion`). Null means detect it from the process or take the generic path. Never require it.
- An unknown build, a pattern that doesn't match, or sites that disagree read as not ok with a reason (`unsupported-build`, `pattern-disagreed`, `no-state`). Never throw.
- A version passed in that differs from the running exe is reported as `version-mismatch`, and reading continues.
- Per-build data (fixed RVAs, offsets) is used only for that exact build and only after the pattern or RTTI path. It is a fallback, never the only way.
- Readers keep no static client state. One reader per client process, so several clients and builds can be read at once. A new pid or start time starts discovery over.

## #292 states and where to look

Status on 2026-10-08. The rows are in the order #292 plans to remove OCR. Details are in "Findings" above.

| State | Where it stands | Next lead |
| --- | --- | --- |
| Home screen | Read: `ScreenHome` in the shown-screens mask (`0x6181`), `ClientScreenMemory` 0.2.0 | A shadow run that reaches a real home screen. Must also hold on a previous-patch client that loads without a home screen |
| Login form | Read as `Login` (`ScreenLoginUnified`, `0x60C1`), but that also covers the AUTHENTICATION "Connecting..." panel (a visible `CLoginDialog`) | One tree capture of the email/password form, to see whether it keeps `CLoginDialog` hidden. Signed in: resolve the `BattlenetAPI_IsAuthenticated` context, or `BGS::AccountHandler` |
| Map loading (no "menu seen" fallback) | Separated in captures: the `CLoadingBar` and `CCustomLoadingPanel` children of the `ScreenLoading` frame show only on map loading | A live check of map loading opened from home on 98348, then a read in the SDK |
| Game data download / Preparing game data | Not identified. DOWNLOADING has mask 0 and no `CDownloadPanel` in the tree; "Preparing game data" is a native window | Find `CDownloadPanel` instances by vtable in private memory, or detect the native window by its text. How the download looks from outside: heroes-client-launch |
| Version mismatch / not available / region dialogs | "Not available" is a visible top-level `CStandardDialog`. The game-launch result code at singleton `+0x08` says which message | Read that code live with the 98297 replay. Region is not reproduced yet. A recorded snapshot test is enough where a state can't be reproduced |
| End screen (MVP / awards) | `CEndOfGameAwardsPanel` (98348 vtable `0x2688628`) turns visible: `0x7A` in the match, `0x7B` on the MVP screen (`ClientScreenKind.Awards`, merged in HeroesClientSDK#6, not released yet) | A live probe run to an MVP screen on both patches, then 0.3.0. `ScreenScore` never shows after a replay. The core-death time from the replay stays the main signal |
