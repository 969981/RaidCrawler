using RaidCrawler.Core.Host;
using Xunit;

namespace RaidCrawler.Tests.Host;

public class HostCaptureVerifierTests
{
    [Fact]
    public void SameRewardRegionMatchesEvenIfOtherBootstrapFieldsDiffer()
    {
        byte[] source = new byte[RaidPointLayout.RaidPointLength];
        byte[] plain = new byte[RaidPointLayout.BootstrapLength];
        RewardRow expected = new(1128, 3, 0, 5);
        expected.Write(source.AsSpan(RaidPointLayout.RewardsInRaidPoint, 16));
        source.AsSpan(RaidPointLayout.RewardsInRaidPoint, RaidPointLayout.RewardsByteLength)
            .CopyTo(plain.AsSpan(RaidPointLayout.RewardsInBootstrap, RaidPointLayout.RewardsByteLength));
        plain[0] = 0xAB; // Not part of reward evidence.

        var report = HostCaptureVerifier.Compare(source, plain);
        Assert.True(report.Matches);
        Assert.Equal(-1, report.FirstMismatchRow);
        Assert.Equal(-1, report.FirstMismatchByte);
        Assert.Null(report.FirstMismatchField);
        Assert.Equal(report.SourceRowsSha256, report.BootstrapRowsSha256);
        Assert.Equal(64, report.SourceRowsSha256.Length);
    }

    [Fact]
    public void AChangedSubjectGivesExactSlotAndField()
    {
        byte[] source = new byte[RaidPointLayout.RaidPointLength];
        byte[] plain = new byte[RaidPointLayout.BootstrapLength];
        // Source item 2, qty 1; transmitted item 2, qty 1 but subject differs.
        new RewardRow(2, 1, 0, 5).Write(
            source.AsSpan(RaidPointLayout.RewardsInRaidPoint + 7 * 16, 16));
        new RewardRow(2, 1, 0, 0).Write(
            plain.AsSpan(RaidPointLayout.RewardsInBootstrap + 7 * 16, 16));
        var report = HostCaptureVerifier.Compare(source, plain);
        Assert.False(report.Matches);
        Assert.Equal(7, report.FirstMismatchRow);
        Assert.Equal(7 * 16, report.FirstMismatchByte);
        Assert.Equal("SubjectType", report.FirstMismatchField);
    }

    [Fact]
    public void WrongSizesRejectedInsteadOfTruncating()
    {
        Assert.Throws<ArgumentException>(() => HostCaptureVerifier.Compare(
            new byte[0x3E7], new byte[RaidPointLayout.BootstrapLength]));
        Assert.Throws<ArgumentException>(() => HostCaptureVerifier.Compare(
            new byte[RaidPointLayout.RaidPointLength], new byte[0xAA1]));
    }

    [Fact]
    public void ReadOnlySnapshotDoesNotAliasTransientSource()
    {
        byte[] source = new byte[RaidPointLayout.RaidPointLength];
        byte[] snapshot = HostCaptureVerifier.CopySourceRewards(source);
        source[RaidPointLayout.RewardsInRaidPoint] = 0xFF;
        Assert.Equal(RaidPointLayout.RewardsByteLength, snapshot.Length);
        Assert.Equal((byte)0, snapshot[0]);
    }
}
