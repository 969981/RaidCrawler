using System.Buffers.Binary;

namespace RaidCrawler.Core.Structures;

/// <summary>One 16-byte row of SV RaidPoint (not a save-block entry).</summary>
public sealed record RaidRewardEntry(int ItemId, int Quantity, int Rare, int Subject);

/// <summary>Local-only reward editing and 45-row encoding; does not write to the Switch.</summary>
public static class RaidRewardEditor
{
    public const int MaxRows = 45;
    public const int MaxQuantity = 999;
    public const int RowSize = 16;
    public const int RewardBytes = MaxRows * RowSize;
    public const int RaidPointRowOffset = 0xE4;
    public const int BootstrapRowOffset = 0x6B8 + RaidPointRowOffset;

    public static IReadOnlyList<RaidRewardEntry> FromCalculated(
        IEnumerable<(int ItemId, int Quantity, int Subject)> calculated, int species, int teraType)
    {
        var rows = calculated.Select(r => new RaidRewardEntry(
            r.ItemId switch
            {
                10000 => Rewards.GetMaterial(species),
                20000 => Rewards.GetTeraShard(teraType),
                _ => r.ItemId,
            },
            r.Quantity, 0, r.Subject)).ToArray();
        Validate(rows);
        return rows;
    }

    public static void Validate(IReadOnlyList<RaidRewardEntry> rows)
    {
        ArgumentNullException.ThrowIfNull(rows);
        if (rows.Count > MaxRows)
            throw new ArgumentException($"RaidPoint supports at most {MaxRows} reward rows.");
        for (int i = 0; i < rows.Count; i++)
        {
            var row = rows[i];
            if (row is null || row.ItemId is < 1 or > 65535 || row.ItemId is 10000 or 20000)
                throw new ArgumentException($"Row {i + 1}: use a real item ID (1..65535), not a display-only placeholder.");
            if (row.Quantity is < 1 or > MaxQuantity)
                throw new ArgumentException($"Row {i + 1}: quantity must be 1..{MaxQuantity}.");
            if (row.Rare is not (0 or 1))
                throw new ArgumentException($"Row {i + 1}: rare flag must be 0 or 1.");
            if (row.Subject is < 0 or > 5)
                throw new ArgumentException($"Row {i + 1}: subject must be 0..5.");
        }
    }

    /// <summary>
    /// Builds command-line reward flags for PokéLDN's actual supported custom-host grammar.
    /// This is separate from the raw row format: PokéLDN accepts only subjects 0 and 5
    /// and forces custom rare flags to zero.
    /// </summary>
    public static string BuildPokeLdnRewardArguments(IReadOnlyList<RaidRewardEntry> rows)
    {
        Validate(rows);
        if (rows.Count == 0)
            throw new ArgumentException("Choose at least one reward for PokéLDN.");
        if (rows.Any(r => r.Subject is not (0 or 5) || r.Rare != 0))
            throw new ArgumentException("PokéLDN custom rewards support only subject 0/5 and rare=0. Export raw rows for other values.");
        return string.Join(" ", rows.Select(r =>
            $"--raid-reward {r.ItemId}:{r.Quantity}" + (r.Subject == 5 ? ":5" : "")));
    }

    /// <summary>Returns ONLY the 720 reward bytes, not a full RaidPoint or network packet.</summary>
    public static byte[] Encode(IReadOnlyList<RaidRewardEntry> rows)
    {
        Validate(rows);
        byte[] result = new byte[RewardBytes];
        for (int i = 0; i < rows.Count; i++)
        {
            var row = rows[i];
            int start = i * RowSize;
            BinaryPrimitives.WriteUInt32LittleEndian(result.AsSpan(start), (uint)row.ItemId);
            BinaryPrimitives.WriteUInt32LittleEndian(result.AsSpan(start + 4), (uint)row.Quantity);
            BinaryPrimitives.WriteUInt32LittleEndian(result.AsSpan(start + 8), (uint)row.Rare);
            BinaryPrimitives.WriteUInt32LittleEndian(result.AsSpan(start + 12), (uint)row.Subject);
        }
        return result;
    }

    public static IReadOnlyList<RaidRewardEntry> Decode(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length != RewardBytes)
            throw new ArgumentException($"Reward row block must be exactly {RewardBytes} bytes.");
        var rows = new List<RaidRewardEntry>();
        bool paddingSeen = false;
        for (int i = 0; i < MaxRows; i++)
        {
            var row = bytes.Slice(i * RowSize, RowSize);
            uint id = BinaryPrimitives.ReadUInt32LittleEndian(row);
            uint quantity = BinaryPrimitives.ReadUInt32LittleEndian(row[4..]);
            uint rare = BinaryPrimitives.ReadUInt32LittleEndian(row[8..]);
            uint subject = BinaryPrimitives.ReadUInt32LittleEndian(row[12..]);
            if (id == 0 && quantity == 0 && rare == 0 && subject == 0)
            {
                paddingSeen = true;
                continue;
            }
            if (paddingSeen)
                throw new ArgumentException($"Row {i + 1} appears after padding.");
            if (id > int.MaxValue || quantity > int.MaxValue || rare > int.MaxValue || subject > int.MaxValue)
                throw new ArgumentException($"Row {i + 1} contains a value too large.");
            rows.Add(new RaidRewardEntry((int)id, (int)quantity, (int)rare, (int)subject));
        }
        Validate(rows);
        return rows;
    }
}
