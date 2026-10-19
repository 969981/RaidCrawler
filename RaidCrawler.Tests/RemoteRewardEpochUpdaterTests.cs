using System.Buffers.Binary;
using RaidCrawler.Core.Structures;
using Xunit;

namespace RaidCrawler.Tests;

public sealed class RemoteRewardEpochUpdaterTests
{
    private sealed class FakeRemote
    {
        public byte[] Cave = new byte[880];
        public byte[] HookEntry = new byte[4];
        public List<ulong> Writes = [];
        public bool FailTemplateWrite;
        public int IdentityChecks;

        public FakeRemote(bool prepareSignature = true)
        {
            if (prepareSignature)
            {
                uint jump = 0x14000000 |
                    (uint)((((long)RemoteRewardEpochUpdater.CaveRva -
                        (long)RemoteRewardEpochUpdater.HookRva) / 4) & 0x03ffffff);
                BinaryPrimitives.WriteUInt32LittleEndian(HookEntry, jump);
                BinaryPrimitives.WriteUInt32LittleEndian(Cave.AsSpan(0x90), 1);
                BinaryPrimitives.WriteUInt32LittleEndian(
                    Cave.AsSpan(0x94), RemoteRewardEpochUpdater.ProtocolMagic);
                "RaidPoin"u8.CopyTo(Cave.AsSpan(0x98));
                foreach (var (offset, opcode) in RemoteRewardEpochUpdater.ReaderAnchors)
                    BinaryPrimitives.WriteUInt32LittleEndian(
                        Cave.AsSpan(checked((int)offset)), opcode);
            }
        }

        public Task Verify(CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            IdentityChecks++;
            return Task.CompletedTask;
        }

        public Task<byte[]> Read(ulong rva, int count, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            if (rva == RemoteRewardEpochUpdater.HookRva && count == 4)
                return Task.FromResult(HookEntry.ToArray());
            int offset = checked((int)(rva - RemoteRewardEpochUpdater.CaveRva));
            return Task.FromResult(Cave.AsSpan(offset, count).ToArray());
        }

        public Task Write(byte[] data, ulong rva, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            Writes.Add(rva);
            if (FailTemplateWrite && rva == RemoteRewardEpochUpdater.TemplateRva)
                throw new IOException("Simulated partial/failed write");
            int offset = checked((int)(rva - RemoteRewardEpochUpdater.CaveRva));
            data.CopyTo(Cave, offset);
            return Task.CompletedTask;
        }

        public uint Epoch => BinaryPrimitives.ReadUInt32LittleEndian(
            Cave.AsSpan(0x90));
        public RemoteRewardEpochUpdater Controller() =>
            new(Read, Write, Verify);
    }

    private static byte[] Reward(int id, int quantity)
    {
        var slots = new RaidRewardEntry?[RaidRewardEditor.MaxRows];
        slots[0] = new RaidRewardEntry(id, quantity, 0, 5);
        return RaidRewardEditor.EncodeSlots(slots);
    }

    [Fact]
    public async Task DisarmedUpdateKeepsOddEpochUntilEnabled()
    {
        var f = new FakeRemote();
        var c = f.Controller();
        var update = await c.ReplaceAsync(Reward(15, 999));
        Assert.Equal(1u, update.OldEpoch);
        Assert.Equal(3u, update.NewEpoch);
        Assert.False(update.Armed);
        Assert.Equal(3u, f.Epoch);
        Assert.Equal(new[] {
            RemoteRewardEpochUpdater.EpochRva,
            RemoteRewardEpochUpdater.TemplateRva }, f.Writes);
        Assert.Equal(4u, await c.SetArmedAsync(true));
        Assert.True((await c.SnapshotAsync()).Armed);
        Assert.True(f.IdentityChecks >= 3);
    }

    [Fact]
    public async Task LiveUpdatePublishesOddThenPayloadThenNewEven()
    {
        var f = new FakeRemote();
        var c = f.Controller();
        await c.ReplaceAsync(Reward(15, 100));
        await c.SetArmedAsync(true);
        f.Writes.Clear();
        var result = await c.ReplaceAsync(Reward(15, 999));
        Assert.Equal(4u, result.OldEpoch);
        Assert.Equal(6u, result.NewEpoch);
        Assert.True(result.Armed);
        Assert.Equal(6u, f.Epoch);
        Assert.Equal(new[] {
            RemoteRewardEpochUpdater.EpochRva,
            RemoteRewardEpochUpdater.TemplateRva,
            RemoteRewardEpochUpdater.EpochRva }, f.Writes);
        Assert.Equal(999, RaidRewardEditor.DecodeSlots(
            (await c.SnapshotAsync()).Rewards)[0]!.Quantity);
    }

    [Fact]
    public async Task TransferFailureLeavesOddSafeFallback()
    {
        var f = new FakeRemote();
        var c = f.Controller();
        await c.ReplaceAsync(Reward(15, 100));
        await c.SetArmedAsync(true);
        f.FailTemplateWrite = true;
        await Assert.ThrowsAsync<IOException>(() => c.ReplaceAsync(Reward(15, 999)));
        Assert.Equal(5u, f.Epoch);
        Assert.False((await c.SnapshotAsync()).Armed);
    }

    [Fact]
    public async Task OldModeOnlyStubNeverGetsRewardWrite()
    {
        var f = new FakeRemote(false);
        var c = f.Controller();
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            c.ReplaceAsync(Reward(15, 999)));
        Assert.Empty(f.Writes);
    }

    [Fact]
    public async Task StaleOrCorruptedReaderInstructionsFailClosed()
    {
        var f = new FakeRemote();
        f.Cave[0x4c] ^= 0x01;
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            f.Controller().ReplaceAsync(Reward(15, 999)));
        Assert.Empty(f.Writes);
    }

    [Fact]
    public async Task ReadbackMustMatchBeforeDataTransfer()
    {
        var f = new FakeRemote();
        Task IgnoreEpoch(byte[] data, ulong offset, CancellationToken ct)
        {
            if (offset == RemoteRewardEpochUpdater.EpochRva)
                return Task.CompletedTask;
            return f.Write(data, offset, ct);
        }

        var c = new RemoteRewardEpochUpdater(f.Read, IgnoreEpoch, f.Verify);
        await Assert.ThrowsAsync<IOException>(() => c.ReplaceAsync(Reward(15, 999)));
        Assert.Empty(f.Writes);
    }

    [Fact]
    public async Task DisableDoesNotRewriteRewards()
    {
        var f = new FakeRemote();
        var c = f.Controller();
        await c.ReplaceAsync(Reward(15, 999));
        await c.SetArmedAsync(true);
        f.Writes.Clear();
        Assert.Equal(5u, await c.SetArmedAsync(false));
        Assert.Equal(new[] { RemoteRewardEpochUpdater.EpochRva }, f.Writes);
    }

    [Fact]
    public async Task UnexpectedHeaderAbortsBeforeChangingMemory()
    {
        var f = new FakeRemote();
        f.Cave[0x94] = 0;
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            f.Controller().ReplaceAsync(Reward(15, 999)));
        Assert.Empty(f.Writes);
    }
}
