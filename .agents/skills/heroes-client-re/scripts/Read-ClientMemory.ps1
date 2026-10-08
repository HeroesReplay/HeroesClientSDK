<#
.SYNOPSIS
Read-only reader for a running client: follows a pointer chain from a module RVA and prints the
bytes there, once or on a watch, and can save labelled snapshots for diffs and SDK tests.

.DESCRIPTION
Opens the process with PROCESS_QUERY_INFORMATION | PROCESS_VM_READ only (the SDK's rights). It
never writes, suspends, injects or attaches a debugger, and it never brings the window forward.

Chain semantics, the same as the SDK readers:
  address = moduleBase + Rva
  for each offset: address = [address] + offset      (read a pointer, add the offset)
  then read -Size bytes at address
So the screen flags byte of LoadingScreen is -Rva 0x3771830 -Offsets 0x218,0x48 -Size 1
(state = [G], screen = [state + 0x218], flags = [screen + 72]) on 2.57.0.98304.

Each qword that points into the module is annotated with its RVA (an object's first qword is its
vftable: look the RVA up with FindSymbols.java ::vftable in Ghidra to name the class).

.PARAMETER Rva
Module-relative address of the global (hex like 0x3771830, or decimal).

.PARAMETER Offsets
Pointer-chain offsets, applied in order (hex or decimal).

.PARAMETER Size
Bytes to read at the end of the chain. Default 64.

.PARAMETER Watch
Seconds between reads. Prints only when the bytes change. Use with -Count.

.PARAMETER Count
Number of reads in watch mode. Default 60.

.PARAMETER Label
A name for this state (home, login, loading, download, dialog, end) stored with -OutFile.

.PARAMETER OutFile
Append each read as one JSON line (time, label, build, chain, hex) for later diffs and test data.
Keep it under C:\heroesreplay\re, not in a repo.

.EXAMPLE
pwsh -NoProfile -File Read-ClientMemory.ps1 -Rva 0x3771830 -Offsets 0x218 -Size 0x80
pwsh -NoProfile -File Read-ClientMemory.ps1 -Rva 0x3771830 -Offsets 0x218,0x48 -Size 1 -Watch 1 -Count 120
pwsh -NoProfile -File Read-ClientMemory.ps1 -Rva 0x3771830 -Offsets 0x218 -Size 0x100 -Label home -OutFile C:\heroesreplay\re\snapshots\98304.jsonl
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$Rva,
    [string[]]$Offsets = @(),
    [string]$Size = '64',
    [double]$Watch = 0,
    [int]$Count = 60,
    [string]$Label,
    [string]$OutFile,
    [int]$ProcessId,
    [string]$ProcessName = 'HeroesOfTheStorm_x64'
)

$ErrorActionPreference = 'Stop'

function ConvertTo-Number([string]$text) {
    $t = $text.Trim()
    if ($t -match '^-?0x') {
        $negative = $t.StartsWith('-')
        $value = [Convert]::ToInt64($t.TrimStart('-').Substring(2), 16)
        if ($negative) { return -$value } else { return $value }
    }
    return [int64]$t
}

Add-Type -TypeDefinition @'
using System;
using System.Runtime.InteropServices;

public static class ReadOnlyMemory
{
    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr OpenProcess(uint access, bool inherit, int pid);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool ReadProcessMemory(IntPtr process, IntPtr address, byte[] buffer, IntPtr size, out IntPtr read);

    [DllImport("kernel32.dll")]
    public static extern bool CloseHandle(IntPtr handle);

    /// <summary>PROCESS_QUERY_INFORMATION | PROCESS_VM_READ, nothing more.</summary>
    public static IntPtr Open(int pid)
    {
        IntPtr handle = OpenProcess(0x0400 | 0x0010, false, pid);
        if (handle == IntPtr.Zero)
        {
            throw new InvalidOperationException("OpenProcess failed: " + Marshal.GetLastWin32Error());
        }
        return handle;
    }

    public static byte[] Read(IntPtr handle, long address, int size)
    {
        byte[] buffer = new byte[size];
        IntPtr read;
        if (address <= 0 || !ReadProcessMemory(handle, (IntPtr)address, buffer, (IntPtr)size, out read) || read.ToInt64() != size)
        {
            return null;
        }
        return buffer;
    }
}
'@

if ($ProcessId) {
    $process = Get-Process -Id $ProcessId
}
else {
    $candidates = @(Get-Process -Name $ProcessName -ErrorAction SilentlyContinue)
    if ($candidates.Count -eq 0) { throw "No $ProcessName process is running. This script never starts one." }
    if ($candidates.Count -gt 1) {
        $candidates | ForEach-Object { Write-Host ("{0}  {1}" -f $_.Id, $_.Path) }
        throw "More than one $ProcessName is running. Pass -ProcessId."
    }
    $process = $candidates[0]
}

$module = $process.MainModule
$base = $module.BaseAddress.ToInt64()
$moduleSize = [int64]$module.ModuleMemorySize
$build = $module.FileVersionInfo.FileVersion
$rvaValue = ConvertTo-Number $Rva
$offsetValues = @($Offsets | ForEach-Object { $_ -split ',' } | Where-Object { $_ } | ForEach-Object { ConvertTo-Number $_ })
$sizeValue = [int](ConvertTo-Number $Size)
$handle = [ReadOnlyMemory]::Open($process.Id)

function Read-Chain {
    $address = $base + $rvaValue
    $steps = [Collections.Generic.List[string]]::new()
    $steps.Add(('G=base+0x{0:X}=0x{1:X}' -f $rvaValue, $address))
    foreach ($offset in $offsetValues) {
        $pointer = [ReadOnlyMemory]::Read($handle, $address, 8)
        if ($null -eq $pointer) { return @{ ok = $false; steps = $steps; why = ('read failed at 0x{0:X}' -f $address) } }
        $value = [BitConverter]::ToInt64($pointer, 0)
        if ($value -eq 0) { return @{ ok = $false; steps = $steps; why = ('null pointer at 0x{0:X}' -f $address) } }
        $address = $value + $offset
        $steps.Add(('[..]=0x{0:X} +0x{1:X} -> 0x{2:X}' -f $value, $offset, $address))
    }
    $bytes = [ReadOnlyMemory]::Read($handle, $address, $sizeValue)
    if ($null -eq $bytes) { return @{ ok = $false; steps = $steps; why = ('read failed at 0x{0:X}' -f $address) } }
    return @{ ok = $true; steps = $steps; address = $address; bytes = $bytes }
}

function Format-Bytes([byte[]]$bytes, [int64]$address) {
    for ($i = 0; $i -lt $bytes.Length; $i += 16) {
        $n = [Math]::Min(16, $bytes.Length - $i)
        $hex = ($bytes[$i..($i + $n - 1)] | ForEach-Object { $_.ToString('X2') }) -join ' '
        $notes = @()
        for ($q = $i; $q + 8 -le $i + $n; $q += 8) {
            $v = [BitConverter]::ToInt64($bytes, $q)
            if ($v -ge $base -and $v -lt $base + $moduleSize) { $notes += ('+0x{0:X}: rva 0x{1:X}' -f $q, ($v - $base)) }
        }
        '  +0x{0:X3}  {1,-47}  {2}' -f $i, $hex, ($notes -join '  ')
    }
}

try {
    Write-Host ('pid {0}  {1}  base 0x{2:X}' -f $process.Id, $build, $base)
    $previous = $null
    $reads = if ($Watch -gt 0) { $Count } else { 1 }
    for ($r = 0; $r -lt $reads; $r++) {
        if ($process.HasExited) { Write-Host 'Process exited.'; break }
        $result = Read-Chain
        $hex = if ($result.ok) { [BitConverter]::ToString($result.bytes).Replace('-', ' ') } else { $null }
        if ($hex -ne $previous -or $r -eq 0) {
            $stamp = (Get-Date).ToString('HH:mm:ss.fff')
            if ($result.ok) {
                Write-Host ('{0}  {1}  @0x{2:X}' -f $stamp, ($result.steps -join '  '), $result.address)
                Format-Bytes $result.bytes $result.address | Write-Host
            }
            else {
                Write-Host ('{0}  {1}  ({2})' -f $stamp, ($result.steps -join '  '), $result.why)
            }
            if ($OutFile) {
                $dir = Split-Path $OutFile -Parent
                if ($dir) { New-Item -ItemType Directory -Force $dir | Out-Null }
                [ordered]@{
                    time    = (Get-Date).ToString('o')
                    label   = $Label
                    build   = $build
                    rva     = ('0x{0:X}' -f $rvaValue)
                    offsets = @($offsetValues | ForEach-Object { '0x{0:X}' -f $_ })
                    ok      = $result.ok
                    why     = $result.why
                    address = if ($result.ok) { '0x{0:X}' -f $result.address } else { $null }
                    hex     = $hex
                } | ConvertTo-Json -Compress | Add-Content -Encoding utf8 $OutFile
            }
            $previous = $hex
        }
        if ($Watch -gt 0 -and $r -lt $reads - 1) { Start-Sleep -Milliseconds ([int]($Watch * 1000)) }
    }
}
finally {
    [void][ReadOnlyMemory]::CloseHandle($handle)
}
