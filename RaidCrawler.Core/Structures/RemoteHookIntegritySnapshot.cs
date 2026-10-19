using System.Buffers.Binary;
using System.Security.Cryptography;

namespace RaidCrawler.Core.Structures;

/// <summary>
/// Read-only code/header/template evidence. Never claims a CPU Hook hit or
/// an actual game reward. Repeated snapshots can reveal code or template drift
/// but do not detect transient execution of the Hook.
/// </summary>
public static class RemoteHookIntegritySnapshot
{
    public sealed record Sample(
        string EntryState,
        string ReaderState,
        bool StableEpoch,
        uint Epoch,
        bool Armed,
        string RewardSha256,
        int Rows,
        int Subject0,
        int Subject5,
        string TemplateComparison,
        string HookHitEvidence = "NOT_OBSERVED",
        string ReceiverEvidence = "NOT_OBSERVED",
        string FilteringEvidence = "NOT_OBSERVED");

    public static async Task<Sample> CaptureAsync(
        Func<ulong, int, CancellationToken, Task<byte[]>> read,
        string? selectedTemplateSha256,
        CancellationToken ct = default)
    {
        const ulong entryRva = 0xE32984;
        const ulong caveRva = 0x343FC90;
        byte[] entry = await read(entryRva, 4, ct);
        byte[] epochBefore = await read(caveRva + 0x90, 4, ct);
        byte[] cave = await read(caveRva, 880, ct);
        byte[] epochAfter = await read(caveRva + 0x90, 4, ct);
        if (entry.Length != 4 || epochBefore.Length != 4 ||
            cave.Length != 880 || epochAfter.Length != 4)
            throw new IOException("Incomplete Hook integrity snapshot.");

        uint entryWord = BinaryPrimitives.ReadUInt32LittleEndian(entry);
        string entryState = entryWord == RemoteViolet400HookPayload.EntryBranch
            ? "EXPECTED_BRANCH"
            : entryWord == RemoteViolet400HookPayload.OriginalInstruction
                ? "ORIGINAL" : "UNKNOWN";
        bool codeOk = RemoteViolet400HookPayload.MatchesReader(cave);
        bool headerOk = RemoteViolet400HookPayload.MatchesHeader(
            cave.AsSpan(0x90, 16));
        uint before = BinaryPrimitives.ReadUInt32LittleEndian(epochBefore);
        uint after = BinaryPrimitives.ReadUInt32LittleEndian(epochAfter);
        uint middle = BinaryPrimitives.ReadUInt32LittleEndian(cave.AsSpan(0x90, 4));
        bool stable = before == after && before == middle;

        ReadOnlySpan<byte> template = cave.AsSpan(0xA0, 720);
        string digest = Convert.ToHexString(SHA256.HashData(template));
        int rows = 0, all = 0, charm = 0;
        for (int slot = 0; slot < 45; slot++)
        {
            ReadOnlySpan<byte> row = template.Slice(slot * 16, 16);
            if (row.IndexOfAnyExcept((byte)0) < 0) continue;
            rows++;
            uint subject = BinaryPrimitives.ReadUInt32LittleEndian(row[12..]);
            if (subject == 0) all++;
            if (subject == 5) charm++;
        }

        string templateState = selectedTemplateSha256 is null
            ? "NOT_PROVIDED"
            : string.Equals(selectedTemplateSha256, digest,
                StringComparison.OrdinalIgnoreCase) ? "HASH_MATCH" : "HASH_MISMATCH";

        return new Sample(entryState,
            codeOk && headerOk ? "V2_CODE_AND_HEADER_MATCH" : "UNKNOWN_OR_MODIFIED",
            stable, after, stable && (after & 1) == 0,
            digest, rows, all, charm, templateState);
    }
}
