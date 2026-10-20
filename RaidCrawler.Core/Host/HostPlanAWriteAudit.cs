namespace RaidCrawler.Core.Host;

/// <summary>
/// Pure byte-level contract for a ONE-ROW experimental process-memory edit.
/// Has no connection to Switch and makes no network-delivery claims.
/// </summary>
public static class HostPlanAWriteAudit
{
    public const string Correct = "EXACT_ONE_ROW_ONLY";

    public static string Check(ReadOnlySpan<byte> original, ReadOnlySpan<byte> proposed,
        int originalRows, uint itemId, uint quantity)
    {
        if (original.Length != RaidPointLayout.RaidPointLength ||
            proposed.Length != RaidPointLayout.RaidPointLength)
            return "INVALID_RAIDPOINT_LENGTH";
        if (itemId != 15 || quantity is 0 or > 10)
            return "ITEM_OR_QUANTITY_NOT_ALLOWED";
        if (originalRows is < 0 or >= RaidPointLayout.RewardCapacity)
            return "REWARD_CAPACITY";
        int offset = RaidPointLayout.RewardsInRaidPoint + originalRows * 16;
        if (!original.Slice(offset, 16).SequenceEqual(new byte[16]))
            return "APPEND_SLOT_NOT_EMPTY";
        if (!original[..offset].SequenceEqual(proposed[..offset]))
            return "ORIGINAL_CONTENT_MODIFIED";
        if (!original[(offset + 16)..].SequenceEqual(proposed[(offset + 16)..]))
            return "UNRELATED_CONTENT_MODIFIED";
        var row = RewardRow.Read(proposed.Slice(offset, 16));
        return row == new RewardRow(itemId, quantity, 0, 5)
            ? Correct : "WRONG_SUBJECT_OR_REWARD";
    }
}
