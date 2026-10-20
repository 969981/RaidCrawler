using RaidCrawler.Core.Host;
using Xunit;

namespace RaidCrawler.Tests.Host;

public sealed class SvRaidLifecycleReaderTests
{
    private static SvRaidSignals Signals(
        byte? overworld = null, byte? lobby = null,
        byte? battle = null, int? guests = null, bool experimental = true) =>
        new(overworld, lobby, battle, guests, experimental, null);

    [Fact]
    public void NeverTreatsOverworldAbsenceAsProofOfLobby()
    {
        var c = new SvRaidLifecycleClassifier();
        Assert.Null(c.Observe(Signals(overworld: 0x00, experimental: false)));
        Assert.Equal(SvRaidPhase.Unknown, c.Current);
        Assert.Null(c.Observe(Signals(lobby: 1, battle: 2, experimental: false)));
        Assert.Equal(SvRaidPhase.Unknown, c.Current);
    }

    [Fact]
    public void RequiresTwoConsecutiveReadsForAStageChange()
    {
        var c = new SvRaidLifecycleClassifier();
        var overworld = Signals(overworld: 0x11, experimental: false);
        Assert.Null(c.Observe(overworld));
        var e = c.Observe(overworld);
        Assert.NotNull(e);
        Assert.Equal(SvRaidPhase.S0Overworld, e.Phase);
        Assert.False(e.ExperimentalUnverified);
        Assert.Null(c.Observe(overworld));
    }

    [Fact]
    public void ExperimentalProbesRecognizeLobbyGuestsAndBattleButStayProvisional()
    {
        var c = new SvRaidLifecycleClassifier();
        var outside = Signals(overworld: 0x11, lobby: 0, battle: 0, guests: 0);
        c.Observe(outside); c.Observe(outside);

        var lobby = Signals(overworld: 0, lobby: 1, battle: 0, guests: 0);
        Assert.Null(c.Observe(lobby));
        Assert.Equal(SvRaidPhase.S1Lobby, c.Observe(lobby)!.Phase);

        var joined = Signals(overworld: 0, lobby: 1, battle: 0, guests: 1);
        c.Observe(joined);
        var e = c.Observe(joined);
        Assert.NotNull(e);
        Assert.Equal(SvRaidPhase.S2GuestsPresent, e.Phase);
        Assert.True(e.ExperimentalUnverified);
        Assert.Contains("非Ready", e.Evidence);

        var battlefield = Signals(overworld: 0, lobby: 1, battle: 2, guests: 1);
        c.Observe(battlefield);
        Assert.Equal(SvRaidPhase.S4RaidBattle, c.Observe(battlefield)!.Phase);
    }

    [Fact]
    public void S3IsInferredOnlyAfterObservedLobbyAndNeedsConsecutiveReads()
    {
        var c = new SvRaidLifecycleClassifier();
        var joined = Signals(overworld: 0, lobby: 1, battle: 0, guests: 2);
        c.Observe(joined); c.Observe(joined);
        var unknownTransition = Signals(overworld: 0, lobby: 0, battle: 1, guests: 2);
        Assert.Null(c.Observe(unknownTransition));
        var e = c.Observe(unknownTransition);
        Assert.NotNull(e);
        Assert.Equal(SvRaidPhase.S3LeavingLobbyCandidate, e.Phase);
        Assert.Contains("推断", e.Evidence);
    }

    [Fact]
    public void InconsistentObservationsDoNotProduceAStage()
    {
        var c = new SvRaidLifecycleClassifier();
        var lobby = Signals(lobby: 1, battle: 0);
        Assert.Null(c.Observe(lobby));
        Assert.Null(c.Observe(Signals()));
        Assert.Null(c.Observe(lobby));
        Assert.Equal(SvRaidPhase.Unknown, c.Current);
    }

    [Fact]
    public void UnknownAndUnverifiedCasesFailClosed()
    {
        Assert.Equal(SvRaidPhase.Unknown, SvRaidLifecycleClassifier.Classify(
            Signals(overworld: 0, lobby: null, battle: null), SvRaidPhase.Unknown));
        Assert.Equal(SvRaidPhase.Unknown, SvRaidLifecycleClassifier.Classify(
            Signals(overworld: null, lobby: 1, battle: 2, guests: 2, experimental: false),
            SvRaidPhase.Unknown));
    }
}
