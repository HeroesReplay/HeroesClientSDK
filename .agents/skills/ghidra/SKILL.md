---
name: ghidra
description: >
  Ghidra 12.1.4 headless on ASA-SERVER for reverse engineering Heroes of the Storm client builds:
  where it is installed, per-build projects under C:\heroesreplay\re\ghidra, analysis options for
  a large x64 binary, reusing a project with -process -noanalysis, and the GhidraScripts in
  scripts/ (FindBytePattern, XrefsToAddress, FindRipRelativeLoads, MakeSignature, ExportFunction,
  FindStringRefs, FindSymbols, FindVftables, SetAnalysisOptions). Use for any Ghidra,
  analyzeHeadless, decompile, xref, byte pattern, signature or RTTI task, or /ghidra. The workflow
  for a new SDK memory read is the heroes-client-re skill.
---

# Ghidra (headless)

## Install on ASA-SERVER

| What | Value |
| --- | --- |
| Ghidra | 12.1.4 PUBLIC (build 2026-09-21), from the official release `Ghidra_12.1.4_build` on github.com/NationalSecurityAgency/ghidra |
| Path | `C:\Tools\ghidra_12.1.4_PUBLIC` |
| Env | User variable `GHIDRA_INSTALL_DIR` = that path. A shell started before it was set does not see it: `[Environment]::GetEnvironmentVariable('GHIDRA_INSTALL_DIR','User')` |
| Java | Microsoft OpenJDK 21.0.12 (`JAVA_HOME` = `C:\Program Files\Microsoft\jdk-21.0.12.101-hotspot\`). Ghidra 12.1 needs JDK 21+. |
| Checked | SHA-256 of `ghidra_12.1.4_PUBLIC_20260921.zip` = `ddac49f903da9d5bac833e5cc79395098b9c33cfd3279be5f31bd00387d2d4db`, the value in the release notes |
| GUI | `C:\Tools\ghidra_12.1.4_PUBLIC\ghidraRun.bat` (not needed for anything below) |

To update: `gh release view -R NationalSecurityAgency/ghidra`, `gh release download <tag> -p '*.zip'` into a new empty folder, compare `Get-FileHash` with the SHA-256 in the release notes, extract to `C:\Tools\ghidra_<version>_PUBLIC`, and point `GHIDRA_INSTALL_DIR` at it. No admin rights are needed.

**PyGhidra** ships with 12.1 (`support\pyghidraRun.bat`, `-H` for headless) but needs CPython 3.9 to 3.14. This box has only the Microsoft Store `python` stub, so it is not enabled. The first run of `pyghidraRun.bat` creates a venv and pip-installs pyghidra. The scripts here are Java GhidraScripts on purpose: headless compiles them itself and needs no Python.

**GhidraMCP** (LaurieWired/GhidraMCP: a Ghidra extension plus an MCP bridge, so an agent can drive the GUI) is an optional extra. It is not installed. Ask the owner before installing it or any other plugin.

## The client exe is encrypted on disk

`HeroesOfTheStorm_x64.exe` (98025, 98304 and 98348 all checked) has its `.text` encrypted in the file. The entropy is 8.0, and neither SDK pattern (screen state, match clock) matches the file. The client decrypts its code after it starts. That is why the SDK rescans every 10 s while a fresh client "unpacks". In the file:

- **Plain:** `.rdata` (strings, RTTI type descriptor names, vftable slots of some classes), `.pdata` (function starts), `.data` (initialized part), and the headers.
- **Not usable:** every byte of code. Disassembling or decompiling the file shows ciphertext, and code references to strings or globals do not exist.

So there are two kinds of program:

1. **The exe file, with a data-only analysis.** It is quick (about 30 s) and gives the string, RTTI and global map. Use it before a client is running.
2. **A memory image of a running client.** Make it with `.agents/skills/heroes-client-re/scripts/Save-ModuleImage.ps1`, which is read-only. The owner has approved this (heroes-client-re, Rules). Give it a full analysis. All the code work happens here. The loaded code is plain: images of running 2.57.0.98348 and 2.57.0.98304 clients (2026-10-08, `C:\heroesreplay\re\dumps\<version>\`) have `.text` entropy 6.58, against 8.0 in the file.

Do not try to decrypt the file offline. That would be circumventing Blizzard's protection, which is out of bounds.

## Layout (outside every repo)

```
C:\heroesreplay\re\
  ghidra\<build>\hots.gpr, hots.rep\    one project per client build (98348, 98304, ...)
  ghidra\<build>\out\                   script reports
  dumps\<file version>\                 memory images + .json sidecars (Save-ModuleImage.ps1)
```

A project holds both programs for a build: `HeroesOfTheStorm_x64.exe` (the file, data-only) and `HeroesOfTheStorm_x64-<version>-image.dmp` (the image, full analysis). Projects and dumps never go into git. The SDK `.gitignore` also ignores `*.gpr`, `*.rep`, `*.dmp` and `re/`.

**Existing project:** `C:\heroesreplay\re\ghidra\98348\hots.gpr` with `HeroesOfTheStorm_x64.exe` (2.57.0.98348, data-only), made 2026-10-08. Its reports are in `out\`: `symbols-screen.txt`, `vftables-screen.txt`, `strings-loadingscreen.txt`, and the 0-match pattern checks.

## Import and analyze

`analyzeHeadless.bat <projectDir> <projectName> -import <file> [options]`. Set the heap first (the headless default is 2 GB), and lower the priority so a live proof on this box keeps the CPU (the JVM inherits it):

```powershell
$g = [Environment]::GetEnvironmentVariable('GHIDRA_INSTALL_DIR','User')
$skills = 'C:\heroesreplay\HeroesClientSDK\.agents\skills\ghidra\scripts'   # or your worktree
$env:GHIDRA_HEADLESS_MAXMEM = '4G'     # 6G-8G for a full analysis of a client image
[Diagnostics.Process]::GetCurrentProcess().PriorityClass = 'BelowNormal'

# 1. The exe file, data only. Import from Data\Clients (no parentheses in the path, see Pitfalls).
& "$g\support\analyzeHeadless.bat" C:\heroesreplay\re\ghidra\98348 hots `
    -import C:\heroesreplay\Data\Clients\Base98348\HeroesOfTheStorm_x64.exe -overwrite `
    -scriptPath $skills -preScript SetAnalysisOptions.java preset:data-only `
    -log C:\heroesreplay\re\ghidra\98348\import-disk.log

# 2. A memory image, full analysis with the large-binary preset. Run it in the background.
& "$g\support\analyzeHeadless.bat" C:\heroesreplay\re\ghidra\98348 hots `
    -import C:\heroesreplay\re\dumps\2.57.0.98348\HeroesOfTheStorm_x64-2.57.0.98348-image.dmp `
    -loader PeLoader -scriptPath $skills -preScript SetAnalysisOptions.java preset:large-x64 `
    -log C:\heroesreplay\re\ghidra\98348\import-image.log
```

The Bash tool's `run_in_background` (or `Start-Process`) keeps a long analysis off the session, so poll its output file. Write `-log` and keep it: the tail has a per-analyzer time table (`Total Time`).

**Analysis options.** `SetAnalysisOptions.java list` prints every option and its value; there are 32 analyzers in 12.1.4. Preset `large-x64` turns off `Decompiler Parameter ID` (on by default for x64 PE, and by far the slowest on a big binary), `Function ID`, `Aggressive Instruction Finder`, `Embedded Media`, `WindowsResourceReference`, `PDB Universal` and `Condense Filler Bytes`. It keeps the reference, constant-reference, switch, stack, RTTI, demangler, string and `.pdata` exception-handling analyzers. If an analysis is still too slow, `Decompiler Switch Analysis` is the next one to turn off (`"Decompiler Switch Analysis:false"`). Preset `data-only` keeps only `ASCII Strings`, `Windows x86 PE RTTI Analyzer`, `Demangler Microsoft` and `Apply Data Archives`. `-analysisTimeoutPerFile <s>` caps a run.

### Measured timings (ASA-SERVER: 8 logical CPUs, 16 GB, Ghidra 12.1.4, JDK 21)

| Run | Time |
| --- | --- |
| Download (570 MB) and extract | 6 s and 4 s |
| `version.dll` import + default analysis | 27 s total (9 s analysis) |
| `decompile.exe` (2.5 MB, 2.0 MB `.text`) file, import + `large-x64` analysis | 67 s total (58 s analysis) |
| The same binary as a memory image (Save-ModuleImage, 10 ms to read) | 70 s total (61 s analysis); same functions, xrefs and RVAs as the file |
| `HeroesOfTheStorm_x64.exe` 2.57.0.98348 (53.6 MB, 38 MB `.text`), import + `data-only` + 4 scripts, 4 GB heap, below-normal | 1.3 min total. Analysis 27 s (ASCII Strings 13.7 s, RTTI 12.9 s). Project 196 MB |
| One `-process ... -noanalysis -readOnly` run with scripts | 5-9 s (JVM start, script compile, open) |
| Full analysis of a client memory image | Not measured yet. Images of 98348 and 98304 exist (`C:\heroesreplay\re\dumps`, about 73 MB each), but no full analysis of one has been logged. Budget 60-90 min with `large-x64` and 6-8 GB, and write the real figure here |

A full analysis of an image the size of the client (about 73 MB mapped) also writes a much bigger project than the 196 MB data-only one. Check the free space on C: first (about 8 GB on 2026-10-08).

## Reuse an analyzed project

Do not analyze twice. Open the saved program and run scripts:

```powershell
& "$g\support\analyzeHeadless.bat" C:\heroesreplay\re\ghidra\98348 hots `
    -process HeroesOfTheStorm_x64.exe -noanalysis -readOnly -scriptPath $skills `
    -postScript FindVftables.java Screen out:C:\heroesreplay\re\ghidra\98348\out\vftables.txt `
    -postScript FindStringRefs.java LoadingScreen scan
```

- `-process <name>` is the program name in the project, which is the imported file's name.
- `-readOnly` saves nothing, so changes a script makes (ExportFunction creating a function) are thrown away. Drop it only when you mean to keep changes.
- Several `-postScript` in one run share one JVM start. `-preScript` runs before analysis, `-postScript` after.
- One process at a time per project. The GUI and headless lock each other out (`hots.lock`).

## Scripts (`scripts/`)

Arguments are positional plus `key:value` options. Addresses are `rva:0x...` (relative to the image base, so the same in a file and an image), `0x...` (absolute), or a symbol name. Every script takes `out:<file>` to also write its report.

| Script | Does | Example |
| --- | --- | --- |
| `FindBytePattern.java` | Byte pattern with `??` (and nibble `4?`) wildcards: each match's address, RVA, block and function. `rip:<disp>:<end>` decodes a rel32 operand in the match. `same` keeps matches where all rip targets agree, and a histogram ranks the targets. | `"48 8B 0D ?? ?? ?? ?? 48 85 C9 74 ?? 33 D2 E8 ?? ?? ?? ?? 84 C0 74 ?? 48 8B 0D ?? ?? ?? ?? E8" rip:3:7 rip:26:30 same block:.text` re-derives LoadingScreenPattern |
| `XrefsToAddress.java` | Recorded references to an address (`span:<n>` for struct fields), with the instruction and the function, plus a per-function count. Needs analysis. | `rva:0x3771830 span:0x8` |
| `FindRipRelativeLoads.java` | Every instruction whose RIP-relative operand names the global (`mov rcx,[G]`, `lea`, `cmp [G],0`). Scans bytes, then confirms by disassembly, so it works after `-noanalysis`. Filters: `mnemonic:MOV`, `operand:1` (a MOV load), `context:<n>`. | `rva:0x3771830 mnemonic:MOV operand:1 context:4` |
| `MakeSignature.java` | Grows a signature from an instruction until it is unique in executable blocks. It wildcards rip displacements, rel8/rel32 branches, absolute addresses, and with `wildoffsets` disp32 struct offsets and immediates. It prints each wildcard's byte offset (for example "rip disp at +3, instruction end at +7"), how many matches name the same global, and C# pattern and mask arrays like MatchClockPattern's. | `rva:0x1234567 insns:16 wildoffsets` |
| `ExportFunction.java` | Decompiles one or more functions to C (`asm` adds the disassembly). After `-noanalysis` it disassembles and builds the function first. | `rva:0x1234567 asm out:...\fn.c` |
| `FindStringRefs.java` | ASCII and UTF-16 strings containing the text (defined data plus a raw scan), with the code that uses them. `scan` adds a RIP-relative code scan and 8-byte pointer tables. `regex`, `case`. | `"ScreenHome" scan` |
| `FindSymbols.java` | Symbols by full name: RTTI classes, `::vftable`, demangled functions, imports, labels. `filter:` adds a second substring. | `Screen filter:vftable` |
| `FindVftables.java` | RTTI from raw bytes: type descriptor, then COL, then vftable for each matching class, with `slots:<n>`. Works with no analysis. An object's first qword is its vftable, so this names the class of a live object that has RTTI. The UI frame classes (`CScreen*`, dialogs, panels) have none; name those by the IsA slot (heroes-client-re, Findings). | `CScreenHome exact slots:4` |
| `SetAnalysisOptions.java` | `list`, `preset:large-x64`, `preset:data-only`, or `"<option>:<value>"`. Use it as a `-preScript`. | `preset:large-x64` |

Tested 2026-10-08 on `decompile.exe` from this Ghidra (file and memory image, analyzed and `-noanalysis`) and on the 98348 client exe file. The file and the image gave identical RVAs. XrefsToAddress (with analysis) and FindRipRelativeLoads (without) found the same 13 uses of a global. FindVftables matched Ghidra's RTTI labels.

## Reading results

- Script lines go to the console as `INFO  <Script>.java> <text> (GhidraScript)`, to `-scriptlog <file>` if given, and to `out:<file>` without the prefix. Filter the console with `Where-Object { $_ -match '\.java>' }`.
- The `-log` file has the loader, the language (`x86:LE:64:default:windows`), warnings and the analysis time table.
- Text to search later (RVAs, function lists) is best kept in the `out:` files under the project's `out\` folder.

## Pitfalls

- **No parentheses in `-import` paths.** `analyzeHeadless.bat` breaks on `C:\Program Files (x86)\...` ("\Heroes was unexpected at this time", exit 255). Import the identical copy in `C:\heroesreplay\Data\Clients\Base<build>\`, or copy the exe to `C:\heroesreplay\re\bin\<build>\`. The 8.3 short path also works, but it names the program `HEROES~1.EXE`.
- **The batch file splits on `;`, `,`, `=` and spaces.** Quote any argument that has one. PowerShell adds quotes only for spaces, so pass `'"a;b"'` for a `-scriptPath` list. That is why the scripts take `key:value`, not `key=value`.
- **Exit code 0 even when a script failed.** Look for `SCRIPT ERROR`, `error:` or `Exception` in the output.
- **One script's compile error breaks every script in the folder** ("The class could not be found"): the folder is compiled as one bundle. Read the first `error:` line. Do not declare `parseInt` or `parseLong` in a script, because `GhidraScript` already has them; the scripts here use `toInt`/`toLong`.
- **`-noanalysis` imports have no instructions.** The PE loader leaves one-byte placeholder functions at the `.pdata` starts (5,493 for `decompile.exe`). `getFunctionContaining` is null inside code, and XrefsToAddress finds nothing. FindBytePattern, FindRipRelativeLoads, FindVftables, MakeSignature and ExportFunction still work.
- **A file and an image have different image bases.** A file is at `0x140000000`. An image keeps the runtime base (for example `0x7FF6...`), so its vftables and pointer tables stay right. Compare RVAs, never absolute addresses.
- **Heap.** The headless default is 2 GB. A big analysis that dies with `OutOfMemoryError` needs a larger `GHIDRA_HEADLESS_MAXMEM`. This box has 16 GB and often only about 3 GB free while a live proof runs. Keep the priority below normal and do not run two big analyses at once.
- **Scripts compile on first use** into `%APPDATA%\ghidra\ghidra_12.1.4_PUBLIC\osgi\compiled-bundles` (a few seconds). An edited script recompiles automatically. Ghidra's own `application.log` and `script.log` are in `%APPDATA%\ghidra\ghidra_12.1.4_PUBLIC`.
- **`-overwrite` replaces a program of the same name** in the project. Without it, a second import of the same file is skipped.
- **Data-only RTTI errors are expected on the exe file.** `Failed to disassemble ... (EHDataTypeUtilities)` and `No vfTable found for RTTICompleteObjectLocator` come from the encrypted code. The `CScreen*` type descriptors are in the file, but nothing in the plain sections points at them. The images confirmed that the UI frame classes have no complete object locator at all, so FindVftables doesn't find their vftables even on an image. Vtable slot `+0x240` (`IsA`) names them instead (heroes-client-re, Findings).
