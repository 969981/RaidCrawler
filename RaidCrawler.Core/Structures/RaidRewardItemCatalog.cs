using PKHeX.Core;

namespace RaidCrawler.Core.Structures;

/// <summary>
/// Searchable Gen9/SV item catalog built from PKHeX.Core resource strings and
/// the actual Scarlet/Violet bag pouch classifications. No reward ID changes.
/// </summary>
public sealed record RaidRewardItemOption(int Id, string Name, string EnglishName)
{
    public override string ToString() => $"{Name}  ·  #{Id}" +
        (string.Equals(Name, EnglishName, StringComparison.OrdinalIgnoreCase) ? "" : $"  /  {EnglishName}");
}

public sealed class RaidRewardItemCatalog
{
    private readonly IReadOnlyList<RaidRewardItemOption> options;
    private readonly IReadOnlyDictionary<int, RaidRewardItemOption> byId;

    public RaidRewardItemCatalog(IEnumerable<RaidRewardItemOption> items)
    {
        options = items.OrderBy(item => item.Id).ToArray();
        byId = options.ToDictionary(item => item.Id);
    }

    /// <summary>
    /// Only actual SV bag items are suggested. Key/event items (including the
    /// Glimmering Charm eligibility token) are deliberately not a drop choice.
    /// </summary>
    public static RaidRewardItemCatalog FromGameStrings(IReadOnlyList<string> localized,
        IReadOnlyList<string> english)
    {
        var items = new List<RaidRewardItemOption>();
        for (int id = 1; id < Math.Min(localized.Count, english.Count) && id <= ushort.MaxValue; id++)
        {
            string name = localized[id];
            string englishName = english[id];
            if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(englishName) ||
                name.All(c => c is '?' or '？' or '-' or '－') ||
                englishName.StartsWith("???", StringComparison.Ordinal))
                continue;
            var type = ItemStorage9SV.GetInventoryPouch((ushort)id);
            if (type is InventoryType.None or InventoryType.KeyItems ||
                !ItemStorage9SV.Instance.IsLegal(type, id, 1))
                continue;
            items.Add(new RaidRewardItemOption(id, name, englishName));
        }
        return new RaidRewardItemCatalog(items);
    }

    public int Count => options.Count;
    public bool TryFind(int id, out RaidRewardItemOption? item) =>
        byId.TryGetValue(id, out item);

    /// <summary>
    /// Search Chinese or English substrings or an item ID; relaxed ordered
    /// character matching is a final fallback (does not require pinyin).
    /// </summary>
    public IReadOnlyList<RaidRewardItemOption> Search(string? query, int maxResults = 100)
    {
        if (maxResults < 1) throw new ArgumentOutOfRangeException(nameof(maxResults));
        string term = (query ?? "").Trim();
        if (term.Length == 0)
        {
            int[] featured = RaidRewardPresets.ScreenshotCharm999().Select(i => i.ItemId).ToArray();
            return featured.Where(byId.ContainsKey).Select(id => byId[id])
                .Concat(options.Where(x => !featured.Contains(x.Id)))
                .Take(maxResults).ToArray();
        }

        return options.Select(o => (Option: o, Score: Rank(o, term)))
            .Where(pair => pair.Score < int.MaxValue)
            .OrderBy(pair => pair.Score).ThenBy(pair => pair.Option.Id)
            .Take(maxResults).Select(pair => pair.Option).ToArray();
    }

    private static int Rank(RaidRewardItemOption o, string term)
    {
        if (int.TryParse(term, out int id) && id == o.Id) return 0;
        if (o.Name.Equals(term, StringComparison.OrdinalIgnoreCase) ||
            o.EnglishName.Equals(term, StringComparison.OrdinalIgnoreCase)) return 1;
        if (o.Name.StartsWith(term, StringComparison.OrdinalIgnoreCase) ||
            o.EnglishName.StartsWith(term, StringComparison.OrdinalIgnoreCase)) return 2;
        if (o.Name.Contains(term, StringComparison.OrdinalIgnoreCase) ||
            o.EnglishName.Contains(term, StringComparison.OrdinalIgnoreCase)) return 3;
        if (o.Id.ToString().StartsWith(term, StringComparison.Ordinal)) return 4;
        if (IsSubsequence(o.Name, term) || IsSubsequence(o.EnglishName, term)) return 5;
        return int.MaxValue;
    }

    private static bool IsSubsequence(string name, string term)
    {
        int pos = 0;
        foreach (char c in name)
        {
            if (char.ToUpperInvariant(c) != char.ToUpperInvariant(term[pos])) continue;
            if (++pos == term.Length) return true;
        }
        return false;
    }
}
