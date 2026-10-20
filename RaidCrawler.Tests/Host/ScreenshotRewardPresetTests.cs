using RaidCrawler.Core.Host;
using Xunit;

namespace RaidCrawler.Tests.Host;

public sealed class ScreenshotRewardPresetTests
{
    private static readonly uint[] ScreenshotIds =
    [
        492, 493, 494, 495, 496, 497, 498, 499,
        795, 796, 1, 50, 1128, 581,
        1907, 1908, 1906, 1904, 1905, 1606,
    ];

    private static byte[] Source(int occupied)
    {
        var data = new byte[RaidPointLayout.RaidPointLength];
        // Keep a sentinel in summary unrelated to the reward array.
        data[RaidPointLayout.SummaryStart] = 0x5A;
        for (int i = 0; i < occupied; i++)
            new RewardRow((uint)(1000 + i), 1, 0, 0).Write(
                data.AsSpan(RaidPointLayout.RewardsInRaidPoint + i * 16, 16));
        return data;
    }

    [Fact]
    public void ScreenshotListMatchesAllTwentyGenNineItemIdsInExactOrder()
    {
        Assert.Equal(20, ScreenshotRewardPreset.Items.Count);
        Assert.Equal(ScreenshotIds, ScreenshotRewardPreset.Items.Select(x => x.ItemId).ToArray());
        Assert.Equal(20 * 16, ScreenshotRewardPreset.Bytes);
        Assert.Equal(25, ScreenshotRewardPreset.MaxOriginalRows);
        Assert.Equal("巨大金珠", ScreenshotRewardPreset.Items[13].Name);
        Assert.Equal((uint)581, ScreenshotRewardPreset.Items[13].ItemId);
        Assert.Equal((uint)499, ScreenshotRewardPreset.Items[7].ItemId); // Sport Ball
        Assert.Equal((uint)1606, ScreenshotRewardPreset.Items[19].ItemId); // Ability Patch
    }

    [Fact]
    public void EveryRequestedRewardIsX999WithSubject5()
    {
        var rows = ScreenshotRewardPreset.BuildRows();
        Assert.Equal(20, rows.Count);
        foreach (var row in rows)
        {
            Assert.Equal(999u, row.Quantity);
            Assert.Equal(0u, row.RareFlag);
            Assert.Equal(5u, row.SubjectType);
        }
        Assert.Equal(ScreenshotIds, rows.Select(x => x.ItemId).ToArray());
    }

    [Theory]
    [InlineData(0)]
    [InlineData(21)]
    [InlineData(25)]
    public void PreservesExistingAndAllUnrelatedBytesWhileAddingTwenty(int occupied)
    {
        byte[] original = Source(occupied);
        byte[] snapshot = (byte[])original.Clone();
        var planned = ScreenshotRewardPreset.Plan(original);
        Assert.True(planned.Accepted);
        Assert.Equal(occupied, planned.OriginalCount);
        Assert.Equal(20, planned.AddedCount);
        Assert.Equal("EXACT_20_ROWS_ONLY",
            ScreenshotRewardPreset.Audit(original, planned.CandidateRaidPoint, occupied));
        Assert.Equal(snapshot, original); // planner never mutates caller memory
        Assert.Equal(0x5A, planned.CandidateRaidPoint[RaidPointLayout.SummaryStart]);
        for (int i = 0; i < occupied; i++)
            Assert.Equal(
                RewardRow.Read(original.AsSpan(RaidPointLayout.RewardsInRaidPoint + i * 16, 16)),
                RewardRow.Read(planned.CandidateRaidPoint.AsSpan(RaidPointLayout.RewardsInRaidPoint + i * 16, 16)));
        for (int i = 0; i < 20; i++)
            Assert.Equal(new RewardRow(ScreenshotIds[i], 999, 0, 5),
                RewardRow.Read(planned.CandidateRaidPoint.AsSpan(
                    RaidPointLayout.RewardsInRaidPoint + (occupied + i) * 16, 16)));
    }

    [Fact]
    public void Rejects26OriginalRowsWithoutAnyPartialAppend()
    {
        var original = Source(26);
        var result = ScreenshotRewardPreset.Plan(original);
        Assert.False(result.Accepted);
        Assert.Equal("INSUFFICIENT_REWARD_SLOTS", result.Reason);
        Assert.Equal(0, result.AddedCount);
        Assert.Equal(original, result.CandidateRaidPoint);
        Assert.Equal("INSUFFICIENT_REWARD_SLOTS",
            ScreenshotRewardPreset.Audit(original, original, 26));
    }

    [Fact]
    public void RejectsUnknownOriginalAndNoncontiguousRows()
    {
        var original = Source(2);
        new RewardRow(42, 1, 0, 0).Write(
            original.AsSpan(RaidPointLayout.RewardsInRaidPoint + 4 * 16, 16));
        var result = ScreenshotRewardPreset.Plan(original);
        Assert.False(result.Accepted);
        Assert.Equal("NONCONTIGUOUS_ORIGINAL_ROWS", result.Reason);
        Assert.Equal(original, result.CandidateRaidPoint);
    }

    [Fact]
    public void RejectsAnyModificationToExistingRowsSummaryOrRequestedItem()
    {
        var original = Source(21);
        var result = ScreenshotRewardPreset.Plan(original);
        Assert.True(result.Accepted);
        byte[] changedOld = (byte[])result.CandidateRaidPoint.Clone();
        changedOld[RaidPointLayout.RewardsInRaidPoint + 8]++;
        Assert.Equal("ORIGINAL_REWARDS_OR_HEADER_CHANGED",
            ScreenshotRewardPreset.Audit(original, changedOld, 21));

        byte[] changedTail = (byte[])result.CandidateRaidPoint.Clone();
        changedTail[RaidPointLayout.SummaryStart]++;
        Assert.Equal("OTHER_RAIDPOINT_BYTES_CHANGED",
            ScreenshotRewardPreset.Audit(original, changedTail, 21));

        byte[] changedPreset = (byte[])result.CandidateRaidPoint.Clone();
        changedPreset[RaidPointLayout.RewardsInRaidPoint + 21 * 16] = 0; // item ID is first u32
        Assert.Equal("PRESET_ROW_MISMATCH_0",
            ScreenshotRewardPreset.Audit(original, changedPreset, 21));
    }

    [Fact]
    public void RejectsAnyNonEmptyTargetRow()
    {
        var original = Source(20);
        var planned = ScreenshotRewardPreset.Plan(original);
        Assert.True(planned.Accepted);
        original[RaidPointLayout.RewardsInRaidPoint + 20 * 16] = 7;
        Assert.Equal("TARGET_SLOTS_NOT_EMPTY",
            ScreenshotRewardPreset.Audit(original, planned.CandidateRaidPoint, 20));
    }
    [Theory]
    [InlineData(0u)]
    [InlineData(5u)]
    public void DonorAccurateRecipientsUseFirstU32AndPreserveExistingRewards(uint subject)
    {
        byte[] original = Source(21);
        byte[] baseline = original.ToArray();
        var result = ScreenshotRewardPreset.Plan(original, subject);
        Assert.True(result.Accepted);
        Assert.Equal(20, result.AddedCount);
        Assert.Equal("EXACT_20_ROWS_ONLY",
            ScreenshotRewardPreset.Audit(original, result.CandidateRaidPoint, 21, subject));
        Assert.Equal(baseline, original);
        int offset = RaidPointLayout.RewardsInRaidPoint + 21 * 16;
        for (int i = 0; i < 20; i++)
        {
            var bytes = result.CandidateRaidPoint.AsSpan(offset + i * 16, 16);
            Assert.Equal(ScreenshotIds[i],
                System.Buffers.Binary.BinaryPrimitives.ReadUInt32LittleEndian(bytes[..4]));
            Assert.Equal(999u,
                System.Buffers.Binary.BinaryPrimitives.ReadUInt32LittleEndian(bytes.Slice(4, 4)));
            Assert.Equal(0u,
                System.Buffers.Binary.BinaryPrimitives.ReadUInt32LittleEndian(bytes.Slice(8, 4)));
            Assert.Equal(subject,
                System.Buffers.Binary.BinaryPrimitives.ReadUInt32LittleEndian(bytes.Slice(12, 4)));
        }
    }

    [Fact]
    public void WrongSubjectOrUnsupportedSubjectCannotPassTheByteAudit()
    {
        var original = Source(25);
        var everyone = ScreenshotRewardPreset.Plan(original, 0);
        var charm = ScreenshotRewardPreset.Plan(original, 5);
        Assert.True(everyone.Accepted);
        Assert.True(charm.Accepted);
        Assert.NotEqual(everyone.CandidateRaidPoint, charm.CandidateRaidPoint);
        Assert.Equal("PRESET_ROW_MISMATCH_0",
            ScreenshotRewardPreset.Audit(original, everyone.CandidateRaidPoint, 25, 5));
        Assert.Equal("PRESET_ROW_MISMATCH_0",
            ScreenshotRewardPreset.Audit(original, charm.CandidateRaidPoint, 25, 0));
        Assert.Equal("UNSUPPORTED_SUBJECT",
            ScreenshotRewardPreset.Plan(original, 2).Reason);
    }

    [Fact]
    public void RetailDonorLayoutPlacesSourceAt798AndItemAt79C()
    {
        Assert.Equal(0xE4, RaidPointLayout.RewardsInRaidPoint);
        Assert.Equal(0x79C, RaidPointLayout.RewardsInBootstrap);
        Assert.Equal(0xE4, RaidPointLayout.ItemIdInRaidPoint);
        var row = new RewardRow(1128, 1, 0, 0);
        byte[] bytes = new byte[16];
        row.Write(bytes);
        Assert.Equal(new byte[] { 0x68,0x04,0,0, 1,0,0,0, 0,0,0,0, 0,0,0,0 }, bytes);
    }

}
