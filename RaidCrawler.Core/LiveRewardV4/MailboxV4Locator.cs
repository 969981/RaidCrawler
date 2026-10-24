using System.Buffers.Binary;
using System.Text.Json;
using SysBot.Base;

namespace RaidCrawler.Core.LiveRewardV4;

/// <summary>
/// Version-scoped exlaunch InlineHook locator. Offline byte parser is testable.
/// A manifest must come from the SAME build as the installed readonly subsdk9.
/// Runtime TitleID/version + nearby 16 code words are checked by discovery.
/// </summary>
public static class MailboxV4Locator
{
    public const string VioletTitleId = "01008F6008C5E000";
    public const string VioletVersion = "4.0.0";
    public const string ExpectedBuildId = "709bfd66115298640155fcc4979dba151c7cc79a";
    public const ulong HookRva = 0xE32984;
    private static uint U32(ReadOnlySpan<byte> data, int offset) =>
        BinaryPrimitives.ReadUInt32LittleEndian(data.Slice(offset, 4));
    private static ulong U64(ReadOnlySpan<byte> data, int offset) =>
        BinaryPrimitives.ReadUInt64LittleEndian(data.Slice(offset, 8));

    public static MailboxV4Manifest ParseManifest(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        if (root.GetProperty("schema").GetString() != "raidcrawler.live-reward.mailbox-locator.v4" ||
            root.GetProperty("title_id").GetString() != VioletTitleId ||
            root.GetProperty("version_expected").GetString() != VioletVersion ||
            root.GetProperty("build_id_expected").GetString() != ExpectedBuildId ||
            root.GetProperty("hook_rva").GetString() != "0xE32984" ||
            root.GetProperty("inline_entry_callback_pointer_offset").GetInt32() != 16 ||
            root.GetProperty("read_only").GetBoolean() != true ||
            root.GetProperty("native_write_capability").GetBoolean() != false)
            throw new InvalidDataException("V4_MANIFEST_MISMATCH");
        long delta = root.GetProperty("callback_to_mailbox_delta").GetInt64();
        string sha = root.GetProperty("subsdk9_sha256").GetString() ?? "";
        if (delta is < -16_777_216 or > 16_777_216 ||
            sha.Length != 64 || !sha.All(Uri.IsHexDigit))
            throw new InvalidDataException("V4_MANIFEST_INVALID_SYMBOL_DELTA_OR_SHA");
        return new MailboxV4Manifest(delta, sha.ToLowerInvariant());
    }

    public static ulong EntryFromHook(ulong hook, ReadOnlySpan<byte> code)
    {
        if (code.Length < 24 || hook < 0x10000)
            throw new InvalidDataException("V4_HOOK_TOO_SHORT");
        uint first = U32(code, 0);
        if ((first & 0xFC000000u) == 0x14000000u)
        {
            int signed = (int)(first & 0x03FFFFFF);
            long bytes = ((long)(signed << 6 >> 6)) << 2;
            return checked((ulong)((long)hook + bytes));
        }
        if (first == 0x58000051u && U32(code, 4) == 0xD61F0220u)
            return U64(code, 8);
        if (first == 0xD503201Fu && U32(code, 4) == 0x58000051u &&
            U32(code, 8) == 0xD61F0220u)
            return U64(code, 12);
        throw new InvalidDataException("V4_HOOK_NOT_INSTALLED");
    }

    public static ulong CallbackFromEntry(ulong entry, ReadOnlySpan<byte> entryCode)
    {
        if (entryCode.Length < 24 || entry < 0x10000 ||
            (U32(entryCode, 4) & 0xFC000000u) != 0x94000000u ||
            (U32(entryCode, 12) & 0xFC000000u) != 0x14000000u)
            throw new InvalidDataException("V4_EXLAUNCH_ENTRY_MISMATCH");
        ulong callback = U64(entryCode, 16);
        if (callback < 0x10000 || (callback & 3u) != 0)
            throw new InvalidDataException("V4_BAD_CALLBACK");
        return callback;
    }

    public static ulong AddressFromCallback(ulong callback, long delta)
    {
        long address;
        try { address = checked(checked((long)callback) + delta); }
        catch (OverflowException ex)
        {
            throw new InvalidDataException("V4_BAD_MAILBOX_ADDRESS", ex);
        }
        if (address < 0x10000 || (address & 7L) != 0 ||
            (ulong)address > ulong.MaxValue - MailboxV4.MailboxLength)
            throw new InvalidDataException("V4_BAD_MAILBOX_ADDRESS");
        return (ulong)address;
    }

    // Game fingerprint does not itself prove the loaded NSO Build ID.
    public static readonly (ulong Rva, uint Word)[] NeighborOpcodes =
    [
        (0xE32968, 0x9101E281), (0xE3296C, 0x52807902),
        (0xE32970, 0xF9035EEB), (0xE32980, 0x94982624),
        (0xE32988, 0xAA1403E0), (0xE32990, 0xAA1703E1),
        (0xE32994, 0x2A1603E2), (0xE329A0, 0x52800024),
        (0xE329B0, 0x141D8073),
    ];

    public static async Task<(ulong Address, MailboxV4Header Header)> DiscoverAsync(
        ISwitchConnectionAsync connection, MailboxV4Manifest manifest,
        CancellationToken token)
    {
        string tid = (await connection.GetTitleID(token).ConfigureAwait(false))
            .Trim().Replace("0x", "", StringComparison.OrdinalIgnoreCase);
        string version = (await connection.GetGameInfo("version", token).ConfigureAwait(false))
            .Trim().TrimEnd('\0');
        if (!tid.Equals(VioletTitleId, StringComparison.OrdinalIgnoreCase) ||
            version != VioletVersion)
            throw new InvalidOperationException("V4_ONLY_VIOLET_400");
        foreach (var (rva, word) in NeighborOpcodes)
        {
            byte[] bytes = await connection.ReadBytesMainAsync(rva, 4, token)
                .ConfigureAwait(false);
            if (bytes.Length != 4 || U32(bytes, 0) != word)
                throw new InvalidOperationException($"V4_ELF_FINGERPRINT_MISMATCH_{rva:X}");
        }
        ulong main = await connection.GetMainNsoBaseAsync(token).ConfigureAwait(false);
        if (main < 0x10000) throw new InvalidDataException("V4_INVALID_MAIN_BASE");
        ulong hook = checked(main + HookRva);
        byte[] hookCode = await connection.ReadBytesAbsoluteAsync(hook, 24, token)
            .ConfigureAwait(false);
        ulong entry = EntryFromHook(hook, hookCode);
        ulong callback = CallbackFromEntry(entry,
            await connection.ReadBytesAbsoluteAsync(entry, 24, token).ConfigureAwait(false));
        ulong address = AddressFromCallback(callback, manifest.CallbackToMailboxDelta);
        var status = await new MailboxV4Client(
            new SysBotMailboxV4Transport(connection))
            .ReadHeaderAsync(address, token).ConfigureAwait(false);
        return (address, status);
    }
}
public sealed record MailboxV4Manifest(long CallbackToMailboxDelta, string NsoSha256);
