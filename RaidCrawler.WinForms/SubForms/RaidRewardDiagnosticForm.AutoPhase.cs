using RaidCrawler.Core.Structures;

namespace RaidCrawler.WinForms.SubForms;

/// <summary>
/// Passive session monitor adapted from hexbyt3/SVRaidBot. It only reads memory:
/// no controller inputs, den edits, native hooks, or auto-bot battle actions.
/// The three offsets are candidate addresses requiring runtime calibration.
/// </summary>
public sealed partial class RaidRewardDiagnosticForm
{
    private readonly RaidPhaseObservationMachine autoPhase =
        new(stableSamplesRequired: 2);
    private readonly System.Windows.Forms.Timer autoPhaseTimer = new()
    {
        Interval = 2000,
    };
    private Button? autoPhaseToggle;
    private readonly Label phaseStatusLabel = new()
    {
        AutoSize = false, Width = 176, Height = 30,
        Text = "团战：等待自动监控", TextAlign = ContentAlignment.MiddleLeft,
        AutoEllipsis = true,
    };
    private bool autoPhaseBusy;
    private bool autoPhaseStarted;
    private int autoPhaseReadCount;
    private string? lastAutoSignal;
    private string? lastAutoError;

    private void ConfigureAutomaticRaidPhaseMonitor(
        FlowLayoutPanel primary, FlowLayoutPanel advanced)
    {
        primary.Controls.Add(phaseStatusLabel);
        autoPhaseToggle = new Button { Text = "停止自动阶段监控", AutoSize = true };
        advanced.Controls.Add(autoPhaseToggle);
        autoPhaseToggle.Click += async (_, _) =>
        {
            if (autoPhaseStarted)
            {
                StopAutomaticPhaseMonitor("USER_DISABLED");
            }
            else
            {
                await StartAutomaticPhaseMonitorAsync();
            }
        };
        autoPhaseTimer.Tick += async (_, _) => await PollAutomaticRaidPhaseAsync();
        // Initial start is opt-in to the diagnostic window itself, not a
        // command to modify the game; this performs only reads.
        Shown += async (_, _) =>
        {
            if (connection is not null && !closing)
            {
                await StartAutomaticPhaseMonitorAsync();
                // Status refresh follows the first phase read, not concurrently
                // using the same sys-botbase socket as initial calibration.
                await RefreshHookStatusOnOpenAsync();
            }
            else
                phaseStatusLabel.Text = "团战：未连接";
        };
    }

    private async Task StartAutomaticPhaseMonitorAsync()
    {
        if (closing || connection is null || autoPhaseStarted) return;
        if (remoteBusy)
        {
            Append("auto-phase-wait", "其他 Hook 操作正在进行，稍后再启动只读自动阶段监控。");
            return;
        }
        try
        {
            using var ct = CancellationTokenSource.CreateLinkedTokenSource(stop.Token);
            ct.CancelAfter(TimeSpan.FromSeconds(20));
            await VerifyViolet400IdentityAsync(ct.Token);
            autoPhase.Reset();
            autoPhaseReadCount = 0;
            lastAutoSignal = null;
            lastAutoError = null;
            autoPhaseStarted = true;
            phaseStatusLabel.Text = "团战：自动监控中";
            autoPhaseTimer.Start();
            if (autoPhaseToggle is not null)
                autoPhaseToggle.Text = "停止自动阶段监控";
            Append("auto-phase-start",
                "自动只读监控已启动：每 2 秒读取 SVRaidBot 候选房间/团战标志和 Overworld 指针；" +
                "连续两次一致才判定阶段。偏移语义须在本机验证，不能据此宣称领取/保存成功。",
                new {
                    source="HEXBYT3_SVRAIDBOT",
                    cadenceMs=autoPhaseTimer.Interval, debounceSamples=2,
                    version="Violet 4.0.0", runtimeCalibrated=false,
                    lobbyMainRva="0x43DF430", loadedMainRva="0x47D2020",
                    overworldPointer="0x473ADE0 -> 0x160 -> 0xE8 -> 0x28",
                    hookHit="NOT_OBSERVED", rewardSettlement="NOT_OBSERVED",
                    gameMemoryWrites=false
                });
            await PollAutomaticRaidPhaseAsync();
        }
        catch (OperationCanceledException)
        {
            if (!closing)
            {
                phaseStatusLabel.Text = "团战：状态读取超时";
                Append("auto-phase-start-failed", "身份验证超时：没有启动自动监控。");
            }
        }
        catch (Exception ex)
        {
            phaseStatusLabel.Text = "团战：监控启动失败";
            Append("auto-phase-start-failed",
                "无法确认《紫》4.0.0 运行环境：" + ex.GetType().Name +
                " / " + ex.Message);
        }
    }

    private void StopAutomaticPhaseMonitor(string reason)
    {
        autoPhaseStarted = false;
        autoPhaseTimer.Stop();
        if (!closing && !phaseStatusLabel.IsDisposed)
            phaseStatusLabel.Text = "团战：自动监控已停止";
        if (autoPhaseToggle is not null && !autoPhaseToggle.IsDisposed)
            autoPhaseToggle.Text = "开始自动阶段监控";
        if (!closing)
            Append("auto-phase-stop",
                "自动阶段监控已停止：" + reason,
                new { reason, readCount=autoPhaseReadCount,
                    lastPhase=autoPhase.Current.ToString(),
                    gameMemoryWrites=false });
    }

    private async Task PollAutomaticRaidPhaseAsync()
    {
        if (!autoPhaseStarted || autoPhaseBusy || remoteBusy || connection is null || closing)
            return;
        autoPhaseBusy = true;
        try
        {
            using var cancel = CancellationTokenSource.CreateLinkedTokenSource(stop.Token);
            cancel.CancelAfter(TimeSpan.FromSeconds(12));
            var token = cancel.Token;
            var result = await SvRaidBotPhaseSignals.ReadAsync(
                (rva, count, ct) => connection.ReadBytesMainAsync(rva, count, ct),
                (pointer, count, ct) => connection.PointerPeek(count, pointer, ct),
                token);
            autoPhaseReadCount++;

            // Process identity can change after a game restart; don't use
            // another process's values to finish the previous run.
            if (autoPhaseReadCount % 15 == 0)
                await VerifyViolet400IdentityAsync(token);

            var v = result.Signals;
            string signal = $"L={v.LobbyFlag?.ToString() ?? "?"}," +
                $"D={v.LoadedFlag?.ToString() ?? "?"}," +
                $"O={v.OverworldFlag?.ToString() ?? "?"}";
            if (signal != lastAutoSignal || autoPhaseReadCount % 30 == 0)
            {
                lastAutoSignal = signal;
                Append("auto-phase-flags",
                    $"SVRaidBot 候选状态 {signal}，阶段={autoPhase.Current}。" +
                    " 原始标志变化不等于结算或 Hook 命中。",
                    new {
                        runId=raidRunId, lobby=v.LobbyFlag,
                        loaded=v.LoadedFlag, overworld=v.OverworldFlag,
                        source=result.Source,
                        result.OverworldPointerAvailable,
                        runtimeCalibrated=false, phase=autoPhase.Current.ToString(),
                        nativeHookHit="NOT_OBSERVED",
                        rewardSettlement="NOT_OBSERVED",
                        gameMemoryWrites=false
                    });
            }
            lastAutoError = null;
            // The user's first live sample included L=255, D=1, O=17.
            // O=17 is the upstream overworld value; a simultaneous
            // nonzero lobby byte is contradictory, NOT an open lobby.
            if (v.OverworldFlag == 0x11 &&
                (v.LobbyFlag is > 0 || v.LoadedFlag == 2))
                phaseStatusLabel.Text = "团战：状态冲突，待校准";
            var t = autoPhase.Observe(v);
            if (t is not null)
                RecordAutomaticPhaseTransition(t,v);
        }
        catch (OperationCanceledException) when (closing || stop.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            string key = ex.GetType().Name + ": " + ex.Message;
            if (lastAutoError != key)
            {
                lastAutoError = key;
                Append("auto-phase-error", "自动只读阶段监控失败：" + key +
                    "。此次采样不会产生任何阶段转换。",
                    new { runId=raidRunId, errorType=ex.GetType().Name,
                        source="READ_ONLY_MONITOR", phase="UNOBSERVED" });
            }
            // Re-check title, version and base after a failure, rather than
            // interpreting communication failure as lobby=0.
        }
        finally { autoPhaseBusy = false; }
    }

    private void RecordAutomaticPhaseTransition(
        RaidPhaseObservationMachine.Transition transition,
        RaidPhaseObservationMachine.Signals raw)
    {
        if (transition.IsNewRaid)
        {
            integrityTimer.Stop();
            raidRunId = Guid.NewGuid().ToString("N");
            readOnlyBattleSamples = 0;
        }
        else if (raidRunId is null)
        {
            // A detached/started mid-session observation is still identifiable.
            raidRunId = Guid.NewGuid().ToString("N");
        }

        string stage = transition.To switch
        {
            RaidPhaseObservationMachine.Phase.Lobby => "auto-lobby-open",
            RaidPhaseObservationMachine.Phase.Battle => "auto-battle-entered",
            RaidPhaseObservationMachine.Phase.BattleConnectionEnded =>
                "auto-battle-connection-ended",
            RaidPhaseObservationMachine.Phase.ReturnedToOverworld =>
                "auto-overworld-returned",
            RaidPhaseObservationMachine.Phase.LobbyClosedWithoutBattle =>
                "auto-lobby-closed-no-battle",
            _ => "auto-phase-change"
        };
        phaseStatusLabel.Text = transition.To switch
        {
            RaidPhaseObservationMachine.Phase.Lobby => "团战：房间中（候选）",
            RaidPhaseObservationMachine.Phase.Battle => "团战：战斗中（候选）",
            RaidPhaseObservationMachine.Phase.BattleConnectionEnded => "团战：连接已结束",
            RaidPhaseObservationMachine.Phase.ReturnedToOverworld => "团战：已回到地图",
            RaidPhaseObservationMachine.Phase.LobbyClosedWithoutBattle => "团战：房间已关闭",
            _ => "团战：状态变化"
        };
        string human = transition.To switch
        {
            RaidPhaseObservationMachine.Phase.Lobby => "自动识别：团战房间状态出现",
            RaidPhaseObservationMachine.Phase.Battle => "自动识别：进入团战加载状态（loaded=2）",
            RaidPhaseObservationMachine.Phase.BattleConnectionEnded =>
                "自动识别：战斗房间连接已结束；不等于奖励已经领取",
            RaidPhaseObservationMachine.Phase.ReturnedToOverworld =>
                "自动识别：已返回野外；奖励结算及保存仍需独立确认",
            RaidPhaseObservationMachine.Phase.LobbyClosedWithoutBattle =>
                "自动识别：房间关闭，没有观察到战斗",
            _ => "自动阶段发生变化"
        };
        Append(stage, human + $"；run={raidRunId[..8]}；{Scenario}。",
            new {
                runId=raidRunId, scenario=Scenario, playerCharm=ConfirmedCharm,
                from=transition.From.ToString(), to=transition.To.ToString(),
                trigger=transition.Trigger, signalLobby=raw.LobbyFlag,
                signalLoaded=raw.LoadedFlag, signalOverworld=raw.OverworldFlag,
                source="SVRAIDBOT_RAM_FLAGS_INFERRED",
                runtimeCalibrated=false, debounceSamples=2,
                signalIsProofOfCallback=false, nativeHookHit="NOT_OBSERVED",
                rewardReceived="NOT_OBSERVED", saved="NOT_OBSERVED",
                gameMemoryWrites=false
            });

        if (transition.To == RaidPhaseObservationMachine.Phase.Battle)
        {
            // Reuse the existing integrity sampler that previously required
            // clicking "已开战". Automatic phase detection is read-only.
            integrityTimer.Start();
            Append("battle-monitor-start",
                "由自动阶段检测启动每 5 秒 Hook 只读快照。",
                new {runId=raidRunId, source="AUTO_PHASE", nativeHookHit="NOT_OBSERVED"});
        }
        if (transition.To is RaidPhaseObservationMachine.Phase.ReturnedToOverworld
            or RaidPhaseObservationMachine.Phase.LobbyClosedWithoutBattle)
        {
            integrityTimer.Stop();
            AppendEvidenceSummary();
        }
    }
}
