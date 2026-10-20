using System.Buffers.Binary;
using RaidCrawler.Core.Host;
using Xunit;

namespace RaidCrawler.Tests.Host;

public sealed class HostBatchMailboxTests
{
    [Fact]
    public void V3BatchMapDoesNotOverlapV2CaptureOrResults()
    {
        Assert.Equal(3, HostProbeMailbox.ProtocolVersion);
        Assert.Equal(0x800, HostProbeMailbox.MailboxLength);
        Assert.Equal(0x600, HostProbeMailbox.BatchNonceOffset);
        Assert.Equal(0x608, HostProbeMailbox.BatchCountOffset);
        Assert.Equal(0x60C, HostProbeMailbox.BatchChecksumOffset);
        Assert.Equal(0x610, HostProbeMailbox.BatchAppliedRowsOffset);
        Assert.Equal(0x618, HostProbeMailbox.BatchResultNonceOffset);
        Assert.Equal(0x620, HostProbeMailbox.BatchRowsOffset);
        Assert.Equal(448, HostProbeMailbox.BatchTableBytes);
        Assert.Equal(0x7E0, HostProbeMailbox.BatchRowsOffset + HostProbeMailbox.BatchTableBytes);
        Assert.Equal(0x600, HostProbeMailbox.RequestResultOffset + 12);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(25)]
    [InlineData(28)]
    public void ExactWireRowsAndChecksumRoundTrip(int count)
    {
        RewardRow[] rows = Enumerable.Range(0, count)
            .Select(i => new RewardRow((uint)(100 + i), (uint)(200 + i), 0, 5)).ToArray();
        byte[] table = HostBatchMailboxCodec.SerializeRows(rows);
        Assert.Equal(448, table.Length);
        for (int i = 0; i < count; i++)
            Assert.Equal(rows[i], RewardRow.Read(table.AsSpan(i * 16, 16)));
        Assert.All(table.AsSpan(count * 16).ToArray(), b => Assert.Equal(0, b));
        ulong nonce = 0x123456789ABCDEF0;
        byte[] header = HostBatchMailboxCodec.SerializeHeader(nonce, (uint)count, table);
        Assert.Equal(nonce, BinaryPrimitives.ReadUInt64LittleEndian(header));
        Assert.Equal((uint)count, BinaryPrimitives.ReadUInt32LittleEndian(header.AsSpan(8, 4)));
        Assert.Equal(HostBatchMailboxCodec.Checksum(nonce, (uint)count, table),
            BinaryPrimitives.ReadUInt32LittleEndian(header.AsSpan(12, 4)));
        table[0] ^= 1;
        Assert.NotEqual(BinaryPrimitives.ReadUInt32LittleEndian(header.AsSpan(12, 4)),
            HostBatchMailboxCodec.Checksum(nonce, (uint)count, table));
    }

    [Fact]
    public void RejectsInvalidCountsDuplicatedRowsAndQuantities()
    {
        Assert.Throws<ArgumentException>(() => HostBatchMailboxCodec.SerializeRows([]));
        Assert.Throws<ArgumentException>(() => HostBatchMailboxCodec.SerializeRows(
            Enumerable.Repeat(new RewardRow(15, 1, 0, 5), 29).ToArray()));
        Assert.Throws<ArgumentException>(() => HostBatchMailboxCodec.SerializeRows(
            [new(15, 1, 0, 5), new(15, 1, 0, 5)]));
        Assert.Throws<ArgumentException>(() => HostBatchMailboxCodec.SerializeRows(
            [new(15, 1000, 0, 5)]));
        Assert.Throws<ArgumentException>(() => HostBatchMailboxCodec.SerializeRows(
            [new(15, 1, 1, 5)]));
        Assert.Throws<ArgumentException>(() => HostBatchMailboxCodec.SerializeRows(
            [new(15, 1, 0, 0)]));
    }

    [Fact]
    public void BatchInspectionConfirmsOnlyAppendedRowsAndCorrelatesNonce()
    {
        var source = new byte[720];
        new RewardRow(1127, 1, 1, 0).Write(source.AsSpan(0, 16));
        new RewardRow(1862, 12, 0, 5).Write(source.AsSpan(16, 16));
        var outgoing = source.ToArray();
        RewardRow[] extras = [new(15, 2, 0, 5), new(50, 1, 0, 5)];
        extras[0].Write(outgoing.AsSpan(32, 16));
        extras[1].Write(outgoing.AsSpan(48, 16));
        var receipt = new HostBatchRequestReceipt(456, extras);
        var snapshot = new HostProbeCapture(source, outgoing);
        var native = new HostProbeResult(0, 0, 0, 0, 0, 40, 2, 2);
        var report = new HostBatchResult(456, 2, 3, 2, 456);
        Assert.True(HostBatchAppendInspector.Inspect(snapshot, native, report, receipt).Valid);
        Assert.False(HostBatchAppendInspector.Inspect(snapshot, native, report with
        { ResultNonce = 999 }, receipt).Valid);
        outgoing[4]++;
        Assert.False(HostBatchAppendInspector.Inspect(snapshot, native, report, receipt).Valid);
    }

    [Fact]
    public void NativeBatchReportParserMatchesV3Offsets()
    {
        byte[] b = new byte[32];
        BinaryPrimitives.WriteUInt64LittleEndian(b.AsSpan(0), 0xDAAB);
        BinaryPrimitives.WriteUInt32LittleEndian(b.AsSpan(8), 25);
        BinaryPrimitives.WriteUInt32LittleEndian(b.AsSpan(12), 0xCAFEBABE);
        BinaryPrimitives.WriteUInt32LittleEndian(b.AsSpan(16), 25);
        BinaryPrimitives.WriteUInt64LittleEndian(b.AsSpan(24), 0xDAAB);
        var parsed = HostBatchMailboxCodec.ParseResult(b);
        Assert.Equal(25u, parsed.RequestedRows);
        Assert.Equal(25u, parsed.AppliedRows);
        Assert.Equal(0xDAABUL, parsed.ResultNonce);
    }
}
