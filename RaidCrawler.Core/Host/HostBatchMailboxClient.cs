using System.Buffers.Binary;
using System.Security.Cryptography;
using SysBot.Base;

namespace RaidCrawler.Core.Host;

/// <summary>
/// Shared V3 native batch protocol: 28 fixed [item, quantity, rare=0, subject=5]
/// rows in mailbox+0x620, with a per-request nonce and FNV-1a integrity check.
/// Configuring it is not proof of actual game network delivery.
/// </summary>
public static class HostBatchMailboxCodec
{
    private static void Put(Span<byte> data, int offset, uint value) =>
        BinaryPrimitives.WriteUInt32LittleEndian(data.Slice(offset, 4), value);

    public static byte[] SerializeRows(IReadOnlyList<RewardRow> rows)
    {
        if (rows is null || rows.Count is < 1 or > HostProbeMailbox.MaxBatchRows)
            throw new ArgumentException("BATCH_ROW_COUNT_1_TO_28", nameof(rows));
        var result = new byte[HostProbeMailbox.BatchTableBytes];
        HashSet<uint> ids = [];
        for (int i = 0; i < rows.Count; i++)
        {
            RewardRow row = rows[i];
            if (row.ItemId is 0 or > 65535 || row.Quantity is 0 or > 999 ||
                row.RareFlag != 0 || row.SubjectType != 5 || !ids.Add(row.ItemId))
                throw new ArgumentException($"INVALID_BATCH_ROW_{i}", nameof(rows));
            row.Write(result.AsSpan(i * 16, 16));
        }
        return result;
    }

    public static uint Checksum(ulong nonce, uint count, ReadOnlySpan<byte> table)
    {
        if (nonce == 0 || count is 0 or > HostProbeMailbox.MaxBatchRows ||
            table.Length != HostProbeMailbox.BatchTableBytes)
            throw new ArgumentException("INVALID_BATCH_FRAME");
        uint hash = 2166136261u;
        void Feed(uint value)
        {
            unchecked
            {
                for (int n = 0; n < 4; n++)
                {
                    hash = (hash ^ (value & 0xFF)) * 16777619u;
                    value >>= 8;
                }
            }
        }
        Feed((uint)nonce);
        Feed((uint)(nonce >> 32));
        Feed(count);
        for (int i = 0; i < table.Length; i += 4)
            Feed(BinaryPrimitives.ReadUInt32LittleEndian(table.Slice(i, 4)));
        return hash;
    }

    public static byte[] SerializeHeader(ulong nonce, uint count, ReadOnlySpan<byte> table)
    {
        var b = new byte[16];
        BinaryPrimitives.WriteUInt64LittleEndian(b, nonce);
        Put(b, 8, count);
        Put(b, 12, Checksum(nonce, count, table));
        return b;
    }

    public static HostBatchResult ParseResult(ReadOnlySpan<byte> b)
    {
        if (b.Length != 0x20)
            throw new InvalidDataException("Invalid batch report length");
        return new(
            BinaryPrimitives.ReadUInt64LittleEndian(b),
            BinaryPrimitives.ReadUInt32LittleEndian(b.Slice(8, 4)),
            BinaryPrimitives.ReadUInt32LittleEndian(b.Slice(12, 4)),
            BinaryPrimitives.ReadUInt32LittleEndian(b.Slice(16, 4)),
            BinaryPrimitives.ReadUInt64LittleEndian(b.Slice(24, 8)));
    }
}

public sealed record HostBatchResult(
    ulong RequestNonce, uint RequestedRows, uint Checksum,
    uint AppliedRows, ulong ResultNonce);

public sealed record HostBatchRequestReceipt(ulong Nonce, RewardRow[] Rows);

/// <summary>
/// Sends only the config to the installed Native v3 hook through the existing
/// sys-botbase control channel. Never writes directly to a game RaidPoint.
/// </summary>
public sealed class HostBatchMailboxClient(ISwitchConnectionAsync connection)
{
    private readonly HostProbeMailboxClient _probe = new(connection);

    public async Task<HostBatchRequestReceipt> QueueAsync(
        ulong address, HostRewardPresetProfile profile, bool apply, CancellationToken token)
    {
        string? error = HostRewardPreset.Validate(profile);
        if (error is not null) throw new InvalidDataException(error);
        var rows = HostRewardPreset.BuildRows(profile);
        var header = await _probe.ReadStatusAsync(address, token).ConfigureAwait(false);
        if (!header.Enabled || header.Status != 1 || header.CaptureCount == 0 ||
            (header.Capabilities & HostProbeMailbox.BatchProtocolCapability) == 0)
            throw new InvalidOperationException("需要匹配的v3 Native模块与真实房主N1捕获，且已开启捕获。");
        if (apply && (header.Capabilities & 1) == 0)
            throw new InvalidOperationException("Native构建未开启批量游戏数据写入；只能预览。");

        // The N1 capture is not a full 0xAA0 snapshot; this is strictly a
        // reward-row capacity check. The hook repeats full-point validation.
        var cap = await _probe.ReadCaptureAsync(address, token).ConfigureAwait(false);
        if (!cap.Capture.Compare().Matches)
            throw new InvalidOperationException("原版源数据与发包前原始奖励不同，禁止配置写入。");
        byte[] source = new byte[RaidPointLayout.RaidPointLength];
        byte[] bootstrap = new byte[RaidPointLayout.BootstrapLength];
        cap.Capture.SourceRows.CopyTo(source, RaidPointLayout.RewardsInRaidPoint);
        cap.Capture.BootstrapRows.CopyTo(bootstrap, RaidPointLayout.RewardsInBootstrap);
        var plan = HostRewardPreset.Plan(profile, source, bootstrap, _ => true);
        if (!plan.Accepted)
            throw new InvalidOperationException(
                $"原有{plan.OriginalCount}行、拟追加{rows.Length}行，整单拒绝：{plan.Reason}");

        byte[] table = HostBatchMailboxCodec.SerializeRows(rows);
        ulong nonce;
        do { nonce = BinaryPrimitives.ReadUInt64LittleEndian(RandomNumberGenerator.GetBytes(8)); }
        while (nonce == 0);
        byte[] config = HostBatchMailboxCodec.SerializeHeader(nonce, (uint)rows.Length, table);
        // Disarm FIRST; write the complete 448-byte table, then header,
        // then arm the mode LAST. Result fields at +0x610..+0x61F belong to Native.
        await connection.WriteBytesAbsoluteAsync(BitConverter.GetBytes(0u),
            address + HostProbeMailbox.RequestModeOffset, token).ConfigureAwait(false);
        await connection.WriteBytesAbsoluteAsync(table,
            address + HostProbeMailbox.BatchRowsOffset, token).ConfigureAwait(false);
        await connection.WriteBytesAbsoluteAsync(config,
            address + HostProbeMailbox.BatchNonceOffset, token).ConfigureAwait(false);
        await connection.WriteBytesAbsoluteAsync(
            BitConverter.GetBytes((uint)(apply
                ? HostProbeMailbox.BatchApplyMode : HostProbeMailbox.BatchPreviewMode)),
            address + HostProbeMailbox.RequestModeOffset, token).ConfigureAwait(false);
        return new(nonce, rows);
    }

    public async Task<(HostProbeHeader Header, HostProbeCapture Capture,
        HostProbeResult Legacy, HostBatchResult Batch)> ReadConsistentAsync(
        ulong address, CancellationToken token)
    {
        for (int i = 0; i < 4; i++)
        {
            var result = await _probe.ReadConsistentReportAsync(address, token).ConfigureAwait(false);
            byte[] bytes = await connection.ReadBytesAbsoluteAsync(
                address + HostProbeMailbox.BatchNonceOffset, 0x20, token).ConfigureAwait(false);
            var after = await _probe.ReadStatusAsync(address, token).ConfigureAwait(false);
            if (result.Header.Generation == after.Generation &&
                result.Header.CaptureCount == after.CaptureCount &&
                (after.Generation & 1) == 0)
                return (after, result.Capture, result.N2, HostBatchMailboxCodec.ParseResult(bytes));
        }
        throw new IOException("Native批量状态和同场奖励快照未能一致读取。");
    }
}

public static class HostBatchAppendInspector
{
    /// <summary>Ensures every pre-existing and untouched slot remains byte-identical.</summary>
    public static AppendInspection Inspect(
        HostProbeCapture capture, HostProbeResult status,
        HostBatchResult batch, HostBatchRequestReceipt expected)
    {
        if (status.LastResult != 40 ||
            batch.ResultNonce != expected.Nonce ||
            batch.AppliedRows != expected.Rows.Length ||
            batch.RequestedRows != expected.Rows.Length)
            return new(false, "NATIVE_BATCH_RESULT_OR_NONCE_MISMATCH");
        if (capture.SourceRows.Length != RaidPointLayout.RewardsByteLength ||
            capture.BootstrapRows.Length != RaidPointLayout.RewardsByteLength)
            return new(false, "INVALID_SNAPSHOT_LENGTH");
        if (status.OriginalRows > RaidPointLayout.RewardCapacity - expected.Rows.Length)
            return new(false, "CAPACITY_EXCEEDED");
        int offset = checked((int)status.OriginalRows * 16);
        int size = expected.Rows.Length * 16;
        if (!capture.SourceRows.AsSpan(offset, size).SequenceEqual(new byte[size]))
            return new(false, "TARGET_NOT_EMPTY");
        if (!capture.SourceRows.AsSpan(..offset).SequenceEqual(capture.BootstrapRows.AsSpan(..offset)) ||
            !capture.SourceRows.AsSpan(offset + size).SequenceEqual(
                capture.BootstrapRows.AsSpan(offset + size)))
            return new(false, "ORIGINAL_OR_OTHER_BYTES_MODIFIED");
        for (int i = 0; i < expected.Rows.Length; i++)
        {
            var actual = RewardRow.Read(capture.BootstrapRows.AsSpan(offset + i * 16, 16));
            if (actual != expected.Rows[i]) return new(false, $"ROW_{i}_MISMATCH");
        }
        return new(true, "EXACT_BATCH_OUTGOING_ONLY");
    }
}
