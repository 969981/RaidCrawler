using System.Buffers.Binary;
using SysBot.Base;

namespace RaidCrawler.Core.Host;

/// <summary>
/// V2 fixed ABI shared with switch/host-probe/source/program/main.cpp.
/// SysBot.Base is the only transport: there is no second network listener.
/// Mailbox address must be obtained from the running module's [RC-N1] log.
/// Neither connection nor client can discover module globals autonomously.
/// </summary>
public static class HostProbeMailbox
{
    public const string ExpectedTitleId = "01008F6008C5E000";
    public const int ProtocolVersion = 2;
    public const int HeaderLength = 0x40;
    public const int MailboxLength = 0x600;
    public const int SourceOffset = 0x40;
    public const int BootstrapOffset = 0x310;
    public const int RegionLength = 720;
    public const int EnabledOffset = 0x10;
    public const int RequestModeOffset = 0x5E0;
    public const int RequestFieldsOffset = 0x5E4;
    public const int RequestResultOffset = 0x5F4;
    private static ReadOnlySpan<byte> Magic => "RCHMB02\0"u8;

    public static HostProbeHeader ParseHeader(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length != HeaderLength)
            throw new InvalidDataException($"内存邮箱头长度必须为 {HeaderLength} 字节。");
        if (!bytes[..8].SequenceEqual(Magic))
            throw new InvalidDataException("未找到 RC 内存邮箱标识；请检查原生模块是否加载及本次地址是否正确。");
        uint version = U32(bytes, 8);
        uint size = U32(bytes, 12);
        if (version != ProtocolVersion || size != MailboxLength)
            throw new InvalidDataException($"未知的内存邮箱协议版本 {version}，大小 0x{size:X}。");
        uint enabled = U32(bytes, 0x10);
        uint status = U32(bytes, 0x14);
        if (enabled > 1 || status > 2)
            throw new InvalidDataException("内存邮箱状态数据无效。");
        return new HostProbeHeader(enabled == 1, status, U32(bytes, 0x18),
            U32(bytes, 0x1C), U32(bytes, 0x20), U64(bytes, 0x28), U32(bytes, 0x24));
    }

    public static HostProbeCapture ParseCapture(ReadOnlySpan<byte> raw)
    {
        if (raw.Length != RegionLength * 2)
            throw new InvalidDataException("应提供两段各 720 字节的原版奖励数据。");
        return new HostProbeCapture(raw[..RegionLength].ToArray(),
            raw[RegionLength..].ToArray());
    }

    private static uint U32(ReadOnlySpan<byte> s, int offset) =>
        BinaryPrimitives.ReadUInt32LittleEndian(s.Slice(offset, 4));
    private static ulong U64(ReadOnlySpan<byte> s, int offset) =>
        BinaryPrimitives.ReadUInt64LittleEndian(s.Slice(offset, 8));

    public static ulong ParseAddress(string text)
    {
        string address = text.Trim();
        if (address.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
            address = address[2..];
        if (!ulong.TryParse(address, System.Globalization.NumberStyles.HexNumber,
                System.Globalization.CultureInfo.InvariantCulture, out ulong parsed) ||
            parsed < 0x10000 || (parsed & 7) != 0 ||
            parsed > ulong.MaxValue - MailboxLength)
            throw new FormatException("请输入有效且按 8 字节对齐的当前进程内存邮箱十六进制地址。");
        return parsed;
    }
}

public sealed record HostProbeHeader(bool Enabled, uint Status, uint CaptureCount,
    uint DroppedCount, uint Generation, ulong Tick, uint Capabilities);

public sealed record HostProbeCapture(byte[] SourceRows, byte[] BootstrapRows)
{
    public HostCaptureComparison Compare()
    {
        // The existing verifier accepts full 0x3E8 and 0xAA0 buffers;
        // reconstruct only their reward regions for its length checks.
        byte[] source = new byte[RaidPointLayout.RaidPointLength];
        byte[] plain = new byte[RaidPointLayout.BootstrapLength];
        SourceRows.CopyTo(source, RaidPointLayout.RewardsInRaidPoint);
        BootstrapRows.CopyTo(plain, RaidPointLayout.RewardsInBootstrap);
        return HostCaptureVerifier.Compare(source, plain);
    }
}

/// <summary>
/// Safe read-only snapshot protocol with an opt-in enable bit.
/// Current status and snapshot are snapshots of a cooperative module, not
/// authentication of Nintendo network traffic or Build ID.
/// </summary>
public sealed class HostProbeMailboxClient(ISwitchConnectionAsync connection)
{
    private readonly ISwitchConnectionAsync _connection = connection;

    public async Task<(ulong Address, HostProbeHeader Status)> DiscoverAsync(
        HostProbeManifest manifest, CancellationToken token)
    {
        await CheckTitleAsync(token).ConfigureAwait(false);
        ulong mainBase = await _connection.GetMainNsoBaseAsync(token).ConfigureAwait(false);
        if (mainBase < 0x10000)
            throw new InvalidOperationException("无法读取当前游戏主模块基址。");
        ulong hook = checked(mainBase + HostProbeLocator.HookRva);
        byte[] code = await _connection.ReadBytesAbsoluteAsync(hook, 24, token)
            .ConfigureAwait(false);
        ulong entry = HostProbeLocator.ParseEntryTarget(hook, code);
        byte[] entryBytes = await _connection.ReadBytesAbsoluteAsync(entry, 24, token)
            .ConfigureAwait(false);
        ulong callback = HostProbeLocator.ParseCallback(entry, entryBytes);
        ulong address = HostProbeLocator.ResolveMailbox(callback, manifest.CallbackToMailboxDelta);
        HostProbeHeader status = await ReadStatusAsync(address, token).ConfigureAwait(false);
        if (status.Status != 1)
            throw new InvalidOperationException("已定位邮箱，但 Native Hook 未处于可用状态。");
        return (address, status);
    }

    public async Task<HostProbeHeader> ReadStatusAsync(ulong address, CancellationToken token)
    {
        await CheckTitleAsync(token).ConfigureAwait(false);
        return HostProbeMailbox.ParseHeader(await _connection.ReadBytesAbsoluteAsync(
            address, HostProbeMailbox.HeaderLength, token).ConfigureAwait(false));
    }

    public async Task<HostProbeHeader> SetEnabledAsync(
        ulong address, bool enable, CancellationToken token)
    {
        // Do not write anything except +0x10 after verifying a live module.
        HostProbeHeader before = await ReadStatusAsync(address, token).ConfigureAwait(false);
        if (before.Status != 1)
            throw new InvalidOperationException("未安装或无法识别原生探针，未发送控制命令。");
        if (!enable)
            await _connection.WriteBytesAbsoluteAsync(BitConverter.GetBytes(0u),
                address + HostProbeMailbox.RequestModeOffset, token).ConfigureAwait(false);
        await _connection.WriteBytesAbsoluteAsync(
            BitConverter.GetBytes(enable ? 1u : 0u),
            address + HostProbeMailbox.EnabledOffset, token).ConfigureAwait(false);
        HostProbeHeader after = await ReadStatusAsync(address, token).ConfigureAwait(false);
        if (after.Enabled != enable)
            throw new IOException("原生探针未确认捕获启停状态。");
        return after;
    }

    public async Task<(HostProbeHeader Header, HostProbeCapture Capture)> ReadCaptureAsync(
        ulong address, CancellationToken token)
    {
        // Readers check the odd/even generation and reread the header after the
        // 1440-byte transport read: no torn multi-frame data accepted.
        for (int attempts = 0; attempts < 4; attempts++)
        {
            HostProbeHeader before = await ReadStatusAsync(address, token).ConfigureAwait(false);
            if (before.Status != 1)
                throw new InvalidOperationException("原生探针未激活。");
            if (before.CaptureCount == 0)
                throw new InvalidOperationException("尚未捕获到任何实机数据。");
            if ((before.Generation & 1) != 0)
                continue;
            byte[] raw = await _connection.ReadBytesAbsoluteAsync(
                address + HostProbeMailbox.SourceOffset, 2 * HostProbeMailbox.RegionLength, token)
                .ConfigureAwait(false);
            HostProbeHeader after = await ReadStatusAsync(address, token).ConfigureAwait(false);
            if (after.CaptureCount == before.CaptureCount &&
                after.Generation == before.Generation &&
                (after.Generation & 1) == 0)
                return (after, HostProbeMailbox.ParseCapture(raw));
        }
        throw new IOException("捕获数据在读取期间发生变化，请在团体战加载完成后重试。");
    }

    public async Task<HostProbeResult> ReadAppendResultAsync(ulong address, CancellationToken token)
    {
        await ReadStatusAsync(address, token).ConfigureAwait(false);
        byte[] data = await _connection.ReadBytesAbsoluteAsync(
            address + HostProbeMailbox.RequestModeOffset, 0x20, token).ConfigureAwait(false);
        if (data.Length != 0x20)
            throw new InvalidDataException("追加状态区长度不正确。");
        static uint Get(ReadOnlySpan<byte> b, int offset) =>
            BinaryPrimitives.ReadUInt32LittleEndian(b.Slice(offset, 4));
        return new HostProbeResult(Get(data, 0), Get(data, 4), Get(data, 8),
            Get(data, 12), Get(data, 16), Get(data, 20),
            Get(data, 24), Get(data, 28));
    }

    /// <summary>
    /// Stage one only. N2 default CI builds have NO writer; mode 2 is only
    /// permitted when paired Native advertises build-time write capability.
    /// </summary>
    public async Task ConfigureAppendAsync(
        ulong address, uint itemId, uint quantity, bool apply, CancellationToken token)
    {
        HostProbeHeader header = await ReadStatusAsync(address, token).ConfigureAwait(false);
        if (header.Status != 1 || !header.Enabled || header.CaptureCount == 0)
            throw new InvalidOperationException("请先启用 N1 并取得至少一次真实房主只读捕获。");
        if (apply && (header.Capabilities & 1) != 1)
            throw new InvalidOperationException("此原生构建未启用 N2 游戏数据写入，仅允许离线预览。");
        if (itemId != 15 || quantity is 0 or > 10)
            throw new InvalidOperationException("当前受控试验仅允许先机球（道具 15），数量 1–10。");
        var pair = await ReadCaptureAsync(address, token).ConfigureAwait(false);
        if (!pair.Capture.Compare().Matches)
            throw new InvalidOperationException("原始源奖励与发送前明文不一致，禁止启用追加。");
        // Set request mode zero before writing row parameters, arm last.
        await _connection.WriteBytesAbsoluteAsync(BitConverter.GetBytes(0u),
            address + HostProbeMailbox.RequestModeOffset, token).ConfigureAwait(false);
        byte[] row = new byte[16];
        BinaryPrimitives.WriteUInt32LittleEndian(row.AsSpan(0, 4), itemId);
        BinaryPrimitives.WriteUInt32LittleEndian(row.AsSpan(4, 4), quantity);
        BinaryPrimitives.WriteUInt32LittleEndian(row.AsSpan(8, 4), 0);
        BinaryPrimitives.WriteUInt32LittleEndian(row.AsSpan(12, 4), 5);
        await _connection.WriteBytesAbsoluteAsync(row,
            address + HostProbeMailbox.RequestFieldsOffset, token).ConfigureAwait(false);
        await _connection.WriteBytesAbsoluteAsync(BitConverter.GetBytes(apply ? 2u : 1u),
            address + HostProbeMailbox.RequestModeOffset, token).ConfigureAwait(false);
    }

    private async Task CheckTitleAsync(CancellationToken token)
    {
        string game = (await _connection.GetTitleID(token).ConfigureAwait(false))
            .Trim().Replace("0x", "", StringComparison.OrdinalIgnoreCase);
        if (!game.Equals(HostProbeMailbox.ExpectedTitleId, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException(
                $"此探针仅支持《紫》4.0.0（Title ID：{HostProbeMailbox.ExpectedTitleId}）。当前连接游戏：{game}。");
    }
}

/// <summary>Outcome is updated at the next real host bootstrap, not immediately on request.</summary>
public sealed record HostProbeResult(
    uint RequestMode, uint ItemId, uint Quantity, uint RareFlag, uint SubjectType,
    uint LastResult, uint AppendedCount, uint OriginalRows);
