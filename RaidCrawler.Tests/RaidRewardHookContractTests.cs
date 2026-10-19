using RaidCrawler.Core.Structures;
using Xunit;

namespace RaidCrawler.Tests;

public class RaidRewardHookContractTests
{
    private static readonly RaidRewardHookContext Valid = new(
        RaidRewardHookContract.VerifiedViolet400BuildId,
        "session-1", 0x1234ABCD, 0x1234ABCD, true, true, true, true);

    private static byte[] FakeFrame()
    {
        var data = new byte[0xAA0];
        "RaidPoint_"u8.CopyTo(data.AsSpan(0x6B8));
        return data;
    }

    [Fact]
    public void OfflineValidSessionPatchesOnlyRewardRegionOnce()
    {
        var handler = new RaidRewardHookContract();
        var frame = FakeFrame();
        var copy = frame.ToArray();
        var result = handler.TryStageOffline(Valid, frame, RaidRewardPresets.ScreenshotCharm45Slots());
        Assert.True(result.Accepted, result.Reason);
        Assert.NotNull(result.ModifiedPlaintext);
        Assert.True(RaidPointOfflinePatch.UntouchedFieldsEqual(frame,
            result.ModifiedPlaintext!, 0x6B8));
        Assert.Equal(copy, frame); // never mutates source
        Assert.Equal(1, BitConverter.ToInt32(result.ModifiedPlaintext!, 0x79C + 10*16));
        Assert.Equal(5, BitConverter.ToInt32(result.ModifiedPlaintext!, 0x79C + 10*16+12));
        Assert.False(handler.TryStageOffline(Valid, frame,
            RaidRewardPresets.ScreenshotCharm45Slots()).Accepted);
    }

    [Theory]
    [InlineData("wrong-build", true, true, true, true, 123, 123)]
    [InlineData(RaidRewardHookContract.VerifiedViolet400BuildId, false, true, true, true, 123, 123)]
    [InlineData(RaidRewardHookContract.VerifiedViolet400BuildId, true, false, true, true, 123, 123)]
    [InlineData(RaidRewardHookContract.VerifiedViolet400BuildId, true, true, false, true, 123, 123)]
    [InlineData(RaidRewardHookContract.VerifiedViolet400BuildId, true, true, true, false, 123, 123)]
    [InlineData(RaidRewardHookContract.VerifiedViolet400BuildId, true, true, true, true, 123, 124)]
    public void RejectsUnverifiedLiveConditions(string build, bool host,
        bool outbound, bool preCompression, bool online, uint seed, uint observed)
    {
        var ctx = Valid with {
            BuildId = build, NativeHostConfirmed = host,
            OutboundBootstrapConfirmed = outbound, BeforeCompression = preCompression,
            IsOfficialOnline = online, SelectedSeed = seed, ObservedSeed = observed,
        };
        var result = new RaidRewardHookContract().TryStageOffline(ctx,
            FakeFrame(), RaidRewardPresets.ScreenshotCharm45Slots());
        Assert.False(result.Accepted);
        Assert.Null(result.ModifiedPlaintext);
    }
}
