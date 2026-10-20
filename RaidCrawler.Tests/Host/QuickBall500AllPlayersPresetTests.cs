using RaidCrawler.Core.Host;
using Xunit;

namespace RaidCrawler.Tests.Host;

/// <summary>
/// Offline fixture tests for the dedicated button's immutable subject=0
/// Quick Ball ×500 single-row preset; NOT live reward-delivery proof.
/// </summary>
public sealed class QuickBall500AllPlayersPresetTests
{
    [Fact]
    public void NamedPresetIsExactlySourceZeroQuickBall15Quantity500ReservedZero()
    {
        Assert.Equal((uint)0, QuickBall500AllPlayersPreset.SubjectType);
        Assert.Equal((uint)15, QuickBall500AllPlayersPreset.ItemId);
        Assert.Equal((uint)500, QuickBall500AllPlayersPreset.Quantity);
        Assert.Equal(new RewardRow(15, 500, 0, 0), QuickBall500AllPlayersPreset.Row);
        byte[] raw = QuickBall500AllPlayersPreset.GetRecordBytes();
        Assert.Equal(new byte[] {
            0x00, 0x00, 0x00, 0x00, // subject/source: everyone
            0x0F, 0x00, 0x00, 0x00, // Quick Ball ID 15
            0xF4, 0x01, 0x00, 0x00, // quantity 500
            0x00, 0x00, 0x00, 0x00, // reserved
        }, raw);
        Assert.Equal(QuickBall500AllPlayersPreset.Row, RewardRow.Read(raw));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(21)]
    [InlineData(44)]
    public void AddsOneRowWithoutChangingAnyExistingBytes(int originalCount)
    {
        byte[] before = new byte[RaidPointLayout.RaidPointLength];
        before[RaidPointLayout.SummaryStart] = 0xAB;
        for (int i = 0; i < originalCount; i++)
            new RewardRow((uint)(1000 + i), 1, 0, 0).Write(
                before.AsSpan(RaidPointLayout.RewardsInRaidPoint + 16 * i, 16));

        byte[] original = before.ToArray();
        var result = QuickBall500AllPlayersPreset.Plan(before);
        Assert.True(result.Accepted, result.Reason);
        Assert.Equal(originalCount, result.OriginalCount);
        Assert.Equal(1, result.AddedCount);
        Assert.Equal(before, original);

        int offset = RaidPointLayout.RewardsInRaidPoint + originalCount * 16;
        Assert.True(before.AsSpan(0, offset).SequenceEqual(
            result.CandidateRaidPoint.AsSpan(0, offset)));
        Assert.Equal(QuickBall500AllPlayersPreset.GetRecordBytes(),
            result.CandidateRaidPoint.AsSpan(offset, 16).ToArray());
        Assert.True(before.AsSpan(offset + 16).SequenceEqual(
            result.CandidateRaidPoint.AsSpan(offset + 16)));
        Assert.Equal("EXACT_ONE_ROW_ONLY",
            HostPlanAWriteAudit.Check(before, result.CandidateRaidPoint,
                originalCount, 15, 500, 0));
    }

    [Fact]
    public void FullFortyFiveRowsRejectWithoutAnyMutation()
    {
        byte[] source = new byte[RaidPointLayout.RaidPointLength];
        for (int i = 0; i < RaidPointLayout.RewardCapacity; i++)
            new RewardRow((uint)(1000 + i), 1, 0, 0).Write(
                source.AsSpan(RaidPointLayout.RewardsInRaidPoint + i * 16, 16));
        var plan = QuickBall500AllPlayersPreset.Plan(source);
        Assert.False(plan.Accepted);
        Assert.Equal("INSUFFICIENT_REWARD_SLOTS", plan.Reason);
        Assert.Equal(source, plan.CandidateRaidPoint);
    }

    [Fact]
    public void DoesNotOverwriteAmbiguousOriginalSourceOnlyRow()
    {
        byte[] source = new byte[RaidPointLayout.RaidPointLength];
        new RewardRow(1000, 1, 0, 0).Write(
            source.AsSpan(RaidPointLayout.RewardsInRaidPoint, 16));
        new RewardRow(0, 0, 0, 5).Write(
            source.AsSpan(RaidPointLayout.RewardsInRaidPoint + 16, 16));
        var plan = QuickBall500AllPlayersPreset.Plan(source);
        Assert.False(plan.Accepted);
        Assert.Equal("AMBIGUOUS_ORIGINAL_ROW", plan.Reason);
        Assert.Equal(source, plan.CandidateRaidPoint);
    }
}
