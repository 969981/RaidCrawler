using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace RaidCrawler.Core.Structures;

/// <summary>
/// Desired additions to NORMAL game-computed rewards. These are not 45
/// replacement RaidPoint slots, and no record here writes to game memory.
/// </summary>
public sealed record CharmBonusItem(int ItemId, int Quantity);

public sealed record CharmBonusProfile(
    int SchemaVersion,
    long Revision,
    bool Enabled,
    IReadOnlyList<CharmBonusItem> Items,
    string Sha256)
{
    public const int CurrentVersion = 1;
    public const int MaxItems = 20;
    public const int CharmSubject = 5;

    public static CharmBonusProfile Create(bool enabled, IEnumerable<CharmBonusItem> items,
        long revision = 0)
    {
        ArgumentNullException.ThrowIfNull(items);
        var entries = items.ToArray();
        ValidateItems(entries);
        if (revision < 0) throw new ArgumentOutOfRangeException(nameof(revision));
        return new CharmBonusProfile(CurrentVersion, revision, enabled, entries,
            Digest(CurrentVersion, revision, enabled, entries));
    }

    public static void ValidateItems(IReadOnlyList<CharmBonusItem> items)
    {
        if (items.Count > MaxItems)
            throw new ArgumentException($"最多只能配置 {MaxItems} 条额外晶耀护符奖励。");
        foreach (var (item, index) in items.Select((value, index) => (value, index)))
        {
            if (item is null || item.ItemId is < 1 or > 65535 ||
                item.ItemId is 10000 or 20000 ||
                item.Quantity is < 1 or > RaidRewardEditor.MaxQuantity)
                throw new ArgumentException($"Invalid charm bonus at position {index + 1}.");
        }
    }

    public void Validate()
    {
        if (SchemaVersion != CurrentVersion || Revision < 0 || Items is null ||
            Sha256 is null || Sha256.Length != 64 || !Sha256.All(Uri.IsHexDigit))
            throw new InvalidDataException("Invalid charm profile metadata.");
        ValidateItems(Items);
        if (!string.Equals(Sha256,
            Digest(SchemaVersion, Revision, Enabled, Items), StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Charm profile fingerprint mismatch.");
    }

    private static string Digest(int version, long revision, bool enabled,
        IReadOnlyList<CharmBonusItem> items)
    {
        var builder = new StringBuilder()
            .Append("charm-bonus-v1\n").Append(version).Append('|')
            .Append(revision).Append('|').Append(enabled ? 1 : 0).Append('|')
            .Append(items.Count).Append('\n');
        foreach (var item in items)
            builder.Append(item.ItemId).Append(':').Append(item.Quantity).Append('\n');
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(builder.ToString())));
    }

    public IReadOnlyList<RaidRewardEntry> AsRewardRows()
    {
        Validate();
        return Enabled ? Items.Select(x => new RaidRewardEntry(
            x.ItemId, x.Quantity, 0, CharmSubject)).ToArray() : [];
    }
}

/// <summary>
/// Independent local profile: %LOCALAPPDATA%/RaidCrawler/charm-bonus-append.json.
/// A legacy 45-slot document is NEVER overwritten or automatically enabled.
/// </summary>
public static class CharmBonusProfileStore
{
    public static string DefaultPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "RaidCrawler", "charm-bonus-append.json");

    public static CharmBonusProfile Load(string path, string? legacyPath = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        if (File.Exists(path))
        {
            var profile = JsonSerializer.Deserialize<CharmBonusProfile>(File.ReadAllText(path))
                ?? throw new InvalidDataException("Empty charm profile.");
            profile.Validate();
            return CharmBonusProfile.Create(profile.Enabled, profile.Items, profile.Revision);
        }

        // A missing new file may IMPORT old Subject=5 rows, but may not enable
        // a live hook or change/remove any of the old saved slots.
        string oldPath = legacyPath ?? RaidRewardGlobalProfileStore.DefaultPath;
        if (!File.Exists(oldPath))
        {
            // The legacy editor shipped with 20 configured charm rows. Keep
            // that familiar draft for fresh installs, but NEVER auto-enable.
            var preset = RaidRewardPresets.ScreenshotCharm45Slots()
                .Where(x => x is { Subject: 5, Rare: 0 })
                .Select(x => new CharmBonusItem(x!.ItemId, x.Quantity));
            return CharmBonusProfile.Create(false, preset);
        }
        var old = RaidRewardGlobalProfileStore.Load(oldPath);
        var selected = old.Where(x => x is { Subject: 5, Rare: 0 })
            .Select(x => new CharmBonusItem(x!.ItemId, x.Quantity)).ToArray();
        return CharmBonusProfile.Create(false, selected);
    }

    public static CharmBonusProfile Save(string path, CharmBonusProfile incoming)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentNullException.ThrowIfNull(incoming);
        incoming.Validate();
        long revision = File.Exists(path) ? checked(Load(path).Revision + 1) : 1;
        var profile = CharmBonusProfile.Create(incoming.Enabled, incoming.Items, revision);
        string parent = Path.GetDirectoryName(Path.GetFullPath(path))!;
        Directory.CreateDirectory(parent);
        string temp = Path.Combine(parent, $".charm-bonus-{Guid.NewGuid():N}.tmp");
        try
        {
            File.WriteAllText(temp, JsonSerializer.Serialize(profile,
                new JsonSerializerOptions { WriteIndented = true }));
            File.Move(temp, path, overwrite: true);
            return profile;
        }
        finally
        {
            if (File.Exists(temp)) File.Delete(temp);
        }
    }
}
