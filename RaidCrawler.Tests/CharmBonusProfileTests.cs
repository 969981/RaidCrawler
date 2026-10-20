using System.Text.Json;
using RaidCrawler.Core.Structures;
using Xunit;

namespace RaidCrawler.Tests;

public sealed class CharmBonusProfileTests
{
    private static CharmBonusProfile Example(bool enabled = true) =>
        CharmBonusProfile.Create(enabled,
            [new CharmBonusItem(111, 10), new CharmBonusItem(111, 10)]);

    [Fact]
    public void ProfileIsSeparateFromLegacyAndDisabledByDefault()
    {
        var dir = Path.Combine(Path.GetTempPath(), "CharmBonusTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            string oldPath = Path.Combine(dir, "global-raid-rewards.json");
            string newPath = Path.Combine(dir, "charm-bonus-append.json");
            var slots = RaidRewardPresets.ScreenshotCharm45Slots();
            slots[21] = new RaidRewardEntry(1, 7, 0, 0);
            RaidRewardGlobalProfileStore.Save(oldPath, slots);
            string legacyBytes = File.ReadAllText(oldPath);
            var migrated = CharmBonusProfileStore.Load(newPath, oldPath);
            Assert.False(migrated.Enabled);
            Assert.Equal(20, migrated.Items.Count);
            Assert.False(File.Exists(newPath));
            Assert.Equal(legacyBytes, File.ReadAllText(oldPath));
            Assert.Empty(migrated.AsRewardRows());
            var enabled = CharmBonusProfile.Create(true, migrated.Items);
            var saved = CharmBonusProfileStore.Save(newPath, enabled);
            Assert.Equal(1, saved.Revision);
            Assert.Equal(saved, CharmBonusProfileStore.Load(newPath, oldPath));
            Assert.Equal(legacyBytes, File.ReadAllText(oldPath));

            var disabled = CharmBonusProfileStore.Save(newPath,
                CharmBonusProfile.Create(false, saved.Items));
            Assert.Equal(2, disabled.Revision);
            Assert.Empty(CharmBonusProfileStore.Load(newPath).AsRewardRows());
            Assert.Equal(legacyBytes, File.ReadAllText(oldPath));
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void MissingAnyProfileStartsDisabledWithNoBonus()
    {
        string empty = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        var result = CharmBonusProfileStore.Load(
            Path.Combine(empty, "new.json"), Path.Combine(empty, "old.json"));
        Assert.False(result.Enabled);
        Assert.Empty(result.Items);
        result.Validate();
    }

    [Fact]
    public void InvalidSubjectAndRowsAreNeverSilentlyConverted()
    {
        Assert.Throws<ArgumentException>(() => CharmBonusProfile.Create(true,
            Enumerable.Repeat(new CharmBonusItem(1, 1), 21)));
        Assert.Throws<ArgumentException>(() => CharmBonusProfile.Create(true,
            [new CharmBonusItem(20000, 1)]));
        Assert.Throws<ArgumentException>(() => CharmBonusProfile.Create(true,
            [new CharmBonusItem(20, 1000)]));
        var prof = Example();
        Assert.All(prof.AsRewardRows(), x =>
        {
            Assert.Equal(5, x.Subject);
            Assert.Equal(0, x.Rare);
        });
        Assert.Equal(prof.AsRewardRows()[0], prof.AsRewardRows()[1]);
    }

    [Fact]
    public void TamperingOrBadWritePreservesPreviouslySavedProfile()
    {
        var dir = Path.Combine(Path.GetTempPath(), "CharmBonusTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            string path = Path.Combine(dir, "charm.json");
            var saved = CharmBonusProfileStore.Save(path, Example());
            string previous = File.ReadAllText(path);
            var invalid = saved with { Items = [new CharmBonusItem(1, 1001)] };
            Assert.ThrowsAny<Exception>(() => CharmBonusProfileStore.Save(path, invalid));
            Assert.Equal(previous, File.ReadAllText(path));
            File.WriteAllText(path, previous.Replace("111", "112"));
            Assert.Throws<InvalidDataException>(() => CharmBonusProfileStore.Load(path));
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void ImportMoreThan20LegacyCharmRowsRejectsInsteadOfTruncating()
    {
        var dir = Path.Combine(Path.GetTempPath(), "CharmBonusTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            string oldPath = Path.Combine(dir, "old.json");
            var slots = new RaidRewardEntry?[45];
            for (int i = 0; i < 21; i++)
                slots[i] = new RaidRewardEntry(101 + i, 1, 0, 5);
            RaidRewardGlobalProfileStore.Save(oldPath, slots);
            Assert.Throws<ArgumentException>(() => CharmBonusProfileStore.Load(
                Path.Combine(dir, "new.json"), oldPath));
            Assert.True(File.Exists(oldPath));
            Assert.False(File.Exists(Path.Combine(dir, "new.json")));
        }
        finally { Directory.Delete(dir, true); }
    }
}
