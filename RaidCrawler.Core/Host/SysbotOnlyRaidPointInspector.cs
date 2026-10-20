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
