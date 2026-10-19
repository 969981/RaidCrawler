using PKHeX.Core;
using RaidCrawler.Core.Structures;
using Xunit;

namespace RaidCrawler.Tests;

public class ChineseGameStringsTests
{
    [Fact]
    public void PkhexCoreContainsSimplifiedChineseItemSpeciesAndMoveNames()
    {
        var chinese = GameInfo.GetStrings("zh-Hans");
        var english = GameInfo.GetStrings("en");
        // Representative reward and boss data use the same numerical IDs in both locales.
        Assert.False(string.IsNullOrWhiteSpace(chinese.Item[1606])); // Ability Patch
        Assert.False(string.IsNullOrWhiteSpace(chinese.Item[796]));  // Gold Bottle Cap
        Assert.False(string.IsNullOrWhiteSpace(chinese.Move[(int)Move.ThunderPunch]));
        Assert.False(string.IsNullOrWhiteSpace(chinese.Species[(int)Species.Metagross]));
        Assert.NotEqual(english.Item[1606], chinese.Item[1606]);
        Assert.NotEqual(english.Species[(int)Species.Metagross], chinese.Species[(int)Species.Metagross]);
    }

    [Fact]
    public void RaidContainerDefaultsToChineseGameStrings()
    {
        var container = new RaidContainer("Violet");
        Assert.Equal(GameInfo.GetStrings("zh-Hans").Item[1606], container.Strings.Item[1606]);
        // Item localization is a presentation concern and must never alter reward serialization.
        var reward = new RaidRewardEntry(1606, 999, 0, 5);
        Assert.Equal(1606, reward.ItemId);
        Assert.Equal(5, reward.Subject);
    }

    [Fact]
    public void ExplicitEnglishLanguageRemainsAvailable()
    {
        var container = new RaidContainer("Scarlet", "en");
        Assert.Equal(GameInfo.GetStrings("en").Item[1606], container.Strings.Item[1606]);
    }
}
