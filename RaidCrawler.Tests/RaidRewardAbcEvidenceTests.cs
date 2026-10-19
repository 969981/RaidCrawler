using System.Text.Json;
using RaidCrawler.Core.Structures;
using Xunit;

namespace RaidCrawler.Tests;

public sealed class RaidRewardAbcEvidenceTests
{
    private static readonly RaidRewardAbcEvidence.RewardRow Original =
        new(1200, 10, 0, 0);
    private static readonly RaidRewardAbcEvidence.RewardRow Charm =
        new(15, 500, 0, 5);
    private static readonly RaidRewardAbcEvidence.RewardRow Another =
        new(16, 50, 0, 0);

    private static string Snapshot(string stage,
        RaidRewardAbcEvidence.RewardRow[] rows, string run = "solo-01",
        string? digest = null, string build = "709bfd66115298640155fcc4979dba151c7cc79a")
    {
        return JsonSerializer.Serialize(new
        {
            @event = stage, run_id = run, capture_origin = "unit_test_fixture",
            build_id = build, rows_sha256 = digest ??
                RaidRewardAbcEvidence.RowsDigest(rows),
            row_entries = rows.Select((r, i) =>
                new[] { i, r.ItemId, r.Quantity, r.Rare, r.Subject })
        });
    }

    private static readonly RaidRewardEntry?[] Template =
    [
        new(15, 500, 0, 5), null, null,
    ];

    [Fact]
    public void OriginalOnlyInEveryStageIsClassifiedAsAbsent()
    {
        string text = string.Join("\n", new[]{
            Snapshot("ABC_A_GENERATED", [Original]),
            Snapshot("ABC_B_FILTERED", [Original]),
            Snapshot("ABC_C_CONSUMED", [Original]),
        });
        var result = RaidRewardAbcEvidence.Analyze(text, Template);
        var run = Assert.Single(result.Runs);
        Assert.Equal("CUSTOM_ROWS_ABSENT_AT_GENERATION", run.Result);
        Assert.Equal("IMPORTED_SUBSEQUENCE", run.AToB);
        Assert.Equal("IMPORTED_ROWS_MATCH", run.BToC);
        Assert.Equal("CUSTOM_ROWS_ABSENT_OR_INCOMPLETE", run.TemplateInA);
        Assert.Equal("NOT_OBSERVED", result.OverallNativeHit);
        Assert.Equal("UNATTESTED_IMPORTED_CAPTURES", run.EvidenceQuality);
    }

    [Fact]
    public void DetectsWhereTemplateDisappears()
    {
        var ab = RaidRewardAbcEvidence.Analyze(string.Join("\n", new[] {
            Snapshot("ABC_A_GENERATED", [Original, Charm]),
            Snapshot("ABC_B_FILTERED", [Original]),
            Snapshot("ABC_C_CONSUMED", [Original])
        }), Template);
        var f = Assert.Single(ab.Runs);
        Assert.Equal("CUSTOM_ROWS_PRESENT_IN_IMPORTED_DATA", f.TemplateInA);
        Assert.Equal("CUSTOM_ROWS_ABSENT_OR_INCOMPLETE", f.TemplateInB);
        Assert.Equal("CUSTOM_ROWS_ABSENT_OR_INCOMPLETE", f.TemplateInC);
        Assert.Equal("CUSTOM_ROWS_LOST_DURING_FILTER", f.Result);

        var bc = RaidRewardAbcEvidence.Analyze(string.Join("\n", new[] {
            Snapshot("ABC_A_GENERATED", [Original, Charm]),
            Snapshot("ABC_B_FILTERED", [Original, Charm]),
            Snapshot("ABC_C_CONSUMED", [Original])
        }), Template);
        Assert.Equal("B_TO_C_NEEDS_INVESTIGATION", Assert.Single(bc.Runs).Result);
    }

    [Fact]
    public void MatchingImportedContentIsNotReceiptOrSaveProof()
    {
        var analysis = RaidRewardAbcEvidence.Analyze(string.Join("\n", new[]{
            Snapshot("ABC_A_GENERATED", [Original, Charm]),
            Snapshot("ABC_B_FILTERED", [Original, Charm]),
            Snapshot("ABC_C_CONSUMED", [Original, Charm]),
        }), Template);
        var run = Assert.Single(analysis.Runs);
        Assert.Equal("CUSTOM_ROWS_PRESENT_IN_C_CLAIM_NOT_BAG_PROOF", run.Result);
        Assert.Equal("NOT_OBSERVED", analysis.OverallNativeHit);
    }

    [Fact]
    public void ValidFilteredSubsequenceHandlesDuplicatedItems()
    {
        var analysis = RaidRewardAbcEvidence.Analyze(string.Join("\n", new[]{
            Snapshot("ABC_A_GENERATED", [Original, Another, Original, Charm]),
            Snapshot("ABC_B_FILTERED", [Original, Charm]),
            Snapshot("ABC_C_CONSUMED", [Original, Charm]),
        }));
        Assert.Equal("IMPORTED_SUBSEQUENCE", Assert.Single(analysis.Runs).AToB);
    }

    [Fact]
    public void DetectsUnknownOriginHashTamperingAndMixedSessions()
    {
        var report = RaidRewardAbcEvidence.Analyze(string.Join("\n", new[]{
            Snapshot("ABC_A_GENERATED", [Original], "one",
                digest: new string('A', 64)),
            Snapshot("ABC_B_FILTERED", [Original], "two"),
            Snapshot("ABC_C_CONSUMED", [Original], "one")
        }));
        Assert.Equal(1, report.Rejected);
        Assert.Equal(2, report.Runs.Count);
        Assert.All(report.Runs, run => Assert.Equal("INCOMPLETE_CAPTURE", run.Result));
    }

    [Fact]
    public void DuplicateStagesAndMixedBuildsFailClosed()
    {
        string mixed = string.Join("\n", new[]{
            Snapshot("ABC_A_GENERATED", [Original], build: "709bfd6611529864AAA"),
            Snapshot("ABC_B_FILTERED", [Original], build: "709bfd6611529864BBB"),
            Snapshot("ABC_C_CONSUMED", [Original]),
        });
        var m = RaidRewardAbcEvidence.Analyze(mixed);
        Assert.Equal("INVALID_MIXED_BUILD", Assert.Single(m.Runs).Result);
        Assert.NotEmpty(m.Warnings);

        var repeat = RaidRewardAbcEvidence.Analyze(
            Snapshot("ABC_A_GENERATED", [Original]) + "\n" +
            Snapshot("ABC_A_GENERATED", [Charm]));
        Assert.Equal("NOT_OBSERVED", Assert.Single(repeat.Runs).A);
        Assert.NotEmpty(repeat.Warnings);
    }

    [Fact]
    public void CurrentRaidCrawlerLogsAreNotABCCaptures()
    {
        string log = string.Join("\n", new[]{
            "{\"stage\":\"read-only-hook-snapshot\",\"detail\":{\"rows\":20}}",
            "{\"stage\":\"auto-battle-entered\",\"detail\":{\"runId\":\"a\"}}",
            "{\"stage\":\"hook-memory-confirmed\",\"message\":\"InstalledArmed\"}",
        });
        var report = RaidRewardAbcEvidence.Analyze(log, Template);
        Assert.Equal(3, report.Ignored);
        Assert.Empty(report.Runs);
        Assert.Equal(0, report.Rejected);
        Assert.Equal("NOT_OBSERVED", report.OverallNativeHit);
    }

    [Fact]
    public void EmptyStageAndGarbageCannotProduceEvidence()
    {
        var text = Snapshot("ABC_A_GENERATED", []) + "\n" +
            "{ garbage!";
        var report = RaidRewardAbcEvidence.Analyze(text);
        Assert.Single(report.Runs);
        Assert.Equal(1, report.Rejected);
        Assert.Equal(0, Assert.Single(report.Runs).ARows);
    }
}
