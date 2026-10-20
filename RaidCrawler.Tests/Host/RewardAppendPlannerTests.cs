using RaidCrawler.Core.Host;
using Xunit;

namespace RaidCrawler.Tests.Host;

public class RewardAppendPlannerTests
{
    private static readonly Func<uint, bool> Allowed = id => id is 2 or 1128 or 15;
    private static readonly RewardRow Extra = new(1128, 2, 0, 5);

    private static byte[] CreateSource(int count)
    {
        var data = new byte[RaidPointLayout.RaidPointLength];
        data[RaidPointLayout.RewardTail] = 0xA5; // 0x3B0 is NOT reward 46.
        data[RaidPointLayout.SummaryStart] = 0x5A;
        for (int i = 0; i < count; i++)
            Put(data, i, new RewardRow(2, (uint)(i + 1), 0, 0));
        return data;
    }

    private static void Put(byte[] data, int slot, RewardRow row) =>
        row.Write(data.AsSpan(RaidPointLayout.RewardsInRaidPoint +
                              slot * RaidPointLayout.RewardRowLength, 16));

    [Fact]
    public void DonorRecordsStartAtE0_WithItemAtE4()
    {
        Assert.Equal(0xE0, RaidPointLayout.RewardsInRaidPoint);
        Assert.Equal(0xE4, RaidPointLayout.ItemIdInRaidPoint);
        Assert.Equal(0x798, RaidPointLayout.RewardsInBootstrap);
        Assert.Equal(0x3B0, RaidPointLayout.RewardTail);
        Assert.Equal(45 * 16, RaidPointLayout.RewardsByteLength);
    }

    [Fact]
    public void PreservesEveryOriginalByteExceptOnePaddingSlot()
    {
        var data = CreateSource(3);
        var plan = RewardAppendPlanner.Plan(data, [Extra], Allowed);
        Assert.True(plan.Accepted);
        Assert.Equal(3, plan.OriginalCount);
        Assert.Equal(1, plan.AddedCount);
        int offset = RaidPointLayout.RewardsInRaidPoint + 3 * 16;
        Assert.Equal(data.AsSpan(0, offset).ToArray(), plan.CandidateRaidPoint.AsSpan(0, offset).ToArray());
        Assert.Equal(data.AsSpan(offset + 16).ToArray(), plan.CandidateRaidPoint.AsSpan(offset + 16).ToArray());
        Assert.Equal(Extra, RewardRow.Read(plan.CandidateRaidPoint.AsSpan(offset, 16)));
        Assert.Equal((byte)0xA5, plan.CandidateRaidPoint[RaidPointLayout.RewardTail]);
        Assert.Equal((byte)0x5A, plan.CandidateRaidPoint[RaidPointLayout.SummaryStart]);
        Assert.Equal((byte)0, data[offset]); // unchanged input
    }

    [Fact]
    public void Full45SlotSourceFailsClosed()
    {
        var data = CreateSource(45);
        var plan = RewardAppendPlanner.Plan(data, [Extra], Allowed);
        Assert.False(plan.Accepted);
        Assert.Equal("INSUFFICIENT_REWARD_SLOTS", plan.Reason);
        Assert.Equal(data, plan.CandidateRaidPoint);
    }

    [Fact]
    public void TwentyRowsRequireTwentyActuallyEmptySlots()
    {
        RewardRow[] additions = Enumerable.Repeat(new RewardRow(15, 1, 0, 5), 20).ToArray();
        var ok = RewardAppendPlanner.Plan(CreateSource(25), additions, Allowed);
        Assert.True(ok.Accepted);
        Assert.Equal(45, ok.OriginalCount + ok.AddedCount);
        var tooMany = CreateSource(26);
        var no = RewardAppendPlanner.Plan(tooMany, additions, Allowed);
        Assert.Equal("INSUFFICIENT_REWARD_SLOTS", no.Reason);
        Assert.Equal(tooMany, no.CandidateRaidPoint);
    }

    [Fact]
    public void GapsAreNotConsideredReusableWithoutSemanticEvidence()
    {
        var data = CreateSource(2);
        Put(data, 3, new RewardRow(2, 1, 0, 0));
        var no = RewardAppendPlanner.Plan(data, [Extra], Allowed);
        Assert.Equal("NONCONTIGUOUS_ORIGINAL_ROWS", no.Reason);
        Assert.Equal(data, no.CandidateRaidPoint);
    }

    [Fact]
    public void PartialRecordAtFirstEmptySlotBlocksAppend()
    {
        var data = CreateSource(21);
        Put(data, 21, new RewardRow(0, 0, 0, 5));
        var no = RewardAppendPlanner.Plan(data, [Extra], Allowed);
        Assert.False(no.Accepted);
        Assert.Equal("AMBIGUOUS_ORIGINAL_ROW", no.Reason);
        Assert.Equal(data, no.CandidateRaidPoint);
    }

    [Fact]
    public void UnknownOriginalSubjectAndNonzeroReservedBlockAppend()
    {
        var data = CreateSource(1);
        Put(data, 0, new RewardRow(2, 1, 0, 8));
        Assert.Equal("AMBIGUOUS_ORIGINAL_ROW", RewardAppendPlanner.Plan(data, [Extra], Allowed).Reason);
        Put(data, 0, new RewardRow(2, 1, 2, 0));
        Assert.Equal("AMBIGUOUS_ORIGINAL_ROW", RewardAppendPlanner.Plan(data, [Extra], Allowed).Reason);
    }

    [Theory]
    [InlineData(0u, 1u, 0u, 5u, "ITEM_NOT_ALLOWLISTED")]
    [InlineData(999u, 1u, 0u, 5u, "ITEM_NOT_ALLOWLISTED")]
    [InlineData(15u, 0u, 0u, 5u, "INVALID_QUANTITY")]
    [InlineData(15u, 1000u, 0u, 5u, "INVALID_QUANTITY")]
    [InlineData(15u, 1u, 0u, 0u, "SUBJECT_NOT_GLIMMERING_CHARM")]
    [InlineData(15u, 1u, 1u, 5u, "UNSUPPORTED_CUSTOM_RESERVED")]
    public void InvalidRequestIsAtomic(uint item, uint qty, uint rare, uint subject, string reason)
    {
        var data = CreateSource(3);
        var no = RewardAppendPlanner.Plan(data, [new RewardRow(item, qty, rare, subject)], Allowed);
        Assert.False(no.Accepted);
        Assert.Equal(reason, no.Reason);
        Assert.Equal(data, no.CandidateRaidPoint);
    }

    [Fact]
    public void RejectEntireListIfSecondAdditionIsInvalid()
    {
        var data = CreateSource(1);
        var no = RewardAppendPlanner.Plan(data,
            [new RewardRow(2, 1, 0, 5), new RewardRow(999, 1, 0, 5)], Allowed);
        Assert.False(no.Accepted);
        Assert.Equal(data, no.CandidateRaidPoint);
    }

    [Fact]
    public void WrongBufferLengthFailsClosed()
    {
        var data = new byte[0xAA0];
        var no = RewardAppendPlanner.Plan(data, [Extra], Allowed);
        Assert.Equal("INVALID_RAIDPOINT_LENGTH", no.Reason);
        Assert.Equal(data, no.CandidateRaidPoint);
    }

    [Fact]
    public void RetailDonorLittleEndianRecordHasSubjectAtZeroItemAtFourQuantityAtEight()
    {
        Span<byte> raw = stackalloc byte[16];
        var row = new RewardRow(0x12345678, 999, 0, 5);
        row.Write(raw);
        Assert.Equal(new byte[] { 5, 0, 0, 0 }, raw[..4].ToArray());
        Assert.Equal(new byte[] { 0x78, 0x56, 0x34, 0x12 }, raw.Slice(4, 4).ToArray());
        Assert.Equal(new byte[] { 0xE7, 0x03, 0, 0 }, raw.Slice(8, 4).ToArray());
        Assert.Equal(0u, System.Buffers.Binary.BinaryPrimitives.ReadUInt32LittleEndian(raw.Slice(12, 4)));
        Assert.Equal(row, RewardRow.Read(raw));
    }
}
