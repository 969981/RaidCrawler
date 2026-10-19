using RaidCrawler.Core.Structures;
using Xunit;

namespace RaidCrawler.Tests;

public sealed class RemoteCodeGapWriteProbeTests
{
    private sealed class Memory
    {
        public readonly byte[] Gap = new byte[RemoteCodeGapWriteProbe.GapLength];
        public int Writes;
        public bool RejectWrite;
        public bool RejectRestore;
        public Task<byte[]> Read(ulong offset, int length, CancellationToken _)
        {
            int i = checked((int)(offset - RemoteCodeGapWriteProbe.GapRva));
            return Task.FromResult(Gap.AsSpan(i, length).ToArray());
        }
        public Task Write(byte[] payload, ulong offset, CancellationToken _)
        {
            ++Writes;
            if (RejectWrite && Writes == 1) throw new IOException("write rejected");
            if (RejectRestore && Writes == 2) throw new IOException("restore rejected");
            int i = checked((int)(offset - RemoteCodeGapWriteProbe.GapRva));
            payload.CopyTo(Gap, i);
            return Task.CompletedTask;
        }
    }

    [Fact]
    public async Task SuccessWritesAndRestoresWithoutTouchingHookSite()
    {
        var m = new Memory();
        var result = await RemoteCodeGapWriteProbe.RunAsync(m.Read, m.Write, CancellationToken.None);
        Assert.True(result.Accepted);
        Assert.True(result.WriteVerified);
        Assert.True(result.RestoreVerified);
        Assert.Equal(2, m.Writes);
        Assert.All(m.Gap, value => Assert.Equal(0, value));
    }

    [Fact]
    public async Task NonemptyGapIsRejectedWithoutAnyWrites()
    {
        var m = new Memory();
        m.Gap[12] = 0x55;
        var result = await RemoteCodeGapWriteProbe.RunAsync(m.Read, m.Write, CancellationToken.None);
        Assert.False(result.Accepted);
        Assert.Equal(0, m.Writes);
    }

    [Fact]
    public async Task FailedWriteStillAttemptsRollback()
    {
        var m = new Memory { RejectWrite = true };
        var result = await RemoteCodeGapWriteProbe.RunAsync(m.Read, m.Write, CancellationToken.None);
        Assert.True(result.Accepted);
        Assert.False(result.WriteVerified);
        Assert.True(result.RestoreVerified);
        Assert.Equal(2, m.Writes);
    }

    [Fact]
    public async Task UnconfirmedRestoreIsReportedAsFailure()
    {
        var m = new Memory { RejectRestore = true };
        var result = await RemoteCodeGapWriteProbe.RunAsync(m.Read, m.Write, CancellationToken.None);
        Assert.True(result.WriteVerified);
        Assert.False(result.RestoreVerified);
    }
}
