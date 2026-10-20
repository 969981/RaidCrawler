using RaidCrawler.Core.Host;
using SysBot.Base;

namespace RaidCrawler.WinForms.SubForms;

/// <summary>
/// User-supplied 20×999 preset. Stock sys-botbase is the only Switch transport.
/// UI is explicitly opt-in; no network delivery/actual item quantity guaranteed.
/// </summary>
public sealed class SysbotOnlyScreenshotPresetWindow : Form
{
    private readonly SysbotOnlyScreenshotPresetWriter _writer;
    private readonly CancellationTokenSource _closed = new();
    private readonly TextBox _ownerAddress = new();
    private readonly CheckBox _confirmTestDevice = new();
    private readonly CheckBox _confirmLimits = new();
    private readonly ComboBox _subjectMode = new();
    private readonly Button _prepare = new();
    private readonly Button _apply = new();
    private readonly Button _restore = new();
    private readonly TextBox _log = new();
    private HostScreenshotPresetPermit? _permit;
    private bool _busy;

    public SysbotOnlyScreenshotPresetWindow(ISwitchConnectionAsync connection, string? owner = null)
    {
        _writer = new SysbotOnlyScreenshotPresetWriter(connection);
        Text = "截图奖励预设：20种 ×999（方案A内存实验）";
        ClientSize = new Size(770, 740);
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;

        var intro = new Label
        {
            Text = "仅《紫》4.0.0 + 标准 sys-botbase。全体玩家 subject=0 / 晶耀护符 subject=5；" +
                   "均需要原奖励≤25行、连续空槽≥20。×999实际到账尚未验证。",
            Location = new Point(16, 14), Size = new Size(730, 50),
        };
        _subjectMode.DropDownStyle = ComboBoxStyle.DropDownList;
        _subjectMode.Location = new Point(16, 74);
        _subjectMode.Size = new Size(342, 28);
        _subjectMode.Items.AddRange([
            "仅持有晶耀护符（subject=5）",
            "全体玩家，无须护符（subject=0）"]);
        _subjectMode.SelectedIndex = 0; // keep previous conservative default
        _subjectMode.SelectedIndexChanged += (_, _) =>
        {
            // Changing the recipient selector invalidates an already prepared
            // exact-byte transaction; old permit must never be reused.
            _permit = null;
            _apply.Enabled = false;
            _restore.Enabled = false;
            Say("领取对象已变更，请重新只读准备。");
        };
        var addressLabel = new Label
        {
            Text = "房主序列化对象绝对地址（不是 RaidPoint 起点）：",
            Location = new Point(16, 109), Size = new Size(365, 26)
        };
        _ownerAddress.Location = new Point(396, 105);
        _ownerAddress.Size = new Size(353, 27);
        _ownerAddress.PlaceholderText = "0x...（需先虚表定位）";
        _ownerAddress.Text = owner ?? "";

        var items = new TextBox
        {
            ReadOnly = true, Multiline = true, ScrollBars = ScrollBars.Vertical,
            Location = new Point(16, 146), Size = new Size(732, 238),
            Font = new Font(FontFamily.GenericMonospace, 9),
        };
        items.Lines = ScreenshotRewardPreset.Items.Select((it, i) =>
            $"{i + 1,2}. {it.Name,-10} ×999   ItemId={it.ItemId,4}").ToArray();

        _confirmTestDevice.Location = new Point(16, 399);
        _confirmTestDevice.Size = new Size(720, 29);
        _confirmTestDevice.Text = "确认：自己的可恢复测试 Switch、《紫》4.0.0，游戏和存档已备份";
        _confirmLimits.Location = new Point(16, 435);
        _confirmLimits.Size = new Size(720, 50);
        _confirmLimits.Text = "理解：subject=0 仅改变本次候选奖励的条件字段，不证明实际发送；" +
                              "20×999可能被重算、截断或超出背包上限";

        _prepare.Text = "1. 只读准备20条";
        _prepare.Location = new Point(16, 494);
        _prepare.Size = new Size(210, 36);
        _apply.Text = "2. 写入全部20条（实验）";
        _apply.Location = new Point(241, 494);
        _apply.Size = new Size(247, 36);
        _apply.Enabled = false;
        _restore.Text = "3. 安全恢复20条";
        _restore.Location = new Point(504, 494);
        _restore.Size = new Size(244, 36);
        _restore.Enabled = false;
        _prepare.Click += async (_, _) => await PrepareAsync();
        _apply.Click += async (_, _) => await ApplyAsync();
        _restore.Click += async (_, _) => await RestoreAsync();

        _log.Multiline = true;
        _log.ReadOnly = true;
        _log.ScrollBars = ScrollBars.Vertical;
        _log.Location = new Point(16, 550);
        _log.Size = new Size(732, 171);
        _log.Font = new Font(FontFamily.GenericMonospace, 9);

        Controls.AddRange([intro, _subjectMode, addressLabel, _ownerAddress, items,
            _confirmTestDevice, _confirmLimits, _prepare, _apply, _restore, _log]);
        FormClosed += (_, _) => _closed.Cancel();
        Say("预设已装载：20种各999，合计20条、320字节；没有执行Switch写入。");
    }

    private void Say(string text)
    {
        if (!IsDisposed) _log.AppendText($"{DateTime.Now:HH:mm:ss} {text}{Environment.NewLine}");
    }

    private bool Confirmed()
    {
        if (_confirmTestDevice.Checked && _confirmLimits.Checked) return true;
        Say("需要确认测试环境及原始奖励/999数量未验证的风险。");
        return false;
    }

    private async Task PrepareAsync()
    {
        if (_busy || !Confirmed()) return;
        await Guarded(async () =>
        {
            _permit = null;
            _apply.Enabled = _restore.Enabled = false;
            ulong owner = HostProbeMailbox.ParseAddress(_ownerAddress.Text);
            uint subject = _subjectMode.SelectedIndex == 1
                ? ScreenshotRewardPreset.EveryoneSubject : ScreenshotRewardPreset.Subject;
            var permit = await _writer.PrepareAsync(owner, subject, _closed.Token);
            _permit = permit;
            _apply.Enabled = true;
            Say($"本次设置：subject={permit.SubjectType}（{(permit.SubjectType == 0 ? "全体玩家" : "仅晶耀护符")}）；原有{permit.OriginalRows}条，追加20条后合计{permit.OriginalRows + 20}/45。");
            Say($"候选={permit.RaidPointName}，Owner=0x{permit.OwnerAddress:X}，RaidPoint=0x{permit.RaidPointAddress:X}");
            Say($"只会写空槽偏移 0x{permit.StartOffset:X} 起320字节；原始SHA={permit.OriginalSha256}");
            Say("已准备，但尚未写入；凭证15秒有效。");
        });
    }

    private async Task ApplyAsync()
    {
        if (_busy || _permit is null || !Confirmed()) return;
        var permit = _permit;
        if (MessageBox.Show(this,
            $"确认通过 sys-botbase 将截图的20种道具各×999写入候选对象的20条空槽？\n" +
            $"领取对象：{(permit.SubjectType == 0 ? "全体玩家（subject=0）" : "晶耀护符玩家（subject=5）")}。\n" +
            $"原版奖励{permit.OriginalRows}条将保留。\n" +
            "这是未通过实机发奖验证的内存实验，不能保证参与者收到或获得999；" +
            "必须确保是可恢复测试机，不要在公共房间试验。",
            "最后确认：20条×999", MessageBoxButtons.YesNo,
            MessageBoxIcon.Warning) != DialogResult.Yes)
            return;
        await Guarded(async () =>
        {
            _apply.Enabled = false;
            _permit = null; // single use, including failures
            var result = await _writer.ApplyAsync(permit, _closed.Token);
            Say($"结果：{result.Code}，内存位置0x{result.RowAddress:X}");
            Say(result.Explanation);
            if (result.Verified)
            {
                _permit = permit;
                _restore.Enabled = true;
            }
        });
    }

    private async Task RestoreAsync()
    {
        if (_busy || _permit is null) return;
        var permit = _permit;
        if (MessageBox.Show(this,
            "只有当前整个0x3E8对象与写入后完全一致才尝试恢复20条原始空槽；" +
            "不能撤回此前可能发出的网络数据。确定？",
            "尝试恢复测试 RAM", MessageBoxButtons.YesNo,
            MessageBoxIcon.Warning) != DialogResult.Yes)
            return;
        await Guarded(async () =>
        {
            _permit = null;
            _restore.Enabled = false;
            var result = await _writer.RestoreAsync(permit, _closed.Token);
            Say($"恢复：{result.Code}。{result.Explanation}");
        });
    }

    private async Task Guarded(Func<Task> operation)
    {
        if (_busy) return;
        _busy = true;
        _prepare.Enabled = false;
        try { await operation(); }
        catch (OperationCanceledException) when (_closed.IsCancellationRequested) { }
        catch (Exception ex)
        {
            Say("操作被拒绝或现场状态不明：" + ex.Message);
            _permit = null;
            _apply.Enabled = _restore.Enabled = false;
        }
        finally
        {
            _busy = false;
            if (!IsDisposed) _prepare.Enabled = true;
        }
    }
}
