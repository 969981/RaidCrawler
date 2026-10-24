using RaidCrawler.Core.LiveRewardV4;
using SysBot.Base;
using System.Globalization;

namespace RaidCrawler.WinForms.SubForms;

/// <summary>
/// V4 N1 research control: publishes ONLY mailbox profiles, NEVER game rewards.
/// Requires separately installed, matched readonly subsdk9 and locator manifest.
/// </summary>
public sealed class LiveRewardV4Window : Form
{
    private readonly ISwitchConnectionAsync _connection;
    private readonly IMailboxV4Transport _transport;
    private readonly MailboxV4Client _mailbox;
    private readonly MailboxV4ObservationReader _observer;
    private readonly CancellationTokenSource _closing = new();
    private readonly DataGridView _rows = new();
    private readonly CheckBox _enabled = new();
    private readonly Label _state = new();
    private readonly TextBox _log = new();
    private ulong _address;
    private bool _busy;

    public LiveRewardV4Window(ISwitchConnectionAsync connection)
    {
        _connection = connection;
        _transport = new SysBotMailboxV4Transport(connection);
        _mailbox = new MailboxV4Client(_transport);
        _observer = new MailboxV4ObservationReader(_transport);
        Text = "SV Raid V4 · N1 只读热配置（不会修改游戏奖励）";
        StartPosition = FormStartPosition.CenterParent;
        ClientSize = new Size(760, 690);
        MinimumSize = new Size(760, 690);

        var notice = new Label {
            Left = 14, Top = 12, Width = 730, Height = 46,
            Text = "仅支持《紫》4.0.0 + 精确配对的只读 subsdk9。提交操作只写入 Native 配置邮箱，" +
                   "不会更改 RaidPoint、Bootstrap 或联网道具；观察成功不等于发奖。" };
        _enabled.Left = 14; _enabled.Top = 65; _enabled.Width = 160;
        _enabled.Text = "启用配置（只读）"; _enabled.Checked = true;
        var policy = new Label {
            Left = 190, Top = 68, Width = 520, Height = 24,
            Text = "模式：无损追加  |  默认数量：999  |  生效方式：持续观察" };

        _rows.Left = 14; _rows.Top = 102;
        _rows.Width = 730; _rows.Height = 315;
        _rows.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
        _rows.AllowUserToAddRows = false;
        _rows.AllowUserToDeleteRows = false;
        _rows.RowHeadersVisible = false;
        _rows.MultiSelect = true;
        _rows.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
        _rows.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
        _rows.Columns.Add(new DataGridViewTextBoxColumn {
            HeaderText = "道具 ID (1–65535)", Name = "ItemId" });
        _rows.Columns.Add(new DataGridViewTextBoxColumn {
            HeaderText = "数量 (1–999)", Name = "Quantity" });
        _rows.Rows.Add("492", "999"); // visible example, user must actively publish

        AddButton("增加道具", 14, 426, 105, () => {
            _rows.EndEdit();
            if (_rows.Rows.Count >= 45) return;
            int i = _rows.Rows.Add("", "999");
            _rows.CurrentCell = _rows.Rows[i].Cells[0];
            _rows.BeginEdit(true);
        });
        AddButton("移除选中", 129, 426, 105, () => {
            foreach (var i in _rows.SelectedRows.Cast<DataGridViewRow>()
                .Select(r => r.Index).OrderDescending())
                _rows.Rows.RemoveAt(i);
        });
        AddButton("自动定位 N1", 14, 471, 150, async () => {
            string path = Path.Combine(AppContext.BaseDirectory, "mailbox-v4-locator.json");
            var manifest = MailboxV4Locator.ParseManifest(
                await File.ReadAllTextAsync(path, _closing.Token));
            var result = await MailboxV4Locator.DiscoverAsync(
                _connection, manifest, _closing.Token);
            _address = result.Address;
            return $"已定位 N1 邮箱：0x{_address:X}；只读能力={result.Header.Capabilities}。";
        });
        AddButton("发布只读配置", 178, 471, 150, async () => {
            RequireMailbox();
            _rows.EndEdit();
            var selected = new List<RewardRowV4>();
            foreach (DataGridViewRow row in _rows.Rows)
            {
                var idText = Convert.ToString(row.Cells[0].Value, CultureInfo.InvariantCulture);
                var qtyText = Convert.ToString(row.Cells[1].Value, CultureInfo.InvariantCulture);
                if (!uint.TryParse(idText, out uint item) || !uint.TryParse(qtyText, out uint qty))
                    throw new InvalidDataException($"第 {row.Index+1} 行道具ID/数量无效");
                selected.Add(new RewardRowV4(item, qty, 0, 5));
            }
            var config = new RewardConfigV4(_enabled.Checked, RewardModeV4.Append,
                RewardApplyPolicyV4.Persistent, selected);
            var receipt = await _mailbox.PublishAsync(_address, config, _closing.Token);
            return $"配置已发布 Revision={receipt.Revision}，等待 Native 下次只读观察。不会修改奖励。";
        });
        AddButton("读取 Native 观察", 342, 471, 170, async () => {
            RequireMailbox();
            var report = await _observer.ReadAsync(_address, _closing.Token);
            return $"调用记录={report.CaptureSerial}，上下文状态={report.ContextResult}，" +
                $"已发布/已观察 Revision={report.PublishedRevision}/{report.ObservedRevision}，" +
                $"源/输出 FNV={report.SourceRewardsFnv32:X8}/{report.OutgoingRewardsFnv32:X8}，" +
                $"奖励区一致={report.RewardHashesMatch}，配置观察成功={report.ConfigurationObserved}。" +
                "以上均不是联网实际到账证明。";
        });
        AddButton("刷新状态", 527, 471, 125, async () => {
            RequireMailbox();
            var h = await _mailbox.ReadHeaderAsync(_address, _closing.Token);
            return $"N1 Ready={h.Status == 1}，Slot={h.ActiveSlot}，" +
                $"Published={h.PublishedRevision}，Observed={h.ObservedRevision}，观察次数={h.ObservationCount}。";
        });

        _state.Left = 14; _state.Top = 518; _state.Width = 730; _state.Height = 30;
        _state.Text = "尚未定位 Native v4 邮箱；仅对配对只读 subsdk9 开放配置。";
        _log.Left = 14; _log.Top = 552; _log.Width = 730; _log.Height = 120;
        _log.Multiline = true; _log.ReadOnly = true; _log.ScrollBars = ScrollBars.Vertical;
        _log.Font = new Font(FontFamily.GenericMonospace, 9);
        Controls.AddRange([notice, policy, _enabled, _rows, _state, _log]);
        FormClosed += (_, _) => _closing.Cancel();
    }

    private void RequireMailbox()
    {
        if (_address < 0x10000)
            throw new InvalidOperationException("必须先成功自动定位只读 N1 邮箱。");
    }

    private void AddButton(string title, int x, int y, int width, Action onClick)
    {
        var btn = new Button { Text = title, Left = x, Top = y, Width = width, Height = 30 };
        btn.Click += (_, _) => Execute(() => { onClick(); return Task.FromResult(""); });
        Controls.Add(btn);
    }

    private void AddButton(string title, int x, int y, int width, Func<Task<string>> operation)
    {
        var btn = new Button { Text = title, Left = x, Top = y, Width = width, Height = 30 };
        btn.Click += async (_, _) => await ExecuteAsync(operation);
        Controls.Add(btn);
    }

    private void Execute(Func<Task<string>> action) => _ = ExecuteAsync(action);

    private async Task ExecuteAsync(Func<Task<string>> action)
    {
        if (_busy || _closing.IsCancellationRequested) return;
        _busy = true;
        try
        {
            string text = await action();
            if (text.Length > 0) Append(text);
        }
        catch (OperationCanceledException) when (_closing.IsCancellationRequested) { }
        catch (Exception ex) { Append("失败：" + ex.Message); }
        finally { _busy = false; }
    }
    private void Append(string line)
    {
        if (IsDisposed) return;
        _state.Text = line.Length > 95 ? line[..95] + "…" : line;
        _log.AppendText($"{DateTime.Now:HH:mm:ss} {line}{Environment.NewLine}");
    }
}
