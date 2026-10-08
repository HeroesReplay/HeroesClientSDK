using System;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace HeroesClientSDK;

/// <summary>
/// One running client, attached read-only: its process handle (opened with
/// <c>PROCESS_QUERY_INFORMATION | PROCESS_VM_READ</c> only), its main module and the build of its
/// exe. Each process (pid and start time) gets its own instance. Pass one to every reader's
/// <c>Read(HeroesClientProcess)</c> to share a single handle, or let each reader attach by itself
/// with <c>Read(Process)</c>.
/// </summary>
public sealed class HeroesClientProcess : IDisposable
{
    private readonly ProcessMemory owned;

    private HeroesClientProcess(
        string reason,
        ClientModule module,
        IProcessMemory memory,
        ProcessMemory owned
    )
    {
        Reason = reason;
        Module = module;
        Memory = memory;
        this.owned = owned;
        DetectedVersion = HeroesClientVersion.TryParse(module.FileVersion);
    }

    /// <summary>True when the process is attached and its main module is known.</summary>
    public bool Ok => Reason == "ok";

    /// <summary>
    /// "ok", or why the process is not attached: "no-process" (null or exited), "open-failed"
    /// (Windows refused the read-only handle), or "no-module" (the main module cannot be read
    /// yet, as on a client that has only just started). A saved image that cannot be served
    /// (<see cref="FromImage"/>) is "no-image" or "bad-image".
    /// </summary>
    public string Reason { get; }

    /// <summary>The main module. Default when the process is not attached.</summary>
    public ClientModule Module { get; }

    /// <summary>
    /// The build of the running exe, or null when its file version is missing or unreadable. A
    /// null version never stops a read: the readers find what they need by pattern.
    /// </summary>
    public HeroesClientVersion DetectedVersion { get; }

    internal IProcessMemory Memory { get; }

    /// <summary>
    /// The screen readers' code scans of this process: a <see cref="LoadingScreen"/> and a
    /// <see cref="ClientScreen"/> that read this client walk its code once between them.
    /// </summary>
    internal ScreenScans ScreenScans { get; } = new();

    /// <summary>
    /// Attaches to <paramref name="process"/> read-only. Never throws: a process that cannot be
    /// attached comes back with <see cref="Ok"/> false and a <see cref="Reason"/>.
    /// </summary>
    public static HeroesClientProcess Attach(Process process) => Attach(process, ReadMainModule);

    /// <summary>
    /// A client served by <paramref name="memory"/> instead of a live process, such as a fake in a
    /// test or bytes recorded from a client. <paramref name="memory"/> stays the caller's.
    /// </summary>
    public static HeroesClientProcess FromMemory(IProcessMemory memory, ClientModule module) =>
        new("ok", module, memory, null);

    /// <summary>
    /// A client served by a saved image of its main module: the file that the
    /// <c>heroes-client-re</c> skill's <c>Save-ModuleImage.ps1</c> writes from a running client
    /// (the decrypted code, laid out by RVA, with the runtime base as its image base). The exe file
    /// itself cannot serve, because its code is encrypted on disk. Every reader's discovery runs
    /// on it offline (<see cref="ClientDiscovery.Run"/>); state that lives on the heap does not
    /// read. The build is the image's own <c>FileVersion</c>, or null. An image has no process: its
    /// module reads as process 1, started when the file was written. Never throws: a missing or
    /// unreadable file is "no-image", and a file that is not an x64 module image is "bad-image".
    /// </summary>
    public static HeroesClientProcess FromImage(string imagePath)
    {
        string reason = ModuleImage.TryLoad(imagePath, out ModuleImage image);
        return reason == "ok" ? new("ok", image.Module, image, null) : Failed(reason);
    }

    internal static HeroesClientProcess Attach(
        Process process,
        Func<Process, MainModule> readModule
    )
    {
        if (process == null)
        {
            return Failed("no-process");
        }

        int processId;
        try
        {
            if (process.HasExited)
            {
                return Failed("no-process");
            }

            processId = process.Id;
        }
        catch (Exception)
        {
            return Failed("no-process");
        }

        IntPtr handle = NativeMethods.OpenProcess(
            NativeMethods.ProcessQueryInformation | NativeMethods.ProcessVmRead,
            false,
            processId
        );
        if (handle == IntPtr.Zero)
        {
            return Failed("open-failed");
        }

        var memory = new ProcessMemory(handle);
        MainModule main;
        try
        {
            main = readModule(process);
        }
        catch (Exception)
        {
            // The module list is not readable: a client that has only just started has none yet.
            // It is not an unsupported build, and the process is only gone if it exited.
            memory.Dispose();
            return Failed(HasExited(process) ? "no-process" : "no-module");
        }

        if (main.BaseAddress <= 0 || main.Size <= 0)
        {
            memory.Dispose();
            return Failed("no-module");
        }

        var module = new ClientModule(
            processId,
            main.BaseAddress,
            main.Size,
            main.FileVersion,
            StartTicks(process)
        );
        return new HeroesClientProcess("ok", module, memory, memory);
    }

    /// <summary>
    /// True while <paramref name="process"/> is this attached process: running, with the same pid
    /// and start time.
    /// </summary>
    internal bool IsProcess(Process process)
    {
        if (!Ok || owned == null || process == null)
        {
            return false;
        }

        try
        {
            return !process.HasExited
                && process.Id == Module.ProcessId
                && StartTicks(process) == Module.StartedAt;
        }
        catch (Exception)
        {
            return false;
        }
    }

    /// <summary>The process start time in UTC ticks, or 0 when Windows does not give it.</summary>
    internal static long StartTicks(Process process)
    {
        try
        {
            return process?.StartTime.ToUniversalTime().Ticks ?? 0;
        }
        catch (Exception)
        {
            return 0;
        }
    }

    /// <summary>Closes the process handle. A client from <see cref="FromMemory"/> has none.</summary>
    public void Dispose() => owned?.Dispose();

    private static HeroesClientProcess Failed(string reason) => new(reason, default, null, null);

    private static bool HasExited(Process process)
    {
        try
        {
            return process.HasExited;
        }
        catch (Exception)
        {
            return true;
        }
    }

    private static MainModule ReadMainModule(Process process)
    {
        ProcessModule main = process.MainModule;
        if (main == null)
        {
            return default;
        }

        return new MainModule(main.BaseAddress.ToInt64(), main.ModuleMemorySize, FileVersion(main));
    }

    /// <summary>The version is optional: a file version that cannot be read is null.</summary>
    private static string FileVersion(ProcessModule main)
    {
        try
        {
            return main.FileVersionInfo.FileVersion;
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>What <see cref="Attach(Process)"/> needs from the main module.</summary>
    internal readonly record struct MainModule(long BaseAddress, long Size, string FileVersion);
}

/// <summary>A live process's memory through its read-only handle.</summary>
internal sealed class ProcessMemory : IProcessMemory, IDisposable
{
    private IntPtr handle;

    public ProcessMemory(IntPtr handle) => this.handle = handle;

    public bool TryRead(long address, Span<byte> buffer)
    {
        return handle != IntPtr.Zero
            && address > 0
            && !buffer.IsEmpty
            && NativeMethods.ReadProcessMemory(
                handle,
                (IntPtr)address,
                ref MemoryMarshal.GetReference(buffer),
                buffer.Length,
                out nint read
            )
            && read == buffer.Length;
    }

    public void Dispose()
    {
        if (handle != IntPtr.Zero)
        {
            NativeMethods.CloseHandle(handle);
            handle = IntPtr.Zero;
        }
    }
}

/// <summary>
/// The attachment a reader keeps for <c>Read(Process)</c>: reused while the process (pid and
/// start time) is the same, attached again for a new process, and retried after a failure.
/// </summary>
internal sealed class ProcessAttachment : IDisposable
{
    private HeroesClientProcess client;

    public HeroesClientProcess For(Process process)
    {
        if (client != null && client.IsProcess(process))
        {
            return client;
        }

        client?.Dispose();
        client = HeroesClientProcess.Attach(process);
        return client;
    }

    public void Dispose()
    {
        client?.Dispose();
        client = null;
    }
}
