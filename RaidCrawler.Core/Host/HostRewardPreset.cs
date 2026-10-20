using System.Text.Json;

namespace RaidCrawler.Core.Host;

/// <summary>
/// Editable host reward profile, independent of live-Native writer capabilities.
/// The default is 25 requested rewards (x999, subject=5), with at most 28 distinct item IDs.
/// A successful profile/byte-level plan is NOT proof of an item being awarded in game.
/// </summary>
public static class HostRewardPreset
{
    public const int MaxItems = 28;
    public const uint DefaultQuantity = 999;
    public const uint Subject = 5;
    public const string Schema = "raidcrawler.host.reward-preset.v1";

    private static readonly HostRewardPresetItem[] BuiltIn =
    [
        new("速度球", 492, 999),
        new("等级球", 493, 999),
        new("诱饵球", 494, 999),
        new("沉重球", 495, 999),
        new("甜蜜球", 496, 999),
        new("友友球", 497, 999),
        new("月亮球", 498, 999),
        new("竞赛球", 499, 999),
        new("究极球", 851, 999),
        new("梦境球", 576, 999),
        new("狩猎球", 5, 999),
        new("银色王冠", 795, 999),
        new("金色王冠", 796, 999),
        new("大师球", 1, 999),
        new("神奇糖果", 50, 999),
        new("经验糖果ＸＬ", 1128, 999),
        new("巨大金珠", 581, 999),
        new("苦味料", 1907, 999),
        new("辣味料", 1908, 999),
        new("酸味料", 1906, 999),
        new("甜味料", 1904, 999),
        new("咸味料", 1905, 999),
        new("特性膏药", 1606, 999),
        new("锁财灵的金币", 2137, 999),
        new("净空年糕", 2479, 999),
    ];

    public static IReadOnlyList<HostRewardPresetItem> Defaults =>
        Array.AsReadOnly(BuiltIn.ToArray());

    public static HostRewardPresetProfile CreateDefault() =>
        new(Schema, Subject, BuiltIn.ToArray());

    public static string? Validate(HostRewardPresetProfile? profile)
    {
        if (profile is null || profile.Schema != Schema)
            return "UNKNOWN_PRESET_SCHEMA";
        if (profile.SubjectType != Subject)
            return "SUBJECT_MUST_BE_FIVE";
        if (profile.Items is null || profile.Items.Length is < 1 or > MaxItems)
            return "ITEM_COUNT_MUST_BE_1_TO_28";
        HashSet<uint> ids = [];
        foreach (HostRewardPresetItem entry in profile.Items)
        {
            if (string.IsNullOrWhiteSpace(entry.Name) || entry.Name.Length > 60)
                return "INVALID_ITEM_NAME";
            if (entry.ItemId is 0 or > 65535)
                return "INVALID_ITEM_ID";
            if (entry.Quantity is 0 or > 999)
                return "INVALID_ITEM_QUANTITY";
            if (!ids.Add(entry.ItemId))
                return "DUPLICATE_ITEM_ID";
        }
        return null;
    }

    public static RewardRow[] BuildRows(HostRewardPresetProfile profile)
    {
        string? error = Validate(profile);
        if (error is not null)
            throw new ArgumentException(error, nameof(profile));
        return profile.Items.Select(i => new RewardRow(i.ItemId, i.Quantity, 0, Subject)).ToArray();
    }

    /// <summary>
    /// Offline-only: source point & outgoing bootstrap must be byte-identical BEFORE the
    /// addition. Fail closed unless all original reward rows remain and enough empty slots exist.
    /// The allowlist callback is required to validate supported bag items per installed game.
    /// </summary>
    public static BootstrapSendCopyPlan Plan(
        HostRewardPresetProfile profile,
        ReadOnlySpan<byte> sourceRaidPoint,
        ReadOnlySpan<byte> outgoingBootstrap,
        Func<uint, bool> isAllowedBagItem)
    {
        string? error = Validate(profile);
        if (error is not null)
            return new(false, error, 0, 0, outgoingBootstrap.ToArray());
        return BootstrapSendCopyPlanner.Plan(sourceRaidPoint, outgoingBootstrap,
            BuildRows(profile), isAllowedBagItem, Subject);
    }

    public static string ToJson(HostRewardPresetProfile profile)
    {
        string? error = Validate(profile);
        if (error is not null)
            throw new ArgumentException(error, nameof(profile));
        return JsonSerializer.Serialize(profile, new JsonSerializerOptions { WriteIndented = true });
    }

    public static HostRewardPresetProfile FromJson(string json)
    {
        HostRewardPresetProfile profile;
        try
        {
            profile = JsonSerializer.Deserialize<HostRewardPresetProfile>(json)
                ?? throw new InvalidDataException("Preset JSON is empty.");
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException("Preset JSON is invalid.", ex);
        }
        string? error = Validate(profile);
        if (error is not null)
            throw new InvalidDataException("Invalid reward preset: " + error);
        return profile;
    }
}

public sealed record HostRewardPresetItem(string Name, uint ItemId, uint Quantity);

public sealed record HostRewardPresetProfile(
    string Schema, uint SubjectType, HostRewardPresetItem[] Items);
