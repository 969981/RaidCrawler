using System.Buffers.Binary;
using RaidCrawler.Core.Structures;
using Xunit;

namespace RaidCrawler.Tests;

public sealed class NativeRaidCodeFingerprintTests
{
    [Fact]
    public void VerifiedViolet400HasDistinctReadOnlyInstructionAnchors()
    {
        var anchors = NativeRaidCodeFingerprint.Violet400;
        Assert.Equal(8, anchors.Count);
        Assert.Equal(8, anchors.Select(a=>a.Rva).Distinct().Count());
        foreach(var anchor in anchors)
        {
            Span<byte> bytes = stackalloc byte[4];
            BinaryPrimitives.WriteUInt32LittleEndian(bytes, anchor.AArch64Opcode);
            Assert.True(NativeRaidCodeFingerprint.Check(anchor, bytes).Matches);
            bytes[0] ^= 1;
            Assert.False(NativeRaidCodeFingerprint.Check(anchor, bytes).Matches);
            Assert.Null(NativeRaidCodeFingerprint.Check(anchor, bytes[..3]).Actual);
        }
    }
}
