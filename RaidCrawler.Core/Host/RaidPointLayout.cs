using System.Buffers.Binary;

namespace RaidCrawler.Core.Host;

/// <summary>
/// PokéLDN 4.0.0 retail donor: full reward row starts at RaidPoint+0xE0.
/// Its item ID (second u32) is at +0xE4; do not treat item ID as row start.
/// Each row (four u32 little-endian): condition_or_source, item_id, quantity, reserved.
/// </summary>
public static class RaidPointLayout
{
    public const int RaidPointLength = 0x3E8;
    public const int BootstrapLength = 0xAA0;
    public const int RaidPointInBootstrap = 0x6B8;
    public const int RewardsInRaidPoint = 0xE0;
    public const int ItemIdInRaidPoint = RewardsInRaidPoint + 4; // 0xE4
    public const int RewardRowLength = 0x10;
    public const int RewardCapacity = 45;
    public const int RewardsByteLength = RewardRowLength * RewardCapacity;
    public const int RewardsInBootstrap = RaidPointInBootstrap + RewardsInRaidPoint; // 0x798
    public const int RewardTail = RewardsInRaidPoint + RewardsByteLength; // 0x3B0
    public const int SummaryStart = 0x3B8;

    public static ReadOnlySpan<byte> GetRaidPointRewards(ReadOnlySpan<byte> raidPoint)
    {
        if (raidPoint.Length != RaidPointLength)
            throw new ArgumentException("RaidPoint must be exactly 0x3E8 bytes.", nameof(raidPoint));
        return raidPoint.Slice(RewardsInRaidPoint, RewardsByteLength);
    }

    public static ReadOnlySpan<byte> GetBootstrapRewards(ReadOnlySpan<byte> bootstrap)
    {
        if (bootstrap.Length != BootstrapLength)
            throw new ArgumentException("Decoded bootstrap must be exactly 0xAA0 bytes.", nameof(bootstrap));
        return bootstrap.Slice(RewardsInBootstrap, RewardsByteLength);
    }
}

/// <summary>
/// Corrected donor row order (see SV_RAID_BOOTSTRAP_ENCODER.md):
/// [condition_or_source, item_id, quantity, reserved].
/// Public positional arguments preserve the existing editor API:
/// RewardRow(itemId, quantity, reserved, subject).
/// </summary>
public readonly record struct RewardRow(uint ItemId, uint Quantity, uint Reserved, uint SubjectType)
{
    // Backward-compatible alias for existing editor/tests; NOT a proven rare flag.
    public uint RareFlag => Reserved;
    public bool IsEmpty => ItemId == 0 && Quantity == 0 && Reserved == 0 && SubjectType == 0;

    public static RewardRow Read(ReadOnlySpan<byte> source)
    {
        if (source.Length != RaidPointLayout.RewardRowLength)
            throw new ArgumentException("Reward row must be exactly 16 bytes.", nameof(source));
        return new RewardRow(
            BinaryPrimitives.ReadUInt32LittleEndian(source.Slice(4, 4)),
            BinaryPrimitives.ReadUInt32LittleEndian(source.Slice(8, 4)),
            BinaryPrimitives.ReadUInt32LittleEndian(source.Slice(12, 4)),
            BinaryPrimitives.ReadUInt32LittleEndian(source.Slice(0, 4)));
    }

    public void Write(Span<byte> destination)
    {
        if (destination.Length != RaidPointLayout.RewardRowLength)
            throw new ArgumentException("Reward row must be exactly 16 bytes.", nameof(destination));
        BinaryPrimitives.WriteUInt32LittleEndian(destination.Slice(0, 4), SubjectType);
        BinaryPrimitives.WriteUInt32LittleEndian(destination.Slice(4, 4), ItemId);
        BinaryPrimitives.WriteUInt32LittleEndian(destination.Slice(8, 4), Quantity);
        BinaryPrimitives.WriteUInt32LittleEndian(destination.Slice(12, 4), Reserved);
    }
}
