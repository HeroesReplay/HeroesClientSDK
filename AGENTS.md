# AGENTS.md

This is the contract for coding agents in this repository. Code and this file win over chat history.

## Product

HeroesClientSDK is read-only access to a running Heroes of the Storm client's memory on Windows:
the match clock (`MatchClock`), the loading screen (`LoadingScreen`) and the menu screens
(`ClientScreen`), on one read-only attachment per process (`HeroesClientProcess`). It is a
`net10.0` library published as a NuGet package. Its main consumer is
[HeroesReplay](https://github.com/HeroesReplay/HeroesReplay), which pins each release by version and
SHA-256. The usage, the install, the build and the release steps are in [README.md](README.md).

| Path | What |
| --- | --- |
| `src/HeroesClientSDK` | The library (the package) |
| `tests/HeroesClientSDK.Tests` | xUnit tests: recorded bytes and fake reads, never a live client |
| `tools/HeroesClientSDK.Probe` | `heroes-client-probe`: prints what the SDK reads from every running client, read-only; `--image <file>` checks every reader's discovery on a saved module image, offline |
| `.agents/skills` | The agent skills below |

## Rules

1. **Read-only.** A reader opens the client with `PROCESS_QUERY_INFORMATION | PROCESS_VM_READ`
   only. No writes, no injected code or threads, no debugger, no suspended threads, no window input.
   Nothing reads the screen.
2. **Several clients at once.** Readers keep no static or global client state. One reader per client
   process. A new pid or process start time starts discovery over. Two clients of different builds
   (the switcher handoff, or a current and a previous patch) must read correctly side by side.
3. **The version is optional.** No API requires a client version. Where one is accepted, it is
   optional (`HeroesClientVersion? clientVersion = null` on every read); null means detect it from
   the process or use the generic path. Per-build data (`BuildProfileRegistry`) follows the running
   exe; a passed version picks a profile only when the exe has no version. An unknown build, or a
   pattern that doesn't match, reads as not ok with a reason (`unsupported-build`,
   `pattern-disagreed`, ...), never an exception. A different version passed in is reported as a
   mismatch (`VersionMismatch`), not thrown. Per-build data is a fallback for that exact build,
   after the pattern scan.
4. **No binaries committed.** Nothing from Blizzard goes into git: no exes, DLLs, memory images,
   CASC files, Ghidra projects, tree captures, `.jsonl` snapshots or decompiled listings. They live
   under `C:\heroesreplay\re\` (the `.gitignore` also ignores `re/`, `*.gpr`, `*.rep/`, `*.dmp`).
   A test keeps only the few bytes it needs, as hex in the test source, with the build and date in a
   comment. No `.nupkg` is committed either.
5. **Launch a client only for live verification or RE, under the lock.** Launching clients and
   reading them with the SDK is the owner's acceptance bar for a new read, so an SDK task may start
   a Heroes client, but only when all of these hold:
   - it is on ASA-SERVER (the dev box), never the stream PC (DESKTOP-8SJEK72);
   - it holds `C:\heroesreplay\asa-live.lock`, taken only while `C:\heroesreplay\asa-release-pending`
     does not exist (a release or e2e agent has priority), and it releases the lock after
     `heroesreplay services stop` shows no Heroes or heroesreplay process left;
   - it starts the client the way HeroesReplay's AGENTS.md ("Current patch and previous patch") does:
     Battle.net SSO (`--exec="launch Hero"`) for the current patch, HeroesSwitcher with a
     `.StormReplay` for a previous patch (or without SSO to show the login form);
   - it never types credentials and never clicks Update, Play or Allow;
   - it reads the client read-only (rule 1).

   Otherwise, work with a client that HeroesReplay or the owner already started. Read-only memory
   images of a running client are approved (skill `heroes-client-re`). Don't decrypt the exe file
   offline.
6. **No credentials or account identifiers** in code, tests, docs or commits (skill
   `heroes-client-launch`).

## Before editing

1. Load the matching skill under `.agents/skills/` (the tables below).
2. Format with CSharpier. Don't hand-format.

```powershell
dotnet tool restore
dotnet csharpier format .
dotnet csharpier check .
dotnet build HeroesClientSDK.slnx -c Release
dotnet test HeroesClientSDK.slnx -c Release
```

Changes reach `main` through pull requests. CI (`.github/workflows/ci.yml`) runs the CSharpier
check, the Release build, the tests and the pack on every pull request. A red check blocks the merge.
A release is a `vX.Y.Z` tag on `main` (`publish.yml`, README "Releases"). HeroesReplay bumps its pin
only after a live proof, because the match clock is in this package.

`AgentDocsTests` keeps this file and `.agents/skills` in step. It fails when a skill folder is
neither first-party (the table below) nor pinned in `.agents/skills/vendored.json`, or when a
`SKILL.md` has no matching `name` and a `description`.

## Repo skills (this tree)

| Skill | Use when |
| --- | --- |
| `.agents/skills/heroes-client-re` | Adding a memory read: the rules, memory images, the frame tree and class names, patterns that survive builds, tests, the multi-client rule, and where each HeroesReplay#292 state stands |
| `.agents/skills/ghidra` | Ghidra 12.1 headless on ASA-SERVER: projects per build, analysis presets, the GhidraScripts, timings and pitfalls |
| `.agents/skills/heroes-client-launch` | How clients are installed, versioned, started and downloaded: `Versions\Base*`, HeroesSwitcher, Battle.net and its Agent, the current and previous patch, missing builds, `Variables.txt`, and the dialogs |
| `.agents/skills/dotnet-10-csharpier` | This repo's .NET 10 setup: the slnx, CSharpier, analyzers, xUnit, MinVer, the package and CI |

## Vendored .NET skills

These are installed under `.agents/skills/<name>/` from [dotnet/skills](https://github.com/dotnet/skills),
commit `e115891bd2ac` (MIT, `.agents/skills/dotnet-skills.LICENSE.txt`), and are unedited. They are
the subset of HeroesReplay's vendored set that fits this repo. `.agents/skills/vendored.json` pins the
commit, the license file and the list. Load the skill whose name matches the task. Each `SKILL.md`
says when to use it.

| Plugin | Skills |
| --- | --- |
| `dotnet` | `csharp-refactoring`, `setup-local-sdk` |
| `dotnet-msbuild` | `directory-build-organization`, `property-patterns`, `msbuild-antipatterns`, `incremental-build` |
| `dotnet-nuget` | `convert-to-cpm` (the repo already uses central package management; the skill also covers its rules) |
| `dotnet-test` | `run-tests`, `filter-syntax`, `platform-detection`, `test-anti-patterns`, `assertion-quality`, `coverage-analysis`, `test-smell-detection` |
| `dotnet-diag` | `analyzing-dotnet-performance`, `microbenchmarking` (reader hot paths: pattern scans, frame-tree walks) |

The tests are xUnit v2 on VSTest, so `run-tests` and `filter-syntax` apply in their VSTest form
(`--filter "Category=Unit"`). Some vendored skills point to skills that aren't vendored here
(`writing-mstest-tests`, `code-testing-agent`, `crap-score`, `mtp-hot-reload`, `test-gap-analysis`,
...). Ignore those pointers, or take the skill from HeroesReplay if a task really needs it.

Not vendored: `dump-collect`, because it collects dumps of .NET processes, and pointed at a Heroes
client it means a suspending MiniDump. Also left out are `dotnet-trace-collect`,
`clr-activation-debugging` (.NET Framework), the upgrade and AOT skills, and the other MSBuild and
test skills. To add one, copy its folder unedited from the same commit, add it to `vendored.json`
and to the table above.
