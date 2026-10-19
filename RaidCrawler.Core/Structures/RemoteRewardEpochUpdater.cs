using System.Buffers.Binary;

namespace RaidCrawler.Core.Structures;

/// <summary>
/// Transport-independent host-side protocol for a prospective seqlock-capable
/// inline trampoline. The CURRENT native/sv_remote_code_patch/stub.S does NOT
/// implement this protocol: all requests fail closed until an independently
/// validated v2 trampoline with matching header/entry is installed.
/// A verified runtime identity must be supplied by the caller on every call.
/// </summary>
public sealed class RemoteRewardEpochUpdater(
    Func<ulong, int, CancellationToken, Task<byte[]>> readMain,
    Func<byte[], ulong, CancellationToken, Task> writeMain,
    Func<CancellationToken, Task> verifyRuntimeIdentity)
{
    public const ulong HookRva = 0xE32984;
    public const ulong CaveRva = 0x343FC90;
    public const ulong EpochRva = CaveRva + 0x90;
    public const ulong MagicRva = CaveRva + 0x94;
    public const ulong MarkerRva = CaveRva + 0x98;
    public const ulong TemplateRva = CaveRva + 0xA0;
    // ASCII "RHS2" little endian. Old stub.S contains zero, so it is rejected.
    public const uint ProtocolMagic = 0x32534852;
    public static readonly (ulong Offset, uint Opcode)[] ReaderAnchors =
    [
        (0x18, 0x88DFFE0A), // ldar w10,[x16]
        (0x4C, 0x88DFFE0B), // ldar w11,[x16]
        (0x50, 0x6B0B015F), // cmp w10,w11
        (0x58, 0x9104F28B), // add x11,x20,#0x13c
        (0x74, 0xD51B420F), // msr nzcv,x15
        (0x88, 0xAA1503E8), // original mov x8,x21
        (0x8C, 0x1767CB1B), // branch back to main+0xE32988
    ];
    public const uint MinEpoch = 1; // odd = disarmed/updating, even >= 2 = armed
    public const uint MaxEpoch = uint.MaxValue - 2;

    private static readonly byte[] Marker = "RaidPoin"u8.ToArray();
    private readonly SemaphoreSlim gate = new(1, 1);

    public sealed record Snapshot(uint Epoch, bool Armed, byte[] Rewards);
    public sealed record UpdateResult(uint OldEpoch, uint NewEpoch, bool Armed,
        string Detail);

    private static uint BranchWord
    {
        get
        {
            long displacement = (long)CaveRva - (long)HookRva;
            if (displacement % 4 != 0 || displacement is < -(1L << 27) or >= (1L << 27))
                throw new InvalidOperationException("Hook branch cannot reach cave.");
            return 0x14000000 | (uint)((displacement / 4) & 0x03ffffff);
        }
    }

    private async Task<byte[]> ReadExact(ulong rva, int length, CancellationToken ct)
    {
        byte[] b = await readMain(rva, length, ct);
        if (b.Length != length)
            throw new IOException($"Short read at main+0x{rva:X}: {b.Length}/{length}.");
        return b;
    }

    private async Task<uint> ReadEpoch(CancellationToken ct) =>
        BinaryPrimitives.ReadUInt32LittleEndian(
            await ReadExact(EpochRva, 4, ct));

    private async Task WriteAndVerifyEpoch(uint value, CancellationToken ct)
    {
        byte[] bytes = new byte[4];
        BinaryPrimitives.WriteUInt32LittleEndian(bytes, value);
        await writeMain(bytes, EpochRva, ct);
        uint observed = await ReadEpoch(ct);
        if (observed != value)
            throw new IOException($"Epoch readback mismatch; expected {value}, observed {observed}. " +
                "Do not touch the reward table.");
    }

    private async Task EnsureInstalled(CancellationToken ct)
    {
        await verifyRuntimeIdentity(ct); // Must verify title, build prefix and live NSO identity.
        byte[] entry = await ReadExact(HookRva, 4, ct);
        if (BinaryPrimitives.ReadUInt32LittleEndian(entry) != BranchWord)
            throw new InvalidOperationException("Unknown entry instruction: v2 Hook not installed.");
        byte[] header = await ReadExact(EpochRva, 16, ct);
        if (BinaryPrimitives.ReadUInt32LittleEndian(header.AsSpan(4)) != ProtocolMagic ||
            !header.AsSpan(8, 8).SequenceEqual(Marker))
            throw new InvalidOperationException("v2 seqlock signature is absent. " +
                "Refusing to treat the legacy mode word as an epoch.");
        byte[] readerCode = await ReadExact(CaveRva, 0x90, ct);
        foreach (var (offset, expected) in ReaderAnchors)
        {
            uint actual = BinaryPrimitives.ReadUInt32LittleEndian(
                readerCode.AsSpan(checked((int)offset), 4));
            if (actual != expected)
                throw new InvalidOperationException(
                    $"v2 reader mismatch at cave+0x{offset:X}; refusing hot update.");
        }
        uint version = BinaryPrimitives.ReadUInt32LittleEndian(header);
        if (version < MinEpoch || version > MaxEpoch)
            throw new InvalidOperationException("Unexpected epoch / possible rollover.");
    }

    /// <summary>Read data only when epoch remains unchanged; no runtime writes.</summary>
    public async Task<Snapshot> SnapshotAsync(CancellationToken ct = default)
    {
        await gate.WaitAsync(ct);
        try
        {
            await EnsureInstalled(ct);
            uint before = await ReadEpoch(ct);
            byte[] payload = await ReadExact(TemplateRva, RaidRewardEditor.RewardBytes, ct);
            uint after = await ReadEpoch(ct);
            if (before != after)
                throw new IOException("Reward table changed during snapshot.");
            RaidRewardEditor.DecodeSlots(payload);
            return new Snapshot(after, (after & 1) == 0, payload);
        }
        finally { gate.Release(); }
    }

    /// <summary>
    /// Publish a new table in the same game process. Odd epoch suppresses new
    /// reads while table is replaced. A v2 trampoline must compare epoch AFTER
    /// copying and restore its source RaidPoint on an epoch mismatch.
    /// If any write fails, DO NOT publish even: odd means safe fallback.
    /// </summary>
    public async Task<UpdateResult> ReplaceAsync(
        ReadOnlyMemory<byte> desired, CancellationToken ct = default)
    {
        byte[] replacement = desired.ToArray();
        RaidRewardEditor.DecodeSlots(replacement);
        await gate.WaitAsync(ct);
        try
        {
            await EnsureInstalled(ct);
            uint before = await ReadEpoch(ct);
            bool armed = (before & 1) == 0;
            if (before > MaxEpoch - 2)
                throw new InvalidOperationException("Epoch nearing wraparound.");
            byte[] old = await ReadExact(TemplateRva, RaidRewardEditor.RewardBytes, ct);
            if (old.AsSpan().SequenceEqual(replacement))
                return new UpdateResult(before, before, armed, "Template unchanged.");

            // IMPORTANT: Even when already odd, advance by 2 to invalidate
            // a delayed callback's previously observed epoch.
            uint staging = armed ? before + 1 : before + 2;
            await WriteAndVerifyEpoch(staging, ct); // must finish before writing data
            await writeMain(replacement, TemplateRva, ct);
            byte[] observed = await ReadExact(TemplateRva,
                RaidRewardEditor.RewardBytes, ct);
            if (!observed.AsSpan().SequenceEqual(replacement))
                throw new IOException("Template readback mismatch; leave odd (disarmed).");

            // With an originally disarmed Hook, keep it disarmed.
            uint finalEpoch = armed ? staging + 1 : staging;
            if (armed)
                await WriteAndVerifyEpoch(finalEpoch, ct);
            // Process may still run an old callback, but its post-copy epoch
            // mismatch must restore the ORIGINAL game reward table.
            return new UpdateResult(before, finalEpoch, armed,
                "Published with version guard; actual callback not verified.");
        }
        finally { gate.Release(); }
    }

    /// <summary>Toggle next-callback behavior without restarting the game.</summary>
    public async Task<uint> SetArmedAsync(bool enabled, CancellationToken ct = default)
    {
        await gate.WaitAsync(ct);
        try
        {
            await EnsureInstalled(ct);
            uint current = await ReadEpoch(ct);
            bool active = (current & 1) == 0;
            if (active == enabled)
                return current;
            if (current >= MaxEpoch)
                throw new InvalidOperationException("Epoch rollover imminent.");
            if (enabled)
            {
                byte[] payload = await ReadExact(TemplateRva,
                    RaidRewardEditor.RewardBytes, ct);
                RaidRewardEditor.DecodeSlots(payload);
            }
            uint next = current + 1;
            await WriteAndVerifyEpoch(next, ct);
            return next;
        }
        finally { gate.Release(); }
    }
}
