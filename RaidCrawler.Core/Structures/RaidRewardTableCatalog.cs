using System.Security.Cryptography;
using System.Text.Json;

namespace RaidCrawler.Core.Structures;

/// <summary>
/// Bundled BASE RESOURCE reward tables only. This does not inspect live
/// Switch memory, raid seed, event BCAT overlays or settled reward rows.
/// </summary>
public static class RaidRewardTableCatalog
{
    public sealed record Row(ulong TableName, string Source, int Slot,
        int Category, int ItemId, int Quantity, int? Subject, int? Weight, bool? Rare);

    public sealed record TableAudit(
        int FixedTables, int LotteryTables, int DistinctKeys,
        string FixedSha256, string LotterySha256, IReadOnlyList<Row> Rows,
        string Provenance = "EMBEDDED_BASE_RESOURCES_NOT_LIVE_SWITCH");

    private static (JsonDocument Doc, string Sha) ReadResource(string filename)
    {
        var assembly = typeof(RaidRewardTableCatalog).Assembly;
        string? resource = assembly.GetManifestResourceNames().SingleOrDefault(n =>
            n.EndsWith(".Base." + filename, StringComparison.OrdinalIgnoreCase));
        if (resource is null) throw new InvalidDataException("Embedded base table resource unavailable: " + filename);
        using var stream = assembly.GetManifestResourceStream(resource)
            ?? throw new InvalidDataException("Missing resource data");
        if (stream.Length is < 1 or > 512 * 1024)
            throw new InvalidDataException("Unexpected embedded table resource length");
        using var buffer = new MemoryStream();
        stream.CopyTo(buffer);
        byte[] bytes = buffer.ToArray();
        return (JsonDocument.Parse(bytes), Convert.ToHexString(SHA256.HashData(bytes)));
    }

    private static (int Tables, string Sha) Collect(
        string source, string resource, int maxSlots, List<Row> rows)
    {
        var (document, sha) = ReadResource(resource);
        using (document)
        {
            if (document.RootElement.ValueKind != JsonValueKind.Array)
                throw new InvalidDataException("Table resource root is not array: " + resource);
            var seen = new HashSet<ulong>();
            foreach (var table in document.RootElement.EnumerateArray())
            {
                ulong key = table.GetProperty("TableName").GetUInt64();
                if (!seen.Add(key)) throw new InvalidDataException("Duplicate TableName in base resource");
                for (int i = 0; i < maxSlots; i++)
                {
                    if (!table.TryGetProperty($"RewardItem{i:00}", out var slot) ||
                        slot.ValueKind != JsonValueKind.Object)
                        continue;
                    int category = slot.GetProperty("Category").GetInt32();
                    int item = slot.GetProperty("ItemID").GetInt32();
                    int qty = slot.GetProperty("Num").GetInt32();
                    int? subject = source == "fixed" ? slot.GetProperty("SubjectType").GetInt32() : null;
                    int? weight = source == "lottery" ? slot.GetProperty("Rate").GetInt32() : null;
                    bool? rare = source == "lottery" ? slot.GetProperty("RareItemFlag").GetBoolean() : null;
                    rows.Add(new Row(key, source, i, category, item, qty, subject, weight, rare));
                }
            }
            return (seen.Count, sha);
        }
    }

    public static TableAudit LoadBaseTables()
    {
        var rows = new List<Row>();
        var (fixedCount, fixedSha) = Collect(
            "fixed", "raid_fixed_reward_item_array.json", 15, rows);
        var (lotteryCount, lotterySha) = Collect(
            "lottery", "raid_lottery_reward_item_array.json", 30, rows);
        return new TableAudit(fixedCount, lotteryCount,
            rows.Select(r => r.TableName).Distinct().Count(),
            fixedSha, lotterySha, rows);
    }

    public static IReadOnlyList<Row> ForTable(TableAudit audit, ulong tableName) =>
        audit.Rows.Where(r => r.TableName == tableName)
            .OrderBy(r => r.Source == "fixed" ? 0 : 1).ThenBy(r => r.Slot).ToArray();
}
