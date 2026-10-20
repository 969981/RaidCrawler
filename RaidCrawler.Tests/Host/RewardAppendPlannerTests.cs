using RaidCrawler.Core.Host;
using Xunit;

namespace RaidCrawler.Tests.Host;

public class RewardAppendPlannerTests
{
    private static readonly Func<uint, bool> ValidItems = item => item is 2 or 1128 or 15;

    private static byte[] CreateSource(int count)
    {
        var data = new byte[RaidPointLayout.RaidPointLength];
        // Fake opaque gap and summary: addition must not touch these bytes.
        data[RaidPointLayout.RewardTail] = 0xA5;
        data[RaidPointLayout.SummaryStart] = 0x5A;
        for (int i = 0; i < count; i++)
            Put(data, i, new RewardRow(2, (uint)(i + 1), 0, (uint)(i % 6)));
        return data;
    }

    private static void Put(byte[] source, int index, RewardRow row) =>
        row.Write(source.AsSpan(RaidPointLayout.RewardsInRaidPoint + index * RaidPointLayout.RewardRowLength,
                                RaidPointLayout.RewardRowLength));

    [Fact]
    public void SmallAdditionPreservesAllOriginalBytesExceptItsEmptySlot()
    {
        byte[] original = CreateSource(3);
        var plan = RewardAppendPlanner.Plan(original, [new RewardRow(1128, 2, 0, 5)], ValidItems);
        Assert.True(plan.Accepted);
        Assert.Equal(3, plan.OriginalCount);
        Assert.Equal(1, plan.AddedCount);
        int pos = RaidPointLayout.RewardsInRaidPoint + 3 * RaidPointLayout.RewardRowLength;
        Assert.Equal(original.AsSpan(0, pos).ToArray(), plan.CandidateRaidPoint.AsSpan(0, pos).ToArray());
        Assert.Equal(original.AsSpan(pos + 16).ToArray(), plan.CandidateRaidPoint.AsSpan(pos + 16).ToArray());
        Assert.Equal(new RewardRow(1128, 2, 0, 5), RewardRow.Read(plan.CandidateRaidPoint.AsSpan(pos, 16)));
        Assert.Equal((byte)0xA5, plan.CandidateRaidPoint[RaidPointLayout.RewardTail]);
        Assert.Equal((byte)0x5A, plan.CandidateRaidPoint[RaidPointLayout.SummaryStart]);
        Assert.Equal(new RewardRow(2, 1, 0, 0),
            RewardRow.Read(plan.CandidateRaidPoint.AsSpan(RaidPointLayout.RewardsInRaidPoint, 16)));
        Assert.Equal((byte)0, original[pos]); // Input remains unchanged.
    }

    [Fact]
    public void Full45SlotsFailsClosed()
    {
        byte[] original = CreateSource(45);
        var plan = RewardAppendPlanner.Plan(original, [new RewardRow(1128, 1, 0, 5)], ValidItems);
        Assert.False(plan.Accepted);
        Assert.Equal("INSUFFICIENT_REWARD_SLOTS", plan.Reason);
        Assert.Equal(original, plan.CandidateRaidPoint);
    }

    [Fact]
    public void TwentyRowsAllowedOnlyWithEnoughEmptySlots()
    {
        RewardRow[] extras = Enumerable.Repeat(new RewardRow(15, 1, 0, 5), 20).ToArray();
        var fits = RewardAppendPlanner.Plan(CreateSource(25), extras, ValidItems);
        Assert.True(fits.Accepted);
        Assert.Equal(45, fits.OriginalCount + fits.AddedCount);
        byte[] tooManyOriginal = CreateSource(26);
        var denied = RewardAppendPlanner.Plan(tooManyOriginal, extras, ValidItems);
        Assert.False(denied.Accepted);
        Assert.Equal(tooManyOriginal, denied.CandidateRaidPoint);
    }

    [Fact]
    public void HolesAndPartialPaddingAreNotSilentlyReused()
    {
        byte[] source = CreateSource(2);
        Put(source, 3, new RewardRow(2, 1, 0, 0));
        var hole = RewardAppendPlanner.Plan(source, [new RewardRow(15, 1, 0, 5)], ValidItems);
        Assert.Equal("NONCONTIGUOUS_ORIGINAL_ROWS", hole.Reason);
        Assert.Equal(source, hole.CandidateRaidPoint);
        source = CreateSource(1);
        Put(source, 1, new RewardRow(0, 1, 0, 0));
        var partial = RewardAppendPlanner.Plan(source, [new RewardRow(15, 1, 0, 5)], ValidItems);
        Assert.Equal("AMBIGUOUS_ORIGINAL_ROW", partial.Reason);
    }

    [Theory]
    [InlineData(0u, 1u, 0u, 5u, "ITEM_NOT_ALLOWLISTED")]
    [InlineData(999u, 1u, 0u, 5u, "ITEM_NOT_ALLOWLISTED")]
    [InlineData(15u, 0u, 0u, 5u, "INVALID_QUANTITY")]
    [InlineData(15u, 1000u, 0u, 5u, "INVALID_QUANTITY")]
    [InlineData(15u, 1u, 0u, 0u, "SUBJECT_NOT_GLIMMERING_CHARM")]
    [InlineData(15u, 1u, 1u, 5u, "UNSUPPORTED_CUSTOM_RARE_TYPE")]
    public void InvalidRequestNeverChangesSource(uint item, uint qty, uint rare, uint subject, string reason)
    {
        byte[] source = CreateSource(3);
        var plan = RewardAppendPlanner.Plan(source, [new RewardRow(item, qty, rare, subject)], ValidItems);
        Assert.False(plan.Accepted);
        Assert.Equal(reason, plan.Reason);
        Assert.Equal(source, plan.CandidateRaidPoint);
    }

    [Fact]
    public void AllOrNothingWhenOneRowIsInvalid()
    {
        byte[] source = CreateSource(1);
        RewardRow[] additions = [new(2, 1, 0, 5), new(999, 1, 0, 5)];
        var plan = RewardAppendPlanner.Plan(source, additions, ValidItems);
        Assert.False(plan.Accepted);
        Assert.Equal(source, plan.CandidateRaidPoint);
    }

    [Fact]
    public void OriginalUnknownSubjectMustNotBeOverwritten()
    {
        byte[] source = CreateSource(1);
        Put(source, 0, new RewardRow(2, 1, 0, 9));
        var plan = RewardAppendPlanner.Plan(source, [new RewardRow(15, 1, 0, 5)], ValidItems);
        Assert.False(plan.Accepted);
        Assert.Equal("AMBIGUOUS_ORIGINAL_ROW", plan.Reason);
    }

    [Fact]
    public void WrongLayoutCannotBePatched()
    {
        byte[] source = new byte[0xAA0];
        var plan = RewardAppendPlanner.Plan(source, [new RewardRow(15, 1, 0, 5)], ValidItems);
        Assert.False(plan.Accepted);
        Assert.Equal("INVALID_RAIDPOINT_LENGTH", plan.Reason);
        Assert.Equal(source, plan.CandidateRaidPoint);
    }

    [Fact]
    public void RowUsesLittleEndianU32()
    {
        Span<byte> raw = stackalloc byte[16];
        new RewardRow(0x12345678, 999, 0, 5).Write(raw);
        Assert.Equal(new byte[] { 0x78, 0x56, 0x34, 0x12 }, raw[..4].ToArray());
        Assert.Equal((byte)0xE7, raw[4]);
        Assert.Equal((byte)0x03, raw[5]);
        Assert.Equal(new RewardRow(0x12345678, 999, 0, 5), RewardRow.Read(raw));
    }
}
