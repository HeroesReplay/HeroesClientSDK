using System;

namespace HeroesClientSDK;

/// <summary>
/// Read-only access to one client process's memory.
/// <see cref="HeroesClientProcess.Attach(System.Diagnostics.Process)"/> reads a running client
/// with <c>ReadProcessMemory</c>. A test or an offline check hands its own implementation (a
/// fake, or bytes recorded from a client) to <see cref="HeroesClientProcess.FromMemory"/>. There
/// is no write.
/// </summary>
public interface IProcessMemory
{
    /// <summary>
    /// Fills <paramref name="buffer"/> with the bytes at <paramref name="address"/>. Returns false,
    /// never throws, when any of them cannot be read.
    /// </summary>
    bool TryRead(long address, Span<byte> buffer);
}
