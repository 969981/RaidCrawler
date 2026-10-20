using System.Buffers.Binary;
using System.Diagnostics;
using System.Security.Cryptography;
using SysBot.Base;

namespace RaidCrawler.Core.Host;

/// <summary>
/// Passive, stock-sys-botbase-only search based on PokéLDN's retail
/// RaidPoint 0x3E8 layout. This discovers MEMORY COPIES, not the source
/// that is proven to be selected by the currently active room.
/// </summary>
public sealed class HostRaidPointAutoLocator(ISwitchConnectionAsync connection)
{
    public const int BlockSize = 0x4000;
    public const int MaxScanMegabytes = 64;
    public const int MaxCandidates = 24;
    private const ulong MainStaticDataStart = 0x4383000;
    private const ulong MainStaticDataEnd = 0x46D9000;
    private static readonly byte[] Needle = "RaidPoint_"u8.ToArray();
    // Current-process discovery cache, never a static ELF pointer root.
    // Invalidated when main/heap base or the observed structure changes.
    private static readonly object CacheGate = new();
    private static HostSessionAddress? _lastSessionAddress;

    public static void ForgetCachedAddress()
    {
        lock (CacheGate) _lastSessionAddress = null;
    }

    private sealed record HostSessionAddress(
        ulong MainBase, ulong HeapBase, ulong Owner, ulong RaidPoint,
        DateTimeOffset ObservedAt);


    /// <summary>
    /// Validate the name, its metadata, repeated difficulty+level, and
    /// important scalar ranges. Do not require all reward rows to have a
    /// specific marker: PokéLDN donors may contain distinct reward sources.
    /// </summary>
    public static bool LooksLikeRetailRaidPoint(ReadOnlySpan<byte> data,
        out string name, out string reason)
    {
        name = "";
        reason = "";
        if (data.Length != RaidPointLayout.RaidPointLength)
        { reason = "INVALID_LENGTH"; return false; }

        try { name = SysbotOnlyRaidPointInspector.GetRaidPointName(data); }
        catch (InvalidDataException) { reason = "NO_RAIDPOINT_NAME"; return false; }

        // PokéLDN SV_RAIDPOINT_REVERSE_ENGINEERING.md documents these
        // offsets from three actual retail donor captures.
        uint stars = BinaryPrimitives.ReadUInt32LittleEndian(data.Slice(0x20, 4));
        uint bossLevel = BinaryPrimitives.ReadUInt32LittleEndian(data.Slice(0x2C, 4));
        uint repeatedStars = BinaryPrimitives.ReadUInt32LittleEndian(data.Slice(0x3B8, 4));
        uint repeatedLevel = BinaryPrimitives.ReadUInt32LittleEndian(data.Slice(0x3C8, 4));
        uint species = BinaryPrimitives.ReadUInt32LittleEndian(data.Slice(0x3BC, 4));
        if (stars is < 1 or > 7 || repeatedStars != stars)
        { reason = "STAR_MISMATCH"; return false; }
        if (bossLevel is < 1 or > 100 || repeatedLevel != bossLevel)
        { reason = "LEVEL_MISMATCH"; return false; }
        if (species is < 1 or > 1500)
        { reason = "SPECIES_OUT_OF_RANGE"; return false; }
        reason = "STRUCTURE_AND_REPEATED_FIELDS_MATCH";
        return true;
    }

    public static IReadOnlyList<int> FindAlignedRaidPointPrefixes(ReadOnlySpan<byte> block)
    {
        var indices = new List<int>();
        int position = 0;
        while (position <= block.Length - Needle.Length)
        {
            int relative = block[position..].IndexOf(Needle);
            if (relative < 0) break;
            int index = position + relative;
            position = index + Needle.Length;
            if ((index & 7) == 0) indices.Add(index);
        }
        return indices;
    }

    /// <summary>
    /// Only used as additional type evidence. The previously hardcoded vtable
    /// at main+0x4459278 is written inside a destructor and can be absent
    /// from a perfectly valid active owner. It MUST NOT be the only filter.
    /// </summary>
    public static HostOwnerVtableEvidence ClassifyOwnerVtable(
        ReadOnlySpan<byte> header, ulong mainBase, ulong functionAtSlot)
    {
        if (header.Length < 0x30 || mainBase < 0x10000)
            return HostOwnerVtableEvidence.None;
        ulong vptr = BinaryPrimitives.ReadUInt64LittleEndian(header[..8]);
        if (SysbotOnlyRaidPointInspector.MatchesSerializerType(header, mainBase))
            return HostOwnerVtableEvidence.DestructorVtableTriple;
        if (vptr >= mainBase + MainStaticDataStart &&
            vptr < mainBase + MainStaticDataEnd &&
            functionAtSlot == mainBase + SysbotOnlyRaidPointInspector.SerializerRva)
            return HostOwnerVtableEvidence.SerializerSlotPointsToE327FC;
        if (vptr >= mainBase + MainStaticDataStart &&
            vptr < mainBase + MainStaticDataEnd)
            return HostOwnerVtableEvidence.MainStaticPointerOnly;
        return HostOwnerVtableEvidence.None;
    }

    public static async Task<HostOwnerVtableEvidence> ReadOwnerTypeEvidenceAsync(
        ISwitchConnectionAsync device, ulong owner, ulong mainBase, CancellationToken token)
    {
        byte[] header = await device.ReadBytesAbsoluteAsync(owner, 0x30, token)
            .ConfigureAwait(false);
        if (header.Length != 0x30 || mainBase < 0x10000)
            return HostOwnerVtableEvidence.None;
        ulong vptr = BinaryPrimitives.ReadUInt64LittleEndian(header.AsSpan(0, 8));
        ulong method = 0;
        if (vptr >= mainBase + MainStaticDataStart &&
            vptr < mainBase + MainStaticDataEnd - 0x68)
        {
            byte[] entry = await device.ReadBytesAbsoluteAsync(
                vptr + (ulong)SysbotOnlyRaidPointInspector.SerializerVirtualSlotOffset,
                8, token).ConfigureAwait(false);
            if (entry.Length == 8)
                method = BinaryPrimitives.ReadUInt64LittleEndian(entry);
        }
        return ClassifyOwnerVtable(header, mainBase, method);
    }

    public async Task<HostRaidPointDiscovery> FindAsync(
        int maxMegabytes, IProgress<HostRaidPointScanProgress>? progress,
        CancellationToken token,
        Func<ulong, int, CancellationToken, Task<byte[]>>? fastRead = null)
    {
        if (maxMegabytes is < 0 or > MaxScanMegabytes)
            throw new ArgumentOutOfRangeException(nameof(maxMegabytes),
                "0代表只复核本次游戏已知指针；扫描范围最多64MiB。");

        var inspector = new SysbotOnlyRaidPointInspector(connection);
        string tid = (await connection.GetTitleID(token).ConfigureAwait(false)).Trim()
            .Replace("0x", "", StringComparison.OrdinalIgnoreCase);
        string version = (await connection.GetGameInfo("version", token).ConfigureAwait(false))
            .Trim().TrimEnd('\0');
        if (!tid.Equals(HostProbeMailbox.ExpectedTitleId, StringComparison.OrdinalIgnoreCase)
            || version != "4.0.0")
            throw new InvalidOperationException(
                $"仅允许《紫》4.0.0：当前Title={tid} Version={version}。");

        byte[] code = await connection.ReadBytesMainAsync(
            SysbotOnlyRaidPointInspector.SerializerRva,
            SysbotOnlyRaidPointInspector.SerializerFingerprintLength, token).ConfigureAwait(false);
        SysbotOnlyRaidPointInspector.ValidateSerializerFingerprint(code);
        ulong mainBase = await connection.GetMainNsoBaseAsync(token).ConfigureAwait(false);
        ulong heapBase = await connection.GetHeapBaseAsync(token).ConfigureAwait(false);
        if (mainBase < 0x10000 || heapBase < 0x10000 || (heapBase & 0xFFF) != 0)
            throw new InvalidDataException("主模块或堆基址无效；已拒绝继续扫描。");

        // Pointer-first path: reuse a previously type-linked owner from this
        // very process only after rereading + validating the whole RaidPoint.
        HostSessionAddress? cached;
        lock (CacheGate) cached = _lastSessionAddress;
        if (cached is not null && cached.MainBase == mainBase &&
            cached.HeapBase == heapBase &&
            DateTimeOffset.UtcNow - cached.ObservedAt < TimeSpan.FromMinutes(20))
        {
            try
            {
                var read = fastRead ?? connection.ReadBytesAbsoluteAsync;
                byte[] first = await read(cached.RaidPoint,
                    RaidPointLayout.RaidPointLength, token).ConfigureAwait(false);
                await Task.Delay(120, token).ConfigureAwait(false);
                byte[] second = await read(cached.RaidPoint,
                    RaidPointLayout.RaidPointLength, token).ConfigureAwait(false);
                if (first.AsSpan().SequenceEqual(second) &&
                    LooksLikeRetailRaidPoint(second, out string cachedName, out _) &&
                    cached.Owner + SysbotOnlyRaidPointInspector.EmbeddedRaidPointOffset ==
                    cached.RaidPoint)
                {
                    HostOwnerVtableEvidence evidence = await ReadOwnerTypeEvidenceAsync(
                        connection, cached.Owner, mainBase, token).ConfigureAwait(false);
                    if (evidence is HostOwnerVtableEvidence.SerializerSlotPointsToE327FC
                        or HostOwnerVtableEvidence.DestructorVtableTriple)
                    {
                        var plan = RewardAppendPlanner.Plan(second,
                            [new RewardRow(15, 500, 0, 0)], id => id == 15, 0);
                        string sha = Convert.ToHexString(SHA256.HashData(
                            RaidPointLayout.GetRaidPointRewards(second)));
                        progress?.Report(new HostRaidPointScanProgress(
                            0, maxMegabytes, 1, 0, 1, 0, 0,
                            "本次游戏地址缓存重新验证成功：直接读取0x3E8，无需全堆扫描"));
                        return new HostRaidPointDiscovery(heapBase, mainBase, 0,
                            [new HostRaidPointDiscoveryCandidate(cached.Owner, cached.RaidPoint,
                                cachedName, evidence, plan.Accepted, plan.Reason,
                                plan.OriginalCount, sha,
                                "REVALIDATED_SESSION_CACHE_NOT_PROVEN_ACTIVE_ROOM")],
                            1, 0, []);
                    }
                }
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }
            catch (Exception) { /* fall back to bounded search, never stale pointer */ }
            ForgetCachedAddress();
        }
        else if (cached is not null)
            ForgetCachedAddress();

        byte[] slot = await connection.ReadBytesMainAsync(
            SysbotOnlyRaidPointInspector.SerializerEntrySlotRva, 8, token).ConfigureAwait(false);
        if (slot.Length != 8 ||
            BinaryPrimitives.ReadUInt64LittleEndian(slot) !=
            checked(mainBase + SysbotOnlyRaidPointInspector.SerializerRva))
            throw new InvalidDataException("主模块序列化槽指令指针不一致；已停止。");

        if (maxMegabytes == 0)
        {
            progress?.Report(new HostRaidPointScanProgress(
                0, 0, 0, 0, 0, 0, 0, "未找到可复核的本次进程指针缓存；尚未执行堆扫描"));
            return new HostRaidPointDiscovery(heapBase, mainBase, 0, [], 0, 0, []);
        }

        // The stock SysBot Wi-Fi client may use a 0x1C0-byte default transfer
        // and 64ms delay PER internal chunk. An 8 MiB sweep can otherwise
        // take ~20 minutes in intentional delays alone. Temporarily reduce
        // delay while using only the same proven chunk size; restore settings
        // in finally, even if cancelled or a TCP request fails.
        using ScopedWifiScanTuning? wifiSpeedMode = fastRead is null
            ? ScopedWifiScanTuning.Create(connection) : null;
        var timer = Stopwatch.StartNew();
        progress?.Report(new HostRaidPointScanProgress(
            0, maxMegabytes, 0, 0, 0, 0, 0,
            fastRead is null ? wifiSpeedMode!.Description
                : "已验证专用高速只读TCP通道（不会写内存）"));
        int totalBlocks = maxMegabytes * 1024 * 1024 / BlockSize;
        var found = new List<HostRaidPointDiscoveryCandidate>();
        int failedBlocks = 0;
        int validBlocks = 0;
        var errors = new List<string>();
        for (int blockIndex = 0; blockIndex < totalBlocks; blockIndex++)
        {
            token.ThrowIfCancellationRequested();
            ulong blockAddress = checked(heapBase + (ulong)blockIndex * BlockSize);
            byte[] bytes;
            try
            {
                // Abort a stalled command instead of waiting minutes at "0 MiB".
                // Use the connection's CancellationToken, NOT an abandoned
                // Task.WaitAsync that would leave an unsafe pending TCP reply.
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
                timeout.CancelAfter(TimeSpan.FromSeconds(12));
                try
                {
                    bytes = await (fastRead ?? connection.ReadBytesAbsoluteAsync)(
                        blockAddress, BlockSize, timeout.Token).ConfigureAwait(false);
                    token.ThrowIfCancellationRequested();
                }
                catch (OperationCanceledException) when (!token.IsCancellationRequested)
                {
                    throw new TimeoutException(
                        $"读取 0x{blockAddress:X} 的16KiB超过12秒。停止扫描；" +
                        "同一TCP连接可能已失去同步，请断开并重新连接后再操作。");
                }
                if (bytes.Length != BlockSize)
                    throw new InvalidDataException(
                        $"读入字节数为{bytes.Length}/{BlockSize}。停止扫描，" +
                        "请检查 sys-botbase Wi-Fi 连接及版本，勿继续大量请求。");
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }
            catch (TimeoutException) { throw; } // A stalled TCP read must fail immediately.
            catch (InvalidDataException) { throw; } // Partial protocol response is unsafe to retry.
            catch (Exception e)
            {
                token.ThrowIfCancellationRequested();
                failedBlocks++;
                if (errors.Count < 5) errors.Add($"0x{blockAddress:X}: {e.Message}");
                if (failedBlocks >= 3)
                    throw new IOException(
                        $"连续/累计 {failedBlocks} 次内存读取失败，已停止扫描；" +
                        "请检查连接与地址范围，避免继续发出数千条命令。", e);
                // Unreadable pages remain incomplete evidence, not zero data.
                continue;
            }
            validBlocks++;
            foreach (int index in FindAlignedRaidPointPrefixes(bytes))
            {
                token.ThrowIfCancellationRequested();
                ulong raidPointAddress = checked(blockAddress + (ulong)index);
                if (raidPointAddress < heapBase + SysbotOnlyRaidPointInspector.EmbeddedRaidPointOffset)
                    continue;
                byte[] first;
                byte[] second;
                try
                {
                    first = await (fastRead ?? connection.ReadBytesAbsoluteAsync)(
                        raidPointAddress, RaidPointLayout.RaidPointLength, token).ConfigureAwait(false);
                    if (!LooksLikeRetailRaidPoint(first, out _, out _)) continue;
                    await Task.Delay(120, token).ConfigureAwait(false);
                    second = await (fastRead ?? connection.ReadBytesAbsoluteAsync)(
                        raidPointAddress, RaidPointLayout.RaidPointLength, token).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }
                catch (Exception) { continue; }
                if (!LooksLikeRetailRaidPoint(second, out string name, out _) ||
                    !first.AsSpan().SequenceEqual(second))
                    continue;

                ulong owner = raidPointAddress -
                              SysbotOnlyRaidPointInspector.EmbeddedRaidPointOffset;
                HostOwnerVtableEvidence evidence = HostOwnerVtableEvidence.None;
                try
                {
                    byte[] header = await connection.ReadBytesAbsoluteAsync(owner, 0x30, token)
                        .ConfigureAwait(false);
                    ulong vptr = BinaryPrimitives.ReadUInt64LittleEndian(header.AsSpan(0, 8));
                    ulong function = 0;
                    if (vptr >= mainBase + MainStaticDataStart &&
                        vptr < mainBase + MainStaticDataEnd - 0x68)
                    {
                        byte[] method = await connection.ReadBytesAbsoluteAsync(
                            vptr + (ulong)SysbotOnlyRaidPointInspector.SerializerVirtualSlotOffset,
                            8, token).ConfigureAwait(false);
                        if (method.Length == 8)
                            function = BinaryPrimitives.ReadUInt64LittleEndian(method);
                    }
                    evidence = ClassifyOwnerVtable(header, mainBase, function);
                }
                catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }
                catch (Exception) { /* keep as unlinked RaidPoint copy */ }

                var plan = RewardAppendPlanner.Plan(second,
                    [new RewardRow(15, 500, 0, 0)], id => id == 15,
                    RewardAppendPlanner.AllPlayersSubject);
                var rewardsHash = Convert.ToHexString(SHA256.HashData(
                    RaidPointLayout.GetRaidPointRewards(second)));
                found.Add(new HostRaidPointDiscoveryCandidate(
                    owner, raidPointAddress, name, evidence,
                    plan.Accepted, plan.Reason, plan.OriginalCount,
                    rewardsHash,
                    "MEMORY_COPY_ONLY_NOT_PROVEN_CURRENT_ROOM_OR_OUTGOING_BOOTSTRAP"));
                if (found.Count >= MaxCandidates) break;
            }
            // Exact completed bytes + elapsed time: always show sub-MiB
            // progress. Avoid thousands of UI callbacks on fast connections.
            if ((blockIndex & 3) == 0 || found.Count >= MaxCandidates)
                progress?.Report(new HostRaidPointScanProgress(
                    (blockIndex + 1) * BlockSize / (1024 * 1024),
                    maxMegabytes, validBlocks, failedBlocks, found.Count,
                    (long)(blockIndex + 1) * BlockSize, timer.ElapsedMilliseconds,
                    $"读到 0x{blockAddress:X}"));
            if (found.Count >= MaxCandidates) break;
        }
        progress?.Report(new HostRaidPointScanProgress(
            (validBlocks + failedBlocks) * BlockSize / (1024 * 1024),
            maxMegabytes, validBlocks, failedBlocks, found.Count,
            (long)(validBlocks + failedBlocks) * BlockSize, timer.ElapsedMilliseconds,
            "本轮扫描完成"));
        if (found.Count == 1 && found[0].OwnerTypeEvidence is
            HostOwnerVtableEvidence.SerializerSlotPointsToE327FC or
            HostOwnerVtableEvidence.DestructorVtableTriple)
        {
            lock (CacheGate)
                _lastSessionAddress = new HostSessionAddress(
                    mainBase, heapBase, found[0].OwnerAddress,
                    found[0].RaidPointAddress, DateTimeOffset.UtcNow);
        }
        return new HostRaidPointDiscovery(heapBase, mainBase, maxMegabytes,
            found, validBlocks, failedBlocks, errors);
    }
}

/// <summary>
/// Restore the shared SysBot connection's original Wi-Fi pacing on leaving
/// the read-only scan. Do NOT enlarge its individual TCP receive size: some
/// SysBot.Base versions assume one ReceiveAsync completes a whole reply.
/// </summary>
internal sealed class ScopedWifiScanTuning : IDisposable
{
    private readonly SwitchSocketAsync? _socket;
    private readonly int _oldDelay;
    private readonly int _oldFactor;
    public string Description { get; }

    private ScopedWifiScanTuning(ISwitchConnectionAsync connection)
    {
        if (connection is not SwitchSocketAsync socket)
        {
            Description = "非标准Wi-Fi Socket（USB/其他）：不更改传输配置";
            return;
        }
        _socket = socket;
        _oldDelay = socket.BaseDelay;
        _oldFactor = socket.DelayFactor;
        int chunk = socket.MaximumTransferSize;
        // Keep stock per-command transfer length. Only eliminate the slow
        // artificial delay (64ms default + transfer/delayFactor).
        socket.BaseDelay = Math.Min(_oldDelay, 2);
        socket.DelayFactor = Math.Max(_oldFactor, 1024);
        Description = $"Wi-Fi传输优化：chunk={chunk}B；每块附加延迟" +
            $" {_oldDelay + chunk / Math.Max(1, _oldFactor)}→" +
            $"{socket.BaseDelay + chunk / Math.Max(1, socket.DelayFactor)}ms；" +
            "扫描结束恢复原参数";
    }

    public static ScopedWifiScanTuning Create(ISwitchConnectionAsync connection) => new(connection);

    public void Dispose()
    {
        if (_socket is null) return;
        _socket.BaseDelay = _oldDelay;
        _socket.DelayFactor = _oldFactor;
    }
}

public enum HostOwnerVtableEvidence
{
    None = 0,
    MainStaticPointerOnly = 1,
    SerializerSlotPointsToE327FC = 2,
    DestructorVtableTriple = 3
}

public sealed record HostRaidPointDiscoveryCandidate(
    ulong OwnerAddress, ulong RaidPointAddress, string Name,
    HostOwnerVtableEvidence OwnerTypeEvidence, bool OfflineAppendEligible,
    string AppendPlanReason, int OriginalRows, string RewardsSha256,
    string EvidenceLimit);

public sealed record HostRaidPointScanProgress(
    int CompletedMegabytes, int TotalMegabytes,
    int ReadableBlocks, int FailedBlocks, int Candidates,
    long ScannedBytes = 0, long ElapsedMilliseconds = 0, string Stage = "");

public sealed record HostRaidPointDiscovery(
    ulong HeapBase, ulong MainBase, int ScannedMegabytes,
    IReadOnlyList<HostRaidPointDiscoveryCandidate> Candidates,
    int ReadableBlocks, int FailedBlocks, IReadOnlyList<string> Errors);
