using System.Buffers.Binary;

namespace RaidCrawler.Core.Structures;

/// <summary>
/// Offline-only pure preservation-first composer. It never writes Switch RAM
/// and is NOT the Lua BattleRewardData representation.
/// </summary>
public enum RewardAppendStatus
{
    Applied,
    Disabled,
    EmptyBonus,
    InvalidFrame,
    InvalidOriginalRows,
    InsufficientCapacity,
}

public sealed record RewardAppendResult(
    RewardAppendStatus Status, byte[] Bytes, int OriginalCount,
    int AddedCount, string Reason)
{
    public bool Applied => Status == RewardAppendStatus.Applied;
}

public static class RewardAppendComposer
{
    private const int RowBytes = RaidRewardEditor.RowSize;
    private const int MaxRows = RaidRewardEditor.MaxRows;

    public static RewardAppendResult ComposeRaidPoint(
        ReadOnlySpan<byte> source, CharmBonusProfile profile) =>
        Compose(source, 0, RaidPointOfflinePatch.RaidPointSize, profile);

    public static RewardAppendResult ComposeBootstrap(
        ReadOnlySpan<byte> source, CharmBonusProfile profile) =>
        Compose(source, RaidPointOfflinePatch.BootstrapPointOffset,
            RaidPointOfflinePatch.BootstrapSize, profile);

    /// <summary>
    /// Pure logical-array variant. No 45-slot capacity implied here: the
    /// consumer's actual bounds must be independently established.
    /// </summary>
    public static IReadOnlyList<RaidRewardEntry> ComposeLogical(
        IReadOnlyList<RaidRewardEntry> original, CharmBonusProfile profile)
    {
        ArgumentNullException.ThrowIfNull(original);
        ArgumentNullException.ThrowIfNull(profile);
        profile.Validate();
        var rows = original.ToArray(); // always detach from caller
        if (!profile.Enabled || profile.Items.Count == 0) return rows;
        return [.. rows, .. profile.AsRewardRows()];
    }

    private static RewardAppendResult Compose(
        ReadOnlySpan<byte> source, int pointOffset, int expectedLength,
        CharmBonusProfile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);
        profile.Validate();
        byte[] preserved = source.ToArray();
        if (source.Length != expectedLength ||
            !source.Slice(pointOffset, "RaidPoint_"u8.Length).SequenceEqual("RaidPoint_"u8))
            return new(RewardAppendStatus.InvalidFrame, preserved, 0, 0,
                "Invalid uncompressed RaidPoint frame; no changes.");

        int rewardStart = pointOffset + RaidPointOfflinePatch.RewardOffset;
        int firstEmpty = MaxRows;
        for (int i = 0; i < MaxRows; i++)
        {
            var row = source.Slice(rewardStart + i * RowBytes, RowBytes);
            bool blank = row.IndexOfAnyExcept((byte)0) < 0;
            if (blank)
            {
                firstEmpty = Math.Min(firstEmpty, i);
                continue;
            }

            // Fail closed if a nonempty reward row follows padding, because
            // packing/reordering would change actual original semantics.
            uint id = BinaryPrimitives.ReadUInt32LittleEndian(row);
            uint qty = BinaryPrimitives.ReadUInt32LittleEndian(row[4..]);
            uint rare = BinaryPrimitives.ReadUInt32LittleEndian(row[8..]);
            uint subject = BinaryPrimitives.ReadUInt32LittleEndian(row[12..]);
            if (firstEmpty != MaxRows || id == 0 || qty == 0 ||
                rare > 1 || subject > 5)
                return new(RewardAppendStatus.InvalidOriginalRows, preserved, 0, 0,
                    "Original rows contain holes or unexpected fields; no changes.");
        }

        if (!profile.Enabled)
            return new(RewardAppendStatus.Disabled, preserved, firstEmpty, 0,
                "Append profile disabled; unchanged original.");
        if (profile.Items.Count == 0)
            return new(RewardAppendStatus.EmptyBonus, preserved, firstEmpty, 0,
                "No extra charm rows; unchanged original.");
        if (profile.Items.Count > MaxRows - firstEmpty)
            return new(RewardAppendStatus.InsufficientCapacity, preserved, firstEmpty, 0,
                $"Original {firstEmpty} + requested {profile.Items.Count} exceeds {MaxRows}; original preserved.");

        byte[] result = (byte[])preserved.Clone();
        var append = profile.AsRewardRows();
        for (int i = 0; i < append.Count; i++)
        {
            var entry = append[i];
            var dst = result.AsSpan(rewardStart + (firstEmpty + i) * RowBytes, RowBytes);
            BinaryPrimitives.WriteUInt32LittleEndian(dst, (uint)entry.ItemId);
            BinaryPrimitives.WriteUInt32LittleEndian(dst[4..], (uint)entry.Quantity);
            BinaryPrimitives.WriteUInt32LittleEndian(dst[8..], 0);
            BinaryPrimitives.WriteUInt32LittleEndian(dst[12..], 5);
        }
        if (!RaidPointOfflinePatch.UntouchedFieldsEqual(source, result, pointOffset) ||
            !source.Slice(rewardStart, firstEmpty * RowBytes)
                .SequenceEqual(result.AsSpan(rewardStart, firstEmpty * RowBytes)))
            throw new InvalidOperationException("Append violates source-preservation invariant.");
        return new(RewardAppendStatus.Applied, result, firstEmpty, append.Count,
            "Offline append composed; NOT a live Lua or Switch modification.");
    }
}
