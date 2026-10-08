<#
.SYNOPSIS
Saves a read-only copy of a running process's main module as a PE file that Ghidra imports.

.DESCRIPTION
HeroesOfTheStorm_x64.exe ships with its .text encrypted on disk (entropy 8.0; no SDK pattern
matches the file). The client decrypts it after start. To reverse engineer the code, take the
image from a running client's memory and import that into Ghidra.

Access is read-only: the process is opened with PROCESS_QUERY_INFORMATION | PROCESS_VM_READ
only, the same rights the SDK readers use. Nothing is written to the process, no thread is
suspended, no debugger attaches, nothing is injected. A read takes about a second and does not
disturb a client the spectator is using.

The image is read region by region (VirtualQueryEx); pages that cannot be read are zero-filled
and listed. The PE headers come from the exe file on disk. Each section's raw pointer is set to
its virtual address, so the file layout is the memory layout, and ImageBase is set to the
runtime base, so absolute pointers (vftables, tables) stay right. RVAs are the same as in the
SDK. A sidecar JSON records the build, base, unreadable ranges and per-section entropy: a .text
entropy near 8.0 means the code was still encrypted (dump again a little later).

Output goes to C:\heroesreplay\re\dumps\<file version>\ by default, outside every repo. Never
commit it.

.PARAMETER ProcessId
The process to read. Required when more than one process has the name.

.PARAMETER ProcessName
Process name without .exe. Default HeroesOfTheStorm_x64.

.PARAMETER OutDir
Root folder for dumps. Default C:\heroesreplay\re\dumps.

.PARAMETER Force
Overwrite an existing dump for the same build.

.EXAMPLE
pwsh -NoProfile -File Save-ModuleImage.ps1
pwsh -NoProfile -File Save-ModuleImage.ps1 -ProcessId 20524
#>
[CmdletBinding()]
param(
    [int]$ProcessId,
    [string]$ProcessName = 'HeroesOfTheStorm_x64',
    [string]$OutDir = 'C:\heroesreplay\re\dumps',
    [switch]$Force
)

$ErrorActionPreference = 'Stop'

Add-Type -TypeDefinition @'
using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;

public static class ModuleImageReader
{
    private const uint ProcessQueryInformation = 0x0400;
    private const uint ProcessVmRead = 0x0010;
    private const uint MemCommit = 0x1000;
    private const uint PageNoAccess = 0x01;
    private const uint PageGuard = 0x100;

    [StructLayout(LayoutKind.Sequential)]
    private struct MemoryBasicInformation
    {
        public IntPtr BaseAddress;
        public IntPtr AllocationBase;
        public uint AllocationProtect;
        public ushort PartitionId;
        public IntPtr RegionSize;
        public uint State;
        public uint Protect;
        public uint Type;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr OpenProcess(uint access, bool inherit, int pid);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool ReadProcessMemory(IntPtr process, IntPtr address, byte[] buffer, IntPtr size, out IntPtr read);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr VirtualQueryEx(IntPtr process, IntPtr address, out MemoryBasicInformation info, IntPtr length);

    [DllImport("kernel32.dll")]
    private static extern bool CloseHandle(IntPtr handle);

    /// <summary>Reads [moduleBase, moduleBase + size) read-only. Unreadable pages stay zero.</summary>
    public static byte[] Read(int pid, long moduleBase, int size, List<string> unreadable)
    {
        IntPtr handle = OpenProcess(ProcessQueryInformation | ProcessVmRead, false, pid);
        if (handle == IntPtr.Zero)
        {
            throw new InvalidOperationException("OpenProcess failed: " + Marshal.GetLastWin32Error());
        }

        try
        {
            byte[] image = new byte[size];
            long address = moduleBase;
            long end = moduleBase + size;
            while (address < end)
            {
                MemoryBasicInformation info;
                if (VirtualQueryEx(handle, (IntPtr)address, out info, (IntPtr)Marshal.SizeOf(typeof(MemoryBasicInformation))) == IntPtr.Zero)
                {
                    unreadable.Add(string.Format("0x{0:X}-0x{1:X} (VirtualQueryEx failed)", address - moduleBase, end - moduleBase));
                    break;
                }

                long regionEnd = Math.Min(info.BaseAddress.ToInt64() + info.RegionSize.ToInt64(), end);
                bool readable = info.State == MemCommit && info.Protect != 0
                    && (info.Protect & PageNoAccess) == 0 && (info.Protect & PageGuard) == 0;
                if (readable)
                {
                    ReadRange(handle, moduleBase, address, regionEnd, image, unreadable);
                }
                else
                {
                    unreadable.Add(string.Format("0x{0:X}-0x{1:X} (state 0x{2:X}, protect 0x{3:X})",
                        address - moduleBase, regionEnd - moduleBase, info.State, info.Protect));
                }

                address = regionEnd;
            }

            return image;
        }
        finally
        {
            CloseHandle(handle);
        }
    }

    private static void ReadRange(IntPtr handle, long moduleBase, long from, long to, byte[] image, List<string> unreadable)
    {
        const int Chunk = 1 << 20;
        for (long at = from; at < to; at += Chunk)
        {
            int count = (int)Math.Min(Chunk, to - at);
            byte[] buffer = new byte[count];
            IntPtr read;
            if (ReadProcessMemory(handle, (IntPtr)at, buffer, (IntPtr)count, out read) && read.ToInt64() == count)
            {
                Buffer.BlockCopy(buffer, 0, image, (int)(at - moduleBase), count);
                continue;
            }

            // Page by page, so one bad page does not lose the chunk.
            for (long page = at; page < at + count; page += 0x1000)
            {
                int pageSize = (int)Math.Min(0x1000, at + count - page);
                byte[] one = new byte[pageSize];
                if (ReadProcessMemory(handle, (IntPtr)page, one, (IntPtr)pageSize, out read) && read.ToInt64() == pageSize)
                {
                    Buffer.BlockCopy(one, 0, image, (int)(page - moduleBase), pageSize);
                }
                else
                {
                    unreadable.Add(string.Format("0x{0:X}-0x{1:X} (read failed)", page - moduleBase, page - moduleBase + pageSize));
                }
            }
        }
    }

    /// <summary>
    /// Puts the on-disk headers on the image, maps every section's raw data to its virtual
    /// address, and sets ImageBase to the runtime base. Returns the section names and ranges.
    /// </summary>
    public static List<string[]> FixHeaders(byte[] image, byte[] fileHeaders, long runtimeBase)
    {
        int lfanew = BitConverter.ToInt32(fileHeaders, 0x3C);
        if (fileHeaders[0] != 'M' || fileHeaders[1] != 'Z' || BitConverter.ToUInt32(fileHeaders, lfanew) != 0x00004550)
        {
            throw new InvalidDataException("The exe on disk has no PE header.");
        }

        int optional = lfanew + 24;
        if (BitConverter.ToUInt16(fileHeaders, optional) != 0x20B)
        {
            throw new InvalidDataException("Not a PE32+ (x64) image.");
        }

        int sizeOfHeaders = BitConverter.ToInt32(fileHeaders, optional + 60);
        Buffer.BlockCopy(fileHeaders, 0, image, 0, Math.Min(sizeOfHeaders, Math.Min(fileHeaders.Length, image.Length)));
        BitConverter.GetBytes(runtimeBase).CopyTo(image, optional + 24);
        int sectionAlignment = BitConverter.ToInt32(image, optional + 32);

        int sectionCount = BitConverter.ToUInt16(image, lfanew + 6);
        int optionalSize = BitConverter.ToUInt16(image, lfanew + 20);
        int table = optional + optionalSize;
        var sections = new List<string[]>();
        for (int i = 0; i < sectionCount; i++)
        {
            int at = table + i * 40;
            string name = System.Text.Encoding.ASCII.GetString(image, at, 8).TrimEnd('\0');
            uint virtualSize = BitConverter.ToUInt32(image, at + 8);
            uint virtualAddress = BitConverter.ToUInt32(image, at + 12);
            long aligned = ((long)virtualSize + sectionAlignment - 1) / sectionAlignment * sectionAlignment;
            long raw = Math.Min(aligned, image.Length - (long)virtualAddress);
            BitConverter.GetBytes((uint)raw).CopyTo(image, at + 16);
            BitConverter.GetBytes(virtualAddress).CopyTo(image, at + 20);
            sections.Add(new[] { name, virtualAddress.ToString(), raw.ToString() });
        }

        return sections;
    }

    public static double Entropy(byte[] data, int offset, int length)
    {
        if (length <= 0)
        {
            return 0;
        }

        var counts = new int[256];
        for (int i = offset; i < offset + length; i++)
        {
            counts[data[i]]++;
        }

        double h = 0;
        foreach (int c in counts)
        {
            if (c > 0)
            {
                double p = (double)c / length;
                h -= p * Math.Log(p, 2);
            }
        }

        return h;
    }
}
'@

if ($ProcessId) {
    $process = Get-Process -Id $ProcessId
}
else {
    $candidates = @(Get-Process -Name $ProcessName -ErrorAction SilentlyContinue)
    if ($candidates.Count -eq 0) {
        throw "No $ProcessName process is running. Start the client yourself; this script never launches it."
    }
    if ($candidates.Count -gt 1) {
        $candidates | ForEach-Object { Write-Host ("{0}  {1}  {2}" -f $_.Id, $_.MainModule.FileVersionInfo.FileVersion, $_.Path) }
        throw "More than one $ProcessName is running. Pass -ProcessId."
    }
    $process = $candidates[0]
}

$module = $process.MainModule
$exe = $module.FileName
$version = $module.FileVersionInfo.FileVersion
if ([string]::IsNullOrWhiteSpace($version)) { $version = 'unknown' }
$version = $version -replace '[, ]+', '.'
$base = $module.BaseAddress.ToInt64()
$size = $module.ModuleMemorySize
$name = [IO.Path]::GetFileNameWithoutExtension($exe)

$folder = Join-Path $OutDir $version
$imagePath = Join-Path $folder "$name-$version-image.dmp"
if ((Test-Path $imagePath) -and -not $Force) {
    throw "$imagePath exists. Pass -Force to replace it."
}
New-Item -ItemType Directory -Force $folder | Out-Null

$watch = [Diagnostics.Stopwatch]::StartNew()
$unreadable = [Collections.Generic.List[string]]::new()
$image = [ModuleImageReader]::Read($process.Id, $base, $size, $unreadable)
$readMs = $watch.ElapsedMilliseconds

$headerLength = [Math]::Min(0x1000, (Get-Item $exe).Length)
$fileHeaders = [byte[]]::new($headerLength)
$stream = [IO.File]::OpenRead($exe)
try { [void]$stream.Read($fileHeaders, 0, $headerLength) } finally { $stream.Dispose() }
$sections = [ModuleImageReader]::FixHeaders($image, $fileHeaders, $base)
[IO.File]::WriteAllBytes($imagePath, $image)

$sectionInfo = foreach ($s in $sections) {
    $va = [int]$s[1]; $raw = [int]$s[2]
    [ordered]@{
        name    = $s[0]
        rva     = ('0x{0:X}' -f $va)
        size    = ('0x{0:X}' -f $raw)
        entropy = [Math]::Round([ModuleImageReader]::Entropy($image, $va, $raw), 3)
    }
}
$text = $sectionInfo | Where-Object { $_.name -eq '.text' } | Select-Object -First 1
$meta = [ordered]@{
    image            = $imagePath
    exe              = $exe
    fileVersion      = $module.FileVersionInfo.FileVersion
    processId        = $process.Id
    processStartUtc  = $process.StartTime.ToUniversalTime().ToString('o')
    savedUtc         = [DateTime]::UtcNow.ToString('o')
    runtimeBase      = ('0x{0:X}' -f $base)
    sizeOfImage      = ('0x{0:X}' -f $size)
    readMilliseconds = $readMs
    unreadable       = @($unreadable)
    sections         = @($sectionInfo)
    textLooksPlain   = ($null -ne $text -and $text.entropy -lt 7.5)
}
$meta | ConvertTo-Json -Depth 4 | Set-Content -Encoding utf8 ([IO.Path]::ChangeExtension($imagePath, '.json'))

Write-Host ("Saved {0} ({1:N0} bytes, read in {2} ms) from pid {3}, {4}, base 0x{5:X}" -f $imagePath, $image.Length, $readMs, $process.Id, $meta.fileVersion, $base)
foreach ($s in $sectionInfo) { Write-Host ("  {0,-8} rva {1,-10} size {2,-10} entropy {3}" -f $s.name, $s.rva, $s.size, $s.entropy) }
if ($unreadable.Count -gt 0) { Write-Host ("  {0} unreadable range(s), zero-filled; see the .json" -f $unreadable.Count) }
if (-not $meta.textLooksPlain) {
    Write-Warning '.text entropy is still high: the code looks encrypted. Wait until the client reaches its home screen and dump again with -Force.'
}
