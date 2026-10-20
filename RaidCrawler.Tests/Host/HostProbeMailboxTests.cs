using System.Buffers.Binary;
using RaidCrawler.Core.Host;
using Xunit;

namespace RaidCrawler.Tests.Host;

public class HostProbeMailboxTests
{
    private static byte[] MakeHeader(uint enabled = 0, uint status = 1,
        uint count = 2, uint generation = 4)
    {
        var buf = new byte[HostProbeMailbox.HeaderLength];
        "RCHMB01\0"u8.CopyTo(buf);
        BinaryPrimitives.WriteUInt32LittleEndian(buf.AsSpan(8), HostProbeMailbox.ProtocolVersion);
        BinaryPrimitives.WriteUInt32LittleEndian(buf.AsSpan(12), HostProbeMailbox.MailboxLength);
        BinaryPrimitives.WriteUInt32LittleEndian(buf.AsSpan(0x10), enabled);
        BinaryPrimitives.WriteUInt32LittleEndian(buf.AsSpan(0x14), status);
        BinaryPrimitives.WriteUInt32LittleEndian(buf.AsSpan(0x18), count);
        BinaryPrimitives.WriteUInt32LittleEndian(buf.AsSpan(0x1C), 1);
        BinaryPrimitives.WriteUInt32LittleEndian(buf.AsSpan(0x20), generation);
        BinaryPrimitives.WriteUInt64LittleEndian(buf.AsSpan(0x28), 12345UL);
        return buf;
    }

    [Fact]
    public void HeaderHasStableOffsetsAndIsParsedCorrectly()
    {
        Assert.Equal(0x40, HostProbeMailbox.HeaderLength);
        Assert.Equal(0x600, HostProbeMailbox.MailboxLength);
        Assert.Equal(0x310, HostProbeMailbox.BootstrapOffset);
        var h = HostProbeMailbox.ParseHeader(MakeHeader(1, 1, 12, 24));
        Assert.True(h.Enabled);
        Assert.Equal(12u, h.CaptureCount);
        Assert.Equal(24u, h.Generation);
        Assert.Equal(12345UL, h.Tick);
        Assert.Equal(1u, h.DroppedCount);
    }

    [Fact]
    public void RejectsWrongMagicSizeVersionAndState()
    {
        byte[] b = MakeHeader();
        b[0] ^= 1;
        Assert.Throws<InvalidDataException>(() => HostProbeMailbox.ParseHeader(b));
        b = MakeHeader();
        BinaryPrimitives.WriteUInt32LittleEndian(b.AsSpan(8), 2);
        Assert.Throws<InvalidDataException>(() => HostProbeMailbox.ParseHeader(b));
        b = MakeHeader();
        BinaryPrimitives.WriteUInt32LittleEndian(b.AsSpan(12), 0x500);
        Assert.Throws<InvalidDataException>(() => HostProbeMailbox.ParseHeader(b));
        Assert.Throws<InvalidDataException>(() => HostProbeMailbox.ParseHeader(MakeHeader(2)));
        Assert.Throws<InvalidDataException>(() => HostProbeMailbox.ParseHeader(MakeHeader(status: 3)));
        Assert.Throws<InvalidDataException>(() => HostProbeMailbox.ParseHeader(new byte[63]));
    }

    [Fact]
    public void ParsesRuntimeHexAddressAndRejectsUnalignedOrTooSmall()
    {
        Assert.Equal(0x12340000UL, HostProbeMailbox.ParseAddress(" 0x12340000 "));
        Assert.Equal(0x12340000UL, HostProbeMailbox.ParseAddress("12340000"));
        Assert.Throws<FormatException>(() => HostProbeMailbox.ParseAddress("0x12340003"));
        Assert.Throws<FormatException>(() => HostProbeMailbox.ParseAddress("0x20"));
        Assert.Throws<FormatException>(() => HostProbeMailbox.ParseAddress("placeholder"));
    }

    [Fact]
    public void CompareUsesOnlyTheTwoNativeRewardArrays()
    {
        byte[] bytes = new byte[HostProbeMailbox.RegionLength * 2];
        new RewardRow(1128, 1, 0, 0).Write(bytes.AsSpan(0, 16));
        Array.Copy(bytes, 0, bytes, HostProbeMailbox.RegionLength, HostProbeMailbox.RegionLength);
        var cap = HostProbeMailbox.ParseCapture(bytes);
        Assert.True(cap.Compare().Matches);
        cap.BootstrapRows[0] = 5;
        var mismatch = cap.Compare();
        Assert.False(mismatch.Matches);
        Assert.Equal(0, mismatch.FirstMismatchRow);
        Assert.Equal("ItemId", mismatch.FirstMismatchField);
    }

    [Fact]
    public void RejectCaptureWithWrongSize()
    {
        Assert.Throws<InvalidDataException>(() =>
            HostProbeMailbox.ParseCapture(new byte[HostProbeMailbox.RegionLength * 2 - 1]));
    }
}
