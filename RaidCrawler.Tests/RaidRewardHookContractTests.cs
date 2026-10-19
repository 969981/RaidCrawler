using RaidCrawler.Core.Structures;
using Xunit;

namespace RaidCrawler.Tests;

public class RaidRewardHookContractTests
{
    private static readonly RaidRewardHookContext VerifiedFixture = new(
        RaidRewardHookContract.VerifiedViolet400BuildId,
        "session-1", 0, 0x1234ABCD, true, true, true, false);

    private static byte[] FakeFrame()
    {
        var data = new byte[0xAA0];
        "RaidPoint_"u8.CopyTo(data.AsSpan(0x6B8));
        return data;
    }

    [Fact]
    public void AnyBattleSeedAndTransportAcceptOnceWithVerifiedRuntimePreconditions()
    {
        var handler = new RaidRewardHookContract();
        var frame = FakeFrame();
        var original = frame.ToArray();
        var slots = RaidRewardPresets.ScreenshotCharm45Slots();
        var context = VerifiedFixture with {
            // Prior selected den is intentionally unrelated to the actual session.
            SelectedSeed = 0x321EE013, ObservedSeed = 0x4EDCA321, IsOfficialOnline = false,
        };
        var result = handler.TryStageOffline(context, frame, slots);
        Assert.True(result.Accepted, result.Reason);
        Assert.NotNull(result.ModifiedPlaintext);
        Assert.Equal(original, frame);
        Assert.True(RaidPointOfflinePatch.UntouchedFieldsEqual(frame,
            result.ModifiedPlaintext!, 0x6B8));
        Assert.Equal(1, BitConverter.ToInt32(result.ModifiedPlaintext!,0x79C+10*16));
        Assert.Equal(5, BitConverter.ToInt32(result.ModifiedPlaintext!,0x79C+10*16+12));
        Assert.False(handler.TryStageOffline(context, frame, slots).Accepted);
        // New battle/session is independent, even when no UI den was selected.
        var next = context with { SessionId="session-2", SelectedSeed=0, ObservedSeed=0,
            IsOfficialOnline=true };
        Assert.True(handler.TryStageOffline(next, frame, slots).Accepted);
    }

    [Theory]
    [InlineData("wrong-build", true, true, true)]
    [InlineData(RaidRewardHookContract.VerifiedViolet400BuildId, false, true, true)]
    [InlineData(RaidRewardHookContract.VerifiedViolet400BuildId, true, false, true)]
    [InlineData(RaidRewardHookContract.VerifiedViolet400BuildId, true, true, false)]
    public void RejectsUnverifiedRuntimeCallbacks(string build, bool host, bool outbound, bool preCompression)
    {
        var ctx = VerifiedFixture with { BuildId=build, NativeHostConfirmed=host,
            OutboundBootstrapConfirmed=outbound, BeforeCompression=preCompression };
        var result = new RaidRewardHookContract().TryStageOffline(ctx,
            FakeFrame(), RaidRewardPresets.ScreenshotCharm45Slots());
        Assert.False(result.Accepted);
        Assert.Null(result.ModifiedPlaintext);
    }

    [Fact]
    public void RejectsMissingSessionOrUnknownPacketFormat()
    {
        var hook = new RaidRewardHookContract();
        var rows = RaidRewardPresets.ScreenshotCharm45Slots();
        Assert.False(hook.TryStageOffline(VerifiedFixture with { SessionId="" },
            FakeFrame(), rows).Accepted);
        Assert.False(hook.TryStageOffline(VerifiedFixture,
            new byte[0xAA0], rows).Accepted);
    }
}
