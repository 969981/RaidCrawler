using System.Buffers.Binary;
using System.Text;
using RaidCrawler.Core.Host;
using Xunit;

namespace RaidCrawler.Tests.Host;

public sealed class SysbotOnlyRaidPointInspectorTests
{
    private static readonly (int Offset, uint Word)[] Fingerprint =
    [
        (0x000, 0xA9BB7BFD), (0x018, 0xAA0003F4),
        (0x02C, 0x52815416), (0x040, 0xAA0003F7),
        (0x048, 0x52802B18), (0x164, 0x911B62E0),
        (0x168, 0xA945AA8B), (0x16C, 0x9101E281),
        (0x170, 0x52807902), (0x174, 0xF9035EEB),
        (0x184, 0x94982624), (0x1B4, 0x141D8073)
    ];

    [Fact]
    public void OwnerObjectIsNotRaidPointStart()
    {
        Assert.Equal(0x710000058UL,
            SysbotOnlyRaidPointInspector.CalculateRaidPointAddress(0x710000000UL, true));
        Assert.Equal(0x710000000UL,
            SysbotOnlyRaidPointInspector.CalculateRaidPointAddress(0x710000000UL, false));
        Assert.Equal(0x13CUL, SysbotOnlyRaidPointInspector.FirstRewardFromOwner);
    }

    [Fact]
    public void RejectsNonAlignedAndImpossibleAddresses()
    {
        Assert.Throws<FormatException>(() =>
            SysbotOnlyRaidPointInspector.CalculateRaidPointAddress(0x710000003UL, true));
        Assert.Throws<FormatException>(() =>
            SysbotOnlyRaidPointInspector.CalculateRaidPointAddress(0x10UL, false));
        Assert.Throws<OverflowException>(() =>
            SysbotOnlyRaidPointInspector.CalculateRaidPointAddress(ulong.MaxValue - 7, true));
    }

    [Fact]
    public void ChecksTwelveOriginalInstructionWords()
    {
        byte[] code = new byte[SysbotOnlyRaidPointInspector.SerializerFingerprintLength];
        foreach (var (offset, word) in Fingerprint)
            BinaryPrimitives.WriteUInt32LittleEndian(code.AsSpan(offset), word);
        SysbotOnlyRaidPointInspector.ValidateSerializerFingerprint(code);
        code[0x184] ^= 1;
        Assert.Throws<InvalidDataException>(() =>
            SysbotOnlyRaidPointInspector.ValidateSerializerFingerprint(code));
        Assert.Throws<InvalidDataException>(() =>
            SysbotOnlyRaidPointInspector.ValidateSerializerFingerprint(new byte[16]));
    }

    [Fact]
    public void OnlyAcceptsInlineRaidPointPrefix()
    {
        byte[] data = new byte[RaidPointLayout.RaidPointLength];
        Encoding.ASCII.GetBytes("RaidPoint_13_1_1").CopyTo(data, 0);
        Assert.Equal("RaidPoint_13_1_1", SysbotOnlyRaidPointInspector.GetRaidPointName(data));
        data[0] = (byte)'X';
        Assert.Throws<InvalidDataException>(() =>
            SysbotOnlyRaidPointInspector.GetRaidPointName(data));
        Assert.Throws<InvalidDataException>(() =>
            SysbotOnlyRaidPointInspector.GetRaidPointName(new byte[12]));
    }
}
