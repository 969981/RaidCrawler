using System.Buffers.Binary;
using SysBot.Base;

namespace RaidCrawler.Core.Host;

/// <summary>
/// V1 fixed ABI shared with switch/host-probe/source/program/main.cpp.
/// SysBot.Base is the only transport: there is no second network listener.
/// Mailbox address must be obtained from the running module's [RC-N1] log.
/// Neither connection nor client can discover module globals autonomously.
/// </summary>
public static class HostProbeMailbox
{
    public const string ExpectedTitleId = "01008F6008C5E000";
    public const int ProtocolVersion = 1;
    public const int HeaderLength = 0x40;
    public const int MailboxLength = 0x600;
    public const int SourceOffset = 0x40;
    public const int BootstrapOffset = 0x310;
    public const int RegionLength = 720;
    public const int EnabledOffset = 0x10;
    private static ReadOnlySpan<byte> Magic => "RCHMB01\0"u8;

    public static HostProbeHeader ParseHeader(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length != HeaderLength)
            throw new InvalidDataException($"Mailbox header length must be {HeaderLength}.");
        if (!bytes[..8].SequenceEqual(Magic))
            throw new InvalidDataException("RC mailbox magic not present. Check the active module and address.");
        uint version = U32(bytes, 8);
        uint size = U32(bytes, 12);
        if (version != ProtocolVersion || size != MailboxLength)
            throw new InvalidDataException($"Unknown RC mailbox protocol {version}, size 0x{size:X}.");
        uint enabled = U32(bytes, 0x10);
        uint status = U32(bytes, 0x14);
        if (enabled > 1 || status > 2)
            throw new InvalidDataException("Invalid RC mailbox state.");
        return new HostProbeHeader(enabled == 1, status, U32(bytes, 0x18),
            U32(bytes, 0x1C), U32(bytes, 0x20), U64(bytes, 0x28));
    }

    public static HostProbeCapture ParseCapture(ReadOnlySpan<byte> raw)
    {
        if (raw.Length != RegionLength * 2)
            throw new InvalidDataException("Expected two 720-byte native reward arrays.");
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
            throw new FormatException("Enter a valid 8-byte-aligned runtime mailbox hexadecimal address.");
        return parsed;
    }
}

public sealed record HostProbeHeader(bool Enabled, uint Status, uint CaptureCount,
    uint DroppedCount, uint Generation, ulong Tick);

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
            throw new InvalidOperationException("Probe is not installed/recognized. No command sent.");
        await _connection.WriteBytesAbsoluteAsync(
            BitConverter.GetBytes(enable ? 1u : 0u),
            address + HostProbeMailbox.EnabledOffset, token).ConfigureAwait(false);
        HostProbeHeader after = await ReadStatusAsync(address, token).ConfigureAwait(false);
        if (after.Enabled != enable)
            throw new IOException("Probe did not acknowledge the enabled flag.");
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
                throw new InvalidOperationException("Probe not active.");
            if (before.CaptureCount == 0)
                throw new InvalidOperationException("No real on-device capture has been recorded.");
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
        throw new IOException("Capture is changing while being read. Retry after the raid bootstrap.");
    }

    private async Task CheckTitleAsync(CancellationToken token)
    {
        string game = (await _connection.GetTitleID(token).ConfigureAwait(false))
            .Trim().Replace("0x", "", StringComparison.OrdinalIgnoreCase);
        if (!game.Equals(HostProbeMailbox.ExpectedTitleId, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException(
                $"This probe is Violet 4.0.0 only (title {HostProbeMailbox.ExpectedTitleId}). Attached: {game}.");
    }
}
