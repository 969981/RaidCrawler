namespace RaidCrawler.Core.Host;

/// <summary>
/// Offline model of the native post-memcpy/pre-LZ4 hook. It edits ONLY the
/// already-serialized outgoing 0xAA0 bootstrap copy, never a live RaidPoint
/// owner, raid save block, compressed packet, or the supplied source buffer.
/// No additional data.bin/reward-table modifications are involved.
/// </summary>
public static class BootstrapSendCopyPlanner
{
    public static BootstrapSendCopyPlan Plan(
        ReadOnlySpan<byte> sourceRaidPoint,
        ReadOnlySpan<byte> outgoingBootstrap,
        IReadOnlyList<RewardRow> additions,
        Func<uint, bool> isAllowedBagItem,
        uint requestedSubject = RewardAppendPlanner.GlimmeringCharmSubject)
    {
        byte[] unchanged = outgoingBootstrap.ToArray();
        BootstrapSendCopyPlan Reject(string reason, int originalCount = 0) =>
            new(false, reason, originalCount, 0, unchanged);

        if (sourceRaidPoint.Length != RaidPointLayout.RaidPointLength)
            return Reject("INVALID_SOURCE_RAIDPOINT_LENGTH");
        if (outgoingBootstrap.Length != RaidPointLayout.BootstrapLength)
            return Reject("INVALID_BOOTSTRAP_LENGTH");

        // Validates the entire 0x3E8-byte copied point, including its metadata,
        // summary, and the 4-byte pad after row 44. Do not trust row-only equality.
        if (!sourceRaidPoint.SequenceEqual(outgoingBootstrap.Slice(
            RaidPointLayout.RaidPointInBootstrap, RaidPointLayout.RaidPointLength)))
            return Reject("SOURCE_BOOTSTRAP_MISMATCH");

        RewardAppendPlan plan = RewardAppendPlanner.Plan(sourceRaidPoint,
            additions, isAllowedBagItem, requestedSubject);
        if (!plan.Accepted)
            return Reject(plan.Reason, plan.OriginalCount);

        byte[] modified = outgoingBootstrap.ToArray();
        int first = plan.OriginalCount * RaidPointLayout.RewardRowLength;
        int length = plan.AddedCount * RaidPointLayout.RewardRowLength;
        plan.CandidateRaidPoint.AsSpan(RaidPointLayout.RewardsInRaidPoint + first, length)
            .CopyTo(modified.AsSpan(RaidPointLayout.RewardsInBootstrap + first, length));

        // Belt-and-suspenders: the serialized source must remain unchanged and
        // nothing outside the new rows may change, including all five PK9 slots.
        int start = RaidPointLayout.RewardsInBootstrap + first;
        int end = start + length;
        if (!outgoingBootstrap[..start].SequenceEqual(modified.AsSpan(0, start)) ||
            !outgoingBootstrap[end..].SequenceEqual(modified.AsSpan(end)))
            return Reject("UNRELATED_BOOTSTRAP_BYTES_MODIFIED", plan.OriginalCount);

        return new(true, "OK_OFFLINE_OUTGOING_COPY_ONLY",
            plan.OriginalCount, plan.AddedCount, modified);
    }
}

public sealed record BootstrapSendCopyPlan(bool Accepted, string Reason,
    int OriginalCount, int AddedCount, byte[] CandidateBootstrap);
