using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text.Json;

namespace RaidCrawler.Core.Structures;

/// <summary>
/// Evidence from PokéLDN's optional host trace and separately provided receiver
/// records. It validates JSONL/45-row hashes and correlates run_id/station_index.
/// Importing external records is NOT a native retail HOOK_HIT observation.
/// </summary>
public static class RaidRewardTraceEvidence
{
    public const int MaxTraceBytes = 2 * 1024 * 1024;
    public const int MaxLines = 5000;
    public sealed record Event(string RunId, string Name, string Origin,
        int? Station, string? ClaimedResult, string? RewardSha,
        int? RowCount, bool RowsVerified);
    public sealed record Run(string RunId, int? Station, int Events, string Source, string Host,
        string WireLoopback, string Receiver, string Filtering,
        string TemplateCompare, int? HostRows);
    public sealed record Analysis(int Lines, int Rejected, IReadOnlyList<Run> Runs,
        IReadOnlyList<string> Warnings, int NativeHits)
    {
        public bool HasReceiverObservation => Runs.Any(x => x.Receiver != "NOT_OBSERVED");
    }

    private static string? Str(JsonElement e, string prop)
    {
        return e.TryGetProperty(prop, out var v) && v.ValueKind == JsonValueKind.String
            ? v.GetString() : null;
    }

    private static int? Number(JsonElement e, string prop)
    {
        return e.TryGetProperty(prop, out var v) && v.ValueKind == JsonValueKind.Number
            && v.TryGetInt32(out var n) ? n : null;
    }

    private static (string? Digest, int? Count, bool Verified) Rows(JsonElement e)
    {
        string? claimed = Str(e, "rows_sha256");
        int? declaredCount = Number(e, "row_count");
        if (!e.TryGetProperty("row_entries", out var entries) ||
            entries.ValueKind != JsonValueKind.Array ||
            claimed is null || claimed.Length != 64 ||
            !claimed.All(Uri.IsHexDigit))
            return (claimed, declaredCount, false);

        byte[] rows = new byte[RaidRewardEditor.RewardBytes];
        var seen = new HashSet<int>();
        foreach (var entry in entries.EnumerateArray())
        {
            if (entry.ValueKind != JsonValueKind.Array || entry.GetArrayLength() != 5)
                return (claimed, declaredCount, false);
            int[] fields;
            try { fields = entry.EnumerateArray().Select(x => x.GetInt32()).ToArray(); }
            catch (Exception ex) when (ex is FormatException or InvalidOperationException)
            { return (claimed, declaredCount, false); }

            var (slot, item, qty, rare, subject) =
                (fields[0], fields[1], fields[2], fields[3], fields[4]);
            if (slot is < 0 or >= 45 || !seen.Add(slot) ||
                item is < 1 or > 65535 || qty is < 1 or > 999 ||
                rare is < 0 or > 1 || subject is < 0 or > 5)
                return (claimed, declaredCount, false);
            for (int i = 0; i < 4; i++)
                BinaryPrimitives.WriteInt32LittleEndian(
                    rows.AsSpan(slot * 16 + i * 4, 4), fields[i + 1]);
        }
        string actual = Convert.ToHexString(SHA256.HashData(rows));
        bool valid = actual.Equals(claimed, StringComparison.OrdinalIgnoreCase) &&
            declaredCount == seen.Count;
        return (claimed.ToUpperInvariant(), declaredCount, valid);
    }

    public static Analysis Analyze(string jsonl, string? configuredTemplateSha = null)
    {
        if (System.Text.Encoding.UTF8.GetByteCount(jsonl) > MaxTraceBytes)
            throw new ArgumentException("Trace exceeds 2 MiB.");
        var lines = jsonl.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        if (lines.Length > MaxLines)
            throw new ArgumentException("Trace exceeds 5000 lines.");
        var events = new List<Event>();
        var warnings = new List<string>();
        int rejected = 0;

        foreach (string line in lines)
        {
            try
            {
                using var doc = JsonDocument.Parse(line);
                var root = doc.RootElement;
                if (root.ValueKind != JsonValueKind.Object)
                    throw new FormatException();
                string? name = Str(root, "event");
                string? id = Str(root, "run_id");
                if (name is not ("S1_SOURCE" or "S2_HOST_BOOTSTRAP" or
                    "S2_WIRE_LOOPBACK" or "S2_RECEIVER_PRE_FILTER" or "S3_FILTERED") ||
                    string.IsNullOrWhiteSpace(id) || id.Length > 128)
                    throw new FormatException();
                var (sha, count, verified) = Rows(root);
                if (name != "S3_FILTERED" && !verified)
                    warnings.Add($"run {id}: {name} row bytes/hash not independently reconstructible or mismatch.");
                events.Add(new Event(id, name, Str(root, "capture_origin") ?? "unspecified",
                    Number(root, "station_index"), Str(root, "stage_result"),
                    sha, count, verified));
            }
            catch (Exception ex) when (ex is JsonException or FormatException or
                InvalidOperationException or ArgumentException or OverflowException)
            {
                rejected++;
            }
        }

        var runs = new List<Run>();
        foreach (var group in events.GroupBy(e => e.RunId).Take(100))
        {
            var stations = group.Where(e => e.Station.HasValue)
                .Select(e => e.Station).Distinct().ToArray();
            if (stations.Length == 0) stations = [null];
            Event? source = group.FirstOrDefault(x => x.Name == "S1_SOURCE");

            static string Compare(Event? a, Event? b)
            {
                if (a is null || b is null) return "NOT_OBSERVED";
                if (!a.RowsVerified || !b.RowsVerified)
                    return "IMPORTED_UNVERIFIED";
                return string.Equals(a.RewardSha, b.RewardSha,
                    StringComparison.OrdinalIgnoreCase) ? "HASH_MATCH" : "HASH_MISMATCH";
            }

            foreach (int? station in stations)
            {
                // Avoid silently matching a host S2 station with a different
                // guest's S2 wire/receiver observation.
                Event? host = group.FirstOrDefault(x => x.Name == "S2_HOST_BOOTSTRAP"
                    && x.Station == station);
                Event? wire = group.FirstOrDefault(x => x.Name == "S2_WIRE_LOOPBACK"
                    && x.Station == station);
                Event? receiver = group.FirstOrDefault(x => x.Name == "S2_RECEIVER_PRE_FILTER"
                    && x.Station == station);
                Event? filtered = group.FirstOrDefault(x => x.Name == "S3_FILTERED"
                    && x.Station == station);

                string sourceState = source is null ? "NOT_OBSERVED" :
                    source.RowsVerified ? "IMPORTED_HASH_VALID" : "IMPORTED_UNVERIFIED";
                string hostState = Compare(source, host);
                string wireState = Compare(host, wire);
                string recvState = Compare(host, receiver);
                string filterState = filtered is null ? "NOT_OBSERVED" :
                    "IMPORTED_ONLY_NO_NATIVE_PROOF";
                string templateState = configuredTemplateSha is null || host is null
                    ? "NOT_OBSERVED"
                    : host.RowsVerified && string.Equals(configuredTemplateSha, host.RewardSha,
                        StringComparison.OrdinalIgnoreCase)
                      ? "HASH_MATCH_UNLINKED_SESSION" : "HASH_MISMATCH_OR_UNVERIFIED";
                int number = group.Count(e => e.Station == station || e.Name == "S1_SOURCE");
                runs.Add(new Run(group.Key, station, number, sourceState, hostState,
                    wireState, recvState, filterState, templateState, host?.RowCount));
            }
        }
        if (events.Count == 0)
            warnings.Add("No valid S1/S2/S3 evidence events. Original RaidCrawler log only contains control-plane readback.");
        return new Analysis(lines.Length, rejected, runs, warnings, NativeHits: 0);
    }
}
