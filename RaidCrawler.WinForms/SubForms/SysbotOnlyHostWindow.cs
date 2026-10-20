using RaidCrawler.Core.Host;
using SysBot.Base;

namespace RaidCrawler.WinForms.SubForms;

/// <summary>Pure sys-botbase read-only validation of a candidate current-host RaidPoint.</summary>
public sealed class SysbotOnlyHostWindow : Form
{
    private readonly SysbotOnlyRaidPointInspector _inspector;
    private readonly CancellationTokenSource _close = new();
    private readonly TextBox _address = new();
    private readonly CheckBox _ownerMode = new();
    private readonly Button _inspect = new();
    private readonly Button _scan = new();
    private readonly ISwitchConnectionAsync _connection;
    private readonly TextBox _results = new();
    private readonly ComboBox _phase = new();
    private readonly Dictionary<string, SysbotOnlyCandidate> _phaseRecords = new();
    private bool _running;

    public SysbotOnlyHostWindow(ISwitchConnectionAsync connection)
    {
        _connection = connection;
        _inspector = new SysbotOnlyRaidPointInspector(connection);
        Text = "方案 A：纯 sys-botbase 房主 RaidPoint 诊断";
        ClientSize = new Size(690, 570);
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;

        var information = new Label
        {
            Text = "不需要 subsdk9 或额外插件；只读检查内存候选地址，不修改游戏。" +
                   "当前尚未证明能自动定位本局房主对象。",
            Location = new Point(14, 12), Size = new Size(654, 42)
        };
        var label = new Label
        {
            Text = "候选绝对地址（16进制）：",
            Location = new Point(14, 69), Size = new Size(188, 24)
        };
        _address.Location = new Point(206, 65);
        _address.Size = new Size(264, 27);
        _address.PlaceholderText = "0x...（需要真实内存来源）";
        _ownerMode.Text = "地址指向序列化输入对象 x0（自动 +0x58）";
        _ownerMode.Checked = true;
        _ownerMode.Location = new Point(14, 107);
        _ownerMode.Size = new Size(455, 25);
        _scan.Text = "虚表定位（只读）";
        _scan.Location = new Point(488, 105);
        _scan.Size = new Size(180, 28);
        _scan.Click += async (_, _) => await ScanAsync();
        _inspect.Text = "只读验证 / 离线预览";
        _inspect.Location = new Point(487, 64);
        _inspect.Size = new Size(181, 31);
        _inspect.Click += async (_, _) => await InspectAsync();
        var phaseLabel = new Label
        {
            Text = "记录阶段：", Location = new Point(14, 153), Size = new Size(94, 26)
        };
        _phase.DropDownStyle = ComboBoxStyle.DropDownList;
        _phase.Location = new Point(104, 149);
        _phase.Size = new Size(420, 27);
        _phase.Items.AddRange([
            "S0 开房前", "S1 房主开房后", "S2 参与者加入后",
            "S3 准备开战前", "S4 开战之后（只读）"]);
        _phase.SelectedIndex = 0;
        var monitorButton = new Button
        {
            Text = "自动状态监测", Location = new Point(530, 147),
            Size = new Size(138, 28)
        };
        monitorButton.Click += (_, _) =>
        {
            if (_running)
            {
                Log("请等待当前扫描结束，再打开自动监测。");
                return;
            }
            using var monitor = new SvRaidAutoMonitorWindow(_connection);
            monitor.ShowDialog(this);
        };
        var writeButton = new Button
        {
            Text = "受控奖励写入（实验）",
            Location = new Point(14, 186),
            Size = new Size(228, 34)
        };
        writeButton.Click += (_, _) =>
        {
            if (_running)
            {
                Log("请等待当前只读检索完成，再进入实验写入窗口。");
                return;
            }
            var candidate = _ownerMode.Checked ? _address.Text : "";
            using var writer = new SysbotOnlyRewardWriteWindow(_connection, candidate);
            writer.ShowDialog(this);
        };
        _results.Multiline = true;
        _results.WordWrap = true;
        _results.ScrollBars = ScrollBars.Vertical;
        _results.ReadOnly = true;
        _results.Location = new Point(14, 231);
        _results.Size = new Size(654, 314);
        _results.Font = new Font(FontFamily.GenericMonospace, 9);
        Controls.AddRange([information, label, _address, _ownerMode, _inspect, _scan, phaseLabel, _phase, monitorButton, writeButton, _results]);
        FormClosed += (_, _) => _close.Cancel();

        Log("默认只读；额外的受控写入实验需要独立确认，且不能证明实际发送。");
        Log("必须先有可证实的绝对地址；ELF 中的 x20 / x0 不是固定指针。");
        Log("原有 RaidBlockPointerBase 指向存档太晶坑块，不能直接当作当前房主 RaidPoint。");
        Log("读取结果相同只证明这份候选内存稳定，不证明即将发送或能安全写入。");
    }

    private void Log(string message)
    {
        if (!IsDisposed)
            _results.AppendText(message + Environment.NewLine);
    }

    private async Task ScanAsync()
    {
        if (_running) return;
        if (MessageBox.Show(this,
            "仅用现有 sys-botbase 扫描堆前 1 MiB，查找具有 ELF 三张虚表指针的房主序列化类实例。" +
            "找到相符的对象也不能证明该对象属于当前房间。请在非开战时操作，是否继续？",
            "方案 A：只读扫描确认", MessageBoxButtons.YesNo,
            MessageBoxIcon.Information) != DialogResult.Yes)
            return;
        _running = true;
        _inspect.Enabled = false;
        _scan.Enabled = false;
        try
        {
            var candidates = await _inspector.ScanSerializerObjectsAsync(1, _close.Token);
            Log($"只读类型扫描结束，发现 {candidates.Count} 个虚表和RaidPoint同时匹配的候选对象。");
            foreach (var result in candidates)
                Log($"  对象0x{result.OwnerAddress:X} RaidPoint0x{result.RaidPointAddress:X} / {result.Name} / 原奖励行{result.OriginalRows}：只确认对象类型，不证明正在发包。");
            if (candidates.Count == 1)
            {
                _address.Text = $"0x{candidates[0].OwnerAddress:X}";
                _ownerMode.Checked = true;
                Log("已填写唯一虚表匹配的对象地址；请再点击只读验证。它仍可能是未使用的旧对象。");
            }
            if (candidates.Count == 0)
                Log("本次堆起始1 MiB没有匹配对象：可能位于其他堆段、仅短暂存活，或活跃派生类使用其他虚表。不能据此否定方案 A。");
        }
        catch (OperationCanceledException) when (_close.IsCancellationRequested) { }
        catch (Exception ex) { Log("扫描未完成：" + ex.Message); }
        finally
        {
            _running = false;
            if (!IsDisposed) _inspect.Enabled = _scan.Enabled = true;
        }
    }

    private async Task InspectAsync()
    {
        if (_running) return;
        _running = true;
        _inspect.Enabled = false;
        _scan.Enabled = false;
        try
        {
            ulong address = HostProbeMailbox.ParseAddress(_address.Text);
            var result = await _inspector.InspectAsync(address, _ownerMode.Checked, _close.Token);
            Log($"RaidPoint地址：0x{result.AbsoluteRaidPoint:X}");
            Log($"对象名称：{result.RaidPointName}");
            Log($"连续读取稳定：{(result.SnapshotsStable ? "是" : "否")}");
            Log($"已识别原奖励行：{result.ObservedOriginalRows}；候选空行：{result.PossibleEmptyRows}");
            Log($"仅离线预览 Subject=5 一行：{(result.OfflineAppendEligible ? "通过" : "不通过")} ({result.Reason})");
            Log($"奖励 SHA-256：{result.RewardSha256}");
            string phase = _phase.SelectedItem?.ToString() ?? "未知阶段";
            var prior = _phaseRecords.LastOrDefault(x => x.Key != phase);
            if (prior.Value is not null)
            {
                Log($"与前一记录 [{prior.Key}] 比较：同一地址={prior.Value.AbsoluteRaidPoint == result.AbsoluteRaidPoint}；" +
                    $"奖励一致={prior.Value.RewardSha256 == result.RewardSha256}；" +
                    $"行数 {prior.Value.ObservedOriginalRows}→{result.ObservedOriginalRows}");
            }
            _phaseRecords[phase] = result;
            Log($"已记录 {phase}，已采集 {_phaseRecords.Count} 个阶段。只显示对象变化，不能证明发送链。");
            Log($"证据限制：{result.EvidenceLimit}");
            Log("没有执行任何 poke/pointerPoke/freeze 命令。");
        }
        catch (OperationCanceledException) when (_close.IsCancellationRequested) { }
        catch (Exception ex)
        {
            Log("验证失败：" + ex.Message);
        }
        finally
        {
            _running = false;
            if (!IsDisposed) _inspect.Enabled = _scan.Enabled = true;
        }
    }
}
