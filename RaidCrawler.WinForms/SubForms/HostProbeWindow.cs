using RaidCrawler.Core.Host;
using SysBot.Base;
using System.Text;

namespace RaidCrawler.WinForms.SubForms;

/// <summary>
/// Main host route: serializer hook -> outgoing Bootstrap plaintext, no heap scan.
/// Default builds are read-only; experimental native builds may append one row.
/// </summary>
public sealed class HostProbeWindow : Form
{
    private readonly HostProbeMailboxClient _probe;
    private readonly HostBatchMailboxClient _batch;
    private readonly TextBox _address;
    private readonly TextBox _log;
    private readonly Button _statusButton;
    private readonly Button _autoButton;
    private readonly Button _startButton;
    private readonly Button _stopButton;
    private readonly Button _captureButton;
    private readonly Button _previewButton;
    private readonly Button _applyButton;
    private readonly NumericUpDown _quantity;
    private readonly Button _batchPreviewButton;
    private readonly CancellationTokenSource _closing = new();
    private bool _busy;

    public HostProbeWindow(ISwitchConnectionAsync connection)
    {
        _probe = new HostProbeMailboxClient(connection);
        _batch = new HostBatchMailboxClient(connection);
        Text = "RaidCrawler 房主 Bootstrap 发送副本（4.0.0）";
        ClientSize = new Size(660, 600);
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        StartPosition = FormStartPosition.CenterParent;

        var intro = new Label {
            Text = "无需堆扫描：已知 Hook 读取发送前副本；需要配对 Native 与 mailbox-locator.json。",
            Location = new Point(14, 14), Size = new Size(625, 38),
        };
        _address = new TextBox {
            Location = new Point(14, 54), Size = new Size(390, 26),
            PlaceholderText = "0x...（本次游戏进程的邮箱绝对地址）",
            MaxLength = 18,
        };
        _statusButton = MakeButton("读取状态", 414, 54, 108);
        _autoButton = MakeButton("自动定位", 526, 54, 112);
        _startButton = MakeButton("开始捕获", 14, 96, 142);
        _stopButton = MakeButton("停止捕获", 170, 96, 142);
        _captureButton = MakeButton("读取并比较", 326, 96, 196);
        _quantity = new NumericUpDown {
            Location = new Point(82, 140), Size = new Size(70, 27),
            Minimum = 1, Maximum = 10, Value = 1,
        };
        _previewButton = MakeButton("预览 Subject=5", 170, 140, 162);
        _applyButton = MakeButton("试验追加（需特殊构建）", 342, 140, 252);
        var qtyLabel = new Label {
            Text = "先机球×", Location = new Point(14, 143), Size = new Size(70, 26),
        };
        var rewardSettingsButton = MakeButton("默认奖励25种（自定义最多28）", 14, 184, 298);
        rewardSettingsButton.Click += (_, _) =>
        {
            using var editor = new HostRewardPresetWindow();
            editor.ShowDialog(this);
            Append("已关闭奖励设置：默认25种×999、最多28种；配置仅保存在本机，" +
                   "当前Native一次仅支持一行先机球×1–10，不能直接发送此25/28行列表。");
        };
        var capacityButton = MakeButton("空间预检（不写入）", 327, 184, 195);
        capacityButton.Click += async (_, _) => await Run(async (address, token) =>
        {
            string file = HostRewardPresetWindow.PresetPath;
            var profile = File.Exists(file)
                ? HostRewardPreset.FromJson(await File.ReadAllTextAsync(file, token))
                : HostRewardPreset.CreateDefault();
            var capture = await _probe.ReadCaptureAsync(address, token);
            var source = new byte[RaidPointLayout.RaidPointLength];
            var wire = new byte[RaidPointLayout.BootstrapLength];
            capture.Capture.SourceRows.CopyTo(source, RaidPointLayout.RewardsInRaidPoint);
            capture.Capture.BootstrapRows.CopyTo(wire, RaidPointLayout.RewardsInBootstrap);
            // Only 720B snapshots are available; reconstruct blank metadata for
            // OFFLINE reward capacity validation. No bag item allowlist is proven.
            var plan = HostRewardPreset.Plan(profile, source, wire, _ => true);
            return $"离线奖励空间预检：{profile.Items.Length} 种、原版 {plan.OriginalCount} 行，" +
                   $"结果 {plan.Reason}。只按捕获的720B计算，不验证背包合法性、" +
                   "当前Native多行能力或任何网络实际到账。";
        });
        _batchPreviewButton = MakeButton("Native只读预览：已保存的1–28条奖励", 14, 225, 420);
        _log = new TextBox {
            Location = new Point(14, 269), Size = new Size(625, 305),
            Multiline = true, ScrollBars = ScrollBars.Both, ReadOnly = true,
            WordWrap = false, Font = new Font(FontFamily.GenericMonospace, 9),
        };
        Controls.AddRange([intro, _address, _statusButton, _autoButton,
            _startButton, _stopButton, _captureButton, qtyLabel, _quantity,
            _previewButton, _applyButton, rewardSettingsButton, capacityButton,
            _batchPreviewButton, _log]);

        _batchPreviewButton.Click += async (_, _) => await Run(async (address, token) =>
        {
            string filename = HostRewardPresetWindow.PresetPath;
            HostRewardPresetProfile profile = File.Exists(filename)
                ? HostRewardPreset.FromJson(await File.ReadAllTextAsync(filename, token))
                : HostRewardPreset.CreateDefault();
            var queued = await _batch.QueueAsync(address, profile, apply: false, token);
            return $"已配置 Native v3 只读预览：{queued.Rows.Length} 条奖励，" +
                $"请求编号={queued.Nonce:X16}。请使用真实房主开启下一局团体战，" +
                "然后点击读取并比较。不会执行奖励写入。";
        });

        _autoButton.Click += async (_, _) =>
        {
            if (_busy) return;
            _busy = true;
            try
            {
                string filename = Path.Combine(AppContext.BaseDirectory, "mailbox-locator.json");
                string json = await File.ReadAllTextAsync(filename, _closing.Token);
                var locator = HostProbeLocator.ParseManifest(json);
                var found = await _probe.DiscoverAsync(locator, _closing.Token);
                if (!IsDisposed)
                {
                    _address.Text = $"0x{found.Address:X}";
                    Append($"已自动定位当前进程邮箱：0x{found.Address:X}；{Describe(found.Status)}");
                }
            }
            catch (OperationCanceledException) when (_closing.IsCancellationRequested) { }
            catch (Exception ex) { if (!IsDisposed) Append($"自动定位失败：{ex.Message}"); }
            finally { _busy = false; }
        };

        _statusButton.Click += async (_, _) => await Run(async (a, t) =>
        {
            HostProbeHeader h = await _probe.ReadStatusAsync(a, t);
            return $"当前状态：{Describe(h)}";
        });
        _startButton.Click += async (_, _) => await Run(async (a, t) =>
        {
            HostProbeHeader h = await _probe.SetEnabledAsync(a, true, t);
            return $"已开始捕获：{Describe(h)}";
        });
        _stopButton.Click += async (_, _) => await Run(async (a, t) =>
        {
            HostProbeHeader h = await _probe.SetEnabledAsync(a, false, t);
            return $"已停止捕获：{Describe(h)}";
        });
        _captureButton.Click += async (_, _) => await Run(async (a, t) =>
        {
            var result = await _batch.ReadConsistentAsync(a, t);
            HostCaptureComparison cmp = result.Capture.Compare();
            HostProbeResult n2 = result.Legacy;
            var inspection = n2.LastResult == 20
                ? Subject5AppendInspector.Inspect(result.Capture, n2)
                : null;
            string field = cmp.FirstMismatchField switch
            {
                nameof(RewardRow.SubjectType) => "奖励领取条件",
                nameof(RewardRow.ItemId) => "道具 ID",
                nameof(RewardRow.Quantity) => "数量",
                nameof(RewardRow.RareFlag) => "稀有标志",
                _ => "无",
            };
            string resultLabel = n2.LastResult switch
            {
                1 => "原版源数据与发送前明文一致",
                10 => "N2 预览通过（未修改游戏）",
                11 => "奖励区容量已满",
                12 => "原始奖励布局不符合安全要求",
                13 => "追加请求参数错误",
                14 => "源数据与发送前明文不一致",
                15 => "当前原生模块不允许修改奖励",
                20 => "只在本机输出明文追加一行（非到账证明）",
                30 => "Native批量奖励只读预览通过",
                31 => "当前空槽不足，整份批量奖励拒绝",
                32 => "批量奖励包含不符合规则的配置",
                33 => "批量请求校验失败",
                34 => "重复批量请求已拒绝",
                35 => "Native构建禁用批量写入",
                40 => "Native报告批量明文写入，仍未验证接收端",
                _ => "等待首次 Native 结果",
            };
            return $"N1 原版奖励捕获次数：{result.Header.CaptureCount} " +
                   $"N2 返回码：{n2.LastResult}（{resultLabel}）；追加次数：{n2.AppendedCount}；原版行数：{n2.OriginalRows}\r\n" +
                   (inspection is null ? "" : $"无损单行追加校验：{(inspection.Valid ? "通过" : "未通过")}（{inspection.Reason}）\r\n") +
                   $"发送副本差异：{(cmp.Matches ? "与源数据完全相同" : inspection?.Valid == true ? "仅有经校验的 subject=5 追加行" : "存在未核验的差异")} 首个差异行：{cmp.FirstMismatchRow} " +
                   $"差异字段：{field}\r\n" +
                   $"批量请求：{result.Batch.RequestedRows} 条；处理：{result.Batch.AppliedRows} 条；" +
                   $"请求编号={result.Batch.RequestNonce:X16}；结果编号={result.Batch.ResultNonce:X16}\r\n" +
                   $"源奖励 SHA256：{cmp.SourceRowsSha256}\r\n" +
                   $"发送前奖励 SHA256：{cmp.BootstrapRowsSha256}\r\n" +
                   "注意：这里只能证明两份捕获数据一致，不能证明参与者已收到奖励。";
        });
        _previewButton.Click += async (_, _) => await Run(async (a, t) =>
        {
            await _probe.ConfigureAppendAsync(a, 15, (uint)_quantity.Value, false, t);
            return "已配置只读追加预览；需要下一次真实房主 Bootstrap 才产生预览结果。";
        });
        _applyButton.Click += async (_, _) =>
        {
            var agree = MessageBox.Show(this,
                "仅供可恢复的本地测试 Switch 使用。N1 两端数据必须先实机验证；此操作会修改原版发送前奖励明文，" +
                "不保证到账或游戏稳定。确认尝试单行 Subject=5 吗？",
                "N2 实验警告", MessageBoxButtons.YesNo, MessageBoxIcon.Warning);
            if (agree != DialogResult.Yes) return;
            await Run(async (a, t) =>
            {
                await _probe.ConfigureAppendAsync(a, 15, (uint)_quantity.Value, true, t);
                return "试验追加请求已下发；将在下一次房主 Bootstrap 时处理。请检查返回码，不代表到账。";
            });
        };
        FormClosed += (_, _) => _closing.Cancel();
        Append("发送副本 Hook v3 默认只读。现支持对已保存的最多28条奖励配置进行原生只读预览；" +
               "不代表可从公开CI构建发放这些道具。");
    }

    private static Button MakeButton(string label, int x, int y, int width) =>
        new() { Text = label, Location = new Point(x, y), Size = new Size(width, 30) };

    private static string Describe(HostProbeHeader h) =>
        $"Hook状态={h.Status} 已启用={(h.Enabled ? "是" : "否")} N2写入能力={((h.Capabilities & 1) == 1 ? "是" : "否")} 捕获次数={h.CaptureCount} " +
        $"丢弃次数={h.DroppedCount} 版本序号={h.Generation} 时间刻度={h.Tick}";

    private void Append(string text) =>
        _log.AppendText($"{DateTime.Now:HH:mm:ss} {text}{Environment.NewLine}");

    private async Task Run(Func<ulong, CancellationToken, Task<string>> action)
    {
        if (_busy) return;
        _busy = true;
        _address.Enabled = false;
        _autoButton.Enabled = _statusButton.Enabled = _startButton.Enabled =
            _stopButton.Enabled = _captureButton.Enabled = _previewButton.Enabled =
            _applyButton.Enabled = _batchPreviewButton.Enabled = false;
        try
        {
            ulong address = HostProbeMailbox.ParseAddress(_address.Text);
            string result = await action(address, _closing.Token);
            if (!IsDisposed) Append(result);
        }
        catch (OperationCanceledException) when (_closing.IsCancellationRequested) { }
        catch (Exception ex) {
            if (!IsDisposed) Append($"错误：{ex.Message}");
        }
        finally
        {
            _busy = false;
            if (!IsDisposed) {
                _address.Enabled = true;
                _autoButton.Enabled = _statusButton.Enabled = _startButton.Enabled =
                    _stopButton.Enabled = _captureButton.Enabled = _previewButton.Enabled =
                    _applyButton.Enabled = _batchPreviewButton.Enabled = true;
            }
        }
    }
}
