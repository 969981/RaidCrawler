using System.Buffers.Binary;
using RaidCrawler.Core.LiveRewardV4;
using Xunit;

namespace RaidCrawler.Tests.LiveRewardV4;

public sealed class MailboxV4Tests
{
    private const ulong Base = 0x100000;

    [Theory]
    [InlineData(1)]
    [InlineData(25)]
    [InlineData(45)]
    public void ExactWireLayoutAndDefaults(int n)
    {
        var rows = Enumerable.Range(1, n)
            .Select(i => RewardConfigV4.DefaultRow((uint)i)).ToArray();
        var config = RewardConfigV4.DefaultWithRows(rows);
        byte[] payload = MailboxV4.Serialize(config, 88);
        Assert.Equal(0x3C0, payload.Length);
        Assert.Equal(45 * 16, MailboxV4.RowTableLength);
        Assert.Equal(0x800, MailboxV4.MailboxLength);
        Assert.Equal(88UL, MailboxV4.ReadU64(payload, 0));
        Assert.Equal(1u, MailboxV4.ReadU32(payload, 8));
        Assert.Equal(1u, MailboxV4.ReadU32(payload, 0xC));
        Assert.Equal(2u, MailboxV4.ReadU32(payload, 0x10));
        Assert.Equal((uint)n, MailboxV4.ReadU32(payload, 0x14));
        Assert.Equal(999u, MailboxV4.ReadU32(payload, 0x24));
        Assert.True(payload.AsSpan(0x20 + n * 16).ToArray().All(b => b == 0));
        var decoded = MailboxV4.ParseSlot(payload);
        Assert.Equal(88UL, decoded.Revision);
        Assert.Equal(n, decoded.Config.Rows.Count);
        Assert.All(decoded.Config.Rows, row =>
        {
            Assert.Equal(999u, row.Quantity);
            Assert.Equal(5u, row.SubjectType);
        });
    }

    [Fact]
    public void GoldenVectorMatchesPortableNativeObserver()
    {
        // Must match switch/live-reward-v4/tests/abi_smoke.cpp exactly.
        byte[] slot = MailboxV4.Serialize(
            RewardConfigV4.DefaultWithRows([RewardConfigV4.DefaultRow(492)]), 1);
        Assert.Equal(0x3FD71947u, MailboxV4.ReadU32(slot, 0x18));
    }

    [Fact]
    public void CorruptPacketAndReservedFieldsFailClosed()
    {
        var profile = RewardConfigV4.DefaultWithRows([RewardConfigV4.DefaultRow(492)]);
        byte[] valid = MailboxV4.Serialize(profile, 1);
        byte[] corrupt = valid.ToArray();
        corrupt[0x20] ^= 0xFF;
        Assert.Throws<InvalidDataException>(() => MailboxV4.ParseSlot(corrupt));
        corrupt = valid.ToArray();
        corrupt[0x300] = 7;
        BinaryPrimitives.WriteUInt32LittleEndian(
            corrupt.AsSpan(0x18), MailboxV4.ComputeChecksum(corrupt));
        Assert.Throws<InvalidDataException>(() => MailboxV4.ParseSlot(corrupt));
    }

    [Fact]
    public void RejectUnsafeOrAmbiguousRows()
    {
        Assert.Throws<InvalidDataException>(() => MailboxV4.Serialize(
            RewardConfigV4.DefaultWithRows([]), 1));
        Assert.Throws<InvalidDataException>(() => MailboxV4.Serialize(
            RewardConfigV4.DefaultWithRows([new(492, 1000, 0, 5)]), 1));
        Assert.Throws<InvalidDataException>(() => MailboxV4.Serialize(
            RewardConfigV4.DefaultWithRows([new(492, 1, 0, 0)]), 1));
        Assert.Throws<InvalidDataException>(() => MailboxV4.Serialize(
            RewardConfigV4.DefaultWithRows([new(492, 1, 0, 5), new(492, 1, 0, 5)]), 1));
        Assert.Throws<InvalidDataException>(() => MailboxV4.Serialize(
            RewardConfigV4.DefaultWithRows(
                Enumerable.Range(1, 46).Select(i => RewardConfigV4.DefaultRow((uint)i)).ToArray()), 1));
        Assert.Throws<InvalidDataException>(() => MailboxV4.Serialize(
            new RewardConfigV4(true, (RewardModeV4)3,
                RewardApplyPolicyV4.Persistent, [RewardConfigV4.DefaultRow(492)]), 1));
    }

    [Fact]
    public async Task PublishSwitchesInactiveSlotWithoutGameDataWrites()
    {
        var fake = new FakeTransport();
        var client = new MailboxV4Client(fake);
        var a = RewardConfigV4.DefaultWithRows([RewardConfigV4.DefaultRow(492)]);
        var b = RewardConfigV4.DefaultWithRows([RewardConfigV4.DefaultRow(493)]);
        var resultA = await client.PublishAsync(Base, a, CancellationToken.None);
        var stateA = await client.ReadHeaderAsync(Base, CancellationToken.None);
        Assert.Equal(0u, stateA.ActiveSlot);
        Assert.Equal(resultA.Revision, stateA.PublishedRevision);
        Assert.Equal(0UL, stateA.ObservedRevision); // publishing is NOT Native observation
        Assert.Equal(492u, (await client.ReadPublishedAsync(Base, CancellationToken.None))!.Config.Rows[0].ItemId);
        var resultB = await client.PublishAsync(Base, b, CancellationToken.None);
        var stateB = await client.ReadHeaderAsync(Base, CancellationToken.None);
        Assert.Equal(1u, stateB.ActiveSlot);
        Assert.Equal(resultA.Revision + 1, resultB.Revision);
        Assert.Equal(493u, (await client.ReadPublishedAsync(Base, CancellationToken.None))!.Config.Rows[0].ItemId);
        Assert.Equal(0UL, stateB.ObservedRevision);
        Assert.All(fake.Writes, w =>
        {
            Assert.InRange(w.Address, Base + 0x18, Base + MailboxV4.MailboxLength - 1);
            Assert.True(w.Address + (ulong)w.Count <= Base + MailboxV4.MailboxLength);
        });
    }

    [Fact]
    public async Task ReadbackMismatchDoesNotPublishNewSlot()
    {
        var fake = new FakeTransport { CorruptSlotWrite = true };
        var client = new MailboxV4Client(fake);
        var config = RewardConfigV4.DefaultWithRows([RewardConfigV4.DefaultRow(492)]);
        await Assert.ThrowsAsync<IOException>(() => client.PublishAsync(Base, config, CancellationToken.None));
        var header = await client.ReadHeaderAsync(Base, CancellationToken.None);
        Assert.Equal(MailboxV4.NoActiveSlot, header.ActiveSlot);
        Assert.Equal(0UL, header.PublishedRevision);
    }

    [Fact]
    public async Task RejectsNonV4AndNonReadonlyNative()
    {
        var fake = new FakeTransport();
        fake.Bytes[8] = 3;
        await Assert.ThrowsAsync<InvalidDataException>(() =>
            new MailboxV4Client(fake).ReadHeaderAsync(Base, CancellationToken.None));
        fake.Bytes = new byte[MailboxV4.MailboxLength];
        MailboxV4.MakeOfflineHeader(capabilities: 3).CopyTo(fake.Bytes, 0);
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            new MailboxV4Client(fake).ReadHeaderAsync(Base, CancellationToken.None));
    }

    private sealed class FakeTransport : IMailboxV4Transport
    {
        public byte[] Bytes = new byte[MailboxV4.MailboxLength];
        public bool CorruptSlotWrite;
        public List<(ulong Address, int Count)> Writes { get; } = [];
        public FakeTransport() =>
            MailboxV4.MakeOfflineHeader().CopyTo(Bytes, 0);

        private int At(ulong address, int count)
        {
            if (address < Base || address - Base > (ulong)Bytes.Length ||
                (ulong)count > (ulong)Bytes.Length - (address - Base))
                throw new IOException("WRITE_OUTSIDE_MAILBOX");
            return (int)(address - Base);
        }
        public Task<byte[]> ReadAsync(ulong address, int length, CancellationToken token) =>
            Task.FromResult(Bytes.AsSpan(At(address, length), length).ToArray());
        public Task WriteAsync(ulong address, byte[] value, CancellationToken token)
        {
            int index = At(address, value.Length);
            Writes.Add((address, value.Length));
            value.CopyTo(Bytes, index);
            if (CorruptSlotWrite && value.Length == MailboxV4.SlotLength)
                Bytes[index + 0x20] ^= 1;
            return Task.CompletedTask;
        }
    }
}
