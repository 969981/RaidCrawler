using System.Text.Json;

namespace RaidCrawler.Core.Structures;

/// <summary>
/// Persistent, game-wide reward TEMPLATE. Never associates rows with a den,
/// map, star rank or seed, and never writes a Switch process.
/// A future native callback needs to provide its own verified battle context.
/// </summary>
public static class RaidRewardGlobalProfileStore
{
    public static string DefaultPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "RaidCrawler", "global-raid-rewards.json");

    public static IReadOnlyList<RaidRewardEntry?> Load(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        if (!File.Exists(path))
            return RaidRewardPresets.ScreenshotCharm45Slots();
        var rows = JsonSerializer.Deserialize<List<RaidRewardEntry?>>(File.ReadAllText(path))
            ?? throw new InvalidDataException("Global reward profile contains no slots.");
        if (rows.Count != RaidRewardEditor.MaxRows)
            throw new InvalidDataException($"Expected exactly {RaidRewardEditor.MaxRows} reward slots; found {rows.Count}.");
        RaidRewardEditor.EncodeSlots(rows); // validate all six Subjects, item ID and quantities
        return rows.ToArray();
    }

    public static void Save(string path, IReadOnlyList<RaidRewardEntry?> slots)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        byte[] bytes = RaidRewardEditor.EncodeSlots(slots); // fail before touching the existing file
        if (bytes.Length != RaidRewardEditor.RewardBytes)
            throw new InvalidDataException("Unexpected encoded reward block length.");
        string parent = Path.GetDirectoryName(Path.GetFullPath(path))!;
        Directory.CreateDirectory(parent);
        string temp = Path.Combine(parent, $".raid-rewards-{Guid.NewGuid():N}.tmp");
        try
        {
            File.WriteAllText(temp, JsonSerializer.Serialize(slots, new JsonSerializerOptions { WriteIndented = true }));
            File.Move(temp, path, overwrite: true);
        }
        finally
        {
            if (File.Exists(temp)) File.Delete(temp);
        }
    }
}
