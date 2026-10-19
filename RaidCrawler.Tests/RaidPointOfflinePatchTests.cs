using RaidCrawler.Core.Structures;
using Xunit;

namespace RaidCrawler.Tests;

public class RaidPointOfflinePatchTests
{
    [Fact]
    public void SparsePhysicalSlotsRetainTheirPosition()
    {
        var slots = new RaidRewardEntry?[45];
        slots[0] = new RaidRewardEntry(1, 999, 0, 5);
        slots[44] = new RaidRewardEntry(796, 999, 0, 4);
        var bytes = RaidRewardEditor.EncodeSlots(slots);
        var restored = RaidRewardEditor.DecodeSlots(bytes);
        Assert.Equal(1, restored[0]!.ItemId);
        Assert.All(restored.Skip(1).Take(43), x => Assert.Null(x));
        Assert.Equal(796, restored[44]!.ItemId);
        Assert.Equal(4, restored[44]!.Subject);
        Assert.Equal(720, bytes.Length);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void OnlyRewardBytesChangeInUncompressedFixture(bool bootstrap)
    {
        int offset = bootstrap ? 0x6B8 : 0;
        var fixture = Enumerable.Range(0, bootstrap ? 0xAA0 : 0x3E8)
            .Select(i => (byte)(i * 27)).ToArray();
        "RaidPoint_"u8.CopyTo(fixture.AsSpan(offset));
        var rows = RaidRewardPresets.ScreenshotCharm45Slots();
        var patched = bootstrap
            ? RaidPointOfflinePatch.PatchBootstrap(fixture, rows)
            : RaidPointOfflinePatch.PatchRaidPoint(fixture, rows);
        Assert.True(RaidPointOfflinePatch.UntouchedFieldsEqual(fixture, patched, offset));
        Assert.Equal(1, BitConverter.ToInt32(patched, offset + 0xE4 + 10 * 16));
        Assert.Equal(5, BitConverter.ToInt32(patched, offset + 0xE4 + 10 * 16 + 12));
        Assert.All(patched.AsSpan(offset + 0xE4 + 20 * 16, 25 * 16).ToArray(),
            x => Assert.Equal((byte)0, x));
        Assert.NotEqual(fixture, patched);
    }

    [Fact]
    public void RefuseUnknownObjectsOrWrongLengths()
    {
        Assert.Throws<ArgumentException>(() => RaidPointOfflinePatch.PatchRaidPoint(
            new byte[0x3E8], RaidRewardPresets.ScreenshotCharm45Slots()));
        Assert.Throws<ArgumentException>(() => RaidPointOfflinePatch.PatchBootstrap(
            new byte[0xAA1], RaidRewardPresets.ScreenshotCharm45Slots()));
    }

    [Fact]
    public void InvalidRowsCannotBePatched()
    {
        var fixture = new byte[0x3E8];
        "RaidPoint_"u8.CopyTo(fixture);
        var slots = RaidRewardPresets.ScreenshotCharm45Slots();
        slots[0] = new RaidRewardEntry(1, 1000, 0, 5);
        Assert.Throws<ArgumentException>(() => RaidPointOfflinePatch.PatchRaidPoint(fixture, slots));
    }
}
