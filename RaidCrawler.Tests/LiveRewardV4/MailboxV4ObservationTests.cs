using System.Buffers.Binary;
using RaidCrawler.Core.LiveRewardV4;
using Xunit;

namespace RaidCrawler.Tests.LiveRewardV4;

public sealed class MailboxV4ObservationTests
{
    [Fact]
    public async Task ReadsConsistentReadonlyCapture()
    {
        byte[] bytes = new byte[MailboxV4.MailboxLength];
        MailboxV4.MakeOfflineHeader(0, 4, 4).CopyTo(bytes, 0);
        Put32(bytes, 0x30, 1); // observer count
        Put32(bytes, 0x34, 1); // observer accepted config
        Put32(bytes, 0x38, 2); // stable even generation
        Put32(bytes, 0x7C0, 1); // capture serial
        Put32(bytes, 0x7C4, 6); // valid bootstrap / observed
        Put32(bytes, 0x7C8, 0xF1231234);
        Put32(bytes, 0x7CC, 0xF1231234);
        var reader = new MailboxV4ObservationReader(new MemoryReader(bytes));
        var value = await reader.ReadAsync(0x100000, CancellationToken.None);
        Assert.True(value.RewardHashesMatch);
        Assert.True(value.ConfigurationObserved);
        Assert.Equal(4UL, value.ObservedRevision);
        Assert.Equal(1u, value.CaptureSerial);
    }

    [Fact]
    public async Task DistinguishesNoConfigAndMismatchedRewards()
    {
        byte[] bytes = new byte[MailboxV4.MailboxLength];
        MailboxV4.MakeOfflineHeader().CopyTo(bytes, 0);
        Put32(bytes, 0x7C4, 4);
        Put32(bytes, 0x7C8, 1);
        Put32(bytes, 0x7CC, 2);
        var value = await new MailboxV4ObservationReader(new MemoryReader(bytes))
            .ReadAsync(0x100000, CancellationToken.None);
        Assert.False(value.RewardHashesMatch);
        Assert.False(value.ConfigurationObserved);
        Put32(bytes, 0x38, 3); // odd seqlock generation never stabilizes
        await Assert.ThrowsAsync<IOException>(() =>
            new MailboxV4ObservationReader(new MemoryReader(bytes))
                .ReadAsync(0x100000, CancellationToken.None));
    }

    private static void Put32(byte[] data, int offset, uint value) =>
        BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(offset, 4), value);

    private sealed class MemoryReader(byte[] bytes) : IMailboxV4Transport
    {
        public Task<byte[]> ReadAsync(ulong address, int size, CancellationToken token)
        {
            int start = checked((int)(address - 0x100000));
            return Task.FromResult(bytes.AsSpan(start, size).ToArray());
        }
        public Task WriteAsync(ulong address, byte[] value, CancellationToken token) =>
            throw new InvalidOperationException("Read-only observer must not write memory");
    }
}
