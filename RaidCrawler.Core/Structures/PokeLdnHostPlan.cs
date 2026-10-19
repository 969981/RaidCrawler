namespace RaidCrawler.Core.Structures;

/// <summary>A snapshot of ONE selected ordinary/black SV raid, never a global raid-table change.</summary>
public sealed record PokeLdnRaidSelection(
    uint Seed, string Version, string Map, string Content, bool IsEvent,
    int Species, int Stars, uint Area, uint Den)
{
    public static PokeLdnRaidSelection FromRaid(Raid raid, ITeraRaid encounter, string game)
    {
        string version = game.Equals("Violet", StringComparison.OrdinalIgnoreCase) ? "violet" :
            game.Equals("Scarlet", StringComparison.OrdinalIgnoreCase) ? "scarlet" :
            throw new ArgumentException("Game must be Scarlet or Violet.");
        string map = raid.MapParent switch
        {
            TeraRaidMapParent.Paldea => "paldea",
            TeraRaidMapParent.Kitakami => "kitakami",
            TeraRaidMapParent.Blueberry => "blueberry",
            _ => throw new ArgumentException("Unsupported SV raid map."),
        };
        return new(raid.Seed, version, map, raid.IsBlack ? "black" : "standard",
            raid.IsEvent, encounter.Species, encounter.Stars, raid.Area, raid.Den);
    }
}

/// <summary>
/// Builds the exact supported PokéLDN sv_host.py argv for ONE session.
/// This pure class never starts a process or connects to any console.
/// </summary>
public static class PokeLdnHostPlan
{
    public static readonly string[] ProgressOptions = ["beginning", "tera", "3star", "4star", "5star", "6star"];

    public static IReadOnlyList<string> BuildArguments(
        PokeLdnRaidSelection selected,
        IReadOnlyList<RaidRewardEntry> rewards,
        string pokemonFile,
        string progress,
        int players = 1,
        int seconds = 240,
        bool ipHost = false)
    {
        ArgumentNullException.ThrowIfNull(selected);
        if (selected.Seed == 0)
            throw new ArgumentException("A valid selected raid Seed is required.");
        if (selected.IsEvent)
            throw new ArgumentException("Event raids require a matching --raid-event delivery folder; this launcher intentionally refuses to simulate an event as a standard raid.");
        if (selected.Version is not ("scarlet" or "violet") ||
            selected.Map is not ("paldea" or "kitakami" or "blueberry") ||
            selected.Content is not ("standard" or "black"))
            throw new ArgumentException("Unsupported SV host version, map or crystal content.");
        if (!ProgressOptions.Contains(progress))
            throw new ArgumentException("Select the matching story progress for the target save.");
        if (players is < 1 or > 3)
            throw new ArgumentException("PokéLDN supports 1..3 guest consoles in a single raid.");
        if (seconds is < 30 or > 7200)
            throw new ArgumentException("Hosting time must be 30..7200 seconds.");
        if (string.IsNullOrWhiteSpace(pokemonFile) || !pokemonFile.StartsWith('/') ||
            pokemonFile.IndexOfAny(['\n', '\r', '\0']) >= 0)
            throw new ArgumentException("Supply an absolute Linux/WSL path for the legal host Pokémon .pk9 file.");
        RaidRewardEditor.Validate(rewards);
        if (rewards.Count is < 1 or > RaidRewardEditor.MaxRows)
            throw new ArgumentException("Set 1..45 explicit replacement rewards.");
        if (rewards.Any(r => r.Rare != 0 || r.Subject is not (0 or 5)))
            throw new ArgumentException("PokéLDN currently accepts custom rare=0 and subject 0 or 5 only.");

        var a = new List<string> {
            "--raid-seed", selected.Seed.ToString("X8"),
            "--raid-version", selected.Version,
            "--raid-map", selected.Map,
            "--raid-content", selected.Content,
            "--raid-progress", progress,
            "--raid-pokemon", pokemonFile,
            "--raid-players", players.ToString(System.Globalization.CultureInfo.InvariantCulture),
            "--seconds", seconds.ToString(System.Globalization.CultureInfo.InvariantCulture),
        };
        if (selected.Version == "violet")
            a.Add("--violet"); // match the LDN communication ID as well as raid encounter table
        if (ipHost)
            a.Add("--ip-host"); // only for ldn_mitm/emulator LAN, NOT native Nintendo online
        foreach (var reward in rewards)
        {
            a.Add("--raid-reward");
            a.Add($"{reward.ItemId}:{reward.Quantity}:{reward.Subject}");
        }
        return a;
    }
}
