using System.Buffers.Binary;
using RaidCrawler.Core.Structures;
using Xunit;

namespace RaidCrawler.Tests;

public sealed class Violet400LuaVmFrameProbeTests
{
    private const ulong Reference = 0x1234561088UL;
    private const ulong State = 0x1234600000UL;
    private const ulong Stack = 0x1234700000UL;
    private static void Set(byte[] b, int offset, ulong v) =>
        BinaryPrimitives.WriteUInt64LittleEndian(b.AsSpan(offset, 8), v);

    private static byte[] Header(bool active = false)
    {
        byte[] b = new byte[Violet400LuaVmFrameProbe.HeaderBytes];
        b[8] = Violet400LuaVmFrameProbe.LuaThreadObjectTag;
        Set(b, 0x10, Stack + 0x10);      // L.top
        Set(b, 0x18, State + 0xC8);      // L.l_G, main state
        Set(b, 0x20, active ? State + 0x380 : State + 0x60); // L.ci
        Set(b, 0x28, Stack + 0x280);     // L.stack_last
        Set(b, 0x30, Stack);             // L.stack
        Set(b, 0x40, Stack);             // L.tbclist
        Set(b, 0x60, Stack);             // base_ci.func
        Set(b, 0x68, Stack + 0x150);     // base_ci.top
        return b;
    }

    private static async Task<(Violet400LuaVmFrameProbe.Observation Result, List<string> Reads)>
        Run(byte[] header, bool active = false, byte functionTag = 0x66,
            bool changeHeader = false, bool shortHeader = false,
            bool retry = false, bool changeHeaderOnce = false)
    {
        List<string> calls = [];
        int headerReads = 0;
        Task<byte[]> Main(ulong rva, int count, CancellationToken ct)
        {
            Assert.Equal(Violet400LuaVmRootProbe.GlobalReferenceSlotRva, rva);
            Assert.Equal(8, count);
            calls.Add("global");
            return Task.FromResult(BitConverter.GetBytes(Reference));
        }
        Task<byte[]> Absolute(ulong address, int count, CancellationToken ct)
        {
            calls.Add($"abs:{count}");
            if (address == Reference - 0x18 && count == 8)
                return Task.FromResult(BitConverter.GetBytes(State));
            if (address == State && count == 0x70)
            {
                headerReads++;
                if (shortHeader) return Task.FromResult(new byte[8]);
                if ((changeHeader && headerReads % 2 == 0) || (changeHeaderOnce && headerReads == 2))
                {
                    byte[] changed = header.ToArray();
                    Set(changed, 0x20, State + 0x400);
                    return Task.FromResult(changed);
                }
                return Task.FromResult(header.ToArray());
            }
            if (address == State + 0x380 && count == 0x20 && active)
            {
                byte[] ci = new byte[0x20];
                Set(ci, 0, Stack + 0x20);
                Set(ci, 8, Stack + 0x160);
                Set(ci, 0x10, State + 0x60);
                return Task.FromResult(ci);
            }
            if (address == Stack + 0x20 && count == 0x10 && active)
            {
                byte[] slot = new byte[0x10];
                slot[8] = functionTag;
                return Task.FromResult(slot);
            }
            throw new InvalidOperationException($"Unexpected read 0x{address:X} len={count}");
        }
        var result = retry
            ? await Violet400LuaVmFrameProbe.ProbeWithRetryAsync(Main, Absolute, CancellationToken.None)
            : await Violet400LuaVmFrameProbe.ProbeAsync(Main, Absolute, CancellationToken.None);
        return (result, calls);
    }

    [Fact]
    public async Task MainStateHeaderAndBaseCiArePlausibleNotActive()
    {
        var (result, reads) = await Run(Header());
        Assert.Equal("MAIN_STATE_LAYOUT_MATCH_BASE_CI_IDLE", result.Status);
        Assert.True(result.SnapshotStable);
        Assert.True(result.MainStateLayoutMatched);
        Assert.True(result.StackPointersPlausible);
        Assert.False(result.ActiveCallInfoCandidate);
        Assert.False(result.ClosureIdentityVerified);
        Assert.False(result.RewardHookInstalled);
        Assert.Equal(8, reads.Count);
        Assert.DoesNotContain("abs:32", reads);
        Assert.DoesNotContain("abs:16", reads);
    }

    [Fact]
    public async Task SamplesActiveFrameAndLuaClosureTagButNotClosureIdentity()
    {
        var (result, reads) = await Run(Header(active:true), active:true);
        Assert.Equal("ACTIVE_CALLINFO_SAMPLED_NO_EXECUTION_ATTESTATION", result.Status);
        Assert.True(result.ActiveCallInfoCandidate);
        Assert.True(result.LuaClosureValueTagObserved);
        Assert.Equal("LUA_CLOSURE_TVALUE_TAG_CANDIDATE", result.FunctionSlotClassification);
        Assert.Equal("NOT_VERIFIED", result.PrototypeIdentity);
        Assert.Equal("NOT_OBSERVED", result.RuntimeCallHit);
        Assert.Equal("NOT_CAPTURED", result.AbcRewards);
        Assert.Equal(10, reads.Count);
    }

    [Fact]
    public async Task ActiveFrameCanBeCFunctionInsteadOfRewardClosure()
    {
        var (result, _) = await Run(Header(active:true), active:true, functionTag:0x56);
        Assert.Equal("C_CLOSURE_TVALUE_TAG_CANDIDATE", result.FunctionSlotClassification);
        Assert.False(result.LuaClosureValueTagObserved);
        Assert.False(result.ClosureIdentityVerified);
    }

    [Fact]
    public async Task CorruptGlobalPointerRejectsBeforeCallInfoRead()
    {
        byte[] wrong = Header(active:true);
        Set(wrong, 0x18, State + 0xD0);
        var (result, reads) = await Run(wrong, active:true);
        Assert.Equal("MAIN_STATE_LAYOUT_MISMATCH", result.Status);
        Assert.DoesNotContain("abs:32", reads);
    }

    [Fact]
    public async Task CorruptStackRejectsBeforeCallInfoRead()
    {
        byte[] wrong = Header(active:true);
        Set(wrong, 0x28, Stack - 0x10);
        var (result, reads) = await Run(wrong, active:true);
        Assert.Equal("STACK_OR_BASE_CI_LAYOUT_MISMATCH", result.Status);
        Assert.DoesNotContain("abs:32", reads);
    }

    [Fact]
    public async Task RaceConditionRejectsWithoutClaimingActivity()
    {
        var (result, _) = await Run(Header(active:true), active:true, changeHeader:true);
        Assert.Equal("STATE_FIELDS_CHANGED_DURING_READ", result.Status);
        Assert.Contains("CallInfo", result.ChangedFields!);
        Assert.False(result.SnapshotStable);
        Assert.False(result.ClosureIdentityVerified);
    }

    [Fact]
    public async Task RetryRecoversAfterOneTransientCallInfoChange()
    {
        var (result, _) = await Run(Header(active:true), active:true,
            retry:true, changeHeaderOnce:true);
        Assert.Equal("ACTIVE_CALLINFO_SAMPLED_NO_EXECUTION_ATTESTATION", result.Status);
        Assert.Equal(2, result.ReadAttempts);
        Assert.True(result.SnapshotStable);
        Assert.Equal("NOT_VERIFIED", result.PrototypeIdentity);
    }

    [Fact]
    public async Task RepeatedVolatilityReportsChangedFieldAndAttemptCount()
    {
        var (result, _) = await Run(Header(active:true), active:true,
            retry:true, changeHeader:true);
        Assert.Equal("STATE_FIELDS_CHANGED_DURING_READ", result.Status);
        Assert.Equal(3, result.ReadAttempts);
        Assert.Contains("CallInfo", result.ChangedFields!);
        Assert.False(result.SnapshotStable);
    }

    [Fact]
    public async Task ShortStateHeaderStopsFurtherReads()
    {
        var (result, reads) = await Run(Header(), shortHeader:true);
        Assert.Equal("STATE_HEADER_SHORT_READ", result.Status);
        Assert.DoesNotContain("abs:32", reads);
    }

    [Fact]
    public async Task CancellationStopsBeforeMemoryAccess()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            Violet400LuaVmFrameProbe.ProbeAsync(
                (_, _, _) => throw new Exception("Never read"),
                (_, _, _) => throw new Exception("Never read"),
                cts.Token));
    }
}
