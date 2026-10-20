using RaidCrawler.Core.Host;
using Xunit;

namespace RaidCrawler.Tests.Host;

public sealed class Subject5AppendInspectorTests
{
    private static HostProbeCapture Original()
    {
        var s = new byte[720];
        new RewardRow(1127, 1, 0, 0).Write(s.AsSpan(0, 16));
        new RewardRow(1862, 10, 0, 5).Write(s.AsSpan(16, 16));
        return new(s, (byte[])s.Clone());
    }

    private static HostProbeResult Success() =>
        new(0, 15, 2, 0, 5, 20, 1, 2);

    [Fact]
    public void SingleSubject5AppendAndOriginalRewardsPreserved()
    {
        var cap = Original();
        new RewardRow(15, 2, 0, 5).Write(cap.BootstrapRows.AsSpan(32, 16));
        Assert.Equal("ONE_SUBJECT5_ROW_ONLY", Subject5AppendInspector.Inspect(cap, Success()).Reason);
        Assert.True(Subject5AppendInspector.Inspect(cap, Success()).Valid);
    }

    [Fact]
    public void RejectsChangesToExistingRewards()
    {
        var cap = Original();
        new RewardRow(15, 2, 0, 5).Write(cap.BootstrapRows.AsSpan(32, 16));
        cap.BootstrapRows[4]++; // original quantity corruption
        Assert.False(Subject5AppendInspector.Inspect(cap, Success()).Valid);
    }

    [Fact]
    public void RejectsExtraUnrelatedModificationAndFalseNativeResult()
    {
        var cap = Original();
        new RewardRow(15, 2, 0, 5).Write(cap.BootstrapRows.AsSpan(32, 16));
        cap.BootstrapRows[64] = 0xAA;
        Assert.Equal("UNRELATED_BYTES_MODIFIED", Subject5AppendInspector.Inspect(cap, Success()).Reason);
        var noProof = Success() with { LastResult = 10 };
        Assert.Equal("NATIVE_APPEND_NOT_CONFIRMED", Subject5AppendInspector.Inspect(cap, noProof).Reason);
    }

    [Fact]
    public void ReportsWrongWrittenSubject()
    {
        var cap = Original();
        new RewardRow(15, 2, 0, 0).Write(cap.BootstrapRows.AsSpan(32, 16));
        Assert.Equal("WRITTEN_ROW_MISMATCH", Subject5AppendInspector.Inspect(cap, Success()).Reason);
    }
}
