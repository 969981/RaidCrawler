using System.Buffers.Binary;
using SysBot.Base;

namespace RaidCrawler.Core.LiveRewardV4;

/// <summary>Transport is limited to a dedicated, independently located V4 mailbox.</summary>
public interface IMailboxV4Transport
{
    Task<byte[]> ReadAsync(ulong absoluteAddress, int length, CancellationToken token);
    Task WriteAsync(ulong absoluteAddress, byte[] value, CancellationToken token);
}

public sealed class SysBotMailboxV4Transport(ISwitchConnectionAsync connection) : IMailboxV4Transport
{
    public Task<byte[]> ReadAsync(ulong address, int length, CancellationToken token) =>
        connection.ReadBytesAbsoluteAsync(address, length, token);

    public Task WriteAsync(ulong address, byte[] value, CancellationToken token) =>
        connection.WriteBytesAbsoluteAsync(value, address, token);
}

public sealed class MailboxV4Client(IMailboxV4Transport transport)
{
    private readonly SemaphoreSlim _publishGate = new(1, 1);

    private async Task<byte[]> ReadExactAsync(ulong address, int count, CancellationToken token)
    {
        var value = await transport.ReadAsync(address, count, token).ConfigureAwait(false);
        if (value.Length != count) throw new IOException("MAILBOX_V4_SHORT_READ");
        return value;
    }

    public async Task<MailboxV4Header> ReadHeaderAsync(ulong address, CancellationToken token)
    {
        var header = MailboxV4.ParseHeader(
            await ReadExactAsync(address, MailboxV4.HeaderLength, token).ConfigureAwait(false));
        if (header.Status != 1 || header.Capabilities != MailboxV4.ReadOnlyObserverCapability)
            throw new InvalidOperationException("MAILBOX_V4_NOT_READONLY_READY");
        return header;
    }

    /// <summary>
    /// Publish profile to an INACTIVE slot, read back exact bytes, then publish the
    /// revision and index. It changes only the V4 mailbox, NEVER game reward data.
    /// Requires an exact V4 build's independently discovered mailbox address.
    /// </summary>
    public async Task<RewardConfigSnapshotV4> PublishAsync(
        ulong mailboxAddress, RewardConfigV4 profile, CancellationToken token)
    {
        if (mailboxAddress < 0x10000 || (mailboxAddress & 7UL) != 0 ||
            mailboxAddress > ulong.MaxValue - MailboxV4.MailboxLength)
            throw new ArgumentOutOfRangeException(nameof(mailboxAddress));
        await _publishGate.WaitAsync(token).ConfigureAwait(false);
        try
        {
            MailboxV4.Validate(profile);
            var before = await ReadHeaderAsync(mailboxAddress, token).ConfigureAwait(false);
            uint nextSlot = before.ActiveSlot == 0 ? 1u : 0u;
            ulong revision = checked(Math.Max(before.PublishedRevision, before.ObservedRevision) + 1);
            byte[] payload = MailboxV4.Serialize(profile, revision);
            ulong slotAddress = checked(mailboxAddress + (ulong)MailboxV4.OffsetOfSlot(nextSlot));
            await transport.WriteAsync(slotAddress, payload, token).ConfigureAwait(false);
            var echoed = await ReadExactAsync(slotAddress, payload.Length, token).ConfigureAwait(false);
            if (!echoed.AsSpan().SequenceEqual(payload))
                throw new IOException("MAILBOX_V4_SLOT_READBACK_MISMATCH");
            var current = await ReadHeaderAsync(mailboxAddress, token).ConfigureAwait(false);
            if (current.ActiveSlot != before.ActiveSlot ||
                current.PublishedRevision != before.PublishedRevision)
                throw new IOException("MAILBOX_V4_CONCURRENT_PUBLISH");
            // Revision is published first, slot selector LAST. During the tiny gap
            // the Native observer must fail closed if revision/slot disagree.
            byte[] nextRevision = new byte[8];
            BinaryPrimitives.WriteUInt64LittleEndian(nextRevision, revision);
            await transport.WriteAsync(mailboxAddress + 0x20, nextRevision, token)
                .ConfigureAwait(false);
            byte[] selector = new byte[4];
            BinaryPrimitives.WriteUInt32LittleEndian(selector, nextSlot);
            await transport.WriteAsync(mailboxAddress + 0x18, selector, token)
                .ConfigureAwait(false);
            var after = await ReadHeaderAsync(mailboxAddress, token).ConfigureAwait(false);
            if (after.ActiveSlot != nextSlot || after.PublishedRevision != revision)
                throw new IOException("MAILBOX_V4_COMMIT_UNCERTAIN");
            return new RewardConfigSnapshotV4(revision, profile);
        }
        finally { _publishGate.Release(); }
    }

    public async Task<RewardConfigSnapshotV4?> ReadPublishedAsync(
        ulong mailboxAddress, CancellationToken token)
    {
        var before = await ReadHeaderAsync(mailboxAddress, token).ConfigureAwait(false);
        if (before.ActiveSlot == MailboxV4.NoActiveSlot) return null;
        var data = await ReadExactAsync(
            checked(mailboxAddress + (ulong)MailboxV4.OffsetOfSlot(before.ActiveSlot)),
            MailboxV4.SlotLength, token).ConfigureAwait(false);
        var after = await ReadHeaderAsync(mailboxAddress, token).ConfigureAwait(false);
        if (before.ActiveSlot != after.ActiveSlot ||
            before.PublishedRevision != after.PublishedRevision)
            throw new IOException("MAILBOX_V4_SNAPSHOT_CHANGED");
        var snapshot = MailboxV4.ParseSlot(data);
        if (snapshot.Revision != after.PublishedRevision)
            throw new IOException("MAILBOX_V4_REVISION_MISMATCH");
        return snapshot;
    }
}
