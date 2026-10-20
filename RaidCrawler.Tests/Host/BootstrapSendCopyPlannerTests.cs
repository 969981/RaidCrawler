using RaidCrawler.Core.Host;
using Xunit;

namespace RaidCrawler.Tests.Host;

public sealed class BootstrapSendCopyPlannerTests
{
    private static readonly Func<uint, bool> Allowed = id => id is 15 or 16;

    private static (byte[] Source, byte[] Bootstrap) Original(int used = 2)
    {
        byte[] source = new byte[RaidPointLayout.RaidPointLength];
        source[0xE0] = 0xC7; // metadata immediately BEFORE row 0; never alter
        source[RaidPointLayout.RewardTail] = 0xA5; // 0x3B4 is not row 45
        source[RaidPointLayout.SummaryStart] = 0x6;
        for (int i = 0; i < used; i++)
        {
            // A rare-flag=1 original is valid; do not reject/replace it.
            uint rare = i == 0 ? 1u : 0u;
            new RewardRow((uint)(1127 + i), 1, rare, i == 1 ? 5u : 0u)
                .Write(source.AsSpan(RaidPointLayout.RewardsInRaidPoint + i * 16, 16));
        }
        byte[] wire = new byte[RaidPointLayout.BootstrapLength];
        wire[0] = 0xCC; // encrypted PK9 part is outside the RaidPoint
        source.CopyTo(wire, RaidPointLayout.RaidPointInBootstrap);
        return (source, wire);
    }

    [Fact]
    public void AppendsMultipleSubjectFiveRowsOnlyToOutgoingCopy()
    {
        var (source, wire) = Original();
        var oldSource = source.ToArray();
        var oldWire = wire.ToArray();
        RewardRow[] added = [new(15, 2, 0, 5), new(16, 1, 0, 5)];
        var plan = BootstrapSendCopyPlanner.Plan(source, wire, added, Allowed);
        Assert.True(plan.Accepted, plan.Reason);
        Assert.Equal(2, plan.OriginalCount);
        Assert.Equal(2, plan.AddedCount);
        Assert.Equal(oldSource, source);
        Assert.Equal(oldWire, wire);
        int offset = RaidPointLayout.RewardsInBootstrap + 2 * 16;
        Assert.Equal(oldWire.AsSpan(..offset).ToArray(),
            plan.CandidateBootstrap.AsSpan(..offset).ToArray());
        Assert.Equal(oldWire.AsSpan(offset + 32).ToArray(),
            plan.CandidateBootstrap.AsSpan(offset + 32).ToArray());
        Assert.Equal(added[0], RewardRow.Read(plan.CandidateBootstrap.AsSpan(offset, 16)));
        Assert.Equal(added[1], RewardRow.Read(plan.CandidateBootstrap.AsSpan(offset + 16, 16)));
        Assert.Equal((byte)0xC7, plan.CandidateBootstrap[RaidPointLayout.RaidPointInBootstrap + 0xE0]);
        Assert.Equal((byte)0xA5, plan.CandidateBootstrap[RaidPointLayout.RaidPointInBootstrap + RaidPointLayout.RewardTail]);
        Assert.Equal((byte)0x6, plan.CandidateBootstrap[RaidPointLayout.RaidPointInBootstrap + RaidPointLayout.SummaryStart]);
    }

    [Fact]
    public void MismatchedMetadataBlocksModificationEvenWhenRewardRowsMatch()
    {
        var (source, wire) = Original();
        wire[RaidPointLayout.RaidPointInBootstrap + 0xE0] ^= 1;
        byte[] previous = wire.ToArray();
        var plan = BootstrapSendCopyPlanner.Plan(source, wire,
            [new RewardRow(15, 1, 0, 5)], Allowed);
        Assert.False(plan.Accepted);
        Assert.Equal("SOURCE_BOOTSTRAP_MISMATCH", plan.Reason);
        Assert.Equal(previous, plan.CandidateBootstrap);
    }

    [Fact]
    public void Full45RowsFailClosedWithoutTruncatingRetailRewards()
    {
        var (source, wire) = Original(45);
        var plan = BootstrapSendCopyPlanner.Plan(source, wire,
            [new RewardRow(15, 1, 0, 5)], Allowed);
        Assert.False(plan.Accepted);
        Assert.Equal("INSUFFICIENT_REWARD_SLOTS", plan.Reason);
        Assert.Equal(wire, plan.CandidateBootstrap);
    }

    [Fact]
    public void InvalidLengthOrGappedSourceIsNeverEdited()
    {
        var (source, wire) = Original();
        Assert.Equal("INVALID_SOURCE_RAIDPOINT_LENGTH",
            BootstrapSendCopyPlanner.Plan(source[..^1], wire,
                [new RewardRow(15, 1, 0, 5)], Allowed).Reason);
        Assert.Equal("INVALID_BOOTSTRAP_LENGTH",
            BootstrapSendCopyPlanner.Plan(source, wire[..^1],
                [new RewardRow(15, 1, 0, 5)], Allowed).Reason);

        new RewardRow(15, 1, 0, 0)
            .Write(source.AsSpan(RaidPointLayout.RewardsInRaidPoint + 4 * 16, 16));
        source.CopyTo(wire, RaidPointLayout.RaidPointInBootstrap);
        var plan = BootstrapSendCopyPlanner.Plan(source, wire,
            [new RewardRow(16, 1, 0, 5)], Allowed);
        Assert.False(plan.Accepted);
        Assert.Equal("NONCONTIGUOUS_ORIGINAL_ROWS", plan.Reason);
    }
}
