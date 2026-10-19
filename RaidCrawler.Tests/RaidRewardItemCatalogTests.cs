using PKHeX.Core;
using RaidCrawler.Core.Structures;
using Xunit;

namespace RaidCrawler.Tests;

public sealed class RaidRewardItemCatalogTests
{
    [Fact]
    public void ChineseEnglishAndIdFuzzySearchKeepStableId()
    {
        var catalog = new RaidRewardItemCatalog([
            new RaidRewardItemOption(795, "银色王冠", "Bottle Cap"),
            new RaidRewardItemOption(796, "金色王冠", "Gold Bottle Cap"),
            new RaidRewardItemOption(1128, "经验糖果ＸＬ", "Exp. Candy XL"),
        ]);
        Assert.Equal([795, 796], catalog.Search("王冠").Select(o=>o.Id));
        Assert.Equal(796, catalog.Search("gold").Single().Id);
        Assert.Equal(796, catalog.Search("796").First().Id);
        Assert.Equal(1128, catalog.Search("经果").First().Id);
        Assert.Empty(catalog.Search("no-such-item-xyz"));
    }

    [Fact]
    public void RealGen9CatalogSupportsAll20DefaultItemsButExcludesKeyItems()
    {
        var names = GameInfo.GetStrings("zh-Hans").Item;
        var english = GameInfo.GetStrings("en").Item;
        var catalog = RaidRewardItemCatalog.FromGameStrings(names, english);
        foreach (var row in RaidRewardPresets.ScreenshotCharm999())
        {
            Assert.True(catalog.TryFind(row.ItemId, out var option), $"Unlisted SV item ID {row.ItemId}");
            Assert.False(string.IsNullOrWhiteSpace(option!.Name));
        }
        Assert.True(catalog.TryFind(796, out _));
        Assert.Contains(catalog.Search("王冠"), x => x.Id == 796);
        Assert.False(catalog.TryFind(2481, out _)); // Glimmering Charm is a key item, not a reward
        Assert.False(catalog.TryFind(10000, out _));
    }

    [Fact]
    public void EmptyQueryShowsFeaturedPresetFirst()
    {
        var catalog = new RaidRewardItemCatalog([
            new RaidRewardItemOption(796, "金色王冠", "Gold Bottle Cap"),
            new RaidRewardItemOption(492, "速度球", "Fast Ball"),
            new RaidRewardItemOption(7, "潜水球", "Dive Ball"),
        ]);
        Assert.Equal([492, 796, 7], catalog.Search("").Select(x=>x.Id));
    }
}
