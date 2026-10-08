using System;
using System.Runtime.InteropServices;

namespace HeroesClientSDK;

/// <summary>
/// The kernel32 calls every memory reader uses: open the client process for query and read,
/// read its memory, and close the handle. Nothing here writes to the client.
/// </summary>
internal static class NativeMethods
{
    public const int ProcessVmRead = 0x0010;
    public const int ProcessQueryInformation = 0x0400;

    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern IntPtr OpenProcess(int access, bool inherit, int processId);

    /// <summary><c>nSize</c> and <c>lpNumberOfBytesRead</c> are <c>SIZE_T</c>.</summary>
    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern bool ReadProcessMemory(
        IntPtr process,
        IntPtr address,
        ref byte buffer,
        nint size,
        out nint read
    );

    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern bool CloseHandle(IntPtr handle);
}
