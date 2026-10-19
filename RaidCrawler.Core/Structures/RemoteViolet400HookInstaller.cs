using System.Buffers.Binary;

namespace RaidCrawler.Core.Structures;

/// <summary>
/// Explicit, foreground-only Switch remote patch transaction for Violet 4.0.0.
/// The supplied identity verifier must validate title/version/Build ID prefix,
/// all non-entry opcode anchors and a stable NSO base before every transaction.
/// This does not produce proof of callback hits, rewards, or save persistence.
/// </summary>
public sealed class RemoteViolet400HookInstaller(
    Func<ulong, int, CancellationToken, Task<byte[]>> read,
    Func<byte[], ulong, CancellationToken, Task> write,
    Func<CancellationToken, Task> verifyIdentity)
{
    private static readonly ulong Hook = NativeRaidCodeFingerprint.SerializerHookRva;
    private static readonly ulong Cave = NativeRaidCodeFingerprint.EndOfTextPaddingRva;
    private const uint Original = RemoteViolet400HookPayload.OriginalInstruction;
    private readonly SemaphoreSlim gate = new(1, 1);

    public enum InstallState
    {
        Empty, InstalledDisarmed, InstalledArmed,
        DetachedCodePresent, ConflictOrIncomplete,
    }

    public sealed record State(InstallState Installation, uint? Epoch, string Detail);
    private static uint Word(ReadOnlySpan<byte> b) =>
        BinaryPrimitives.ReadUInt32LittleEndian(b);

    private async Task<byte[]> ReadExact(ulong offset, int size, CancellationToken ct)
    {
        byte[] data = await read(offset, size, ct);
        if (data.Length != size)
            throw new IOException($"Short main read at 0x{offset:X}: {data.Length}/{size}.");
        return data;
    }

    private RemoteRewardEpochUpdater Updater() =>
        new(read, write, verifyIdentity);

    private async Task<State> Observe(CancellationToken ct)
    {
        await verifyIdentity(ct);
        uint entry = Word(await ReadExact(Hook, 4, ct));
        byte[] image = await ReadExact(Cave, RemoteViolet400HookPayload.TotalBytes, ct);
        bool unmodified = entry == Original;
        bool patched = entry == RemoteViolet400HookPayload.EntryBranch;
        bool allZero = Array.TrueForAll(image, b => b == 0);
        bool correctCode = RemoteViolet400HookPayload.MatchesReader(image);
        bool correctHeader =
            RemoteViolet400HookPayload.MatchesHeader(image.AsSpan(0x90, 16));
        if (unmodified && allZero)
            return new(InstallState.Empty, null, "入口原版，候选 880B 全零。");
        if (correctCode && correctHeader && (patched || unmodified))
        {
            uint epoch = Word(image.AsSpan(0x90, 4));
            if (unmodified)
                return new(InstallState.DetachedCodePresent, epoch,
                    "原始入口已恢复；旧跳板仍保留以便在途调用结束。");
            return new(epoch % 2 == 0 ?
                    InstallState.InstalledArmed : InstallState.InstalledDisarmed,
                epoch, "入口跳转及完整跳板匹配；尚不能证明 CPU 命中。");
        }
        return new(InstallState.ConflictOrIncomplete, null,
            "入口或内存不属于已知原版/本版本；禁止覆盖。");
    }

    public async Task<State> StatusAsync(CancellationToken ct = default)
    {
        await gate.WaitAsync(ct);
        try { return await Observe(ct); }
        finally { gate.Release(); }
    }

    /// <summary>
    /// Stage the entire disarmed image, readback, re-check original entry,
    /// and patch one aligned branch instruction LAST.
    /// If an error occurs, never erase potentially executing code.
    /// </summary>
    public async Task<State> InstallAsync(
        ReadOnlyMemory<byte> rewardTemplate, CancellationToken ct = default)
    {
        byte[] payload = RemoteViolet400HookPayload.MakeDisarmed(rewardTemplate.Span);
        await gate.WaitAsync(ct);
        try
        {
            var before = await Observe(ct);
            if (before.Installation != InstallState.Empty)
                throw new InvalidOperationException(
                    "Refusing install over non-empty or previously detached code region: " +
                    before.Detail);
            // The final PLT instruction is checked separately from the entry.
            uint lastPlt = Word(await ReadExact(
                NativeRaidCodeFingerprint.LastPltInstructionRva, 4, ct));
            if (lastPlt != NativeRaidCodeFingerprint.LastPltInstructionOpcode)
                throw new InvalidOperationException("The final PLT opcode differs.");
            await verifyIdentity(ct);
            await write(payload, Cave, ct);
            byte[] echoed = await ReadExact(Cave, payload.Length, ct);
            if (!echoed.AsSpan().SequenceEqual(payload))
                throw new IOException("Full 880B trampoline readback mismatch; entry not patched.");
            // Do not override another patch installed while staging the cave.
            await verifyIdentity(ct);
            if (Word(await ReadExact(Hook, 4, ct)) != Original)
                throw new InvalidOperationException(
                    "Serializer entry changed during staging; refusing overwrite.");
            byte[] branch = new byte[4];
            BinaryPrimitives.WriteUInt32LittleEndian(
                branch, RemoteViolet400HookPayload.EntryBranch);
            await write(branch, Hook, ct);
            uint observed = Word(await ReadExact(Hook, 4, ct));
            if (observed != RemoteViolet400HookPayload.EntryBranch)
                throw new IOException("Entry branch readback mismatch; inspect before further actions.");
            return await Observe(ct);
        }
        finally { gate.Release(); }
    }

    public async Task<RemoteRewardEpochUpdater.UpdateResult> UpdateAsync(
        ReadOnlyMemory<byte> rows, CancellationToken ct = default)
    {
        await gate.WaitAsync(ct);
        try { return await Updater().ReplaceAsync(rows, ct); }
        finally { gate.Release(); }
    }

    public async Task<State> SetEnabledAsync(bool enabled, CancellationToken ct = default)
    {
        await gate.WaitAsync(ct);
        try
        {
            await Updater().SetArmedAsync(enabled, ct);
            return await Observe(ct);
        }
        finally { gate.Release(); }
    }

    /// <summary>
    /// Restore only the original entry after disarming; intentionally leave
    /// the code cave intact because a previously entered callback may still
    /// be running there. Reinstall in the same process is intentionally blocked.
    /// </summary>
    public async Task<State> UninstallAsync(CancellationToken ct = default)
    {
        await gate.WaitAsync(ct);
        try
        {
            var current = await Observe(ct);
            if (current.Installation is not
                (InstallState.InstalledArmed or InstallState.InstalledDisarmed))
                throw new InvalidOperationException("Only a verified v2 Hook can be detached.");
            await Updater().SetArmedAsync(false, ct);
            await verifyIdentity(ct);
            if (Word(await ReadExact(Hook, 4, ct)) !=
                RemoteViolet400HookPayload.EntryBranch)
                throw new InvalidOperationException("Entry changed before restore.");
            byte[] original = new byte[4];
            BinaryPrimitives.WriteUInt32LittleEndian(original, Original);
            await write(original, Hook, ct);
            if (Word(await ReadExact(Hook, 4, ct)) != Original)
                throw new IOException("Original entry readback mismatch; manual recovery required.");
            return await Observe(ct);
        }
        finally { gate.Release(); }
    }
}
