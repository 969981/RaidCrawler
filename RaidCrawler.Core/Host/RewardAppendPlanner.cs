namespace RaidCrawler.Core.Host;

/// <summary>
/// Pure offline experiment. No output must be sent to a game unless the
/// captured version and reward interpretation are independently validated.
/// Keeps the original bytes unchanged on every rejected plan.
/// </summary>
public static class RewardAppendPlanner
{
    public const uint GlimmeringCharmMarker = 5;
    // UI/editor limit, NOT the receiving item's tested SlotMaxNum.
    public const uint EditorQuantityLimit = 999;

    public static RewardAppendPlan Plan(
        ReadOnlySpan<byte> sourceRaidPoint,
        IReadOnlyList<RewardRow> additions,
        Func<uint, bool> isAllowedBagItem)
    {
        ArgumentNullException.ThrowIfNull(additions);
        ArgumentNullException.ThrowIfNull(isAllowedBagItem);
        byte[] candidate = sourceRaidPoint.ToArray();
        RewardAppendPlan Reject(string reason, int count = 0) =>
            new(false, reason, count, 0, candidate);

        if (sourceRaidPoint.Length != RaidPointLayout.RaidPointLength)
            return Reject("INVALID_RAIDPOINT_LENGTH");

        int count = 0;
        bool sawPadding = false;
        var rows = RaidPointLayout.GetRaidPointRewards(sourceRaidPoint);
        for (int i = 0; i < RaidPointLayout.RewardCapacity; i++)
        {
            RewardRow row = RewardRow.Read(rows.Slice(
                i * RaidPointLayout.RewardRowLength, RaidPointLayout.RewardRowLength));
            if (row.IsEmpty)
            {
                sawPadding = true;
                continue;
            }

            if (sawPadding)
                return Reject("NONCONTIGUOUS_ORIGINAL_ROWS", count);
            // Important: the retail donor contains (5,0,0,0), which is not an
            // ordinary item and may delimit or control the reward table.
            // Never append through such an ambiguous metadata row.
            if (row.ItemId == 0 || row.Quantity == 0 || row.Reserved != 0 || row.Marker > 5)
                return Reject("AMBIGUOUS_ORIGINAL_ROW", count);
            count++;
        }

        if (additions.Count > RaidPointLayout.RewardCapacity - count)
            return Reject("INSUFFICIENT_REWARD_SLOTS", count);

        // Precheck all additions before writing one byte.
        foreach (RewardRow row in additions)
        {
            if (row.ItemId == 0 || !isAllowedBagItem(row.ItemId))
                return Reject("ITEM_NOT_ALLOWLISTED", count);
            if (row.Quantity is 0 or > EditorQuantityLimit)
                return Reject("INVALID_QUANTITY", count);
            if (row.Marker != GlimmeringCharmMarker)
                return Reject("MARKER_NOT_GLIMMERING_CHARM", count);
            if (row.Reserved != 0)
                return Reject("UNSUPPORTED_RESERVED_FIELD", count);
        }

        for (int i = 0; i < additions.Count; i++)
        {
            int offset = RaidPointLayout.RewardsInRaidPoint +
                         (count + i) * RaidPointLayout.RewardRowLength;
            additions[i].Write(candidate.AsSpan(offset, RaidPointLayout.RewardRowLength));
        }

        return new(true, "OK_OFFLINE_ONLY", count, additions.Count, candidate);
    }
}

public sealed record RewardAppendPlan(
    bool Accepted,
    string Reason,
    int OriginalCount,
    int AddedCount,
    byte[] CandidateRaidPoint);
