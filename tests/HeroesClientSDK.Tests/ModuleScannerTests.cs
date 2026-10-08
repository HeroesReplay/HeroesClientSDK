using System;
using System.Collections.Generic;
using System.Text;
using Xunit;

namespace HeroesClientSDK.Tests;

[Trait(TestCategories.Category, TestCategories.Unit)]
public class ModuleScannerTests
{
    private const long Base = 0x140000000L;
    private const int SectionRva = 0x1000;

    [Fact]
    public void WalkWhole_ReadsTheSectionInOneReadWhenItCan()
    {
        var memory = new SectionMemory(ModuleScanner.Chunk + 0x100, wholeReads: true);
        var visits = new List<(int Length, long Rva)>();

        ModuleScanner.WalkWhole(
            memory,
            Base,
            memory.Section,
            MatchClockPattern.MulssEnd,
            (slice, rva) => visits.Add((slice.Length, rva))
        );

        Assert.Equal(new[] { (ModuleScanner.Chunk + 0x100, (long)SectionRva) }, visits);
    }

    [Fact]
    public void WalkWhole_FallsBackToOverlappingChunks_AndSeesAPatternAcrossTheBoundary()
    {
        // A clock pattern that starts 4 bytes before the 1 MB chunk boundary.
        var memory = new SectionMemory(ModuleScanner.Chunk + 0x100, wholeReads: false);
        int at = ModuleScanner.Chunk - 4;
        memory.Write(at, ClockPattern());
        var sites = new List<MatchClockPattern.Site>();

        ModuleScanner.WalkWhole(
            memory,
            Base,
            memory.Section,
            MatchClockPattern.MulssEnd,
            (slice, rva) => sites.AddRange(MatchClockPattern.Find(slice, rva))
        );

        Assert.Single(sites);
        Assert.Equal(SectionRva + at + MatchClockPattern.MovdEnd + 0x100, sites[0].TickRva);
    }

    [Fact]
    public void TrySections_KeepsOnlySectionsInsideTheModule()
    {
        var memory = new SectionMemory(0x2000, wholeReads: true);

        Assert.True(ModuleScanner.TrySections(memory, Base, 0x10000, out var inside));
        Assert.False(ModuleScanner.TrySections(memory, Base, 0x2000, out var outside));
        Assert.Single(inside);
        Assert.Equal(".text", inside[0].Name);
        Assert.True(inside[0].Executable);
        Assert.Empty(outside);
    }

    [Fact]
    public void TrySections_UnreadableHeaders_IsFalse()
    {
        Assert.False(ModuleScanner.TrySections(null, Base, 0x10000, out _));
        Assert.False(ModuleScanner.TrySections(TestMemory.Unreadable, Base, 0x10000, out _));
    }

    private static byte[] ClockPattern()
    {
        byte[] bytes = new byte[MatchClockPattern.MulssEnd];
        bytes[0] = 0x84;
        bytes[1] = 0xC0;
        bytes[2] = 0x74;
        bytes[3] = 0x0A;
        bytes[4] = 0x66;
        bytes[5] = 0x0F;
        bytes[6] = 0x6E;
        bytes[7] = 0x05;
        BitConverter.GetBytes(0x100).CopyTo(bytes, MatchClockPattern.TickDisplacement);
        bytes[12] = 0x0F;
        bytes[13] = 0x5B;
        bytes[14] = 0xC0;
        bytes[15] = 0xF3;
        bytes[16] = 0x0F;
        bytes[17] = 0x59;
        bytes[18] = 0x05;
        BitConverter.GetBytes(0x200).CopyTo(bytes, MatchClockPattern.SpeedDisplacement);
        return bytes;
    }

    /// <summary>PE headers and one code section. Reads longer than a chunk can be refused.</summary>
    private sealed class SectionMemory : IProcessMemory
    {
        private readonly byte[] headers = new byte[ModuleScanner.HeaderSize];
        private readonly byte[] code;
        private readonly bool wholeReads;

        public SectionMemory(int size, bool wholeReads)
        {
            code = new byte[size];
            this.wholeReads = wholeReads;
            headers[0] = (byte)'M';
            headers[1] = (byte)'Z';
            BitConverter.GetBytes(0x80).CopyTo(headers, 0x3C);
            headers[0x80] = (byte)'P';
            headers[0x81] = (byte)'E';
            BitConverter.GetBytes((ushort)1).CopyTo(headers, 0x80 + 6);
            int table = 0x80 + 24;
            Encoding.ASCII.GetBytes(".text").CopyTo(headers, table);
            BitConverter.GetBytes(size).CopyTo(headers, table + 8);
            BitConverter.GetBytes((uint)SectionRva).CopyTo(headers, table + 12);
            BitConverter.GetBytes(0x60000020u).CopyTo(headers, table + 36);
            Section = new ModuleSection(".text", SectionRva, size, 0x60000020);
        }

        public ModuleSection Section { get; }

        public void Write(int offset, byte[] bytes) => bytes.CopyTo(code, offset);

        public bool TryRead(long address, Span<byte> buffer)
        {
            if (address == Base && buffer.Length <= headers.Length)
            {
                headers.AsSpan(0, buffer.Length).CopyTo(buffer);
                return true;
            }

            long offset = address - Base - SectionRva;
            if (
                offset < 0
                || offset + buffer.Length > code.Length
                || (!wholeReads && buffer.Length > ModuleScanner.Chunk + 64)
            )
            {
                return false;
            }

            code.AsSpan((int)offset, buffer.Length).CopyTo(buffer);
            return true;
        }
    }
}
