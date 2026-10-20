using RaidCrawler.Core.Structures;
using Xunit;

namespace RaidCrawler.Tests;

public sealed class Violet400LuaVmRootProbeTests
{
    private const ulong Global = 0x127ABC0088UL;
    private const ulong State = 0x127DEF1000UL;

    [Fact]
    public async Task StableCandidatePerformsOnlyFourEightByteReads()
    {
        var calls = new List<string>();
        Task<byte[]> Main(ulong rva, int size, CancellationToken token)
        {
            Assert.Equal(Violet400LuaVmRootProbe.GlobalReferenceSlotRva, rva);
            Assert.Equal(8, size);
            calls.Add("main");
            return Task.FromResult(BitConverter.GetBytes(Global));
        }
        Task<byte[]> Absolute(ulong address, int size, CancellationToken token)
        {
            Assert.Equal(Global - 0x18UL, address);
            Assert.Equal(8, size);
            calls.Add("absolute");
            return Task.FromResult(BitConverter.GetBytes(State));
        }
        var result = await Violet400LuaVmRootProbe.ProbeAsync(Main, Absolute, CancellationToken.None);
        Assert.Equal("CANDIDATE_POINTER_STABLE_NOT_VM_VERIFIED", result.Status);
        Assert.Equal(new[] { "main", "absolute", "absolute", "main" }, calls);
        Assert.Equal(Global, result.GlobalReference);
        Assert.Equal(State, result.LuaStateCandidate);
        Assert.True(result.StableAcrossReads);
        Assert.True(result.ReadOnly);
        Assert.False(result.VmStateConfirmed);
        Assert.Equal("NOT_OBSERVED", result.HookCpuHit);
        Assert.Equal("NOT_OBSERVED", result.AbcRuntimeCapture);
    }

    [Fact]
    public async Task NullGlobalSkipsAllAbsoluteReads()
    {
        int reads = 0;
        var result = await Violet400LuaVmRootProbe.ProbeAsync(
            (_, _, _) => Task.FromResult(new byte[8]),
            (_, _, _) => { reads++; throw new InvalidOperationException("Not allowed"); },
            CancellationToken.None);
        Assert.Equal("GLOBAL_SLOT_NULL", result.Status);
        Assert.Equal(0, reads);
    }

    [Fact]
    public async Task RejectsMisalignedGlobalWithoutDereference()
    {
        int reads = 0;
        var result = await Violet400LuaVmRootProbe.ProbeAsync(
            (_, _, _) => Task.FromResult(BitConverter.GetBytes(Global + 1)),
            (_, _, _) => { reads++; throw new InvalidOperationException(); },
            CancellationToken.None);
        Assert.Equal("GLOBAL_SLOT_INVALID_POINTER", result.Status);
        Assert.Equal(0, reads);
    }

    [Fact]
    public async Task NullLuaStateDoesNotAttestVm()
    {
        var result = await Violet400LuaVmRootProbe.ProbeAsync(
            (_, _, _) => Task.FromResult(BitConverter.GetBytes(Global)),
            (_, _, _) => Task.FromResult(new byte[8]), CancellationToken.None);
        Assert.Equal("LUA_STATE_FIELD_NULL", result.Status);
        Assert.False(result.StableAcrossReads);
        Assert.False(result.VmStateConfirmed);
    }

    [Fact]
    public async Task ChangedPointersFailClosed()
    {
        int mainCalls = 0;
        var result = await Violet400LuaVmRootProbe.ProbeAsync(
            (_, _, _) => Task.FromResult(BitConverter.GetBytes(
                ++mainCalls == 1 ? Global : Global + 8)),
            (_, _, _) => Task.FromResult(BitConverter.GetBytes(State)),
            CancellationToken.None);
        Assert.Equal("POINTER_CHANGED_DURING_SAMPLE", result.Status);
        Assert.False(result.StableAcrossReads);
        Assert.Null(result.LuaStateCandidate);
    }

    [Fact]
    public async Task ShortReadRejectsAndDoesNotDereference()
    {
        int reads = 0;
        await Assert.ThrowsAsync<InvalidDataException>(() =>
            Violet400LuaVmRootProbe.ProbeAsync(
                (_, _, _) => Task.FromResult(new byte[4]),
                (_, _, _) => { reads++; throw new Exception(); },
                CancellationToken.None));
        Assert.Equal(0, reads);
    }

    [Fact]
    public async Task CancellationPropagatesBeforeReading()
    {
        using var token = new CancellationTokenSource();
        token.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            Violet400LuaVmRootProbe.ProbeAsync(
                (_, _, _) => throw new InvalidOperationException(),
                (_, _, _) => throw new InvalidOperationException(),
                token.Token));
    }
}
