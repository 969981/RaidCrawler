using RaidCrawler.Core.Structures;
using Xunit;

namespace RaidCrawler.Tests;

public sealed class SoloCharmAppendEvidenceGateTests
{
    private static readonly RaidRewardEntry[] Original =
    [
        new(500, 2, 0, 0),
        new(501, 3, 0, 0),
        new(502, 1, 0, 4),
        new(503, 10, 0, 5),
    ];
    private static readonly RaidRewardEntry Extra = new(800, 1, 0, 5);
    private static readonly CharmBonusProfile Profile = CharmBonusProfile.Create(true,
        [new CharmBonusItem(800, 1)]);

    private static SoloCharmAppendEvidenceGate.Evidence Sample(
        IReadOnlyList<RaidRewardEntry>? a0 = null,
        IReadOnlyList<RaidRewardEntry>? a1 = null,
        IReadOnlyList<RaidRewardEntry>? b = null,
        IReadOnlyList<RaidRewardEntry>? c = null,
        int? before = 10, int? after = 11, int? reloaded = 11,
        bool? hasCharm = true, bool? solo = true, string? runId = "solo-001")
    {
        return new(a0 ?? Original, a1 ?? [.. Original, Extra],
            b ?? [Original[0], Original[1], Original[3], Extra],
            c ?? [Original[0], Original[1], Original[3], Extra],
            before, after, reloaded, hasCharm, solo, runId);
    }

    private static SoloCharmAppendEvidenceGate.Finding Check(
        SoloCharmAppendEvidenceGate.Evidence e,
        CharmBonusProfile? profile = null) =>
        SoloCharmAppendEvidenceGate.Evaluate(e, profile ?? Profile).Finding;

    [Fact]
    public void FullContentOnlyTrialNeverAttestsSwitchExecution()
    {
        var result = SoloCharmAppendEvidenceGate.Evaluate(Sample(), Profile);
        Assert.True(result.ContentConsistent);
        Assert.Equal(SoloCharmAppendEvidenceGate.Finding.ImportedContentConsistentNotAttested, result.Finding);
        Assert.Equal("NOT_ATTESTED_BY_THIS_CLASS", result.RuntimeAttestation);
        Assert.Equal("NOT_OBSERVED_BY_THIS_CLASS", result.HookCpuHit);
    }

    [Fact]
    public void ReportsAppendAbsentOrChangedOriginalRows()
    {
        Assert.Equal(SoloCharmAppendEvidenceGate.Finding.SourceOriginalChanged,
            Check(Sample(a1: [Original[1], Original[0], Original[2], Original[3], Extra])));
        Assert.Equal(SoloCharmAppendEvidenceGate.Finding.BonusNotAppendedAtSource,
            Check(Sample(a1: [.. Original])));
    }

    [Fact]
    public void ReportsSubject5LossAndSettlementDivergence()
    {
        Assert.Equal(SoloCharmAppendEvidenceGate.Finding.BonusLostInFilter,
            Check(Sample(b: [Original[0], Original[1], Original[3]])));
        Assert.Equal(SoloCharmAppendEvidenceGate.Finding.FilterToSettlementMismatch,
            Check(Sample(c: [Original[0], Original[1], Extra])));
        Assert.Equal(SoloCharmAppendEvidenceGate.Finding.BonusMissingAtSettlement,
            Check(Sample(c: [Original[0], Original[1], Original[3]])));
    }

    [Fact]
    public void DoesNotConfuseBagStateWithStageCHavingContent()
    {
        Assert.Equal(SoloCharmAppendEvidenceGate.Finding.ActualBagDeltaNotObserved,
            Check(Sample(before: null)));
        Assert.Equal(SoloCharmAppendEvidenceGate.Finding.BagDidNotReceiveBonus,
            Check(Sample(after: 10)));
        Assert.Equal(SoloCharmAppendEvidenceGate.Finding.SavePersistenceNotObserved,
            Check(Sample(reloaded: null)));
        Assert.Equal(SoloCharmAppendEvidenceGate.Finding.SaveDidNotRetainBonus,
            Check(Sample(reloaded: 10)));
    }

    [Fact]
    public void RefusesToAttributablyCountAnItemAlsoDroppedByGame()
    {
        var conflicting = CharmBonusProfile.Create(true, [new CharmBonusItem(500, 1)]);
        Assert.Equal(SoloCharmAppendEvidenceGate.Finding.BonusItemAlsoInOriginal,
            Check(Sample(a1: [.. Original, new RaidRewardEntry(500, 1, 0, 5)],
                b: [Original[0], Original[1], Original[3], new RaidRewardEntry(500, 1, 0, 5)],
                c: [Original[0], Original[1], Original[3], new RaidRewardEntry(500, 1, 0, 5)]),
                conflicting));
    }

    [Fact]
    public void RejectsMultiplayerMissingSourcesAndDisabledProfile()
    {
        var missing = Sample(solo: false);
        Assert.Equal(SoloCharmAppendEvidenceGate.Finding.IncompleteRuntimeEvidence,
            Check(missing));
        Assert.Equal(SoloCharmAppendEvidenceGate.Finding.IncompleteRuntimeEvidence,
            Check(Sample(hasCharm:null)));
        Assert.Equal(SoloCharmAppendEvidenceGate.Finding.NotASingleCharmBonusTrial,
            Check(Sample(), CharmBonusProfile.Create(false, [new CharmBonusItem(800,1)])));
        Assert.Equal(SoloCharmAppendEvidenceGate.Finding.NotASingleCharmBonusTrial,
            Check(Sample(), CharmBonusProfile.Create(true,
                [new CharmBonusItem(800, 1), new CharmBonusItem(801, 1)])));
    }

    [Fact]
    public void ComposeLogicalAddsOnlyOneRowAndDoesNotModifySource()
    {
        var source = Original.ToArray();
        var a1 = RewardAppendComposer.ComposeLogical(source, Profile);
        Assert.Equal(Original.Length + 1, a1.Count);
        Assert.True(a1.Take(Original.Length).SequenceEqual(Original));
        Assert.Equal(Extra, a1[^1]);
        Assert.Equal(Original, source);
        Assert.NotSame(source, a1);
        // Dry run only: this is not a Switch A1 runtime capture.
        Assert.Equal(SoloCharmAppendEvidenceGate.Finding.ImportedContentConsistentNotAttested,
            Check(Sample(a1:a1)));
    }
}
