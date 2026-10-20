using System.Buffers.Binary;
using RaidCrawler.Core.Host;
using Xunit;

namespace RaidCrawler.Tests.Host;

/// <summary>
/// Minimal extracted structural facts of published PokéLDN Violet donor:
/// first row at +E4, 21 occupied rows; row 20 includes subject 5.
/// Source: Decryptu/pokeldn@8b2c19c.. / sv_raid_reward_donor.bin
/// </summary>
public sealed class DonorAlignmentTests
{
    [Fact]
    public void FixtureLastSubjectFiveIsNotAnExtraMarkerRow()
    {
        var data = new byte[RaidPointLayout.RaidPointLength];
        BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(0xE0), 0);
        for (int i = 0; i < 21; i++)
            new RewardRow((uint)(1127 + i), 1, 0, 0)
                .Write(data.AsSpan(RaidPointLayout.RewardsInRaidPoint + i * 16, 16));
        new RewardRow(1127, 1, 0, 0).Write(data.AsSpan(0xE4, 16));
        new RewardRow(1862, 10, 0, 5).Write(data.AsSpan(0xE4 + 20 * 16, 16));
        var candidate = RewardAppendPlanner.Plan(
            data, [new RewardRow(15, 1, 0, 5)], item => item == 15);
        Assert.True(candidate.Accepted);
        Assert.Equal(21, candidate.OriginalCount);
        Assert.Equal(1, candidate.AddedCount);
        Assert.Equal(new RewardRow(1862, 10, 0, 5),
            RewardRow.Read(candidate.CandidateRaidPoint.AsSpan(0xE4 + 20 * 16, 16)));
        Assert.Equal(new RewardRow(15, 1, 0, 5),
            RewardRow.Read(candidate.CandidateRaidPoint.AsSpan(0xE4 + 21 * 16, 16)));
        Assert.Equal(data.AsSpan(0x3B4).ToArray(),
            candidate.CandidateRaidPoint.AsSpan(0x3B4).ToArray());
    }
}
