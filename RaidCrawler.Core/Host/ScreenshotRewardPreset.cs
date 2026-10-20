namespace RaidCrawler.Core.Host;

/// <summary>
/// Exact 20 × 999 screenshot preset, for a 45-row SV RaidPoint reward table.
/// This is a byte-layout/editor preset: no claim the retail receiver grants 999
/// for every item, that an existing bag accepts 999 more, or that an observed
/// owner is truly selected for network serialization.
/// </summary>
public static class ScreenshotRewardPreset
{
    public const uint Quantity = 999;
    public const uint Subject = 5;
    public const int Count = 20;
    public const int Bytes = Count * RaidPointLayout.RewardRowLength;
    public const int MaxOriginalRows = RaidPointLayout.RewardCapacity - Count; // 25

    public static IReadOnlyList<ScreenshotPresetItem> Items { get; } =
        Array.AsReadOnly(new ScreenshotPresetItem[]
        {
            new("速度球", 492),           // Fast Ball
            new("等级球", 493),           // Level Ball
            new("诱饵球", 494),           // Lure Ball
            new("沉重球", 495),           // Heavy Ball
            new("甜蜜球", 496),           // Love Ball
            new("友友球", 497),           // Friend Ball
            new("月亮球", 498),           // Moon Ball
            new("竞赛球", 499),           // Sport Ball
            new("银色王冠", 795),         // Bottle Cap
            new("金色王冠", 796),         // Gold Bottle Cap
            new("大师球", 1),             // Master Ball
            new("神奇糖果", 50),          // Rare Candy
            new("经验糖果 XL", 1128),     // Exp. Candy XL
            new("巨大金珠", 581),         // Big Nugget (not Nugget 92)
            new("苦味料", 1907),          // Bitter Herba Mystica
            new("辣味料", 1908),          // Spicy Herba Mystica
            new("酸味料", 1906),          // Sour Herba Mystica
            new("甜味料", 1904),          // Sweet Herba Mystica
            new("咸味料", 1905),          // Salty Herba Mystica
            new("特性膏药", 1606),        // Ability Patch
        });

    public static IReadOnlyList<RewardRow> BuildRows() =>
        Items.Select(item => new RewardRow(item.ItemId, Quantity, 0, Subject)).ToArray();

    public static RewardAppendPlan Plan(ReadOnlySpan<byte> source) =>
        RewardAppendPlanner.Plan(source, BuildRows(), id => Items.Any(item => item.ItemId == id));

    /// <summary>
    /// Checks EXACTLY the 20 slots in screenshot order and untouched bytes
    /// everywhere else in the 0x3e8 original. Fail closed, no partial result.
    /// </summary>
    public static string Audit(ReadOnlySpan<byte> original,
        ReadOnlySpan<byte> candidate, int originalRows)
    {
        if (original.Length != RaidPointLayout.RaidPointLength ||
            candidate.Length != RaidPointLayout.RaidPointLength)
            return "INVALID_RAIDPOINT_LENGTH";
        if (originalRows is < 0 or > MaxOriginalRows)
            return "INSUFFICIENT_REWARD_SLOTS";
        int offset = RaidPointLayout.RewardsInRaidPoint +
                     originalRows * RaidPointLayout.RewardRowLength;
        int end = offset + Bytes;
        if (!original.Slice(offset, Bytes).SequenceEqual(new byte[Bytes]))
            return "TARGET_SLOTS_NOT_EMPTY";
        if (!original[..offset].SequenceEqual(candidate[..offset]))
            return "ORIGINAL_REWARDS_OR_HEADER_CHANGED";
        if (!original[end..].SequenceEqual(candidate[end..]))
            return "OTHER_RAIDPOINT_BYTES_CHANGED";
        for (int i = 0; i < Count; i++)
        {
            var item = Items[i];
            var actual = RewardRow.Read(candidate.Slice(offset + i * 16, 16));
            if (actual != new RewardRow(item.ItemId, Quantity, 0, Subject))
                return $"PRESET_ROW_MISMATCH_{i}";
        }
        return "EXACT_20_ROWS_ONLY";
    }
}

public sealed record ScreenshotPresetItem(string Name, uint ItemId);
