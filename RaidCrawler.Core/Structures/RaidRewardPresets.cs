namespace RaidCrawler.Core.Structures;

/// <summary>
/// 20 screenshot-based Glimmering Charm-only rewards for manual SV hosting research.
/// Missing rows remain empty (zero-filled) in the 45×16-byte output.
/// All quantities are *requested* rewards, never guaranteed bag deltas.
/// </summary>
public static class RaidRewardPresets
{
    public const int PresetRows = 20;
    public const int CharmSubject = 5;
    public const int CharmItemId = 2481;

    public static IReadOnlyList<RaidRewardEntry> ScreenshotCharm999()
    {
        int[] items = [
            492, // Fast Ball (速度球)
            493, // Level Ball (等级球)
            494, // Lure Ball (诱饵球)
            495, // Heavy Ball (沉重球)
            496, // Love Ball (甜蜜球)
            497, // Friend Ball (友友球)
            498, // Moon Ball (月亮球)
            499, // Sport Ball (竞赛球)
            795, // Bottle Cap (银色王冠)
            796, // Gold Bottle Cap (金色王冠)
            1,   // Master Ball (大师球)
            50,  // Rare Candy (神奇糖果)
            1128,// Exp Candy XL (经验糖果 XL)
            581, // Big Nugget (巨大金珠)
            1907,// Bitter Herba Mystica (秘传：苦味料)
            1908,// Spicy Herba Mystica (秘传：辣味料)
            1906,// Sour Herba Mystica (秘传：酸味料)
            1904,// Sweet Herba Mystica (秘传：甜味料)
            1905,// Salty Herba Mystica (秘传：咸味料)
            1606,// Ability Patch (特性膏药)
        ];
        var rows = items.Select(item => new RaidRewardEntry(item, 999, 0, CharmSubject)).ToArray();
        RaidRewardEditor.Validate(rows);
        return rows;
    }

    /// <summary>Never reuses the same mutable list between editor sessions.</summary>
    public static RaidRewardEntry?[] ScreenshotCharm45Slots()
    {
        var slots = new RaidRewardEntry?[RaidRewardEditor.MaxRows];
        var preset = ScreenshotCharm999();
        for (int i = 0; i < preset.Count; i++)
            slots[i] = preset[i];
        return slots;
    }
}
