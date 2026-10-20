namespace RaidCrawler.Core.Host;

/// <summary>
/// Pure offline planner. Never writes Switch memory. Rejected plans contain
/// an unchanged copy of the input. The caller must supply a version-specific
/// item legality predicate; positive IDs alone do not prove bag eligibility.
/// </summary>
public static class RewardAppendPlanner
{
    public const uint GlimmeringCharmSubject = 5;
    // This is an editor limit, NOT a promise about each item's SlotMaxNum.
    public const uint EditorQuantityLimit = 999;

    public static RewardAppendPlan Plan(
        ReadOnlySpan<byte> sourceRaidPoint,
        IReadOnlyList<RewardRow> additions,
        Func<uint, bool> isAllowedBagItem)
    {
        ArgumentNullException.ThrowIfNull(additions);
        ArgumentNullException.ThrowIfNull(isAllowedBagItem);

        // Candidate never aliases the input data, even when the plan fails.
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
            RewardRow row = RewardRow.Read(rows.Slice(i * RaidPointLayout.RewardRowLength,
                                                       RaidPointLayout.RewardRowLength));
            if (row.IsEmpty)
            {
                sawPadding = true;
                continue;
            }

            // Do not infer slot usage if the original encoding is unusual.
            if (sawPadding)
                return Reject("NONCONTIGUOUS_ORIGINAL_ROWS", count);
            if (row.ItemId == 0 || row.Quantity == 0 || row.SubjectType > 5)
                return Reject("AMBIGUOUS_ORIGINAL_ROW", count);
            count++;
        }

        if (additions.Count > RaidPointLayout.RewardCapacity - count)
            return Reject("INSUFFICIENT_REWARD_SLOTS", count);

        // Check the entire request before changing any bytes: no partial writes.
        foreach (RewardRow row in additions)
        {
            if (row.ItemId == 0 || !isAllowedBagItem(row.ItemId))
                return Reject("ITEM_NOT_ALLOWLISTED", count);
            if (row.Quantity is 0 or > EditorQuantityLimit)
                return Reject("INVALID_QUANTITY", count);
            if (row.SubjectType != GlimmeringCharmSubject)
                return Reject("SUBJECT_NOT_GLIMMERING_CHARM", count);
            if (row.RareType != 0)
                return Reject("UNSUPPORTED_CUSTOM_RARE_TYPE", count);
        }

        for (int i = 0; i < additions.Count; i++)
        {
            int offset = RaidPointLayout.RewardsInRaidPoint
                       + (count + i) * RaidPointLayout.RewardRowLength;
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
