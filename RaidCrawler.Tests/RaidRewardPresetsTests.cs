using RaidCrawler.Core.Structures;
using Xunit;

namespace RaidCrawler.Tests;

public class RaidRewardPresetsTests
{
    [Fact]
    public void ScreenshotMatches20CharmOnlyRowsAnd25EmptySlots()
    {
        int[] ids = [492,493,494,495,496,497,498,499,795,796,1,50,1128,581,1907,1908,1906,1904,1905,1606];
        var template = RaidRewardPresets.ScreenshotCharm999();
        Assert.Equal(20, template.Count);
        Assert.Equal(ids, template.Select(r => r.ItemId));
        Assert.All(template, r => { Assert.Equal(999,r.Quantity); Assert.Equal(0,r.Rare); Assert.Equal(5,r.Subject); });
        var slots = RaidRewardPresets.ScreenshotCharm45Slots();
        Assert.Equal(45, slots.Length);
        Assert.All(slots.Take(20), r => Assert.NotNull(r));
        Assert.All(slots.Skip(20), r => Assert.Null(r));
    }

    [Fact]
    public void Encoded45SlotsHaveZeroFilledTailAndRoundTrip()
    {
        var template = RaidRewardPresets.ScreenshotCharm999();
        byte[] bytes = RaidRewardEditor.Encode(template);
        Assert.Equal(720, bytes.Length);
        Assert.All(bytes.AsSpan(20 * 16).ToArray(), b => Assert.Equal((byte)0, b));
        Assert.Equal(template, RaidRewardEditor.Decode(bytes));
    }

    [Fact]
    public void SupportsAllSixSubjectsWithoutConvertingToAllPlayers()
    {
        var rows = Enumerable.Range(0,6).Select(i => new RaidRewardEntry(492+i, 99, 0, i)).ToArray();
        Assert.Equal(rows, RaidRewardEditor.Decode(RaidRewardEditor.Encode(rows)));
        Assert.Throws<ArgumentException>(() => RaidRewardEditor.BuildPokeLdnRewardArguments(rows));
    }

    [Fact]
    public void FortySixRowsAlwaysRejected()
    {
        var rows = Enumerable.Repeat(new RaidRewardEntry(492,999,0,5),46).ToArray();
        Assert.Throws<ArgumentException>(()=>RaidRewardEditor.Encode(rows));
    }
}
