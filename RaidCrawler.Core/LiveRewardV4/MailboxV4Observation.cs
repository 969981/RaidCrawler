using System.Buffers.Binary;

namespace RaidCrawler.Core.LiveRewardV4;

public sealed record MailboxV4Observation(
    uint CaptureSerial, uint ContextResult, uint SourceRewardsFnv32,
    uint OutgoingRewardsFnv32, ulong PublishedRevision, ulong ObservedRevision,
    uint ObservationCount, uint ObserverResult, uint Generation)
{
    public bool RewardHashesMatch => ContextResult is 4 or 5 or 6 &&
        SourceRewardsFnv32 == OutgoingRewardsFnv32;
    public bool ConfigurationObserved => ContextResult == 6 &&
        PublishedRevision == ObservedRevision && PublishedRevision != 0;
}

/// <summary>
/// Double-reads the Native generation word around the telemetry tail,
/// rejecting torn captures. Never writes memory.
/// This does NOT prove actual transport or item delivery.
/// </summary>
public sealed class MailboxV4ObservationReader(IMailboxV4Transport transport)
{
    public const int TelemetryOffset = 0x7C0;
    public const int TelemetryLength = 0x40;

    public async Task<MailboxV4Observation> ReadAsync(
        ulong mailboxAddress, CancellationToken token)
    {
        var client = new MailboxV4Client(transport);
        for (int attempt = 0; attempt < 6; attempt++)
        {
            MailboxV4Header first = await client.ReadHeaderAsync(
                mailboxAddress, token).ConfigureAwait(false);
            if ((first.Generation & 1) != 0) continue;
            var tail = await transport.ReadAsync(mailboxAddress + TelemetryOffset,
                TelemetryLength, token).ConfigureAwait(false);
            if (tail.Length != TelemetryLength)
                throw new IOException("MAILBOX_V4_TELEMETRY_SHORT_READ");
            MailboxV4Header second = await client.ReadHeaderAsync(
                mailboxAddress, token).ConfigureAwait(false);
            if (first.Generation != second.Generation ||
                second.ObservationCount != first.ObservationCount ||
                second.ObservedRevision != first.ObservedRevision ||
                (second.Generation & 1) != 0) continue;
            uint serial = BinaryPrimitives.ReadUInt32LittleEndian(tail);
            uint status = BinaryPrimitives.ReadUInt32LittleEndian(tail.AsSpan(4));
            uint sourceHash = BinaryPrimitives.ReadUInt32LittleEndian(tail.AsSpan(8));
            uint outgoingHash = BinaryPrimitives.ReadUInt32LittleEndian(tail.AsSpan(12));
            if (status > 6)
                throw new InvalidDataException("MAILBOX_V4_BAD_OBSERVATION_RESULT");
            return new(serial, status, sourceHash, outgoingHash,
                second.PublishedRevision, second.ObservedRevision,
                second.ObservationCount, second.LastObservationStatus,
                second.Generation);
        }
        throw new IOException("MAILBOX_V4_TELEMETRY_RACE");
    }
}
