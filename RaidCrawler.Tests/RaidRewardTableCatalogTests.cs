using RaidCrawler.Core.Structures;
using Xunit;

namespace RaidCrawler.Tests;

public sealed class RaidRewardTableCatalogTests
{
    [Fact]
    public void BundledBaseTablesHaveExpectedShapeAndSeparateProvenance()
    {
        var audit = RaidRewardTableCatalog.LoadBaseTables();
        Assert.Equal(37, audit.FixedTables);
        Assert.Equal(37, audit.LotteryTables);
        Assert.Equal(49, audit.DistinctKeys);
        Assert.Equal("EMBEDDED_BASE_RESOURCES_NOT_LIVE_SWITCH", audit.Provenance);
        Assert.Equal(64, audit.FixedSha256.Length);
        Assert.Equal(64, audit.LotterySha256.Length);
        Assert.All(audit.Rows, row =>
        {
            Assert.InRange(row.Slot, 0, row.Source == "fixed" ? 14 : 29);
            Assert.Contains(row.Source, new[] { "fixed", "lottery" });
            if (row.Source == "fixed")
            {
                Assert.NotNull(row.Subject);
                Assert.Null(row.Weight);
                Assert.Null(row.Rare);
            }
            else
            {
                Assert.Null(row.Subject);
                Assert.NotNull(row.Weight);
                Assert.NotNull(row.Rare);
            }
        });
    }

    [Fact]
    public void LookupUsesExactUInt64TableNameNoFloatingPointPrecisionLoss()
    {
        var audit = RaidRewardTableCatalog.LoadBaseTables();
        const ulong key = 12665484173717205406UL;
        var rows = RaidRewardTableCatalog.ForTable(audit, key);
        Assert.Contains(rows, row => row.Source == "fixed" && row.Slot == 0 &&
                                      row.ItemId == 4 && row.Quantity == 1 &&
                                      row.Category == 0 && row.Subject == 0);
        Assert.Contains(rows, row => row.Source == "lottery" && row.Slot == 0 &&
                                      row.ItemId == 1124 && row.Weight == 50);
        Assert.All(rows, row => Assert.Equal(key, row.TableName));
        Assert.Empty(RaidRewardTableCatalog.ForTable(audit, ulong.MaxValue));
    }
}
