using PKHeX.Core;
using RaidCrawler.Core.Structures;
using Xunit;

namespace RaidCrawler.Tests.Host;

public class SimplifiedChineseLocalizationTests
{
    [Fact]
    public void PKHeXProvidesSimplifiedChinesePokemonNames()
    {
        var localized = GameInfo.GetStrings("zh-Hans");
        Assert.Equal("妙蛙种子", localized.Species[1]);
        Assert.Equal("皮卡丘", localized.Species[25]);
        Assert.NotEqual(GameInfo.GetStrings("en").Species[25], localized.Species[25]);
    }

    [Fact]
    public void ChineseAreaNamesKeepOriginalIndexMapping()
    {
        Assert.Equal("南第1区", Areas.GetArea(0, TeraRaidMapParent.Paldea));
        Assert.Equal("草原区", Areas.GetArea(0, TeraRaidMapParent.Blueberry));
    }
}
