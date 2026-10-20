using System.Buffers.Binary;
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

    public async Task<HostRaidPointDiscovery> FindAsync(
        int maxMegabytes, IProgress<HostRaidPointScanProgress>? progress,
        CancellationToken token)
    {
        if (maxMegabytes is < 1 or > MaxScanMegabytes)
            throw new ArgumentOutOfRangeException(nameof(maxMegabytes),
                "搜索限定在堆起始处最多 64 MiB；不得任意扫描其他进程或未知内存。");

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

        byte[] slot = await connection.ReadBytesMainAsync(
            SysbotOnlyRaidPointInspector.SerializerEntrySlotRva, 8, token).ConfigureAwait(false);
        if (slot.Length != 8 ||
            BinaryPrimitives.ReadUInt64LittleEndian(slot) !=
            checked(mainBase + SysbotOnlyRaidPointInspector.SerializerRva))
            throw new InvalidDataException("主模块序列化槽指令指针不一致；已停止。");

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
                bytes = await connection.ReadBytesAbsoluteAsync(blockAddress, BlockSize, token)
                    .ConfigureAwait(false);
                if (bytes.Length != BlockSize) throw new IOException("读取长度不足");
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }
            catch (Exception e)
            {
                failedBlocks++;
                if (errors.Count < 5) errors.Add($"0x{blockAddress:X}: {e.Message}");
                continue; // unallocated/disallowed pages are not fake negatives
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
                    first = await connection.ReadBytesAbsoluteAsync(
                        raidPointAddress, RaidPointLayout.RaidPointLength, token).ConfigureAwait(false);
                    if (!LooksLikeRetailRaidPoint(first, out _, out _)) continue;
                    await Task.Delay(120, token).ConfigureAwait(false);
                    second = await connection.ReadBytesAbsoluteAsync(
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
            if (blockIndex % 32 == 0 || found.Count >= MaxCandidates)
                progress?.Report(new HostRaidPointScanProgress(
                    (blockIndex + 1) * BlockSize / (1024 * 1024),
                    maxMegabytes, validBlocks, failedBlocks, found.Count));
            if (found.Count >= MaxCandidates) break;
            if (blockIndex % 16 == 0)
                await Task.Delay(1, token).ConfigureAwait(false);
        }
        progress?.Report(new HostRaidPointScanProgress(
            maxMegabytes, maxMegabytes, validBlocks, failedBlocks, found.Count));
        return new HostRaidPointDiscovery(heapBase, mainBase, maxMegabytes,
            found, validBlocks, failedBlocks, errors);
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
    int ReadableBlocks, int FailedBlocks, int Candidates);

public sealed record HostRaidPointDiscovery(
    ulong HeapBase, ulong MainBase, int ScannedMegabytes,
    IReadOnlyList<HostRaidPointDiscoveryCandidate> Candidates,
    int ReadableBlocks, int FailedBlocks, IReadOnlyList<string> Errors);
