using System.Buffers.Binary;
using RaidCrawler.Core.Structures;
using Xunit;

namespace RaidCrawler.Tests;

public sealed class NativeRaidCodeFingerprintTests
{
    [Fact]
    public void VerifiedViolet400HasDistinctReadOnlyInstructionAnchors()
    {
        Assert.Equal((ulong)0x3440000,
            NativeRaidCodeFingerprint.EndOfTextPaddingRva +
            (ulong)NativeRaidCodeFingerprint.EndOfTextPaddingLength);
        Assert.Equal(0x370, NativeRaidCodeFingerprint.EndOfTextPaddingLength);
        Assert.Equal((ulong)0xE32984, NativeRaidCodeFingerprint.SerializerHookRva);
        Assert.Equal((ulong)0x343FC8C, NativeRaidCodeFingerprint.LastPltInstructionRva);
        Assert.Equal(0xD61F0220U, NativeRaidCodeFingerprint.LastPltInstructionOpcode);
        Assert.True(NativeRaidCodeFingerprint.CandidateSharesLastPltPage);

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
