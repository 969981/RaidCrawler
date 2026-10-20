using System.Buffers.Binary;
using System.Text.Json;

namespace RaidCrawler.Core.Host;

/// <summary>
/// Byte-level resolver for exlaunch@f9f4b0d InlineHook Entry.
/// No unbounded RAM scan. The start is the versioned main hook site and
/// the exact callback-to-mailbox delta is in the paired native manifest.
/// </summary>
public static class HostProbeLocator
{
    public const ulong HookRva = 0xE32984;
    private static uint U32(ReadOnlySpan<byte> b, int pos) =>
        BinaryPrimitives.ReadUInt32LittleEndian(b.Slice(pos, 4));
    private static ulong U64(ReadOnlySpan<byte> b, int pos) =>
        BinaryPrimitives.ReadUInt64LittleEndian(b.Slice(pos, 8));

    public static HostProbeManifest ParseManifest(string json)
    {
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        string schema = root.GetProperty("schema").GetString() ?? "";
        string game = root.GetProperty("title_id").GetString() ?? "";
        string build = root.GetProperty("build_id_expected").GetString() ?? "";
        string site = root.GetProperty("main_hook_rva").GetString() ?? "";
        int off = root.GetProperty("jit_callback_pointer_offset").GetInt32();
        long delta = root.GetProperty("callback_to_mailbox_delta").GetInt64();
        if (schema != "raidcrawler.host.mailbox-locator.v1" ||
            game != HostProbeMailbox.ExpectedTitleId ||
            build != "709bfd66115298640155fcc4979dba151c7cc79a" ||
            site != "0xE32984" || off != 16 ||
            delta < -16_777_216 || delta > 16_777_216)
            throw new InvalidDataException("自动定位清单与《紫》4.0.0 / 当前 exlaunch 插桩协议不匹配。");
        return new HostProbeManifest(delta);
    }

    public static ulong ParseEntryTarget(ulong hookAbsolute, ReadOnlySpan<byte> code)
    {
        if (code.Length < 24)
            throw new InvalidDataException("发送入口指令长度不足。");
        uint first = U32(code, 0);
        if ((first & 0xFC000000u) == 0x14000000u)
        {
            int imm26 = (int)(first & 0x03FFFFFF);
            long delta = ((long)(imm26 << 6 >> 6)) << 2;
            return checked((ulong)checked((long)hookAbsolute + delta));
        }
        // exlaunch long branch: LDR X17, +8; BR X17; .quad EntryAddress
        if (first == 0x58000051 && U32(code, 4) == 0xD61F0220)
            return U64(code, 8);
        // Some exlaunch patches prepend one NOP before the 16-byte jump.
        if (first == 0xD503201F && U32(code, 4) == 0x58000051 &&
            U32(code, 8) == 0xD61F0220)
            return U64(code, 12);
        throw new InvalidDataException("未找到已安装的 exlaunch InlineHook，拒绝自动定位。");
    }

    public static ulong ParseCallback(ulong entry, ReadOnlySpan<byte> body)
    {
        if (body.Length < 24)
            throw new InvalidDataException("exlaunch Hook Entry 不完整。");
        // Entry = 4 ARM64 instructions + u64 callback.
        // Reject unrelated code instead of blindly treating it as a pointer.
        uint bl = U32(body, 4), branch = U32(body, 12);
        if ((bl & 0xFC000000) != 0x94000000 ||
            (branch & 0xFC000000) != 0x14000000)
            throw new InvalidDataException("exlaunch Hook Entry 指令指纹不匹配。");
        ulong callback = U64(body, 16);
        if (callback < 0x10000 || (callback & 3) != 0 || entry < 0x10000)
            throw new InvalidDataException("Hook 回调指针无效。");
        return callback;
    }

    public static ulong ResolveMailbox(ulong callback, long delta)
    {
        long address = checked((long)callback + delta);
        if (address < 0x10000 || (address & 7) != 0)
            throw new InvalidDataException("计算出的邮箱地址无效。");
        return checked((ulong)address);
    }
}

public sealed record HostProbeManifest(long CallbackToMailboxDelta);
