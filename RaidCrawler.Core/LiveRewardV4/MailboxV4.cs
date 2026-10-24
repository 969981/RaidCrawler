using System.Buffers.Binary;

namespace RaidCrawler.Core.LiveRewardV4;

/// <summary>
/// Independent Mailbox V4 wire contract. This codec does not touch game data.
/// The initial V4 milestone is read-only observation of committed profiles.
/// </summary>
public static class MailboxV4
{
    public const int MailboxLength = 0x800;
    public const int HeaderLength = 0x40;
    public const int SlotLength = 0x3C0;
    public const int SlotAOffset = 0x40;
    public const int SlotBOffset = 0x400;
    public const int RowLength = 16;
    public const int MaxRows = 45;
    public const int RowTableOffset = 0x20;
    public const int RowTableLength = MaxRows * RowLength;
    public const uint Version = 4;
    public const uint NoActiveSlot = uint.MaxValue;
    public const uint ReadOnlyObserverCapability = 1;
    public const uint ModeAppend = 1;
    public const uint PolicyPersistent = 2;
    public const uint DefaultQuantity = 999;
    private static ReadOnlySpan<byte> Magic => "RCHMB04\0"u8;

    public static ulong ReadU64(ReadOnlySpan<byte> b, int at) =>
        BinaryPrimitives.ReadUInt64LittleEndian(b.Slice(at, 8));
    public static uint ReadU32(ReadOnlySpan<byte> b, int at) =>
        BinaryPrimitives.ReadUInt32LittleEndian(b.Slice(at, 4));
    private static void WriteU32(Span<byte> b, int at, uint value) =>
        BinaryPrimitives.WriteUInt32LittleEndian(b.Slice(at, 4), value);
    private static void WriteU64(Span<byte> b, int at, ulong value) =>
        BinaryPrimitives.WriteUInt64LittleEndian(b.Slice(at, 8), value);

    public static MailboxV4Header ParseHeader(ReadOnlySpan<byte> data)
    {
        if (data.Length != HeaderLength || !data[..8].SequenceEqual(Magic) ||
            ReadU32(data, 8) != Version || ReadU32(data, 12) != MailboxLength)
            throw new InvalidDataException("MAILBOX_V4_HEADER_MISMATCH");
        uint active = ReadU32(data, 0x18);
        if (active != NoActiveSlot && active > 1)
            throw new InvalidDataException("MAILBOX_V4_ACTIVE_SLOT_INVALID");
        uint status = ReadU32(data, 0x14);
        if (status > 2)
            throw new InvalidDataException("MAILBOX_V4_STATUS_INVALID");
        return new MailboxV4Header(
            ReadU32(data, 0x10), status, active,
            ReadU64(data, 0x20), ReadU64(data, 0x28),
            ReadU32(data, 0x30), ReadU32(data, 0x34),
            ReadU32(data, 0x38));
    }

    public static byte[] MakeOfflineHeader(uint activeSlot = NoActiveSlot,
        ulong publishedRevision = 0, ulong observedRevision = 0,
        uint status = 1, uint capabilities = ReadOnlyObserverCapability)
    {
        if (activeSlot != NoActiveSlot && activeSlot > 1)
            throw new ArgumentOutOfRangeException(nameof(activeSlot));
        byte[] bytes = new byte[HeaderLength];
        Magic.CopyTo(bytes);
        WriteU32(bytes, 8, Version);
        WriteU32(bytes, 12, MailboxLength);
        WriteU32(bytes, 0x10, capabilities);
        WriteU32(bytes, 0x14, status);
        WriteU32(bytes, 0x18, activeSlot);
        WriteU64(bytes, 0x20, publishedRevision);
        WriteU64(bytes, 0x28, observedRevision);
        return bytes;
    }

    public static int OffsetOfSlot(uint slot) => slot switch
    {
        0 => SlotAOffset, 1 => SlotBOffset,
        _ => throw new ArgumentOutOfRangeException(nameof(slot))
    };

    public static byte[] Serialize(RewardConfigV4 config, ulong revision)
    {
        ArgumentNullException.ThrowIfNull(config);
        if (revision == 0) throw new ArgumentOutOfRangeException(nameof(revision));
        Validate(config);
        byte[] data = new byte[SlotLength];
        WriteU64(data, 0, revision);
        WriteU32(data, 8, config.Enabled ? 1u : 0u);
        WriteU32(data, 0x0C, (uint)config.Mode);
        WriteU32(data, 0x10, (uint)config.Policy);
        WriteU32(data, 0x14, (uint)config.Rows.Count);
        for (int i = 0; i < config.Rows.Count; i++)
        {
            var row = config.Rows[i];
            int offset = RowTableOffset + i * RowLength;
            WriteU32(data, offset, row.ItemId);
            WriteU32(data, offset + 4, row.Quantity);
            WriteU32(data, offset + 8, row.RareFlag);
            WriteU32(data, offset + 12, row.SubjectType);
        }
        WriteU32(data, 0x18, ComputeChecksum(data));
        return data;
    }

    public static RewardConfigSnapshotV4 ParseSlot(ReadOnlySpan<byte> data)
    {
        if (data.Length != SlotLength || ReadU64(data, 0) == 0)
            throw new InvalidDataException("MAILBOX_V4_SLOT_INVALID");
        uint expected = ReadU32(data, 0x18);
        if (expected != ComputeChecksum(data))
            throw new InvalidDataException("MAILBOX_V4_CHECKSUM_MISMATCH");
        if (ReadU32(data, 0x1C) != 0 ||
            data[(RowTableOffset + RowTableLength)..].IndexOfAnyExcept((byte)0) >= 0)
            throw new InvalidDataException("MAILBOX_V4_NONZERO_RESERVED");
        uint enabled = ReadU32(data, 8);
        if (enabled > 1) throw new InvalidDataException("MAILBOX_V4_BAD_ENABLED");
        uint count = ReadU32(data, 0x14);
        if (count > MaxRows) throw new InvalidDataException("MAILBOX_V4_BAD_COUNT");
        var rows = new RewardRowV4[(int)count];
        for (int i = 0; i < count; i++)
        {
            int offset = RowTableOffset + i * RowLength;
            rows[i] = new RewardRowV4(ReadU32(data, offset),
                ReadU32(data, offset + 4), ReadU32(data, offset + 8),
                ReadU32(data, offset + 12));
        }
        if (data.Slice(RowTableOffset + (int)count * RowLength,
                RowTableLength - (int)count * RowLength).IndexOfAnyExcept((byte)0) >= 0)
            throw new InvalidDataException("MAILBOX_V4_UNUSED_ROWS_NONZERO");
        var config = new RewardConfigV4(enabled == 1,
            (RewardModeV4)ReadU32(data, 0x0C),
            (RewardApplyPolicyV4)ReadU32(data, 0x10), rows);
        Validate(config);
        return new RewardConfigSnapshotV4(ReadU64(data, 0), config);
    }

    public static uint ComputeChecksum(ReadOnlySpan<byte> slot)
    {
        if (slot.Length != SlotLength) throw new ArgumentException("INVALID_SLOT_LENGTH");
        uint hash = 2166136261u;
        unchecked
        {
            for (int i = 0; i < slot.Length; i++)
            {
                byte value = i is >= 0x18 and < 0x1C ? (byte)0 : slot[i];
                hash = (hash ^ value) * 16777619u;
            }
        }
        return hash;
    }

    public static void Validate(RewardConfigV4 config)
    {
        if (config.Mode != RewardModeV4.Append || config.Policy != RewardApplyPolicyV4.Persistent)
            throw new InvalidDataException("MAILBOX_V4_READONLY_SUPPORTS_APPEND_PERSISTENT_ONLY");
        if (config.Rows is null || config.Rows.Count > MaxRows ||
            (config.Enabled && config.Rows.Count == 0))
            throw new InvalidDataException("MAILBOX_V4_BAD_ROW_COUNT");
        HashSet<uint> unique = [];
        foreach (var row in config.Rows)
        {
            if (row.ItemId is 0 or > 65535 || row.Quantity is 0 or > 999 ||
                row.RareFlag != 0 || row.SubjectType != 5 || !unique.Add(row.ItemId))
                throw new InvalidDataException("MAILBOX_V4_INVALID_REWARD_ROW");
        }
    }
}

public enum RewardModeV4 : uint { Append = MailboxV4.ModeAppend }
public enum RewardApplyPolicyV4 : uint { Persistent = MailboxV4.PolicyPersistent }
public readonly record struct RewardRowV4(uint ItemId, uint Quantity, uint RareFlag, uint SubjectType);
public sealed record RewardConfigV4(bool Enabled, RewardModeV4 Mode, RewardApplyPolicyV4 Policy,
    IReadOnlyList<RewardRowV4> Rows)
{
    // No implicit item list: user selects rows explicitly. Empty list cannot be armed.
    public static RewardConfigV4 DefaultWithRows(IReadOnlyList<RewardRowV4> rows) =>
        new(true, RewardModeV4.Append, RewardApplyPolicyV4.Persistent, rows);
    public static RewardRowV4 DefaultRow(uint itemId) => new(itemId, MailboxV4.DefaultQuantity, 0, 5);
}
public sealed record RewardConfigSnapshotV4(ulong Revision, RewardConfigV4 Config);
public sealed record MailboxV4Header(uint Capabilities, uint Status, uint ActiveSlot,
    ulong PublishedRevision, ulong ObservedRevision,
    uint ObservationCount, uint LastObservationStatus, uint Generation);
