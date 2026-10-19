using System.Buffers.Binary;
using RaidCrawler.Core.Structures;
using Xunit;

namespace RaidCrawler.Tests;

public sealed class RemoteHookIntegritySnapshotTests
{
    private sealed class MemoryView
    {
        public byte[] Code = new byte[880];
        public byte[] Entry = new byte[4];

        public MemoryView()
        {
            BinaryPrimitives.WriteUInt32LittleEndian(Entry,
                RemoteViolet400HookPayload.EntryBranch);
            RemoteViolet400HookPayload.CodeSpan.CopyTo(Code);
            BinaryPrimitives.WriteUInt32LittleEndian(Code.AsSpan(0x90), 2);
            "RHS2RaidPoin"u8.CopyTo(Code.AsSpan(0x94));
            BinaryPrimitives.WriteUInt32LittleEndian(Code.AsSpan(0xA0), 15);
            BinaryPrimitives.WriteUInt32LittleEndian(Code.AsSpan(0xA4), 5);
            BinaryPrimitives.WriteUInt32LittleEndian(Code.AsSpan(0xAC), 5);
        }
        public Task<byte[]> Read(ulong rva, int count, CancellationToken ct)
        {
            if (rva == 0xE32984)
                return Task.FromResult(Entry.ToArray());
            int offset = checked((int)(rva - 0x343FC90));
            return Task.FromResult(Code.AsSpan(offset,count).ToArray());
        }
    }

    [Fact]
    public async Task SnapshotReportsIntegrityButNoExecutionEvidence()
    {
        var simulated = new MemoryView();
        var result = await RemoteHookIntegritySnapshot.CaptureAsync(
            simulated.Read, null);
        Assert.Equal("EXPECTED_BRANCH", result.EntryState);
        Assert.Equal("V2_CODE_AND_HEADER_MATCH", result.ReaderState);
        Assert.True(result.StableEpoch);
        Assert.True(result.Armed);
        Assert.Equal(2u, result.Epoch);
        Assert.Equal(1, result.Rows);
        Assert.Equal(1, result.Subject5);
        Assert.Equal(0, result.Subject0);
        Assert.Equal("NOT_OBSERVED", result.HookHitEvidence);
        Assert.Equal("NOT_OBSERVED", result.FilteringEvidence);
    }

    [Fact]
    public async Task DriftReportsMismatchAndCannotClaimHook()
    {
        var simulated = new MemoryView();
        simulated.Code[0x4c] ^= 1;
        var result = await RemoteHookIntegritySnapshot.CaptureAsync(
            simulated.Read, "DEADBEEF");
        Assert.Equal("UNKNOWN_OR_MODIFIED", result.ReaderState);
        Assert.Equal("HASH_MISMATCH", result.TemplateComparison);
        Assert.Equal("NOT_OBSERVED", result.ReceiverEvidence);
    }

    [Fact]
    public async Task OriginalEntryIsNotAHit()
    {
        var simulated = new MemoryView();
        BinaryPrimitives.WriteUInt32LittleEndian(simulated.Entry,
            RemoteViolet400HookPayload.OriginalInstruction);
        var result = await RemoteHookIntegritySnapshot.CaptureAsync(
            simulated.Read, null);
        Assert.Equal("ORIGINAL", result.EntryState);
        Assert.Equal("NOT_OBSERVED", result.HookHitEvidence);
    }

    [Fact]
    public async Task ChangedEpochIsReportedAsUnstable()
    {
        var simulated = new MemoryView();
        int readCount = 0;
        async Task<byte[]> Read(ulong rva, int n, CancellationToken ct)
        {
            byte[] data = await simulated.Read(rva, n, ct);
            if (rva == 0x343FC90 + 0x90 && ++readCount == 2)
                BinaryPrimitives.WriteUInt32LittleEndian(data, 4);
            return data;
        }
        var result = await RemoteHookIntegritySnapshot.CaptureAsync(Read,null);
        Assert.False(result.StableEpoch);
        Assert.False(result.Armed);
    }
}
