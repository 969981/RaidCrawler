using RaidCrawler.Core.Host;
using SysBot.Base;

namespace RaidCrawler.WinForms.SubForms;

/// <summary>Pure sys-botbase read-only validation of a candidate current-host RaidPoint.</summary>
public sealed class SysbotOnlyHostWindow : Form
{
    private readonly SysbotOnlyRaidPointInspector _inspector;
    private readonly HostRaidPointAutoLocator _autoLocator;
    private readonly CancellationTokenSource _close = new();
    private readonly TextBox _address = new();
    private readonly CheckBox _ownerMode = new();
    private readonly Button _inspect = new();
    private readonly Button _scan = new();
    private readonly Button _stopScan = new();
    private CancellationTokenSource? _scanCancel;
    private readonly ISwitchConnectionAsync _connection;
    private readonly TextBox _results = new();
    private readonly ComboBox _phase = new();
    private readonly Dictionary<string, SysbotOnlyCandidate> _phaseRecords = new();
    private bool _running;

    public SysbotOnlyHostWindow(ISwitchConnectionAsync connection)
    {
        _connection = connection;
        _inspector = new SysbotOnlyRaidPointInspector(connection);
        _autoLocator = new HostRaidPointAutoLocator(connection);
        Text = "方案 A：纯 sys-botbase 房主 RaidPoint 诊断";
        ClientSize = new Size(690, 570);
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;

        var information = new Label
        {
            Text = "无需安装额外 Switch 插件。定位默认只读；写入需进入独立实验窗口并二次确认。" +
                   " 尚未证明候选就是本局真实发包对象。",
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
        _scan.Text = "① 自动查找候选（只读）";
        _scan.Location = new Point(488, 105);
        _scan.Size = new Size(180, 28);
        _scan.Click += async (_, _) => await ScanAsync();
        _scan.BackColor = Color.FromArgb(42, 111, 198);
        _scan.ForeColor = Color.White;
        _scan.FlatStyle = FlatStyle.Flat;
        _stopScan.Text = "停止扫描";
        _stopScan.Location = new Point(520, 186);
        _stopScan.Size = new Size(148, 34);
        _stopScan.Enabled = false;
        _stopScan.Click += (_, _) =>
        {
            Log("用户请求取消只读扫描；正在等待当前sys-botbase内存命令结束。");
            _scanCancel?.Cancel();
            _stopScan.Enabled = false;
        };
        _inspect.Text = "② 验证找到的地址";
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
        var presetButton = new Button
        {
            Text = "截图奖励20种×999（实验）",
            Location = new Point(260, 186),
            Size = new Size(253, 34)
        };
        presetButton.Click += (_, _) =>
        {
            if (_running)
            {
                Log("请等待只读检索结束，再打开20种奖励预设。");
                return;
            }
            var owner = _ownerMode.Checked ? _address.Text : "";
            using var preset = new SysbotOnlyScreenshotPresetWindow(_connection, owner);
            preset.ShowDialog(this);
        };
        var quick500PresetButton = new Button
        {
            Text = "全体玩家 · 先机球×500（单行验证预设）",
            Location = new Point(14, 229),
            Size = new Size(654, 34)
        };
        quick500PresetButton.Click += (_, _) =>
        {
            if (_running)
            {
                Log("请等待当前只读扫描完成，再加载先机球×500预设。");
                return;
            }
            string owner = _ownerMode.Checked ? _address.Text : "";
            using var preset = new SysbotOnlyRewardWriteWindow(
                _connection, owner, useAllPlayersQuickBall500Preset: true);
            preset.ShowDialog(this);
        };
        _results.Multiline = true;
        _results.WordWrap = true;
        _results.ScrollBars = ScrollBars.Vertical;
        _results.ReadOnly = true;
        _results.Location = new Point(14, 276);
        _results.Size = new Size(654, 269);
        _results.Font = new Font(FontFamily.GenericMonospace, 9);
        Controls.AddRange([information, label, _address, _ownerMode, _inspect, _scan, _stopScan, phaseLabel, _phase, monitorButton, writeButton, presetButton, quick500PresetButton, _results]);
        FormClosed += (_, _) => { _scanCancel?.Cancel(); _close.Cancel(); };

        Log("操作顺序：先连接Switch → 点击①自动查找候选 → 有地址后点击②验证。");
        Log("地址栏为空时不要点②；不会自动拿 ELF 的 x0/x20 当实际指针。");
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
        _running = true;
        _inspect.Enabled = _scan.Enabled = false;
        _stopScan.Enabled = true;
        using var scanCts = CancellationTokenSource.CreateLinkedTokenSource(_close.Token);
        _scanCancel = scanCts;
        FastSysbotReadChannel? fastReader = null;
        string originalTitle = Text;
        try
        {
            Log("先尝试本次游戏已缓存的房主候选指针；首次定位再使用独立高速TCP读通道。");
            var direct = await _autoLocator.FindAsync(0, null, scanCts.Token);
            if (direct.Candidates.Count == 1)
            {
                var cached = direct.Candidates[0];
                _address.Text = $"0x{cached.OwnerAddress:X}";
                _ownerMode.Checked = true;
                Log($"无需扫描：重新验证本次游戏候选 Owner=0x{cached.OwnerAddress:X} " +
                    $"RaidPoint=0x{cached.RaidPointAddress:X} {cached.Name}，" +
                    $"SHA={cached.RewardsSha256}，类型={cached.OwnerTypeEvidence}。");
                Log("这仍不证明对象属于当前房间的真实发送链；点击②只读验证。");
                return;
            }
            Log("本次进程暂无可复核的候选指针；准备高速只读扫描。");
            Log("进度按KiB/百分比/速度显示；超时会自动停止，并拒绝使用不完整读取数据。");
            if (_connection is SwitchSocketAsync wifi)
            {
                try
                {
                    Log("先校准独立高速TCP只读通道：448B→4KiB→16KiB与原SysBot逐字节对照。");
                    ulong baseAddress = await _connection.GetMainNsoBaseAsync(scanCts.Token);
                    fastReader = await FastSysbotReadChannel.ConnectAndCalibrateAsync(
                        _connection, wifi.Name, 6000,
                        checked(baseAddress + SysbotOnlyRaidPointInspector.SerializerRva),
                        scanCts.Token);
                    Log($"高速TCP校准通过：{fastReader.CalibrationEvidence}，" +
                        $"本次每条读取最多{fastReader.CalibratedChunkSize}B。");
                }
                catch (OperationCanceledException) when (scanCts.IsCancellationRequested) { throw; }
                catch (Exception ex)
                {
                    Log("高速TCP校准失败：" + ex.Message +
                        "。没有启用不可信的高速数据，请检查 sys-botbase 当前版本。");
                    if (MessageBox.Show(this,
                        "高速只读通道验证失败。是否继续旧版慢速Wi-Fi读取？" +
                        "8MiB可能需要十几分钟。推荐先取消、检查连接与原版sys-botbase。",
                        "高速模式不可用", MessageBoxButtons.YesNo,
                        MessageBoxIcon.Warning) != DialogResult.Yes)
                        return;
                    if (fastReader is not null)
                    {
                        await fastReader.DisposeAsync();
                        fastReader = null;
                    }
                }
            }
            foreach (int sizeMiB in new[] { 8, 32, 64 })
            {
                long lastLoggedMs = -2500;
                var progress = new Progress<HostRaidPointScanProgress>(p =>
                {
                    if (IsDisposed) return;
                    if (p.ScannedBytes == 0)
                    {
                        Log(p.Stage);
                        Text = originalTitle + " — 检查连接和读取配置";
                        return;
                    }
                    double processedKiB = p.ScannedBytes / 1024.0;
                    double totalKiB = p.TotalMegabytes * 1024.0;
                    double percent = totalKiB > 0 ? processedKiB / totalKiB * 100.0 : 0;
                    double seconds = Math.Max(.001, p.ElapsedMilliseconds / 1000.0);
                    double speed = processedKiB / seconds;
                    double eta = speed > 0 ? (totalKiB - processedKiB) / speed : 0;
                    Text = $"{originalTitle} — {processedKiB:N0}/{totalKiB:N0} KiB {percent:F1}% | {speed:F0} KiB/s";
                    if (p.ElapsedMilliseconds - lastLoggedMs < 2200 && p.ScannedBytes < p.TotalMegabytes * 1024L * 1024L)
                        return;
                    lastLoggedMs = p.ElapsedMilliseconds;
                    Log($"扫描：{processedKiB:N0}/{totalKiB:N0} KiB ({percent:F1}%)，" +
                        $"速率约{speed:F0}KiB/s，预计剩余{eta:F0}秒；" +
                        $"候选={p.Candidates}，已读块={p.ReadableBlocks}，失败块={p.FailedBlocks}");
                });
                var result = await _autoLocator.FindAsync(sizeMiB, progress,
                    scanCts.Token, fastReader?.ReadBytesAbsoluteAsync);
                Log($"扫描 {sizeMiB}MiB 完成：结构候选 {result.Candidates.Count}，" +
                    $"可读块 {result.ReadableBlocks}，不可读块 {result.FailedBlocks}");
                foreach (var candidate in result.Candidates)
                    Log($"候选 Owner=0x{candidate.OwnerAddress:X} " +
                        $"RaidPoint=0x{candidate.RaidPointAddress:X} {candidate.Name} " +
                        $"虚表证据={candidate.OwnerTypeEvidence} " +
                        $"奖励可追加={candidate.OfflineAppendEligible}：{candidate.AppendPlanReason}");
                if (result.Candidates.Count > 0)
                {
                    var strong = result.Candidates.Where(c =>
                        c.OwnerTypeEvidence is
                            HostOwnerVtableEvidence.SerializerSlotPointsToE327FC or
                            HostOwnerVtableEvidence.DestructorVtableTriple).ToArray();
                    if (result.Candidates.Count == 1 && strong.Length == 1)
                    {
                        _address.Text = $"0x{strong[0].OwnerAddress:X}";
                        _ownerMode.Checked = true;
                        Log("已填入唯一类型匹配候选。仍需②验证、跨房间比对；不代表已证明当前发包对象。");
                    }
                    else
                        Log("无唯一高可信度候选，不自动选择写入目标；请保留日志供下一步分析。");
                    break;
                }
                if (sizeMiB == 64 ||
                    MessageBox.Show(this, "未发现完整结构，扩大只读扫描范围？",
                        "继续查找", MessageBoxButtons.YesNo,
                        MessageBoxIcon.Question) != DialogResult.Yes)
                    break;
            }
        }
        catch (OperationCanceledException) when (scanCts.IsCancellationRequested)
        {
            if (!_close.IsCancellationRequested)
                Log("扫描已取消，未执行任何写入。");
        }
        catch (Exception ex) { Log("结构扫描失败：" + ex.Message); }
        finally
        {
            if (fastReader is not null)
                await fastReader.DisposeAsync();
            _scanCancel = null;
            _running = false;
            if (!IsDisposed)
            {
                Text = originalTitle;
                _stopScan.Enabled = false;
                _inspect.Enabled = _scan.Enabled = true;
            }
        }
    }

    private async Task InspectAsync()
    {
        if (_running) return;
        if (string.IsNullOrWhiteSpace(_address.Text))
        {
            Log("未提供候选地址：请先点击①自动查找候选（只读）。这里只验证地址，不负责凭空生成地址。");
            return;
        }
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
