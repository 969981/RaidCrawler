using System.Buffers.Binary;
using System.Text.Json;

namespace RaidCrawler.Core.Structures;

/// <summary>
/// Verify and inspect the Switch SIDE N0/N1 native observer's JSONL output.
/// Import only: no console memory access. The producer is a Skyline callback,
/// not proof of host direction, delivery or inventory/save persistence.
/// </summary>
public static class NativeRaidRewardCapture
{
    public const int Slots = 45;
    public const int MaxJsonlBytes = 4 * 1024 * 1024;
    public const int MaxLines = 512;

    public sealed record Row(int Slot, uint ItemId, uint Quantity, uint Rare, uint Subject, string State);
    public sealed record Event(ulong Sequence, bool SourceMatchesWire,
        string SourceFnv64, string WireFnv64,
        IReadOnlyList<Row> SourceRows, IReadOnlyList<Row> WireRows,
        int SourceNonzero, int WireNonzero, int SourceUnrecognized, int WireUnrecognized,
        int SourceTailZeros, int WireTailZeros,
        string Evidence = "NATIVE_SERIALIZER_READONLY_NO_GUEST_RECEIPT");
    public sealed record Analysis(int Lines, int Ignored, int Rejected,
        IReadOnlyList<Event> Events, IReadOnlyList<string> Warnings)
    {
        public bool RealSwitchCpuHookVerified => false; // JSONL can be forged or imported.
        public bool GuestRewardReceivedVerified => false;
    }

    private static string RequiredString(JsonElement item, string field) =>
        item.TryGetProperty(field, out var element) &&
        element.ValueKind == JsonValueKind.String
            ? element.GetString() ?? throw new FormatException(field + " null")
            : throw new FormatException(field + " absent");

    private static int RequiredInt(JsonElement item, string field) =>
        item.TryGetProperty(field, out var element) &&
        element.ValueKind == JsonValueKind.Number &&
        element.TryGetInt32(out int value)
            ? value : throw new FormatException(field + " absent or out of range");

    private static bool RequiredBool(JsonElement item, string field) =>
        item.TryGetProperty(field, out var element) &&
        element.ValueKind is JsonValueKind.True or JsonValueKind.False
            ? element.GetBoolean() : throw new FormatException(field + " absent");

    private static ulong ParseSequence(JsonElement item)
    {
        if (!item.TryGetProperty("seq", out var value) ||
            value.ValueKind != JsonValueKind.Number || !value.TryGetUInt64(out ulong seq))
            throw new FormatException("Sequence missing");
        return seq;
    }

    public static ulong Fnv64(IReadOnlyList<Row> rows)
    {
        ulong hash = 0xcbf29ce484222325UL;
        Span<byte> raw = stackalloc byte[16];
        foreach (var row in rows)
        {
            BinaryPrimitives.WriteUInt32LittleEndian(raw[0..4], row.ItemId);
            BinaryPrimitives.WriteUInt32LittleEndian(raw[4..8], row.Quantity);
            BinaryPrimitives.WriteUInt32LittleEndian(raw[8..12], row.Rare);
            BinaryPrimitives.WriteUInt32LittleEndian(raw[12..16], row.Subject);
            foreach (byte b in raw)
            {
                hash ^= b;
                hash = unchecked(hash * 0x100000001b3UL);
            }
        }
        return hash;
    }

    private static Row[] ReadRows(JsonElement root, string property)
    {
        if (!root.TryGetProperty(property, out var arr) ||
            arr.ValueKind != JsonValueKind.Array || arr.GetArrayLength() != Slots)
            throw new FormatException("Expected exactly 45 slots: " + property);
        var rows = new Row[Slots];
        int i = 0;
        foreach (var item in arr.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object ||
                RequiredInt(item, "slot") != i)
                throw new FormatException("Noncontiguous slot index");
            static uint Number(JsonElement element, string field) =>
                element.TryGetProperty(field, out var value) &&
                value.ValueKind == JsonValueKind.Number && value.TryGetUInt32(out uint n)
                    ? n : throw new FormatException("Invalid row field: " + field);
            uint id = Number(item, "itemId"), qty = Number(item, "quantity");
            uint rare = Number(item, "rare"), subject = Number(item, "subject");
            string state = RequiredString(item, "state");
            bool zero = id == 0 && qty == 0 && rare == 0 && subject == 0;
            bool valid = id is >= 1 and <= 65535 && qty is >= 1 and <= 999 &&
                         rare <= 1 && subject <= 5;
            string expected = zero ? "ZERO_CANDIDATE" :
                valid ? "NONZERO" : "NONZERO_UNRECOGNIZED";
            if (state != expected)
                throw new FormatException("Row state disagrees with raw values");
            rows[i] = new Row(i, id, qty, rare, subject, state);
            i++;
        }
        return rows;
    }

    private static Event Parse(JsonElement obj)
    {
        if (RequiredString(obj, "event") != "N1_NATIVE_RAID_REWARD_SNAPSHOT" ||
            RequiredString(obj, "version") != "Violet4.0.0" ||
            RequiredString(obj, "provenance") !=
                "NATIVE_SERIALIZER_CALLBACK_NOT_RECEIVER_ATTESTED" ||
            RequiredString(obj, "direction") != "UNVERIFIED_FROM_CALLBACK_ALONE" ||
            RequiredBool(obj, "memoryWritten") ||
            RequiredBool(obj, "readyForAppend") ||
            RequiredBool(obj, "guestReceiptVerified") ||
            RequiredBool(obj, "bagSaveVerified"))
            throw new FormatException("Unknown or unexpectedly authoritative native event");
        var src = ReadRows(obj, "sourceRows");
        var dst = ReadRows(obj, "wireRows");
        ulong a = Fnv64(src), b = Fnv64(dst);
        string aStr = a.ToString("X16"), bStr = b.ToString("X16");
        if (aStr != RequiredString(obj, "sourceFnv64") ||
            bStr != RequiredString(obj, "wireFnv64"))
            throw new FormatException("Native reward row digest mismatch");
        bool same = src.SequenceEqual(dst);
        if (RequiredBool(obj, "originalSourceMatchesWire") != same)
            throw new FormatException("Native source/wire match flag disagrees with rows");
        var x = obj.GetProperty("sourceStats");
        var y = obj.GetProperty("wireStats");
        int sc = src.Count(z => z.State != "ZERO_CANDIDATE");
        int dc = dst.Count(z => z.State != "ZERO_CANDIDATE");
        int sx = src.Count(z => z.State == "NONZERO_UNRECOGNIZED");
        int dx = dst.Count(z => z.State == "NONZERO_UNRECOGNIZED");
        if (RequiredInt(x, "nonzero") != sc || RequiredInt(y, "nonzero") != dc ||
            RequiredInt(x, "nonzeroUnrecognized") != sx ||
            RequiredInt(y, "nonzeroUnrecognized") != dx)
            throw new FormatException("Native row counts inconsistent");
        static int Tail(Row[] rows) => rows.Reverse().TakeWhile(r => r.State == "ZERO_CANDIDATE").Count();
        int sa = Tail(src), db = Tail(dst);
        if (RequiredInt(x, "trailingZeroCandidates") != sa ||
            RequiredInt(y, "trailingZeroCandidates") != db)
            throw new FormatException("Native trailing blank count inconsistent");
        return new Event(ParseSequence(obj), same, aStr, bStr,
            src, dst, sc, dc, sx, dx, sa, db);
    }

    public static Analysis Analyze(string contents)
    {
        if (System.Text.Encoding.UTF8.GetByteCount(contents) > MaxJsonlBytes)
            throw new InvalidDataException("Native JSONL exceeds 4 MiB");
        string[] lines = contents.Split('\n');
        if (lines.Length > MaxLines + 1)
            throw new InvalidDataException("Too many native JSONL lines");
        var results = new List<Event>();
        var warnings = new List<string>();
        int rejected = 0, ignored = 0;
        foreach (string line in lines)
        {
            if (string.IsNullOrWhiteSpace(line)) continue;
            try
            {
                using var document = JsonDocument.Parse(line);
                JsonElement root = document.RootElement;
                if (root.ValueKind != JsonValueKind.Object ||
                    !root.TryGetProperty("event", out var kind) ||
                    kind.GetString() != "N1_NATIVE_RAID_REWARD_SNAPSHOT")
                {
                    ignored++;
                    continue;
                }
                results.Add(Parse(root));
            }
            catch (Exception error) when (error is JsonException or FormatException
                                           or ArgumentException or InvalidOperationException)
            {
                rejected++;
                if (warnings.Count < 10)
                    warnings.Add(error.GetType().Name + ": rejected invalid native event");
            }
        }
        return new Analysis(lines.Length, ignored, rejected, results, warnings);
    }
}
