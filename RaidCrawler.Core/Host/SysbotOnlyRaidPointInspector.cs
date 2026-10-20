using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using SysBot.Base;

namespace RaidCrawler.Core.Host;

/// <summary>
/// Plan A: stock sys-botbase ONLY. Reads a caller-supplied candidate address.
/// No code patching, no Native/subsdk9, no write/process freezing.
/// Crucial limitation: an absolute candidate address is NOT a proven pointer chain
/// to the current active host RaidPoint. This is a diagnostic until validated.
/// </summary>
public sealed class SysbotOnlyRaidPointInspector(ISwitchConnectionAsync connection)
{
    public const ulong SerializerRva = 0xE327FC;
    public const int SerializerFingerprintLength = 0x1B8;
    public const ulong EmbeddedRaidPointOffset = 0x58;
    public const ulong FirstRewardFromOwner = 0x13C;

    private static readonly (int Offset, uint Word)[] Opcodes =
    [
        (0x000, 0xA9BB7BFD), (0x018, 0xAA0003F4),
        (0x02C, 0x52815416), (0x040, 0xAA0003F7),
        (0x048, 0x52802B18), (0x164, 0x911B62E0),
        (0x168, 0xA945AA8B), (0x16C, 0x9101E281),
        (0x170, 0x52807902), (0x174, 0xF9035EEB),
        (0x184, 0x94982624), (0x1B4, 0x141D8073)
    ];

    public static void ValidateSerializerFingerprint(ReadOnlySpan<byte> code)
    {
        if (code.Length != SerializerFingerprintLength)
            throw new InvalidDataException("序列化指令样本长度不是 0x1B8 字节。");
        foreach (var (offset, expected) in Opcodes)
        {
            uint actual = BinaryPrimitives.ReadUInt32LittleEndian(code.Slice(offset, 4));
            if (actual != expected)
                throw new InvalidDataException(
                    $"《紫》4.0.0 ELF 指令不匹配：main+0x{SerializerRva + (ulong)offset:X}。" +
                    "禁止将当前数据解释为房主 RaidPoint。");
        }
    }

    public static ulong CalculateRaidPointAddress(ulong candidateAddress, bool isOwnerObject)
    {
        if (candidateAddress < 0x10000 || (candidateAddress & 7) != 0)
            throw new FormatException("请提供有效的、8字节对齐的当前进程绝对地址。");
        ulong addr = checked(candidateAddress + (isOwnerObject ? EmbeddedRaidPointOffset : 0UL));
        if (addr > ulong.MaxValue - RaidPointLayout.RaidPointLength)
            throw new FormatException("内存地址超出有效范围。");
        return addr;
    }

    public static string GetRaidPointName(ReadOnlySpan<byte> data)
    {
        if (data.Length != RaidPointLayout.RaidPointLength)
            throw new InvalidDataException("RaidPoint 必须为完整的 0x3E8 字节。");
        ReadOnlySpan<byte> name = data[..32];
        int nul = name.IndexOf((byte)0);
        if (nul >= 0) name = name[..nul];
        if (!name.StartsWith("RaidPoint_"u8) || name.Length < 12)
            throw new InvalidDataException(
                "所给内存地址没有可信的 RaidPoint_ 名称签名；可能是旧数据、错误指针或对象尚未生成。");
        foreach (byte c in name)
            if (c is < 0x20 or > 0x7E)
                throw new InvalidDataException("RaidPoint 名称不是有效的 ASCII 字符序列。");
        return Encoding.ASCII.GetString(name);
    }

    public const ulong SerializerVTableRva = 0x4459278;
    public const ulong SerializerEntrySlotRva = 0x44592E0;
    public const ulong SecondaryVTableRva = 0x4459308;
    public const ulong TertiaryVTableRva = 0x4459358;
    public const int SerializerVirtualSlotOffset = 0x68;

    /// <summary>
    /// Exact vtable triple written to the serializer class instance by
    /// main+0x1E63204..+0x1E6321C, identified via ELF RELATIVE relocation.
    /// This establishes CLASS IDENTITY only, not current-raid ownership.
    /// </summary>
    public static bool MatchesSerializerType(ReadOnlySpan<byte> header, ulong mainBase)
    {
        if (header.Length < 0x30 || mainBase < 0x10000)
            return false;
        static ulong Ptr(ReadOnlySpan<byte> span, int start) =>
            BinaryPrimitives.ReadUInt64LittleEndian(span.Slice(start, 8));
        return Ptr(header, 0) == checked(mainBase + SerializerVTableRva) &&
               Ptr(header, 0x20) == checked(mainBase + SecondaryVTableRva) &&
               Ptr(header, 0x28) == checked(mainBase + TertiaryVTableRva);
    }

    /// <summary>
    /// Pure sys-botbase bounded HEAP START scan for the actual C++ serializer
    /// object TYPE rather than arbitrary "RaidPoint_" text copies.
    /// Does NOT prove the object is current or ever sent.
    /// </summary>
    public async Task<IReadOnlyList<HostTypedOwnerCandidate>> ScanSerializerObjectsAsync(
        int maxMegabytes, CancellationToken token)
    {
        if (maxMegabytes is < 1 or > 8)
            throw new ArgumentOutOfRangeException(nameof(maxMegabytes), "只允许扫描堆起始1到8MiB。");
        string tid = (await connection.GetTitleID(token).ConfigureAwait(false)).Trim()
            .Replace("0x", "", StringComparison.OrdinalIgnoreCase);
        string version = (await connection.GetGameInfo("version", token).ConfigureAwait(false))
            .Trim().TrimEnd('\0');
        if (!tid.Equals(HostProbeMailbox.ExpectedTitleId, StringComparison.OrdinalIgnoreCase)
            || version != "4.0.0")
            throw new InvalidOperationException("仅可扫描已审计的《紫》4.0.0。");

        ValidateSerializerFingerprint(await connection.ReadBytesMainAsync(
            SerializerRva, SerializerFingerprintLength, token).ConfigureAwait(false));
        ulong mainBase = await connection.GetMainNsoBaseAsync(token).ConfigureAwait(false);
        if (mainBase < 0x10000)
            throw new InvalidDataException("当前游戏主模块基址异常。");

        // The relocation table of Violet4.0.0.elf places serializer RVA
        // 0xE327FC at main+0x44592E0 (= vtable + 0x68).
        byte[] entry = await connection.ReadBytesMainAsync(
            SerializerEntrySlotRva, 8, token).ConfigureAwait(false);
        if (entry.Length != 8 ||
            BinaryPrimitives.ReadUInt64LittleEndian(entry) != checked(mainBase + SerializerRva))
            throw new InvalidDataException("主模块中对应虚函数槽不是预期的序列化函数；已停止扫描。");

        ulong heap = await connection.GetHeapBaseAsync(token).ConfigureAwait(false);
        if (heap < 0x10000 || (heap & 0xFFF) != 0)
            throw new InvalidDataException("堆起始地址不可信。");

        byte[] vtableBytes = new byte[8];
        BinaryPrimitives.WriteUInt64LittleEndian(vtableBytes, checked(mainBase + SerializerVTableRva));
        var results = new List<HostTypedOwnerCandidate>();
        const int readSize = 0x4000;
        for (int offset = 0; offset < maxMegabytes * 1024 * 1024; offset += readSize)
        {
            token.ThrowIfCancellationRequested();
            ulong baseAddress = checked(heap + (ulong)offset);
            byte[] block = await connection.ReadBytesAbsoluteAsync(
                baseAddress, readSize, token).ConfigureAwait(false);
            if (block.Length != readSize)
                throw new IOException("sys-botbase 返回了不完整的堆内存数据，已终止。");

            for (int i = 0; i <= block.Length - 8 && results.Count < 12; i += 8)
            {
                if (!block.AsSpan(i, 8).SequenceEqual(vtableBytes))
                    continue;

                ulong owner = checked(baseAddress + (ulong)i);
                byte[] header = await connection.ReadBytesAbsoluteAsync(owner, 0x30, token)
                    .ConfigureAwait(false);
                if (!MatchesSerializerType(header, mainBase))
                    continue;
                ulong raidPoint = CalculateRaidPointAddress(owner, true);
                byte[] data = await connection.ReadBytesAbsoluteAsync(
                    raidPoint, RaidPointLayout.RaidPointLength, token).ConfigureAwait(false);
                string name;
                try { name = GetRaidPointName(data); }
                catch (InvalidDataException) { continue; }
                var plan = RewardAppendPlanner.Plan(data,
                    [new RewardRow(15, 1, 0, 5)], id => id == 15);
                if (!plan.Accepted) continue;

                // Same owner must still have all 3 live vtable pointers,
                // otherwise the scan raced with a changing/destroyed object.
                byte[] again = await connection.ReadBytesAbsoluteAsync(owner, 0x30, token)
                    .ConfigureAwait(false);
                if (!MatchesSerializerType(again, mainBase))
                    continue;

                results.Add(new HostTypedOwnerCandidate(owner, raidPoint, name,
                    plan.OriginalCount, "ELF_VTABLE_TYPE_ONLY_NOT_ACTIVE_RAID"));
            }
            if (results.Count >= 12) break;
            await Task.Delay(5, token).ConfigureAwait(false);
        }
        return results;
    }

    /// <summary>
    /// Bounded opt-in HEAP-START signature search, maximum 8 MiB.
    /// May find stale/irrelevant copies; does NOT discover a pointer chain.
    /// Never runs automatically and never writes game memory.
    /// </summary>
    public async Task<IReadOnlyList<HostRaidPointAddressCandidate>> ScanHeapStartAsync(
        int maxMegabytes, CancellationToken token)
    {
        if (maxMegabytes is < 1 or > 8)
            throw new ArgumentOutOfRangeException(nameof(maxMegabytes), "只允许只读扫描堆起始处的 1–8 MiB。");
        string tid = (await connection.GetTitleID(token).ConfigureAwait(false)).Trim()
            .Replace("0x", "", StringComparison.OrdinalIgnoreCase);
        if (!tid.Equals(HostProbeMailbox.ExpectedTitleId, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("仅支持已审计的《紫》4.0.0。");
        string version = (await connection.GetGameInfo("version", token).ConfigureAwait(false))
            .Trim().TrimEnd('\0');
        if (version != "4.0.0")
            throw new InvalidOperationException("当前并非《紫》4.0.0。");
        byte[] code = await connection.ReadBytesMainAsync(SerializerRva,
            SerializerFingerprintLength, token).ConfigureAwait(false);
        ValidateSerializerFingerprint(code);

        ulong heapBase = await connection.GetHeapBaseAsync(token).ConfigureAwait(false);
        if (heapBase < 0x10000 || (heapBase & 0xFFF) != 0)
            throw new InvalidDataException("堆起始地址不可信，已终止搜索。");

        const int chunk = 0x4000;
        int length = maxMegabytes * 1024 * 1024;
        var found = new List<HostRaidPointAddressCandidate>();
        for (int offset = 0; offset < length; offset += chunk)
        {
            token.ThrowIfCancellationRequested();
            ulong baseAddress = checked(heapBase + (ulong)offset);
            byte[] memory = await connection.ReadBytesAbsoluteAsync(baseAddress, chunk, token)
                .ConfigureAwait(false);
            if (memory.Length != chunk)
                throw new IOException("堆内存读取长度不正确，扫描已终止。");
            // No broad heap scan: only the user-requested small first N MiB.
            int cursor = 0;
            while (cursor <= memory.Length - 10 && found.Count < 12)
            {
                int delta = memory.AsSpan(cursor).IndexOf("RaidPoint_"u8);
                if (delta < 0) break;
                int index = cursor + delta;
                cursor = index + 10;
                ulong address = baseAddress + (ulong)index;
                if ((address & 7) != 0) continue;

                byte[] candidate = await connection.ReadBytesAbsoluteAsync(address,
                    RaidPointLayout.RaidPointLength, token).ConfigureAwait(false);
                try
                {
                    string name = GetRaidPointName(candidate);
                    var report = RewardAppendPlanner.Plan(candidate,
                        [new RewardRow(15, 1, 0, 5)], id => id == 15);
                    if (!report.Accepted) continue;
                    found.Add(new HostRaidPointAddressCandidate(address, name,
                        report.OriginalCount, "POSSIBLE_HEAP_COPY_ONLY"));
                }
                catch (InvalidDataException)
                {
                    // Inline text might resemble a RaidPoint without complete
                    // valid object data. A signature alone is not enough.
                }
            }
            if (found.Count >= 12) break;
            await Task.Delay(5, token).ConfigureAwait(false);
        }
        return found;
    }

    public async Task<SysbotOnlyCandidate> InspectAsync(
        ulong inputAddress, bool isOwnerObject, CancellationToken token)
    {
        string tid = (await connection.GetTitleID(token).ConfigureAwait(false)).Trim()
            .Replace("0x", "", StringComparison.OrdinalIgnoreCase);
        if (!tid.Equals(HostProbeMailbox.ExpectedTitleId, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("方案 A 目前只支持已审计的《紫》Title ID。");
        string version = (await connection.GetGameInfo("version", token).ConfigureAwait(false))
            .Trim().TrimEnd('\0');
        if (version != "4.0.0")
            throw new InvalidOperationException($"游戏版本 {version} 不是已审计的《紫》4.0.0。");

        byte[] code = await connection.ReadBytesMainAsync(SerializerRva,
            SerializerFingerprintLength, token).ConfigureAwait(false);
        ValidateSerializerFingerprint(code);

        ulong raidPointAddress = CalculateRaidPointAddress(inputAddress, isOwnerObject);
        byte[] first = await connection.ReadBytesAbsoluteAsync(raidPointAddress,
            RaidPointLayout.RaidPointLength, token).ConfigureAwait(false);
        string name = GetRaidPointName(first);

        // Read the exact same candidate a second time. Equality means only a
        // stable object view, NEVER proves the object is used by the host.
        await Task.Delay(150, token).ConfigureAwait(false);
        byte[] second = await connection.ReadBytesAbsoluteAsync(raidPointAddress,
            RaidPointLayout.RaidPointLength, token).ConfigureAwait(false);
        string secondName = GetRaidPointName(second);
        bool stable = first.AsSpan().SequenceEqual(second) && name == secondName;
        var candidatePlan = RewardAppendPlanner.Plan(second,
            [new RewardRow(15, 1, 0, 5)], itemId => itemId == 15);
        bool preview = stable && candidatePlan.Accepted;
        string reason = !stable ? "SNAPSHOT_CHANGED" : candidatePlan.Reason;

        byte[] rewardBytes = RaidPointLayout.GetRaidPointRewards(second).ToArray();
        return new SysbotOnlyCandidate(raidPointAddress, name, stable,
            candidatePlan.OriginalCount, candidatePlan.OriginalCount >= 0
                ? RaidPointLayout.RewardCapacity - candidatePlan.OriginalCount : 0,
            preview, reason,
            Convert.ToHexString(SHA256.HashData(rewardBytes)),
            "仅证明指定地址存在符合布局的内存副本；尚未证明是本局真实房主即将发送的对象。");
    }
}

public sealed record SysbotOnlyCandidate(
    ulong AbsoluteRaidPoint, string RaidPointName, bool SnapshotsStable,
    int ObservedOriginalRows, int PossibleEmptyRows, bool OfflineAppendEligible,
    string Reason, string RewardSha256, string EvidenceLimit);

/// <summary>Possible heap copy only; NOT an authenticated current host object.</summary>
public sealed record HostRaidPointAddressCandidate(
    ulong Address, string Name, int OriginalRows, string EvidenceLevel);

/// <summary>ELF-vtable-matched owner, not a verified active-room pointer.</summary>
public sealed record HostTypedOwnerCandidate(
    ulong OwnerAddress, ulong RaidPointAddress, string Name,
    int OriginalRows, string EvidenceLevel);
