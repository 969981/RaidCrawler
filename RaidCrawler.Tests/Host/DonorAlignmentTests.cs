using System.Buffers.Binary;
using RaidCrawler.Core.Host;
using Xunit;

namespace RaidCrawler.Tests.Host;

/// <summary>
/// Minimal extracted structural facts of published PokéLDN Violet donor:
/// first row at +E4 (item ID at row start); synthetic 21 consecutive occupied rows.
/// Source: Decryptu/pokeldn@8b2c19c.. / sv_raid_reward_donor.bin
/// </summary>
public sealed class DonorAlignmentTests
{
    [Fact]
    public void FixtureLastSubjectFiveIsNotAnExtraMarkerRow()
    {
        var data = new byte[RaidPointLayout.RaidPointLength];
        for (int i = 0; i < 21; i++)
            new RewardRow((uint)(1127 + i), 1, 0, 0)
                .Write(data.AsSpan(RaidPointLayout.RewardsInRaidPoint + i * 16, 16));
        new RewardRow(1862, 10, 0, 5).Write(data.AsSpan(RaidPointLayout.RewardsInRaidPoint + 20 * 16, 16));
        var candidate = RewardAppendPlanner.Plan(
            data, [new RewardRow(15, 1, 0, 5)], item => item == 15);
        Assert.True(candidate.Accepted);
        Assert.Equal(21, candidate.OriginalCount);
        Assert.Equal(1, candidate.AddedCount);
        Assert.Equal(new RewardRow(1862, 10, 0, 5),
            RewardRow.Read(candidate.CandidateRaidPoint.AsSpan(RaidPointLayout.RewardsInRaidPoint + 20 * 16, 16)));
        Assert.Equal(new RewardRow(15, 1, 0, 5),
            RewardRow.Read(candidate.CandidateRaidPoint.AsSpan(RaidPointLayout.RewardsInRaidPoint + 21 * 16, 16)));
        Assert.Equal(data.AsSpan(RaidPointLayout.RewardTail).ToArray(),
            candidate.CandidateRaidPoint.AsSpan(RaidPointLayout.RewardTail).ToArray());
    }
}
