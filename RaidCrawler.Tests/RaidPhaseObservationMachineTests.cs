using RaidCrawler.Core.Structures;
using Xunit;

namespace RaidCrawler.Tests;

public sealed class RaidPhaseObservationMachineTests
{
    private static RaidPhaseObservationMachine.Signals Flags(
        byte lobby, byte loaded, byte? overworld = 0) =>
        new(lobby, loaded, overworld);

    [Fact]
    public void DetectsLobbyBattleDisconnectAndOverworldWithoutClaimingSettlement()
    {
        var m = new RaidPhaseObservationMachine();
        Assert.Null(m.Observe(Flags(1, 0)));
        var lobby = m.Observe(Flags(1, 0));
        Assert.NotNull(lobby);
        Assert.Equal(RaidPhaseObservationMachine.Phase.Lobby, lobby.To);
        Assert.True(lobby.IsNewRaid);

        Assert.Null(m.Observe(Flags(1, 2)));
        var battle = m.Observe(Flags(1, 2));
        Assert.Equal(RaidPhaseObservationMachine.Phase.Battle, battle!.To);
        Assert.False(battle.HookHitVerified);

        Assert.Null(m.Observe(Flags(0, 2)));
        var exit = m.Observe(Flags(0, 2));
        Assert.Equal(RaidPhaseObservationMachine.Phase.BattleConnectionEnded, exit!.To);
        Assert.False(exit.RewardSettledVerified);
        Assert.Equal("battle-connection-ended", exit.Trigger);

        Assert.Null(m.Observe(Flags(0, 0, 0x11)));
        var end = m.Observe(Flags(0, 0, 0x11));
        Assert.Equal(RaidPhaseObservationMachine.Phase.ReturnedToOverworld, end!.To);
        Assert.False(end.RewardSettledVerified);
    }

    [Fact]
    public void ShortGlitchOrReadFailureNeverCreatesSpuriousBattle()
    {
        var m = new RaidPhaseObservationMachine(3);
        m.Observe(Flags(1, 0));
        m.Observe(Flags(0, 0));
        m.Observe(new(null, null, null));
        Assert.Equal(RaidPhaseObservationMachine.Phase.Idle, m.Current);
        Assert.Null(m.Observe(Flags(0, 2)));
        Assert.Null(m.Observe(Flags(0, 2)));
        var t = m.Observe(Flags(0, 2));
        Assert.NotNull(t);
        Assert.Equal("battle-late-attach", t.Trigger);
        Assert.False(m.HasObservedLobby);
    }

    [Fact]
    public void SoloBattleNeverEndsJustBecauseLobbyFlagStaysZero()
    {
        var m = new RaidPhaseObservationMachine();
        m.Observe(Flags(0, 2, 0));
        var started = m.Observe(Flags(0, 2, 0));
        Assert.Equal(RaidPhaseObservationMachine.Phase.Battle, started!.To);
        Assert.False(m.HasObservedLobby);

        for (int i = 0; i < 8; i++)
            Assert.Null(m.Observe(Flags(0, 2, 0)));
        Assert.Equal(RaidPhaseObservationMachine.Phase.Battle, m.Current);

        m.Observe(Flags(0, 0, 0x11));
        var returned = m.Observe(Flags(0, 0, 0x11));
        Assert.Equal(RaidPhaseObservationMachine.Phase.ReturnedToOverworld, returned!.To);
        Assert.False(returned.RewardSettledVerified);
    }

    [Fact]
    public void LobbyCancellationIsNotMarkedAsRewardSettlement()
    {
        var m = new RaidPhaseObservationMachine();
        m.Observe(Flags(1, 0));
        m.Observe(Flags(1, 0));
        m.Observe(Flags(0, 0, 0x11));
        var t = m.Observe(Flags(0, 0, 0x11));
        Assert.Equal(RaidPhaseObservationMachine.Phase.LobbyClosedWithoutBattle, t!.To);
        Assert.False(t.RewardSettledVerified);
        Assert.False(t.HookHitVerified);
    }

    [Fact]
    public void NewRaidAfterReturningOverworldCreatesNewSession()
    {
        var m = new RaidPhaseObservationMachine();
        foreach (var signals in new[]{
            Flags(1,0),Flags(1,0),Flags(1,2),Flags(1,2),
            Flags(0,2),Flags(0,2),Flags(0,0,0x11),Flags(0,0,0x11),
        }) m.Observe(signals);
        Assert.Equal(RaidPhaseObservationMachine.Phase.ReturnedToOverworld, m.Current);
        m.Observe(Flags(1,0));
        var next = m.Observe(Flags(1,0));
        Assert.True(next!.IsNewRaid);
        Assert.Equal(RaidPhaseObservationMachine.Phase.Lobby,next.To);
    }

    [Fact]
    public void UnknownSignalDoesNotTurnIntoConfirmedCompletion()
    {
        var m = new RaidPhaseObservationMachine();
        foreach(var s in new[]{Flags(1,0),Flags(1,0),Flags(1,2),Flags(1,2)})
            m.Observe(s);
        for(int i=0;i<6;i++)
            Assert.Null(m.Observe(new(null, 0, 0x11)));
        Assert.Equal(RaidPhaseObservationMachine.Phase.Battle,m.Current);
    }

    [Fact]
    public void ContradictoryOverworldAndBattleFlagsAreRejected()
    {
        var m = new RaidPhaseObservationMachine();
        for (int i = 0; i < 5; i++)
            Assert.Null(m.Observe(Flags(1, 2, 0x11)));
        Assert.Equal(RaidPhaseObservationMachine.Phase.Idle, m.Current);
    }

    [Fact]
    public async Task ReaderUsesBotbaseCandidateRvasAndTreatsBrokenPointerAsUnknown()
    {
        var offsets = new List<ulong>();
        Task<byte[]> Read(ulong offset, int count, CancellationToken ct)
        {
            offsets.Add(offset);
            return Task.FromResult(new[]{(byte)(offset == 0x047D2020 ? 2 : 1)});
        }
        Task<byte[]> Pointer(IReadOnlyList<long> p,int count,CancellationToken ct) =>
            throw new IOException("unresolved");
        var r = await SvRaidBotPhaseSignals.ReadAsync(Read, Pointer);
        Assert.Equal(new ulong[]{0x043DF430,0x047D2020},offsets);
        Assert.Equal((byte)1,r.Signals.LobbyFlag);
        Assert.Equal((byte)2,r.Signals.LoadedFlag);
        Assert.Null(r.Signals.OverworldFlag);
        Assert.False(r.OverworldPointerAvailable);
    }
}
