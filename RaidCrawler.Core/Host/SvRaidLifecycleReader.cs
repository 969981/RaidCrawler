using SysBot.Base;

namespace RaidCrawler.Core.Host;

/// <summary>
/// Passive state detector inspired by hexbyt3/SVRaidBot, NEVER copies its
/// 3.0.0 memory addresses as verified Violet 4.0.0 addresses.
/// No buttons, no game writes, no RAM hooks.
/// </summary>
public sealed class SvRaidLifecycleReader(ISwitchConnectionAsync connection)
{
    // Explicitly labeled as 4.0.0 in Eppin/Sysbot.NET,
    // SysBot.Pokemon/SV/Vision/PokeDataOffsetsSV.cs.
    // A matching memory read is still not evidence of the active host object.
    private static readonly long[] OverworldPointer400 = [0x473ADE0, 0x160, 0xE8, 0x28];

    // hexbyt3/SVRaidBot/main, PokeDataOffsetsSV.cs, marked 3.0.0.
    // ONLY sample if user explicitly consents to UNVERIFIED 4.0.0 probes.
    public const ulong LegacyLobbyMainRva = 0x043DF430;
    public const ulong LegacyBattleMainRva = 0x047D2020;
    private static readonly long[] LegacyNidSlotPointer = [0x475EA28, 0xF8, 0x8];

    public async Task CheckViolet400Async(CancellationToken token)
    {
        string tid = (await connection.GetTitleID(token).ConfigureAwait(false)).Trim()
            .Replace("0x", "", StringComparison.OrdinalIgnoreCase);
        string version = (await connection.GetGameInfo("version", token).ConfigureAwait(false))
            .Trim().TrimEnd('\0');
        if (!tid.Equals(HostProbeMailbox.ExpectedTitleId, StringComparison.OrdinalIgnoreCase)
            || version != "4.0.0")
            throw new InvalidOperationException(
                $"自动状态诊断只支持已审计的《紫》4.0.0：当前 Title={tid}, Version={version}。");
    }

    public async Task<SvRaidSignals> ReadAsync(bool allowUnverifiedOffsets, CancellationToken token)
    {
        // Read individual observations separately so one bad pointer never
        // becomes a fabricated "false" / "ready" stage.
        byte? overworld = null;
        byte? lobby = null;
        byte? battle = null;
        int? players = null;
        string? error = null;

        try
        {
            var b = await connection.PointerPeek(1, OverworldPointer400, token)
                .ConfigureAwait(false);
            if (b.Length == 1)
                overworld = b[0];
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }
        catch (Exception e) { error = $"野外状态读取失败：{e.Message}"; }

        if (allowUnverifiedOffsets)
        {
            try
            {
                var b = await connection.ReadBytesMainAsync(LegacyLobbyMainRva, 1, token)
                    .ConfigureAwait(false);
                if (b.Length == 1 && b[0] is 0 or 1)
                    lobby = b[0];
                else error = Append(error, "旧版房间候选标志超出0/1，拒绝解释");
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }
            catch (Exception e) { error = Append(error, $"旧版房间候选读取失败：{e.Message}"); }

            try
            {
                var b = await connection.ReadBytesMainAsync(LegacyBattleMainRva, 1, token)
                    .ConfigureAwait(false);
                if (b.Length == 1 && b[0] <= 7)
                    battle = b[0];
                else error = Append(error, "旧版战斗候选状态越界，拒绝解释");
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }
            catch (Exception e) { error = Append(error, $"旧版战斗候选读取失败：{e.Message}"); }

            try
            {
                // Three contiguous u64 participant slots. Count only; never
                // retain / display Nintendo network identifiers.
                var b = await connection.PointerPeek(24, LegacyNidSlotPointer, token)
                    .ConfigureAwait(false);
                if (b.Length == 24)
                {
                    players = 0;
                    for (int i = 0; i < 3; i++)
                        if (System.Buffers.Binary.BinaryPrimitives.ReadUInt64LittleEndian(
                                b.AsSpan(i * 8, 8)) != 0)
                            players++;
                }
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }
            catch (Exception e) { error = Append(error, $"旧版参与者槽候选读取失败：{e.Message}"); }
        }

        return new SvRaidSignals(overworld, lobby, battle, players, allowUnverifiedOffsets, error);
    }

    private static string Append(string? a, string b) =>
        string.IsNullOrEmpty(a) ? b : a + "；" + b;
}

public sealed record SvRaidSignals(
    byte? OverworldRaw,
    byte? LobbyRaw,
    byte? BattleRaw,
    int? GuestCount,
    bool IsUnverifiedLegacyProbe,
    string? Warning)
{
    public bool? IsOverworld => OverworldRaw.HasValue ? OverworldRaw == 0x11 : null;
    public bool? IsLobby => LobbyRaw.HasValue ? LobbyRaw != 0 : null;
    public bool? IsBattle => BattleRaw.HasValue ? BattleRaw == 0x02 : null;
}

/// <summary>
/// Debounced OBSERVATIONAL phase classifier. S3 is a transitional inference,
/// NOT a game-native all-players-ready flag. S1/S2/S4 are also provisional
/// while 3.0.0 memory offsets have not been calibrated on Violet 4.0.0.
/// </summary>
public enum SvRaidPhase
{
    Unknown,
    S0Overworld,
    S1Lobby,
    S2GuestsPresent,
    S3LeavingLobbyCandidate,
    S4RaidBattle
}

public sealed record SvRaidPhaseEvent(
    SvRaidPhase Phase, SvRaidSignals Signals,
    int StablePolls, bool ExperimentalUnverified,
    string Evidence);

public sealed class SvRaidLifecycleClassifier
{
    public SvRaidPhase Current { get; private set; } = SvRaidPhase.Unknown;
    private SvRaidPhase _pending = SvRaidPhase.Unknown;
    private int _consecutive;
    public const int RequiredStablePolls = 2;

    public SvRaidPhaseEvent? Observe(SvRaidSignals signals)
    {
        SvRaidPhase next = Classify(signals, Current);
        if (next == SvRaidPhase.Unknown)
        {
            _pending = SvRaidPhase.Unknown;
            _consecutive = 0;
            return null;
        }
        if (next == Current)
        {
            _pending = SvRaidPhase.Unknown;
            _consecutive = 0;
            return null;
        }
        if (next != _pending)
        {
            _pending = next;
            _consecutive = 1;
            return null;
        }
        _consecutive++;
        if (_consecutive < RequiredStablePolls)
            return null;
        Current = next;
        _pending = SvRaidPhase.Unknown;
        _consecutive = 0;
        return new SvRaidPhaseEvent(next, signals, RequiredStablePolls,
            signals.IsUnverifiedLegacyProbe && next != SvRaidPhase.S0Overworld,
            next switch
            {
                SvRaidPhase.S0Overworld => "4.0.0 外部来源野外状态指针，仍需实机观察",
                SvRaidPhase.S1Lobby => "Hexbyt3 3.0.0 房间状态偏移候选，未证实适用于4.0.0",
                SvRaidPhase.S2GuestsPresent => "Hexbyt3 3.0.0 参与者NID候选，非Ready信号",
                SvRaidPhase.S3LeavingLobbyCandidate => "推断过渡阶段，不代表全员准备或发送窗口",
                SvRaidPhase.S4RaidBattle => "Hexbyt3 3.0.0 战斗状态偏移候选，未证实适用于4.0.0",
                _ => "未知",
            });
    }

    public static SvRaidPhase Classify(SvRaidSignals s, SvRaidPhase previous)
    {
        if (s.IsUnverifiedLegacyProbe && s.IsBattle == true)
            return SvRaidPhase.S4RaidBattle;
        if (s.IsUnverifiedLegacyProbe && s.IsLobby == true)
            return s.GuestCount is > 0 ? SvRaidPhase.S2GuestsPresent : SvRaidPhase.S1Lobby;
        if (s.IsUnverifiedLegacyProbe && s.IsLobby == false &&
            s.IsBattle != true && s.IsOverworld != true &&
            previous is SvRaidPhase.S1Lobby or SvRaidPhase.S2GuestsPresent)
            return SvRaidPhase.S3LeavingLobbyCandidate;
        if (s.IsOverworld == true && s.IsLobby != true && s.IsBattle != true)
            return SvRaidPhase.S0Overworld;
        return SvRaidPhase.Unknown;
    }
}
