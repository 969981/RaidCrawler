namespace RaidCrawler.Core.Structures;

/// <summary>
/// Read-only, content-only acceptance gate for one proposed solo charm reward.
/// All inputs must come from an independently validated runtime capture in any
/// future live test. This class DOES NOT read a Switch, run Lua, or install Hooks.
/// Passing it is only an internal CONTENT CONSISTENCY result, not proof of an
/// actual native callback, actual receipt, or save persistence.
/// </summary>
public static class SoloCharmAppendEvidenceGate
{
    public enum Finding
    {
        IncompleteRuntimeEvidence,
        NotASingleCharmBonusTrial,
        SourceOriginalChanged,
        BonusNotAppendedAtSource,
        BonusLostInFilter,
        FilterToSettlementMismatch,
        BonusMissingAtSettlement,
        BonusItemAlsoInOriginal,
        ActualBagDeltaNotObserved,
        BagDidNotReceiveBonus,
        SavePersistenceNotObserved,
        SaveDidNotRetainBonus,
        ImportedContentConsistentNotAttested
    }

    public sealed record Evidence(
        IReadOnlyList<RaidRewardEntry>? OriginalA0,
        IReadOnlyList<RaidRewardEntry>? AppendedA1,
        IReadOnlyList<RaidRewardEntry>? FilteredB,
        IReadOnlyList<RaidRewardEntry>? ConsumedC,
        int? BagCountBefore,
        int? BagCountAfter,
        int? BagCountAfterReload,
        bool? HasGlimmeringCharm,
        bool? OfflineSolo,
        string? RunId);

    public sealed record Assessment(
        Finding Finding, string Message, bool ContentConsistent,
        string RuntimeAttestation = "NOT_ATTESTED_BY_THIS_CLASS",
        string HookCpuHit = "NOT_OBSERVED_BY_THIS_CLASS");

    /// <summary>
    /// First live trial must configure exactly one additional subject=5 row.
    /// For attributable bag delta, its item should NOT appear in the original
    /// reward list. Otherwise, A/B/C content can be compared but inventory
    /// attribution is inherently ambiguous.
    /// </summary>
    public static Assessment Evaluate(Evidence e, CharmBonusProfile profile)
    {
        ArgumentNullException.ThrowIfNull(e);
        ArgumentNullException.ThrowIfNull(profile);
        profile.Validate();
        Assessment No(Finding code, string message) => new(code, message, false);

        if (profile is not { Enabled: true } || profile.Items.Count != 1)
            return No(Finding.NotASingleCharmBonusTrial,
                "Configure exactly one enabled Subject=5 bonus for P1 solo trial.");
        if (e.OfflineSolo != true || e.HasGlimmeringCharm != true ||
            string.IsNullOrWhiteSpace(e.RunId) ||
            e.OriginalA0 is null || e.AppendedA1 is null ||
            e.FilteredB is null || e.ConsumedC is null)
            return No(Finding.IncompleteRuntimeEvidence,
                "Actual same-run solo A0/A1/B/C capture and confirmed charm required.");

        var a0 = e.OriginalA0;
        var a1 = e.AppendedA1;
        var b = e.FilteredB;
        var c = e.ConsumedC;
        var additional = profile.AsRewardRows()[0];

        if (a1.Count < a0.Count || !a1.Take(a0.Count).SequenceEqual(a0))
            return No(Finding.SourceOriginalChanged,
                "A1 changed, removed, or reordered original A0 rows.");
        if (a1.Count != a0.Count + 1 || a1[^1] != additional)
            return No(Finding.BonusNotAppendedAtSource,
                "A1 has no exactly-one-row Subject=5 suffix.");

        // Filtering may legitimately remove original role / sandwich rows,
        // but in this test the user already owns the charm, so the appended
        // row is expected to survive at the end of the filtered sequence.
        if (b.Count == 0 || b[^1] != additional)
            return No(Finding.BonusLostInFilter,
                "Subject=5 row did not survive filtering (not a missing-charm assumption).");
        if (c.Count == 0 || c[^1] != additional)
            return No(Finding.BonusMissingAtSettlement,
                "Custom row missing at settlement entry.");
        if (!b.SequenceEqual(c))
            return No(Finding.FilterToSettlementMismatch,
                "Filtered B rows differ from C actually consumed by settlement.");

        if (a0.Any(row => row.ItemId == additional.ItemId))
            return No(Finding.BonusItemAlsoInOriginal,
                "The bonus item also occurs in original A0, so bag delta cannot be attributed uniquely.");
        if (!e.BagCountBefore.HasValue || !e.BagCountAfter.HasValue)
            return No(Finding.ActualBagDeltaNotObserved,
                "A/B/C match, but no independently observed bag before/after counts.");
        if (e.BagCountAfter.Value - e.BagCountBefore.Value < additional.Quantity)
            return No(Finding.BagDidNotReceiveBonus,
                "Observed bag delta smaller than requested; check item cap/Bag.AddItem.");
        if (!e.BagCountAfterReload.HasValue)
            return No(Finding.SavePersistenceNotObserved,
                "Inventory delta observed, but no post-reload count.");
        if (e.BagCountAfterReload.Value < e.BagCountAfter.Value)
            return No(Finding.SaveDidNotRetainBonus,
                "Post-reload quantity is smaller; check save/commit or later spending.");

        return new Assessment(Finding.ImportedContentConsistentNotAttested,
            "A0/A1/B/C and bag delta are self-consistent, but this class does NOT attest real console origin.",
            true);
    }
}
