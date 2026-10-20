using RaidCrawler.Core.Host;
using Xunit;

namespace RaidCrawler.Tests.Host;

public sealed class HostPlanAWriteAuditTests
{
    private static (byte[] Before, byte[] After) Example()
    {
        var before = new byte[RaidPointLayout.RaidPointLength];
        new RewardRow(1127, 1, 0, 0).Write(
            before.AsSpan(RaidPointLayout.RewardsInRaidPoint, 16));
        new RewardRow(1862, 10, 0, 5).Write(
            before.AsSpan(RaidPointLayout.RewardsInRaidPoint + 16, 16));
        var after = before.ToArray();
        new RewardRow(15, 2, 0, 5).Write(
            after.AsSpan(RaidPointLayout.RewardsInRaidPoint + 32, 16));
        return (before, after);
    }

    [Fact]
    public void ExactSingleSubjectFiveRowIsAccepted()
    {
        var (before, after) = Example();
        Assert.Equal(HostPlanAWriteAudit.Correct,
            HostPlanAWriteAudit.Check(before, after, 2, 15, 2));
        Assert.Equal(0, before[RaidPointLayout.RewardsInRaidPoint + 32]);
    }

    [Fact]
    public void ExistingRewardsMayNotChange()
    {
        var (before, after) = Example();
        after[RaidPointLayout.RewardsInRaidPoint + 20] ^= 1;
        Assert.Equal("ORIGINAL_CONTENT_MODIFIED",
            HostPlanAWriteAudit.Check(before, after, 2, 15, 2));
    }

    [Fact]
    public void NoOtherRaidpointBytesMayChange()
    {
        var (before, after) = Example();
        after[0x3B8] = 1;
        Assert.Equal("UNRELATED_CONTENT_MODIFIED",
            HostPlanAWriteAudit.Check(before, after, 2, 15, 2));
    }

    [Fact]
    public void RejectsInvalidQuantityOrWrongSubject()
    {
        var (before, after) = Example();
        Assert.Equal("ITEM_OR_QUANTITY_NOT_ALLOWED",
            HostPlanAWriteAudit.Check(before, after, 2, 15, 999));
        new RewardRow(15, 2, 0, 0).Write(
            after.AsSpan(RaidPointLayout.RewardsInRaidPoint + 32, 16));
        Assert.Equal("WRONG_SUBJECT_OR_REWARD",
            HostPlanAWriteAudit.Check(before, after, 2, 15, 2));
    }

    [Fact]
    public void RejectsNonEmptyInsertSlotAndWrongLength()
    {
        var (before, after) = Example();
        before[RaidPointLayout.RewardsInRaidPoint + 32] = 3;
        Assert.Equal("APPEND_SLOT_NOT_EMPTY",
            HostPlanAWriteAudit.Check(before, after, 2, 15, 2));
        Assert.Equal("INVALID_RAIDPOINT_LENGTH",
            HostPlanAWriteAudit.Check(new byte[10], after, 2, 15, 2));
    }
}
