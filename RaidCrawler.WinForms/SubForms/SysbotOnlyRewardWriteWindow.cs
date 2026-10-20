using RaidCrawler.Core.Host;
using SysBot.Base;

namespace RaidCrawler.WinForms.SubForms;

/// <summary>
/// Explicit opt-in, local experiment on ONE candidate RaidPoint.
/// No plugins/ARM64 hook; existing sys-botbase is the only transport.
/// </summary>
public sealed class SysbotOnlyRewardWriteWindow : Form
{
    private readonly SysbotOnlyRewardWriter _writer;
    private readonly CancellationTokenSource _close = new();
    private readonly TextBox _address = new();
    private readonly NumericUpDown _quantity = new();
    private readonly ComboBox _subjectMode = new();
    private readonly CheckBox _confirmVersion = new();
    private readonly CheckBox _confirmUnverified = new();
    private readonly Button _prepare = new();
    private readonly Button _apply = new();
    private readonly Button _restore = new();
    private readonly TextBox _log = new();
    private HostPlanAWritePermit? _permit;
    private bool _busy;

    public SysbotOnlyRewardWriteWindow(ISwitchConnectionAsync connection, string? candidateAddress = null)
    {
        _writer = new SysbotOnlyRewardWriter(connection);
        Text = "方案 A：纯 sys-botbase 受控奖励写入（实验）";
        ClientSize = new Size(730, 620);
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        var intro = new Label
        {
            Text = "注意：仅支持已审计的《紫》4.0.0！实验性修改当前候选内存一条奖励，" +
                   "没有证明它是本局发送对象；不能保证参与者到账或游戏稳定。",
            Location = new Point(14, 12), Size = new Size(704, 48)
        };
        _subjectMode.DropDownStyle = ComboBoxStyle.DropDownList;
        _subjectMode.Location = new Point(14, 69);
        _subjectMode.Size = new Size(439, 30);
        _subjectMode.Items.AddRange([
            "持有晶耀护符者（subject=5，先机球×1–10）",
            "全体玩家（subject=0，允许先机球×500对照实验）"]);
        _subjectMode.SelectedIndex = 0;
        _subjectMode.SelectedIndexChanged += (_, _) =>
        {
            _quantity.Maximum = _subjectMode.SelectedIndex == 1 ? 500 : 10;
            _quantity.Value = _subjectMode.SelectedIndex == 1 ? 500 : 1;
            _permit = null;
            _apply.Enabled = _restore.Enabled = false;
            Say("领取对象已切换；必须重新准备内存快照。");
        };
        var addrLabel = new Label
        {
            Text = "候选对象绝对地址：",
            Location = new Point(14, 116), Size = new Size(165, 27)
        };
        _address.Location = new Point(186, 112);
        _address.Size = new Size(250, 28);
        _address.PlaceholderText = "0x...（必须为对象起点）";
        _address.Text = candidateAddress ?? "";
        var qtyLabel = new Label
        {
            Text = "先机球×", Location = new Point(455, 116), Size = new Size(75, 27)
        };
        _quantity.Location = new Point(535, 112);
        _quantity.Size = new Size(70, 28);
        _quantity.Minimum = 1;
        _quantity.Maximum = 10;
        _quantity.Value = 1;

        _confirmVersion.Text = "我确认是自己的测试 Switch，运行《紫》4.0.0，并已备份存档";
        _confirmVersion.Location = new Point(14, 159);
        _confirmVersion.Size = new Size(697, 27);
        _confirmUnverified.Text = "理解 Hexbyt3 房间偏移与当前对象归属未经实机验证；写入可能无效或引发崩溃";
        _confirmUnverified.Location = new Point(14, 191);
        _confirmUnverified.Size = new Size(704, 42);

        _prepare.Text = "1. 读取并准备";
        _prepare.Location = new Point(14, 245);
        _prepare.Size = new Size(179, 35);
        _apply.Text = "2. 写入这一行";
        _apply.Location = new Point(211, 245);
        _apply.Size = new Size(179, 35);
        _apply.Enabled = false;
        _restore.Text = "3. 尝试安全恢复";
        _restore.Location = new Point(407, 245);
        _restore.Size = new Size(179, 35);
        _restore.Enabled = false;

        _prepare.Click += async (_, _) => await PrepareAsync();
        _apply.Click += async (_, _) => await ApplyAsync();
        _restore.Click += async (_, _) => await RestoreAsync();
        _log.Multiline = true;
        _log.ReadOnly = true;
        _log.ScrollBars = ScrollBars.Vertical;
        _log.Font = new Font(FontFamily.GenericMonospace, 9);
        _log.Location = new Point(14, 301);
        _log.Size = new Size(702, 301);
        Controls.AddRange([intro, _subjectMode, addrLabel, _address, qtyLabel, _quantity,
            _confirmVersion, _confirmUnverified, _prepare, _apply, _restore, _log]);
        FormClosed += (_, _) => _close.Cancel();
        Say("这是直接内存写入实验，不是已验证的房主奖励修改功能。");
        Say("单行：道具15。晶耀护符模式1–10；全体模式额外支持×500对照实验。Reserved=0。");
        Say("使用修正的 16B [source,item,quantity,reserved] 行，起点 owner+0x58+0xE0。不安装插件。");
    }

    private void Say(string msg)
    {
        if (!IsDisposed) _log.AppendText($"{DateTime.Now:HH:mm:ss} {msg}{Environment.NewLine}");
    }

    private bool Allowed()
    {
        if (_confirmVersion.Checked && _confirmUnverified.Checked)
            return true;
        Say("需要先勾选两项试验前提与风险确认。");
        return false;
    }

    private async Task PrepareAsync()
    {
        if (_busy || !Allowed()) return;
        await Run(async () =>
        {
            _permit = null;
            _apply.Enabled = _restore.Enabled = false;
            ulong owner = HostProbeMailbox.ParseAddress(_address.Text);
            uint subject = _subjectMode.SelectedIndex == 1 ? 0u : 5u;
            var permit = await _writer.PrepareAsync(owner, (uint)_quantity.Value, subject, _close.Token);
            _permit = permit;
            _apply.Enabled = true;
            Say($"准备成功：Owner=0x{permit.OwnerAddress:X} RaidPoint=0x{permit.RaidPointAddress:X}");
            Say($"对象={permit.RaidPointName} 原版行数={permit.OriginalRows} 空槽偏移=0x{permit.RowOffset:X}");
            Say($"领取对象 subject={permit.SubjectType}，道具15×{permit.Quantity}；原始对象SHA256={permit.OriginalSha256}");
            Say("有效期15秒；只证明候选对象可编辑，不证明会被原版发包程序读取。");
        });
    }

    private async Task ApplyAsync()
    {
        if (_busy || _permit is null || !Allowed()) return;
        var permit = _permit;
        if (MessageBox.Show(this,
            $"将通过 sys-botbase 向候选 RaidPoint 空行写入先机球×{permit.Quantity}、subject={permit.SubjectType}。" +
            "写入后仅能验证游戏RAM，无法保证参与者收到、背包入账或恢复原状。" +
            "严禁在公共房间中以此冒充已经验证的奖励发放。确认继续？",
            "确认修改测试 Switch 的 RAM", MessageBoxButtons.YesNo,
            MessageBoxIcon.Warning) != DialogResult.Yes)
            return;
        await Run(async () =>
        {
            // Single-use permit even if write fails or readback is uncertain.
            _permit = null;
            _apply.Enabled = false;
            var result = await _writer.ApplyAsync(permit, _close.Token);
            Say($"写入结果：{result.Code} 地址0x{result.RowAddress:X}");
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
            "仅当整个对象仍与刚才的写入后数据完全一致，才会恢复原来16字节。" +
            "若游戏已更新或已发送数据，恢复无法撤回网络发奖。继续？",
            "手动安全恢复", MessageBoxButtons.YesNo,
            MessageBoxIcon.Warning) != DialogResult.Yes)
            return;
        await Run(async () =>
        {
            _permit = null;
            _restore.Enabled = false;
            var result = await _writer.RestoreAsync(permit, _close.Token);
            Say($"恢复结果：{result.Code}");
            Say(result.Explanation);
        });
    }

    private async Task Run(Func<Task> operation)
    {
        if (_busy) return;
        _busy = true;
        _prepare.Enabled = false;
        try { await operation(); }
        catch (OperationCanceledException) when (_close.IsCancellationRequested) { }
        catch (Exception e)
        {
            _apply.Enabled = _restore.Enabled = false;
            _permit = null;
            Say("已拒绝或操作不确定：" + e.Message);
        }
        finally
        {
            _busy = false;
            if (!IsDisposed) _prepare.Enabled = true;
        }
    }
}
