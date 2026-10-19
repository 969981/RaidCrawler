using RaidCrawler.Core.Structures;
using Xunit;

namespace RaidCrawler.Tests;

public sealed class RaidRewardGlobalProfileStoreTests
{
    [Fact]
    public void FirstRunStartsWith20CharmRowsAnd25EmptySlots()
    {
        string path = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"), "rewards.json");
        var rows = RaidRewardGlobalProfileStore.Load(path);
        Assert.Equal(45, rows.Count);
        Assert.Equal(20, rows.Count(x=>x is not null));
        Assert.All(rows.Where(x=>x is not null), x=>Assert.Equal(5,x!.Subject));
    }

    [Fact]
    public void SavesAndRestoresGlobalSlotsWithoutAnyMapOrSeed()
    {
        string folder = Path.Combine(Path.GetTempPath(), "RaidCrawlerTests",Guid.NewGuid().ToString("N"));
        string path=Path.Combine(folder,"profile.json");
        try
        {
            var slots=RaidRewardPresets.ScreenshotCharm45Slots();
            slots[0]=new RaidRewardEntry(796,10,0,0);
            slots[20]=new RaidRewardEntry(1,33,0,4);
            RaidRewardGlobalProfileStore.Save(path,slots);
            var again=RaidRewardGlobalProfileStore.Load(path);
            Assert.Equal(slots,again);
            Assert.Equal(45,again.Count);
            Assert.Null(again[21]);
            Assert.DoesNotContain("321EE013",File.ReadAllText(path));
            Assert.DoesNotContain("den",File.ReadAllText(path),StringComparison.OrdinalIgnoreCase);
        }
        finally { if(Directory.Exists(folder)) Directory.Delete(folder,true); }
    }

    [Fact]
    public void InvalidUpdateDoesNotCorruptExistingProfile()
    {
        string folder = Path.Combine(Path.GetTempPath(), "RaidCrawlerTests",Guid.NewGuid().ToString("N"));
        string path=Path.Combine(folder,"profile.json");
        try
        {
            var good=RaidRewardPresets.ScreenshotCharm45Slots();
            RaidRewardGlobalProfileStore.Save(path, good);
            var original=File.ReadAllText(path);
            var invalid=RaidRewardPresets.ScreenshotCharm45Slots();
            invalid[0]=new RaidRewardEntry(1,1000,0,5);
            Assert.Throws<ArgumentException>(()=>RaidRewardGlobalProfileStore.Save(path,invalid));
            Assert.Equal(original,File.ReadAllText(path));
            Assert.Equal(good,RaidRewardGlobalProfileStore.Load(path));
        }
        finally { if(Directory.Exists(folder)) Directory.Delete(folder,true); }
    }
}
