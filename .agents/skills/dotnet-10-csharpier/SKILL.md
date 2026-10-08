---
name: dotnet-10-csharpier
description: >
  .NET 10, HeroesClientSDK.slnx, CSharpier 1.3, the analyzer rules, xUnit v2 tests, MinVer
  versions and the NuGet package of HeroesClientSDK. Use when changing a csproj,
  Directory.Build.props, Directory.Packages.props, formatting, tests, CI or the package, or
  /dotnet-10-csharpier. Generic .NET work: the vendored dotnet/skills listed in AGENTS.md.
---

# .NET 10 + CSharpier (HeroesClientSDK)

## Layout

| Project | Target | Notes |
| --- | --- | --- |
| `src/HeroesClientSDK` | `net10.0` | The package. `IsPackable`. Packs `README.md` (the package readme) and `LICENSE`. `InternalsVisibleTo` the tests |
| `tests/HeroesClientSDK.Tests` | `net10.0` | xUnit 2.9.3 on VSTest (`Microsoft.NET.Test.Sdk`, `xunit.runner.visualstudio`, `coverlet.collector`). Not packable |
| `tools/HeroesClientSDK.Probe` | `net10.0` | Console `heroes-client-probe`: reads every running client, read-only. Not packable |

- Solution: `HeroesClientSDK.slnx` only (no `.sln`). There is no `global.json`, so any .NET 10 SDK
  builds it (CI: `setup-dotnet` `10.0.x`).
- `Directory.Build.props`: `LangVersion` latest; `Nullable` and `ImplicitUsings` **disabled**;
  analyzers on with `EnforceCodeStyleInBuild`; XML docs generated; deterministic; SourceLink with
  the PDB embedded; `[SupportedOSPlatform("windows")]` on every assembly; MinVer.
- `Directory.Packages.props`: central package management (`ManagePackageVersionsCentrally`). A
  `PackageReference` has no version; the version is a `PackageVersion` there. The test stack is
  the same versions as HeroesReplay's.
- `.editorconfig`: unused usings (IDE0005), unused private members (IDE0051, IDE0052) and unused
  locals and fields (CS0168, CS0169, CS0219, CS0414, CS8321) are **build errors**. Other analyzer
  warnings stay warnings (`TreatWarningsAsErrors` false).
- Format: local tool `csharpier` 1.3.0 (`.config/dotnet-tools.json`), config `.csharpierrc.json`
  (print width 100, 4 spaces). `.csharpierignore` skips `bin`, `obj`, `artifacts` and `*.g.cs`.

## Commands

```powershell
dotnet tool restore
dotnet csharpier format .
dotnet csharpier check .
dotnet build HeroesClientSDK.slnx -c Release
dotnet test HeroesClientSDK.slnx -c Release
dotnet test HeroesClientSDK.slnx -c Release --filter "Category=Unit"
dotnet test HeroesClientSDK.slnx -c Release --filter "FullyQualifiedName~AgentDocsTests"
dotnet pack src/HeroesClientSDK -c Release -o artifacts
dotnet run --project tools/HeroesClientSDK.Probe -c Release -- --watch 250
```

CI (`.github/workflows/ci.yml`, job `build`, `windows-latest`) runs, in order: `dotnet tool restore`, `csharpier check .`, a Release build,
`dotnet test --no-build`, and `dotnet pack`, on every pull request to `main` and every push to `main`.
Run the same steps locally before you push. The checkout needs full history
(`fetch-depth: 0`) because MinVer reads the tags.

## Style

- File-scoped namespaces; usings at the top of the file; `using` declarations where the dispose scope
  is the method.
- No implicit usings and no nullable annotations. Write every `using` you need, and nothing more,
  because IDE0005 fails the build.
- Public types and members get XML doc comments (the build generates the docs file). The tests
  project turns off CS1591.
- Tests: `[Trait(TestCategories.Category, TestCategories.Unit)]` on the class, `[Fact]`/`[Theory]`,
  `Assert.*` from xUnit. Name tests `Method_Condition_Result`. Test data from a real client is a few
  bytes of hex in the test source with the build and date in a comment, never a file from the client
  (heroes-client-re).
- The library runs only on Windows and reads other processes. A test must not need a running Heroes
  client: use recorded bytes and a fake read function.

## Versions and packages

- The version comes from the git tag `vX.Y.Z` (MinVer, `MinVerTagPrefix` `v`). Between tags a build
  is `X.Y.(Z+1)-alpha.0.N`. Don't set `Version` in a project.
- A tag push runs `publish.yml`. It builds, tests and packs with the tag's version, pushes the `.nupkg`
  to GitHub Packages, and attaches the same file to the GitHub Release with its SHA-256. An asset
  already on a release is never replaced, because HeroesReplay pins that hash. Tag only on `main`,
  after CI is green.
- Keep dependencies at zero for the library itself: it uses only the BCL and P/Invoke
  (`NativeMethods`). A new package for the library needs a reason in the PR. Test and build packages
  go in `Directory.Packages.props`.
- Stay on .NET 10 LTS. Don't move to a .NET 11 preview SDK or TFM.
