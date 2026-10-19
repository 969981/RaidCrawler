using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text.Json;
using RaidCrawler.Core.Structures;
using Xunit;

namespace RaidCrawler.Tests;

public sealed class RaidRewardTraceEvidenceTests
{
    private static string Hash(int item = 15, int qty = 5, int subject = 0)
    {
        byte[] region = new byte[720];
        BinaryPrimitives.WriteInt32LittleEndian(region.AsSpan(0), item);
        BinaryPrimitives.WriteInt32LittleEndian(region.AsSpan(4), qty);
        BinaryPrimitives.WriteInt32LittleEndian(region.AsSpan(8), 0);
        BinaryPrimitives.WriteInt32LittleEndian(region.AsSpan(12), subject);
        return Convert.ToHexString(SHA256.HashData(region));
    }

    private static string Row(string eventName, string runId = "demo",
        int item = 15, int qty = 5, int subject = 0, string? overrideSha = null)
    {
        return JsonSerializer.Serialize(new
        {
            run_id = runId,
            @event = eventName,
            capture_origin = "pokeldn_host",
            stage_result = "RECORDED",
            station_index = 1,
            rows_sha256 = overrideSha ?? Hash(item, qty, subject),
            row_count = 1,
            row_entries = new[] { new[] { 0, item, qty, 0, subject } }
        });
    }

    [Fact]
    public void ValidHostPipelineCanBeConfirmedWithoutClaimingNativeHookHit()
    {
        string text = string.Join("\n", new[]{
            Row("S1_SOURCE"), Row("S2_HOST_BOOTSTRAP"), Row("S2_WIRE_LOOPBACK")});
        var result = RaidRewardTraceEvidence.Analyze(text, Hash());
        Assert.Equal(0, result.Rejected);
        var run = Assert.Single(result.Runs);
        Assert.Equal("IMPORTED_HASH_VALID", run.Source);
        Assert.Equal("HASH_MATCH", run.Host);
        Assert.Equal("HASH_MATCH", run.WireLoopback);
        Assert.Equal("NOT_OBSERVED", run.Receiver);
        Assert.Equal("NOT_OBSERVED", run.Filtering);
        Assert.Equal("HASH_MATCH_UNLINKED_SESSION", run.TemplateCompare);
        Assert.Equal(0, result.NativeHits);
    }

    [Fact]
    public void HashMismatchDoesNotCountAsGoodBootstrap()
    {
        var analysis = RaidRewardTraceEvidence.Analyze(
            Row("S1_SOURCE") + "\n" +
            Row("S2_HOST_BOOTSTRAP", item: 16));
        var run = Assert.Single(analysis.Runs);
        Assert.Equal("HASH_MISMATCH", run.Host);
        Assert.Equal("NOT_OBSERVED", run.WireLoopback);
    }

    [Fact]
    public void FakeOrEditedDigestCannotPass()
    {
        var analysis = RaidRewardTraceEvidence.Analyze(
            Row("S1_SOURCE", overrideSha: new string('A',64)));
        Assert.Equal("IMPORTED_UNVERIFIED", Assert.Single(analysis.Runs).Source);
        Assert.NotEmpty(analysis.Warnings);
    }

    [Fact]
    public void OldRaidCrawlerLogDoesNotInventEvents()
    {
        var old = "{\"stage\":\"hook-memory-confirmed\",\"message\":\"InstalledArmed, epoch=2\"}";
        var analysis = RaidRewardTraceEvidence.Analyze(old);
        Assert.Empty(analysis.Runs);
        Assert.Equal(1, analysis.Rejected);
        Assert.Equal(0, analysis.NativeHits);
    }

    [Fact]
    public void ImportedReceiverIsOnlyAnExternalClaim()
    {
        var jsonl = Row("S1_SOURCE") + "\n" + Row("S2_HOST_BOOTSTRAP") +
            "\n" + Row("S2_RECEIVER_PRE_FILTER") + "\n" + Row("S3_FILTERED");
        var analysis = RaidRewardTraceEvidence.Analyze(jsonl);
        var run = Assert.Single(analysis.Runs);
        Assert.Equal("HASH_MATCH", run.Receiver);
        Assert.Equal("IMPORTED_ONLY_NO_NATIVE_PROOF", run.Filtering);
        Assert.Equal(0, analysis.NativeHits);
    }

    [Fact]
    public void DifferentStationsCannotBeMistakenForSameBootstrap()
    {
        string hostStation1 = Row("S2_HOST_BOOTSTRAP");
        string wireStation2 = Row("S2_WIRE_LOOPBACK")
            .Replace("\"station_index\":1", "\"station_index\":2");
        var result = RaidRewardTraceEvidence.Analyze(
            Row("S1_SOURCE") + "\n" + hostStation1 + "\n" + wireStation2);
        Assert.Equal(2, result.Runs.Count);
        var station1 = Assert.Single(result.Runs.Where(x => x.Station == 1));
        var station2 = Assert.Single(result.Runs.Where(x => x.Station == 2));
        Assert.Equal("HASH_MATCH", station1.Host);
        Assert.Equal("NOT_OBSERVED", station1.WireLoopback);
        Assert.Equal("NOT_OBSERVED", station2.Host);
        Assert.Equal("NOT_OBSERVED", station2.WireLoopback);
    }

    [Fact]
    public void OtherRunsAreNotMixed()
    {
        var analysis = RaidRewardTraceEvidence.Analyze(Row("S1_SOURCE","a") +
            "\n" + Row("S2_HOST_BOOTSTRAP","b"));
        Assert.Equal(2, analysis.Runs.Count);
        Assert.All(analysis.Runs, run => Assert.Equal("NOT_OBSERVED", run.Host));
    }
}
