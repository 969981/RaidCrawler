namespace RaidCrawler.Core.Host;

/// <summary>
/// Offline fail-closed planner. This class never writes the Switch process.
/// Scarlet/Violet donor row layout: ItemId/Quantity/RareFlag/SubjectType.
/// </summary>
public static class RewardAppendPlanner
{
    public const uint GlimmeringCharmSubject = 5;
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
        bool paddingSeen = false;
        ReadOnlySpan<byte> source = RaidPointLayout.GetRaidPointRewards(sourceRaidPoint);
        for (int i = 0; i < RaidPointLayout.RewardCapacity; i++)
        {
            RewardRow row = RewardRow.Read(source.Slice(i * RaidPointLayout.RewardRowLength, RaidPointLayout.RewardRowLength));
            if (row.IsEmpty)
            {
                paddingSeen = true;
                continue;
            }
            if (paddingSeen)
                return Reject("NONCONTIGUOUS_ORIGINAL_ROWS", count);
            if (row.ItemId == 0 || row.Quantity == 0 || row.SubjectType > 5 || row.RareFlag > 1)
                return Reject("AMBIGUOUS_ORIGINAL_ROW", count);
            count++;
        }

        if (additions.Count > RaidPointLayout.RewardCapacity - count)
            return Reject("INSUFFICIENT_REWARD_SLOTS", count);

        foreach (RewardRow row in additions)
        {
            if (row.ItemId == 0 || !isAllowedBagItem(row.ItemId))
                return Reject("ITEM_NOT_ALLOWLISTED", count);
            if (row.Quantity is 0 or > EditorQuantityLimit)
                return Reject("INVALID_QUANTITY", count);
            if (row.SubjectType != GlimmeringCharmSubject)
                return Reject("SUBJECT_NOT_GLIMMERING_CHARM", count);
            if (row.RareFlag != 0)
                return Reject("UNSUPPORTED_CUSTOM_RARE_FLAG", count);
        }

        for (int i = 0; i < additions.Count; i++)
        {
            int offset = RaidPointLayout.RewardsInRaidPoint + (count + i) * RaidPointLayout.RewardRowLength;
            additions[i].Write(candidate.AsSpan(offset, RaidPointLayout.RewardRowLength));
        }
        return new(true, "OK_OFFLINE_ONLY", count, additions.Count, candidate);
    }
}
public sealed record RewardAppendPlan(
    bool Accepted, string Reason, int OriginalCount, int AddedCount, byte[] CandidateRaidPoint);
