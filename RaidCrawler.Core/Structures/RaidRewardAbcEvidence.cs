using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace RaidCrawler.Core.Structures;

/// <summary>
/// A/B/C lifecycle evidence correlation for a FUTURE trusted runtime capture
/// adapter. JSONL imports are untrusted claims, never proof that a Switch Lua
/// callback ran. The existing Hook readback and botbase phase flags are NOT
/// A/B/C sources.
/// </summary>
public static class RaidRewardAbcEvidence
{
    public const int MaxBytes = 2 * 1024 * 1024;
    public const int MaxLines = 5000;
    public const int MaxRows = 128;
    public const string ExpectedBuildPrefix = "709bfd6611529864";

    public sealed record RewardRow(int ItemId, int Quantity, int Rare, int Subject);
    public sealed record Snapshot(string RunId, string Event, string Origin,
        string BuildId, IReadOnlyList<RewardRow> Rows, string Digest);
    public sealed record Run(string RunId, string A, string B, string C,
        string AToB, string BToC, string TemplateInA, string TemplateInB,
        string TemplateInC, string Result, string EvidenceQuality,
        int? ARows, int? BRows, int? CRows);
    public sealed record Analysis(int Lines, int Rejected, int Ignored,
        IReadOnlyList<Run> Runs, IReadOnlyList<string> Warnings,
        string OverallNativeHit = "NOT_OBSERVED");

    private static string? String(JsonElement root, string name) =>
        root.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String
            ? v.GetString() : null;

    private static bool IsEvent(string? name) =>
        name is "ABC_A_GENERATED" or "ABC_B_FILTERED" or "ABC_C_CONSUMED";

    /// <summary>
    /// Stable capture-file digest: for each row in its observed sequence,
    /// write five little-endian uint32 fields [index, item, quantity, rare,
    /// subject]. This is NOT the 720B RaidPoint template digest.
    /// </summary>
    public static string RowsDigest(IReadOnlyList<RewardRow> rows)
    {
        using var ms = new MemoryStream();
        for (int i = 0; i < rows.Count; i++)
        {
            Span<byte> row = stackalloc byte[20];
            BinaryPrimitives.WriteInt32LittleEndian(row[0..4], i);
            BinaryPrimitives.WriteInt32LittleEndian(row[4..8], rows[i].ItemId);
            BinaryPrimitives.WriteInt32LittleEndian(row[8..12], rows[i].Quantity);
            BinaryPrimitives.WriteInt32LittleEndian(row[12..16], rows[i].Rare);
            BinaryPrimitives.WriteInt32LittleEndian(row[16..20], rows[i].Subject);
            ms.Write(row);
        }
        return Convert.ToHexString(SHA256.HashData(ms.ToArray()));
    }

    private static Snapshot Parse(JsonElement root)
    {
        string runId = String(root, "run_id") ?? throw new FormatException("run_id required");
        string eventName = String(root, "event") ?? throw new FormatException("event required");
        string origin = String(root, "capture_origin") ??
            throw new FormatException("capture_origin required");
        string build = String(root, "build_id") ?? throw new FormatException("build_id required");
        string digest = String(root, "rows_sha256") ??
            throw new FormatException("rows_sha256 required");
        if (runId.Length is < 1 or > 128 || origin.Length is < 1 or > 128 ||
            !build.StartsWith(ExpectedBuildPrefix, StringComparison.OrdinalIgnoreCase) ||
            digest.Length != 64 || !digest.All(Uri.IsHexDigit))
            throw new FormatException("Invalid run/origin/build/digest");

        if (!root.TryGetProperty("row_entries", out var data) ||
            data.ValueKind != JsonValueKind.Array || data.GetArrayLength() > MaxRows)
            throw new FormatException("Invalid row_entries");
        var rows = new List<RewardRow>();
        foreach (var item in data.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Array || item.GetArrayLength() != 5)
                throw new FormatException("Each row is [index,item,quantity,rare,subject]");
            var v = item.EnumerateArray().Select(x =>
                x.ValueKind == JsonValueKind.Number && x.TryGetInt32(out int n)
                    ? n : throw new FormatException("Invalid row field")).ToArray();
            if (v[0] != rows.Count || v[1] is < 1 or > 65535 ||
                v[2] is < 1 or > 999999 ||
                v[3] is < 0 or > 1 || v[4] is < 0 or > 5)
                throw new FormatException("Invalid row order or fields");
            rows.Add(new RewardRow(v[1], v[2], v[3], v[4]));
        }
        string hash = RowsDigest(rows);
        if (!hash.Equals(digest, StringComparison.OrdinalIgnoreCase))
            throw new FormatException("Capture row digest mismatch");
        return new Snapshot(runId, eventName, origin, build, rows, hash);
    }

    private static bool Same(RewardRow a, RewardRow b) => a == b;

    private static bool Subsequence(IReadOnlyList<RewardRow> source,
        IReadOnlyList<RewardRow> selected)
    {
        int i = 0;
        foreach (var row in source)
            if (i < selected.Count && Same(row, selected[i])) i++;
        return i == selected.Count;
    }

    private static string TemplateState(Snapshot? stage,
        IReadOnlyList<RewardRow> template)
    {
        if (stage is null) return "NOT_OBSERVED";
        if (template.Count == 0) return "NO_TEMPLATE";
        // Multiset containment, not contiguous order; the runtime may
        // preserve unrelated fixed rows alongside custom rows.
        var counts = stage.Rows.GroupBy(x => x).ToDictionary(g => g.Key, g => g.Count());
        foreach (var row in template)
        {
            if (!counts.TryGetValue(row, out int n) || n == 0)
                return "CUSTOM_ROWS_ABSENT_OR_INCOMPLETE";
            counts[row] = n - 1;
        }
        return "CUSTOM_ROWS_PRESENT_IN_IMPORTED_DATA";
    }

    public static Analysis Analyze(string jsonl,
        IReadOnlyList<RaidRewardEntry?>? configuredSlots = null)
    {
        if (Encoding.UTF8.GetByteCount(jsonl) > MaxBytes)
            throw new ArgumentException("Capture exceeds 2 MiB.");
        string[] lines = jsonl.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        if (lines.Length > MaxLines)
            throw new ArgumentException("Capture exceeds 5000 lines.");

        var snapshots = new List<Snapshot>();
        var warnings = new List<string>();
        int rejected = 0, ignored = 0;
        foreach (var line in lines)
        {
            try
            {
                using var doc = JsonDocument.Parse(line);
                var root = doc.RootElement;
                if (root.ValueKind != JsonValueKind.Object) { rejected++; continue; }
                string? name = String(root, "event");
                if (!IsEvent(name)) { ignored++; continue; }
                snapshots.Add(Parse(root));
            }
            catch (Exception ex) when (ex is JsonException or FormatException or
                InvalidOperationException or ArgumentException or OverflowException)
            { rejected++; }
        }

        var template = configuredSlots?.Where(x => x is not null)
            .Select(x => new RewardRow(x!.ItemId, x.Quantity, x.Rare, x.Subject))
            .ToArray() ?? [];
        var runs = new List<Run>();
        foreach (var group in snapshots.GroupBy(x => x.RunId).Take(100))
        {
            Snapshot? Pick(string stage)
            {
                var events = group.Where(x => x.Event == stage).ToArray();
                if (events.Length == 0) return null;
                if (events.Length > 1)
                {
                    warnings.Add("run " + group.Key + ": repeated " + stage +
                        " snapshots; refusing to silently choose one.");
                    return null;
                }
                return events[0];
            }
            var a = Pick("ABC_A_GENERATED");
            var b = Pick("ABC_B_FILTERED");
            var c = Pick("ABC_C_CONSUMED");

            string handoffAB = a is null || b is null ? "NOT_OBSERVED" :
                Subsequence(a.Rows, b.Rows) ? "IMPORTED_SUBSEQUENCE" :
                "IMPORTED_FILTER_OR_SOURCE_DIVERGENCE";
            string handoffBC = b is null || c is null ? "NOT_OBSERVED" :
                b.Rows.SequenceEqual(c.Rows) ? "IMPORTED_ROWS_MATCH" :
                "IMPORTED_ROWS_DIVERGED";
            string ta = TemplateState(a, template);
            string tb = TemplateState(b, template);
            string tc = TemplateState(c, template);
            string status = a is null || b is null || c is null ? "INCOMPLETE_CAPTURE" :
                handoffAB != "IMPORTED_SUBSEQUENCE" ? "A_TO_B_NEEDS_INVESTIGATION" :
                handoffBC != "IMPORTED_ROWS_MATCH" ? "B_TO_C_NEEDS_INVESTIGATION" :
                tc == "CUSTOM_ROWS_PRESENT_IN_IMPORTED_DATA" ?
                    "CUSTOM_ROWS_PRESENT_IN_C_CLAIM_NOT_BAG_PROOF" :
                    "CUSTOM_ROWS_NOT_OBSERVED_IN_C";

            if (group.Select(x => x.BuildId).Distinct(StringComparer.OrdinalIgnoreCase).Count() != 1)
            {
                warnings.Add("run " + group.Key + ": mixed game builds, data not correlatable");
                status = "INVALID_MIXED_BUILD";
            }

            runs.Add(new Run(group.Key,
                a is null ? "NOT_OBSERVED" : "IMPORTED_HASH_VALID",
                b is null ? "NOT_OBSERVED" : "IMPORTED_HASH_VALID",
                c is null ? "NOT_OBSERVED" : "IMPORTED_HASH_VALID",
                handoffAB, handoffBC, ta, tb, tc, status,
                "UNATTESTED_IMPORTED_CAPTURES",
                a?.Rows.Count, b?.Rows.Count, c?.Rows.Count));
        }
        if (runs.Count == 0)
            warnings.Add("No A/B/C runtime reward capture. Hook readback is NOT a Lua-object trace.");
        return new Analysis(lines.Length, rejected, ignored, runs, warnings);
    }
}
