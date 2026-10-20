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
    private readonly TextBox _results = new();
    private bool _running;

    public SysbotOnlyHostWindow(ISwitchConnectionAsync connection)
    {
        _inspector = new SysbotOnlyRaidPointInspector(connection);
        Text = "方案 A：纯 sys-botbase 房主 RaidPoint 诊断";
        ClientSize = new Size(690, 465);
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
        _inspect.Text = "只读验证 / 离线预览";
        _inspect.Location = new Point(487, 64);
        _inspect.Size = new Size(181, 31);
        _inspect.Click += async (_, _) => await InspectAsync();
        _results.Multiline = true;
        _results.WordWrap = true;
        _results.ScrollBars = ScrollBars.Vertical;
        _results.ReadOnly = true;
        _results.Location = new Point(14, 145);
        _results.Size = new Size(654, 300);
        _results.Font = new Font(FontFamily.GenericMonospace, 9);
        Controls.AddRange([information, label, _address, _ownerMode, _inspect, _results]);
        FormClosed += (_, _) => _close.Cancel();

        Log("方案 A 仅读取当前进程的候选 RaidPoint。");
        Log("必须先有可证实的绝对地址；ELF 中的 x20 / x0 不是固定指针。");
        Log("原有 RaidBlockPointerBase 指向存档太晶坑块，不能直接当作当前房主 RaidPoint。");
        Log("读取结果相同只证明这份候选内存稳定，不证明即将发送或能安全写入。");
    }

    private void Log(string message)
    {
        if (!IsDisposed)
            _results.AppendText(message + Environment.NewLine);
    }

    private async Task InspectAsync()
    {
        if (_running) return;
        _running = true;
        _inspect.Enabled = false;
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
            if (!IsDisposed) _inspect.Enabled = true;
        }
    }
}
