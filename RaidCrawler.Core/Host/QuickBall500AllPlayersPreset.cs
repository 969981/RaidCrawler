namespace RaidCrawler.Core.Host;

/// <summary>
/// Single-row offline research preset matching the published PokéLDN
/// Quick Ball ×500 all-participants test parameters.
/// This does NOT reproduce PokéLDN's synthetic-host Bootstrap replay.
/// </summary>
public static class QuickBall500AllPlayersPreset
{
    public const string DisplayName = "全体玩家 · 先机球 ×500（单行验证）";
    public const uint SubjectType = 0; // all recipients (candidate semantic)
    public const uint ItemId = 15; // Quick Ball
    public const uint Quantity = 500;
    public const uint Reserved = 0;

    public static RewardRow Row => new(ItemId, Quantity, Reserved, SubjectType);

    /// <summary>
    /// Full 16-byte little-endian record, NOT an item ID at row offset zero.
    /// At a 4.0.0 RaidPoint it occupies one vacant row starting at +0xE0+16*N.
    /// </summary>
    public static byte[] GetRecordBytes()
    {
        var data = new byte[RaidPointLayout.RewardRowLength];
        Row.Write(data);
        return data;
    }

    /// <summary>
    /// Offline-only, atomically plan one new row without changing original rewards.
    /// Live writes require all existing owner/room/version/memory checks.
    /// </summary>
    public static RewardAppendPlan Plan(ReadOnlySpan<byte> original) =>
        RewardAppendPlanner.Plan(original, [Row], id => id == ItemId, SubjectType);
}
