namespace RaidCrawler.Core.Structures;

/// <summary>
/// Pure, conservative read-only state machine based on candidate SVRaidBot
/// status bytes. Phases require repeated observations and are NOT proof of
/// reward settlement or native serializer execution.
/// </summary>
public sealed class RaidPhaseObservationMachine
{
    public enum Phase
    {
        Idle,
        Lobby,
        Battle,
        BattleConnectionEnded,
        ReturnedToOverworld,
        LobbyClosedWithoutBattle,
    }

    public sealed record Signals(byte? LobbyFlag, byte? LoadedFlag,
        byte? OverworldFlag)
    {
        public bool HasCoreSignals => LobbyFlag.HasValue && LoadedFlag.HasValue;
        public bool IsLobby => LobbyFlag is > 0;
        public bool IsBattle => LoadedFlag == 2;
        public bool IsOverworld => OverworldFlag == 0x11;
    }

    public sealed record Transition(Phase From, Phase To, string Trigger,
        bool IsNewRaid, bool RewardSettledVerified = false,
        bool HookHitVerified = false);

    public Phase Current { get; private set; } = Phase.Idle;
    public bool HasObservedLobby { get; private set; }
    public int StableSamplesRequired { get; }
    private string? candidate;
    private int candidateSamples;

    public RaidPhaseObservationMachine(int stableSamplesRequired = 2)
    {
        if (stableSamplesRequired < 2 || stableSamplesRequired > 10)
            throw new ArgumentOutOfRangeException(nameof(stableSamplesRequired));
        StableSamplesRequired = stableSamplesRequired;
    }

    public Transition? Observe(Signals? signals)
    {
        // Never infer game state from a failed read, or a random byte value.
        if (signals is null || !signals.HasCoreSignals ||
            signals.LoadedFlag is > 5 ||
            (signals.IsOverworld && (signals.IsLobby || signals.IsBattle)))
        {
            // Unknown/contradictory readings are not evidence of a game
            // phase transition. Candidate flag offsets require calibration.
            candidate = null;
            candidateSamples = 0;
            return null;
        }

        string? next = Current switch
        {
            Phase.Idle when signals.IsBattle => "battle-late-attach",
            Phase.Idle when signals.IsLobby => "lobby-open",
            Phase.Lobby when signals.IsBattle => "battle-start",
            Phase.Lobby when !signals.IsLobby && signals.IsOverworld =>
                "lobby-cancelled",
            Phase.Battle when !signals.IsLobby => "battle-connection-ended",
            Phase.BattleConnectionEnded when signals.IsOverworld =>
                "returned-overworld",
            Phase.ReturnedToOverworld when signals.IsLobby => "new-lobby",
            Phase.ReturnedToOverworld when signals.IsBattle => "battle-late-attach",
            Phase.LobbyClosedWithoutBattle when signals.IsLobby => "new-lobby",
            Phase.LobbyClosedWithoutBattle when signals.IsBattle => "battle-late-attach",
            _ => null,
        };

        if (next is null)
        {
            candidate = null;
            candidateSamples = 0;
            return null;
        }
        if (candidate != next)
        {
            candidate = next;
            candidateSamples = 1;
            return null;
        }
        if (++candidateSamples < StableSamplesRequired)
            return null;

        candidate = null;
        candidateSamples = 0;
        var before = Current;
        Current = next switch
        {
            "lobby-open" or "new-lobby" => Phase.Lobby,
            "battle-start" or "battle-late-attach" => Phase.Battle,
            "battle-connection-ended" => Phase.BattleConnectionEnded,
            "returned-overworld" => Phase.ReturnedToOverworld,
            "lobby-cancelled" => Phase.LobbyClosedWithoutBattle,
            _ => before,
        };
        if (Current == Phase.Lobby)
            HasObservedLobby = true;
        return new Transition(before, Current, next,
            next is "lobby-open" or "new-lobby" or "battle-late-attach");
    }

    public void Reset()
    {
        Current = Phase.Idle;
        HasObservedLobby = false;
        candidate = null;
        candidateSamples = 0;
    }
}
