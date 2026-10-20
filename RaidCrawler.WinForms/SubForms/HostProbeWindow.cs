using RaidCrawler.Core.Host;
using SysBot.Base;
using System.Text;

namespace RaidCrawler.WinForms.SubForms;

/// <summary>
/// Single sys-botbase-backed control panel for the experimental N1 mailbox.
/// No new socket/port; no file I/O on Switch and no game reward writes.
/// </summary>
public sealed class HostProbeWindow : Form
{
    private readonly HostProbeMailboxClient _probe;
    private readonly TextBox _address;
    private readonly TextBox _log;
    private readonly Button _statusButton;
    private readonly Button _startButton;
    private readonly Button _stopButton;
    private readonly Button _captureButton;
    private readonly CancellationTokenSource _closing = new();
    private bool _busy;

    public HostProbeWindow(ISwitchConnectionAsync connection)
    {
        _probe = new HostProbeMailboxClient(connection);
        Text = "RaidCrawler 房主探针（N1／只读）";
        ClientSize = new Size(660, 440);
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        StartPosition = FormStartPosition.CenterParent;

        var intro = new Label {
            Text = "仅支持《紫》4.0.0；请填入当前原生模块日志中的 [RC-N1] mailbox=0x... 地址。",
            Location = new Point(14, 14), Size = new Size(625, 38),
        };
        _address = new TextBox {
            Location = new Point(14, 54), Size = new Size(390, 26),
            PlaceholderText = "0x...（本次游戏进程的邮箱绝对地址）",
            MaxLength = 18,
        };
        _statusButton = MakeButton("读取状态", 414, 54, 108);
        _startButton = MakeButton("开始捕获", 14, 96, 142);
        _stopButton = MakeButton("停止捕获", 170, 96, 142);
        _captureButton = MakeButton("读取并比较", 326, 96, 196);
        _log = new TextBox {
            Location = new Point(14, 148), Size = new Size(625, 269),
            Multiline = true, ScrollBars = ScrollBars.Both, ReadOnly = true,
            WordWrap = false, Font = new Font(FontFamily.GenericMonospace, 9),
        };
        Controls.AddRange([intro, _address, _statusButton,
            _startButton, _stopButton, _captureButton, _log]);

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
            var result = await _probe.ReadCaptureAsync(a, t);
            HostCaptureComparison cmp = result.Capture.Compare();
            string field = cmp.FirstMismatchField switch
            {
                nameof(RewardRow.Marker) => "奖励条件标记",
                nameof(RewardRow.ItemId) => "道具 ID",
                nameof(RewardRow.Quantity) => "数量",
                nameof(RewardRow.Reserved) => "保留字段",
                _ => "无",
            };
            return $"N1 原版奖励捕获次数：{result.Header.CaptureCount} " +
                   $"数据一致：{(cmp.Matches ? "是" : "否")} 首个差异行：{cmp.FirstMismatchRow} " +
                   $"差异字段：{field}\r\n" +
                   $"源奖励 SHA256：{cmp.SourceRowsSha256}\r\n" +
                   $"发送前奖励 SHA256：{cmp.BootstrapRowsSha256}\r\n" +
                   "注意：这里只能证明两份捕获数据一致，不能证明参与者已收到奖励。";
        });
        FormClosed += (_, _) => _closing.Cancel();
        Append("探针默认禁用。此窗口只能读取快照和启停捕获，不能追加或修改游戏奖励。");
    }

    private static Button MakeButton(string label, int x, int y, int width) =>
        new() { Text = label, Location = new Point(x, y), Size = new Size(width, 30) };

    private static string Describe(HostProbeHeader h) =>
        $"Hook状态={h.Status} 已启用={(h.Enabled ? "是" : "否")} 捕获次数={h.CaptureCount} " +
        $"丢弃次数={h.DroppedCount} 版本序号={h.Generation} 时间刻度={h.Tick}";

    private void Append(string text) =>
        _log.AppendText($"{DateTime.Now:HH:mm:ss} {text}{Environment.NewLine}");

    private async Task Run(Func<ulong, CancellationToken, Task<string>> action)
    {
        if (_busy) return;
        _busy = true;
        _address.Enabled = false;
        _statusButton.Enabled = _startButton.Enabled =
            _stopButton.Enabled = _captureButton.Enabled = false;
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
                _statusButton.Enabled = _startButton.Enabled =
                    _stopButton.Enabled = _captureButton.Enabled = true;
            }
        }
    }
}
