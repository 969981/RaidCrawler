using System.Buffers.Binary;

namespace RaidCrawler.Core.Host;

/// <summary>
/// Verified Violet 4.0.0 donor convention: rows start at RaidPoint+0xE4.
/// A preceding u32 at +0xE0 is not part of the first reward row.
/// Each row is ItemId, Quantity, RareFlag, SubjectType.
/// </summary>
public static class RaidPointLayout
{
    public const int RaidPointLength = 0x3E8;
    public const int BootstrapLength = 0xAA0;
    public const int RaidPointInBootstrap = 0x6B8;
    public const int RewardsInRaidPoint = 0xE4;
    public const int RewardRowLength = 0x10;
    public const int RewardCapacity = 45;
    public const int RewardsByteLength = RewardRowLength * RewardCapacity;
    public const int RewardsInBootstrap = RaidPointInBootstrap + RewardsInRaidPoint; // 0x79C
    public const int RewardTail = RewardsInRaidPoint + RewardsByteLength; // 0x3B4
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
/// Donor-verified row order: item ID, quantity, rare flag, subject.
/// Previous erroneous +0xE0 parsing shifted every row by four bytes,
/// causing last row subject=5 to be mistaken for special metadata.
/// </summary>
public readonly record struct RewardRow(uint ItemId, uint Quantity, uint RareFlag, uint SubjectType)
{
    public bool IsEmpty => ItemId == 0 && Quantity == 0 && RareFlag == 0 && SubjectType == 0;

    public static RewardRow Read(ReadOnlySpan<byte> source)
    {
        if (source.Length != RaidPointLayout.RewardRowLength)
            throw new ArgumentException("Reward row must be exactly 16 bytes.", nameof(source));
        return new RewardRow(
            BinaryPrimitives.ReadUInt32LittleEndian(source.Slice(0, 4)),
            BinaryPrimitives.ReadUInt32LittleEndian(source.Slice(4, 4)),
            BinaryPrimitives.ReadUInt32LittleEndian(source.Slice(8, 4)),
            BinaryPrimitives.ReadUInt32LittleEndian(source.Slice(12, 4)));
    }

    public void Write(Span<byte> destination)
    {
        if (destination.Length != RaidPointLayout.RewardRowLength)
            throw new ArgumentException("Reward row must be exactly 16 bytes.", nameof(destination));
        BinaryPrimitives.WriteUInt32LittleEndian(destination.Slice(0, 4), ItemId);
        BinaryPrimitives.WriteUInt32LittleEndian(destination.Slice(4, 4), Quantity);
        BinaryPrimitives.WriteUInt32LittleEndian(destination.Slice(8, 4), RareFlag);
        BinaryPrimitives.WriteUInt32LittleEndian(destination.Slice(12, 4), SubjectType);
    }
}
