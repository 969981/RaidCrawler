using RaidCrawler.Core.Host;
using SysBot.Base;

namespace RaidCrawler.WinForms.SubForms;

/// <summary>
/// Passive, no-plugin host phase tracking based on hexbyt3 signals.
/// Every read uses the existing ISwitchConnectionAsync; no button presses,
/// no network listener, and absolutely no write to Switch RAM.
/// </summary>
public sealed class SvRaidAutoMonitorWindow : Form
{
    private readonly SvRaidLifecycleReader _reader;
    private readonly SysbotOnlyRaidPointInspector _inspector;
    private readonly CancellationTokenSource _closed = new();
    private readonly CheckBox _allowLegacy = new();
    private readonly TextBox _owner = new();
    private readonly CheckBox _ownerMode = new();
    private readonly TextBox _events = new();
    private readonly Button _startStop = new();
    private CancellationTokenSource? _watch;
    private SysbotOnlyCandidate? _previous;
    private int _phaseCount;

    public SvRaidAutoMonitorWindow(ISwitchConnectionAsync connection)
    {
        _reader = new SvRaidLifecycleReader(connection);
        _inspector = new SysbotOnlyRaidPointInspector(connection);
        Text = "纯 sys-botbase：房间状态自动识别（只读）";
        ClientSize = new Size(770, 555);
        StartPosition = FormStartPosition.CenterParent;
        MaximizeBox = false;
        FormBorderStyle = FormBorderStyle.FixedDialog;

        var intro = new Label
        {
            Location = new Point(15, 12), Size = new Size(735, 41),
            Text = "参照 hexbyt3/SVRaidBot 持续读取房间、玩家和战斗状态；" +
                   "只有野外指针有4.0.0公开实现依据，其他偏移仍需实机校准。"
        };
        _allowLegacy.Location = new Point(15, 59);
        _allowLegacy.Size = new Size(725, 27);
        _allowLegacy.Text = "实验启用 Hexbyt3 的 3.0.0 房间/战斗/NID 候选偏移（在《紫》4.0.0 未校准）";
        _allowLegacy.Checked = false;
        var label = new Label
        {
            Text = "候选地址（可留空）：",
            Location = new Point(15, 102), Size = new Size(164, 22)
        };
        _owner.Location = new Point(177, 97);
        _owner.Size = new Size(239, 27);
        _owner.PlaceholderText = "0x...（同一候选对象）";
        _ownerMode.Location = new Point(430, 98);
        _ownerMode.Size = new Size(310, 25);
        _ownerMode.Text = "对象起点（自动+0x58）";
        _ownerMode.Checked = true;

        _startStop.Text = "开始自动监测";
        _startStop.Location = new Point(15, 140);
        _startStop.Size = new Size(160, 31);
        _startStop.Click += async (_, _) => await ToggleAsync();

        _events.ReadOnly = true;
        _events.Multiline = true;
        _events.WordWrap = false;
        _events.ScrollBars = ScrollBars.Both;
        _events.Font = new Font(FontFamily.GenericMonospace, 9);
        _events.Location = new Point(15, 187);
        _events.Size = new Size(735, 349);
        Controls.AddRange([intro, _allowLegacy, label, _owner, _ownerMode, _startStop, _events]);
        FormClosed += (_, _) => { _watch?.Cancel(); _closed.Cancel(); };

        Append("默认只识别野外状态；S1/S2/S3/S4依赖未验证的旧版参考值，必须主动勾选。");
        Append("S3仅表示疑似离开房间与战斗之间的过渡，不代表全员已经准备。");
        Append("如果输入经虚表定位取得的候选对象地址，阶段变化时会自动读取并比较其奖励SHA。");
    }

    private void Append(string s)
    {
        if (!IsDisposed)
            _events.AppendText($"{DateTime.Now:HH:mm:ss} {s}{Environment.NewLine}");
    }

    private async Task ToggleAsync()
    {
        if (_watch is not null)
        {
            _watch.Cancel();
            return;
        }
        bool experimental = _allowLegacy.Checked;
        if (experimental && MessageBox.Show(this,
            "SVRaidBot公开偏移标记为3.0.0，本程序尚未在《紫》4.0.0上验证这些字段！" +
            "自动识别结果只代表实验候选信号，不作为奖励写入依据。确认开始只读采样？",
            "旧版状态地址尚未校准",
            MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes)
            return;
        ulong? candidate = null;
        if (!string.IsNullOrWhiteSpace(_owner.Text))
        {
            try { candidate = HostProbeMailbox.ParseAddress(_owner.Text); }
            catch (Exception e) { Append("候选地址无效：" + e.Message); return; }
        }

        using var tokenSource = CancellationTokenSource.CreateLinkedTokenSource(_closed.Token);
        _watch = tokenSource;
        _startStop.Text = "停止自动监测";
        _allowLegacy.Enabled = _owner.Enabled = _ownerMode.Enabled = false;
        _previous = null;
        _phaseCount = 0;
        try
        {
            await _reader.CheckViolet400Async(tokenSource.Token);
            var classifier = new SvRaidLifecycleClassifier();
            Append("自动只读监测已启动；轮询间隔1000毫秒。无插件、无写入、无手柄操作。");
            int polls = 0;
            while (!tokenSource.IsCancellationRequested)
            {
                polls++;
                // Recheck version periodically to fail closed on game changes/reboots.
                if (polls % 30 == 0)
                    await _reader.CheckViolet400Async(tokenSource.Token);
                SvRaidSignals signals = await _reader.ReadAsync(experimental, tokenSource.Token);
                if (signals.Warning is not null && polls % 20 == 1)
                    Append("部分信号未取得：" + signals.Warning);
                SvRaidPhaseEvent? evt = classifier.Observe(signals);
                if (evt is not null)
                {
                    _phaseCount++;
                    Append($"阶段#{_phaseCount}：{evt.Phase} " +
                           $"野外={signals.OverworldRaw?.ToString("X2") ?? "?"} " +
                           $"房间={signals.LobbyRaw?.ToString() ?? "?"} " +
                           $"战斗={signals.BattleRaw?.ToString() ?? "?"} " +
                           $"参与者槽数={signals.GuestCount?.ToString() ?? "?"}；" +
                           (evt.ExperimentalUnverified ? "[未经4.0.0校准] " : "[公开4.0.0参考] ") +
                           evt.Evidence);
                    if (candidate.HasValue)
                        await CaptureCandidateAsync(candidate.Value, _ownerMode.Checked,
                            evt.Phase, tokenSource.Token);
                }
                await Task.Delay(1000, tokenSource.Token);
            }
        }
        catch (OperationCanceledException) when (tokenSource.IsCancellationRequested) { }
        catch (Exception ex) { Append("已停止自动监测：" + ex.Message); }
        finally
        {
            _watch = null;
            if (!IsDisposed)
            {
                _startStop.Text = "开始自动监测";
                _allowLegacy.Enabled = _owner.Enabled = _ownerMode.Enabled = true;
                Append("本次自动观察完成：没有调用任何写入或手柄命令。");
            }
        }
    }

    private async Task CaptureCandidateAsync(ulong candidate, bool ownerMode,
        SvRaidPhase stage, CancellationToken token)
    {
        try
        {
            var view = await _inspector.InspectAsync(candidate, ownerMode, token);
            Append($"  {stage}: RaidPoint=0x{view.AbsoluteRaidPoint:X} " +
                   $"Name={view.RaidPointName} Count={view.ObservedOriginalRows} " +
                   $"Stable={view.SnapshotsStable} SHA256={view.RewardSha256}");
            if (_previous is not null)
            {
                Append($"  对上一阶段：地址变动={_previous.AbsoluteRaidPoint != view.AbsoluteRaidPoint} " +
                       $"奖励变化={_previous.RewardSha256 != view.RewardSha256} " +
                       $"行数 {_previous.ObservedOriginalRows}→{view.ObservedOriginalRows}");
            }
            Append("  上述变化不证明该对象属于本局实际发送者。");
            _previous = view;
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }
        catch (Exception e)
        {
            Append($"  {stage} 的候选快照失败：{e.Message}；不能确认房主对象。");
        }
    }
}
