using System;

namespace HeroesClientSDK.Tests;

/// <summary>A clock a test moves by hand: <c>Options(() => now)</c> reads the test's own variable.</summary>
internal sealed class TestTime : TimeProvider
{
    private readonly Func<DateTimeOffset> now;

    public TestTime(Func<DateTimeOffset> now) => this.now = now;

    public override DateTimeOffset GetUtcNow() => now();

    public static HeroesClientOptions Options(Func<DateTimeOffset> now) =>
        new() { TimeProvider = new TestTime(now) };
}

/// <summary>Memory served by a function, such as one where every read fails.</summary>
internal sealed class TestMemory : IProcessMemory
{
    private readonly Func<long, byte[], bool> read;

    public TestMemory(Func<long, byte[], bool> read) => this.read = read;

    public static TestMemory Unreadable { get; } = new((_, _) => false);

    public bool TryRead(long address, Span<byte> buffer)
    {
        byte[] bytes = new byte[buffer.Length];
        if (!read(address, bytes))
        {
            return false;
        }

        bytes.CopyTo(buffer);
        return true;
    }
}
