using System.Buffers.Binary;
using System.Text;
using RaidCrawler.Core.Host;
using Xunit;

namespace RaidCrawler.Tests.Host;

/// <summary>
/// Entirely synthetic deterministic bytes; these tests verify safe candidate
/// discovery and never claim a real active-room Switch pointer.
/// </summary>
public sealed class HostRaidPointAutoLocatorTests
{
    private static byte[] RetailLikeRaidPoint()
    {
        var data = new byte[RaidPointLayout.RaidPointLength];
        Encoding.ASCII.GetBytes("RaidPoint_13_1_1").CopyTo(data, 0);
        data[0x18] = 0x40;
        BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(0x20, 4), 5);
        BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(0x2C, 4), 75);
        BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(0x3B8, 4), 5);
        BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(0x3BC, 4), 713);
        BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(0x3C8, 4), 75);
        new RewardRow(1128, 1, 0, 0).Write(data.AsSpan(0xE0, 16));
        return data;
    }

    [Fact]
    public void StrictRetailSignatureAcceptsAlignedPokeLdnDonorShape()
    {
        var data = RetailLikeRaidPoint();
        Assert.True(HostRaidPointAutoLocator.LooksLikeRetailRaidPoint(
            data, out string name, out string reason), reason);
        Assert.Equal("RaidPoint_13_1_1", name);
        Assert.Equal("STRUCTURE_AND_REPEATED_FIELDS_MATCH", reason);
    }

    [Theory]
    [InlineData(0x20)]
    [InlineData(0x2C)]
    [InlineData(0x3B8)]
    [InlineData(0x3BC)]
    [InlineData(0x3C8)]
    public void CorruptScalarFieldsRejectCandidate(int offset)
    {
        var data = RetailLikeRaidPoint();
        BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(offset, 4), 0);
        Assert.False(HostRaidPointAutoLocator.LooksLikeRetailRaidPoint(
            data, out _, out _));
    }

    [Fact]
    public void SearchIgnoresUnalignedTextCopiesAndFindsAlignedInstances()
    {
        var block = new byte[0x4000];
        var text = Encoding.ASCII.GetBytes("RaidPoint_13_1_1");
        text.CopyTo(block, 0x103);
        text.CopyTo(block, 0x900);
        Assert.Equal([0x900], HostRaidPointAutoLocator.FindAlignedRaidPointPrefixes(block));
    }

    [Fact]
    public void ThreeVTableEvidenceIsSeparateFromActiveOwnerProof()
    {
        const ulong mainBase = 0x710000000UL;
        var header = new byte[0x30];
        BinaryPrimitives.WriteUInt64LittleEndian(header.AsSpan(0, 8),
            mainBase + SysbotOnlyRaidPointInspector.SerializerVTableRva);
        BinaryPrimitives.WriteUInt64LittleEndian(header.AsSpan(0x20, 8),
            mainBase + SysbotOnlyRaidPointInspector.SecondaryVTableRva);
        BinaryPrimitives.WriteUInt64LittleEndian(header.AsSpan(0x28, 8),
            mainBase + SysbotOnlyRaidPointInspector.TertiaryVTableRva);
        Assert.Equal(HostOwnerVtableEvidence.DestructorVtableTriple,
            HostRaidPointAutoLocator.ClassifyOwnerVtable(header, mainBase, 0));

        // A non-destructor vptr pointing to the same serializer code is a
        // useful hint, but never an authenticated current-room owner.
        BinaryPrimitives.WriteUInt64LittleEndian(header.AsSpan(0, 8),
            mainBase + SysbotOnlyRaidPointInspector.SerializerVTableRva + 0x800);
        Assert.Equal(HostOwnerVtableEvidence.SerializerSlotPointsToE327FC,
            HostRaidPointAutoLocator.ClassifyOwnerVtable(header, mainBase,
                mainBase + SysbotOnlyRaidPointInspector.SerializerRva));
        Assert.Equal(HostOwnerVtableEvidence.MainStaticPointerOnly,
            HostRaidPointAutoLocator.ClassifyOwnerVtable(header, mainBase, 0));
    }

    [Fact]
    public void SourceOnlyUncertainRowDoesNotInvalidateStructuralDetection()
    {
        var data = RetailLikeRaidPoint();
        new RewardRow(0, 0, 0, 5).Write(data.AsSpan(0xE0 + 16, 16));
        Assert.True(HostRaidPointAutoLocator.LooksLikeRetailRaidPoint(
            data, out _, out _));
        // It still MUST fail the separately guarded append eligibility.
        Assert.False(RewardAppendPlanner.Plan(data, [new RewardRow(15, 500, 0, 0)],
            id => id == 15, 0).Accepted);
    }
}
