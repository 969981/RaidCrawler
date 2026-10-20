namespace RaidCrawler.Core.Host;

/// <summary>
/// Byte-for-byte verification that one outgoing row was appended and NO
/// preceding reward or unrelated padding byte changed. This is offline
/// structure evidence, not a proof the client received or saved the item.
/// </summary>
public static class Subject5AppendInspector
{
    public static AppendInspection Inspect(HostProbeCapture capture, HostProbeResult report)
    {
        ReadOnlySpan<byte> before = capture.SourceRows;
        ReadOnlySpan<byte> after = capture.BootstrapRows;
        if (before.Length != RaidPointLayout.RewardsByteLength ||
            after.Length != RaidPointLayout.RewardsByteLength)
            return new(false, "INVALID_REGION_LENGTH");
        if (report.LastResult != 20)
            return new(false, "NATIVE_APPEND_NOT_CONFIRMED");
        if (report.OriginalRows >= RaidPointLayout.RewardCapacity)
            return new(false, "CAPACITY_EXCEEDED");
        if (report.ItemId != 15 || report.Quantity is 0 or > 10 ||
            report.RareFlag != 0 || report.SubjectType != 5)
            return new(false, "REQUEST_NOT_ALLOWED");
        int start = checked((int)report.OriginalRows * RaidPointLayout.RewardRowLength);
        if (!before.Slice(start, 16).SequenceEqual(new byte[16]))
            return new(false, "ORIGINAL_SLOT_WAS_NOT_EMPTY");
        if (!before[..start].SequenceEqual(after[..start]))
            return new(false, "ORIGINAL_REWARD_WAS_MODIFIED");
        if (!before[(start + 16)..].SequenceEqual(after[(start + 16)..]))
            return new(false, "UNRELATED_BYTES_MODIFIED");
        var written = RewardRow.Read(after.Slice(start, 16));
        if (written != new RewardRow(report.ItemId, report.Quantity, report.RareFlag, report.SubjectType))
            return new(false, "WRITTEN_ROW_MISMATCH");
        return new(true, "ONE_SUBJECT5_ROW_ONLY");
    }
}
public sealed record AppendInspection(bool Valid, string Reason);
