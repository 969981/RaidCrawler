using System.Buffers.Binary;
using RaidCrawler.Core.Structures;
using Xunit;

namespace RaidCrawler.Tests;

public sealed class RemoteViolet400HookInstallerTests
{
    private sealed class SimulatedSwitch
    {
        public readonly byte[] Cave = new byte[880];
        public readonly byte[] Entry = new byte[4];
        public readonly List<ulong> Writes = [];
        public bool CorruptCaveReadback;
        public bool WrongIdentity;
        public int IdentityChecks;
        public SimulatedSwitch()
        {
            BinaryPrimitives.WriteUInt32LittleEndian(
                Entry, RemoteViolet400HookPayload.OriginalInstruction);
        }
        public Task Verify(CancellationToken ct)
        {
            IdentityChecks++;
            if (WrongIdentity) throw new InvalidOperationException("process changed");
            return Task.CompletedTask;
        }
        public Task<byte[]> Read(ulong rva, int len, CancellationToken ct)
        {
            if (rva == NativeRaidCodeFingerprint.SerializerHookRva)
                return Task.FromResult(Entry.ToArray());
            if (rva == NativeRaidCodeFingerprint.LastPltInstructionRva)
            {
                var word = new byte[4];
                BinaryPrimitives.WriteUInt32LittleEndian(word,
                    NativeRaidCodeFingerprint.LastPltInstructionOpcode);
                return Task.FromResult(word);
            }
            int pos = checked((int)(rva - NativeRaidCodeFingerprint.EndOfTextPaddingRva));
            byte[] bytes = Cave.AsSpan(pos, len).ToArray();
            if (CorruptCaveReadback && pos == 0 && len == 880)
                bytes[1] ^= 1;
            return Task.FromResult(bytes);
        }
        public Task Write(byte[] data, ulong rva, CancellationToken ct)
        {
            Writes.Add(rva);
            if (rva == NativeRaidCodeFingerprint.SerializerHookRva)
                data.CopyTo(Entry, 0);
            else
                data.CopyTo(Cave, checked((int)(rva -
                    NativeRaidCodeFingerprint.EndOfTextPaddingRva)));
            return Task.CompletedTask;
        }
        public RemoteViolet400HookInstaller Manager() => new(Read, Write, Verify);
        public uint EntryWord => BinaryPrimitives.ReadUInt32LittleEndian(Entry);
    }

    private static byte[] Reward()
    {
        RaidRewardEntry?[] slots = new RaidRewardEntry?[45];
        slots[0] = new RaidRewardEntry(15, 999, 0, 5);
        return RaidRewardEditor.EncodeSlots(slots);
    }

    [Fact]
    public void PayloadExactlyMatchesOfflineV2CodeAndLayout()
    {
        byte[] payload = RemoteViolet400HookPayload.MakeDisarmed(Reward());
        Assert.Equal(880, payload.Length);
        Assert.Equal(1u, BinaryPrimitives.ReadUInt32LittleEndian(payload.AsSpan(0x90)));
        Assert.Equal("RHS2RaidPoin", System.Text.Encoding.ASCII.GetString(payload, 0x94, 12));
        Assert.True(RemoteViolet400HookPayload.MatchesReader(payload));
        Assert.True(Reward().SequenceEqual(payload.AsSpan(0xA0).ToArray()));
    }

    [Fact]
    public async Task InstallStagesVerifiedPayloadBeforePatchingOneEntryInstruction()
    {
        var f = new SimulatedSwitch();
        var manager = f.Manager();
        var result = await manager.InstallAsync(Reward());
        Assert.Equal(RemoteViolet400HookInstaller.InstallState.InstalledDisarmed,
            result.Installation);
        Assert.Equal(new[] {
            NativeRaidCodeFingerprint.EndOfTextPaddingRva,
            NativeRaidCodeFingerprint.SerializerHookRva }, f.Writes);
        Assert.Equal(RemoteViolet400HookPayload.EntryBranch, f.EntryWord);
        Assert.True(f.IdentityChecks >= 3);
    }

    [Fact]
    public async Task HotUpdateEnableDisableThenUninstallLeavesCaveIntact()
    {
        var f = new SimulatedSwitch();
        var manager = f.Manager();
        await manager.InstallAsync(Reward());
        var enabled = await manager.SetEnabledAsync(true);
        Assert.Equal(RemoteViolet400HookInstaller.InstallState.InstalledArmed,
            enabled.Installation);
        var replacement = new RaidRewardEntry?[45];
        replacement[0] = new RaidRewardEntry(15, 500, 0, 0);
        await manager.UpdateAsync(RaidRewardEditor.EncodeSlots(replacement));
        var disabled = await manager.SetEnabledAsync(false);
        Assert.Equal(RemoteViolet400HookInstaller.InstallState.InstalledDisarmed,
            disabled.Installation);
        var detached = await manager.UninstallAsync();
        Assert.Equal(RemoteViolet400HookInstaller.InstallState.DetachedCodePresent,
            detached.Installation);
        Assert.Equal(RemoteViolet400HookPayload.OriginalInstruction, f.EntryWord);
        Assert.True(RemoteViolet400HookPayload.MatchesReader(f.Cave));
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            manager.InstallAsync(Reward()));
    }

    [Fact]
    public async Task BrokenReadbackMustNotPatchExecutionEntry()
    {
        var f = new SimulatedSwitch { CorruptCaveReadback = true };
        await Assert.ThrowsAsync<IOException>(() => f.Manager().InstallAsync(Reward()));
        Assert.Equal(RemoteViolet400HookPayload.OriginalInstruction, f.EntryWord);
        Assert.Equal(new[] { NativeRaidCodeFingerprint.EndOfTextPaddingRva }, f.Writes);
    }

    [Fact]
    public async Task IdentityFailureMeansNoWrites()
    {
        var f = new SimulatedSwitch { WrongIdentity = true };
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            f.Manager().InstallAsync(Reward()));
        Assert.Empty(f.Writes);
    }

    [Fact]
    public async Task ForeignEntryPatchBlocksInstallation()
    {
        var f = new SimulatedSwitch();
        BinaryPrimitives.WriteUInt32LittleEndian(f.Entry, 0x14000001);
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            f.Manager().InstallAsync(Reward()));
        Assert.Empty(f.Writes);
    }
}
