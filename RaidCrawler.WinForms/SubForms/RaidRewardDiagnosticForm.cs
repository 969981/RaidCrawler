using System.Security.Cryptography;
using System.Text;
using RaidCrawler.Core.Structures;
using SysBot.Base;

namespace RaidCrawler.WinForms.SubForms;

/// <summary>
/// User-initiated, one-session console diagnostic. This window intentionally
/// has no method that writes game memory or installs a native hook.
/// </summary>
public sealed class RaidRewardDiagnosticForm : Form
{
    private readonly ISwitchConnectionAsync? connection;
    private readonly RaidRewardDiagnosticLog trace = new();
    private readonly TextBox logText = new()
    {
        Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Both,
        Dock = DockStyle.Fill, WordWrap = false, Font = new Font("Consolas", 9),
    };
    private readonly Button inspect = new()
    {
        Text = "读取实机信息（只读）", AutoSize = true,
    };
    private readonly CancellationTokenSource stop = new();
    private bool closing;

    public RaidRewardDiagnosticForm(ISwitchConnectionAsync? connection,
        IReadOnlyList<RaidRewardEntry?> slots)
    {
        this.connection = connection;
        Text = "太晶坑奖励写入／Hook 实机诊断";
        StartPosition = FormStartPosition.CenterParent;
        MinimumSize = new Size(720, 440);
        Size = new Size(920, 570);

        var panel = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(10),
            ColumnCount = 1, RowCount = 3 };
        panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 75));
        panel.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 45));
        Controls.Add(panel);
        var header = new Label
        {
            Dock = DockStyle.Fill,
            Text = "全局奖励模板（与所选坑位无关）：记录配置、版本和连接状态。\r\n" +
                "尚未安装原生对战构建 Hook；本窗口不写内存，开战节点需要手动标记。",
            ForeColor = Color.DarkRed,
            AutoEllipsis = true,
        };
        panel.Controls.Add(header, 0, 0);
        panel.Controls.Add(logText, 0, 1);

        var buttons = new FlowLayoutPanel { Dock = DockStyle.Fill };
        buttons.Controls.Add(inspect);
        var markLobby = new Button { Text = "标记：已开房", AutoSize = true };
        var markBattle = new Button { Text = "标记：已开战", AutoSize = true };
        var markResult = new Button { Text = "标记：已结算", AutoSize = true };
        var openFolder = new Button { Text = "打开日志目录", AutoSize = true };
        buttons.Controls.AddRange([markLobby, markBattle, markResult, openFolder]);
        panel.Controls.Add(buttons, 0, 2);

        var raw = RaidRewardEditor.EncodeSlots(slots);
        var sha = Convert.ToHexString(SHA256.HashData(raw));
        var configured = slots.Count(x => x is not null);
        Append("config", $"45槽校验通过：{configured}/45 已配置；SHA256={sha}", new
        {
            configured, empty=45-configured, payloadLength=raw.Length, sha256=sha,
            subjects=slots.Where(s => s is not null).GroupBy(s => s!.Subject)
                .ToDictionary(g=>g.Key.ToString(),g=>g.Count()),
        });
        Append("scope", "全局奖励模板：不限地图、Seed、坑位、活动标记；尚未绑定任何实际战斗。",
            new { mode="all-raids", actualBattleSeed=(uint?)null, targetDen=(int?)null });
        Append("hook", "原生构建 Hook：尚未安装；未捕获实际对战事件，也没有修改奖励。");
        Append("log", "诊断日志：" + trace.Path);

        inspect.Click += async (_,_) => await RunInspectionAsync();
        markLobby.Click += (_,_) => Append("manual-lobby", "用户标记：已开房（可能为本地或互联网）。不是实际 Hook 命中。");
        markBattle.Click += (_,_) => Append("manual-battle", "用户手动标记：已进入战斗。并非发送回调证据。");
        markResult.Click += (_,_) => Append("manual-result", "用户手动标记：已结算。实际奖励增量和保存尚需独立核验。");
        openFolder.Click += (_,_) =>
        {
            try
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = System.IO.Path.GetDirectoryName(trace.Path)!,
                    UseShellExecute = true
                });
            }
            catch (Exception ex) { Append("error", "打开日志目录失败：" + ex.GetType().Name); }
        };
        FormClosing += (_,_) => { closing = true; stop.Cancel(); trace.Dispose(); };
    }

    private void Append(string stage, string message, object? detail=null)
    {
        if (closing || IsDisposed) return;
        string line = $"[{DateTimeOffset.Now:HH:mm:ss.fff}] {message}";
        logText.AppendText(line+Environment.NewLine);
        trace.Write(stage,message,detail);
    }

    private async Task RunInspectionAsync()
    {
        if (connection is null)
        {
            Append("disconnected", "没有已连接的 Switch。请回主界面连接并读取坑位后重试。");
            return;
        }
        inspect.Enabled = false;
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(stop.Token);
        deadline.CancelAfter(TimeSpan.FromSeconds(18));
        try
        {
            var token=deadline.Token;
            Append("probe", "读取游戏身份与静态指令指纹（仅受支持的版本）；不扫描奖励对象，不进行写入。");
            string title=(await connection.GetTitleID(token)).Trim();
            string version=(await connection.GetGameInfo("version", token)).Trim();
            string botbase=(await connection.GetBotbaseVersion(token)).Trim();
            Append("console", $"TitleID={title}; game={version}; botbase={botbase}",
                new {title,version,botbase});
            if (title is not (Offsets.ScarletID or Offsets.VioletID))
            {
                Append("blocked", "不是《朱／紫》游戏。无法继续原生 Hook 的任何判断。");
                return;
            }
            // The ASLR-dependent NSO base is useful for later native callback
            // research, but this read does not execute a hook or find a live RaidPoint.
            ulong mainBase = await connection.GetMainNsoBaseAsync(token);
            Append("main-base", $"Main NSO base=0x{mainBase:X}; 本次地址受 ASLR 影响，不是永久奖励指针。");
            if (title == Offsets.VioletID && version == "4.0.0")
            {
                int matched = 0;
                foreach (var anchor in NativeRaidCodeFingerprint.Violet400)
                {
                    token.ThrowIfCancellationRequested();
                    var bytes = await connection.ReadBytesMainAsync(anchor.Rva, 4, token);
                    var (same, actual) = NativeRaidCodeFingerprint.Check(anchor, bytes);
                    if (same) matched++;
                    Append("opcode", $"main+0x{anchor.Rva:X}: {(same ? "MATCH" : "MISMATCH")}; " +
                        $"expected=0x{anchor.AArch64Opcode:X8}; actual={(actual.HasValue ? $"0x{actual.Value:X8}" : "N/A")}",
                        new { offset=anchor.Rva, expected=anchor.AArch64Opcode, observed=actual, matches=same });
                }
                Append("fingerprint", $"《紫》4.0.0 静态指令匹配 {matched}/{NativeRaidCodeFingerprint.Violet400.Count}。这不表示 Hook 被调用。",
                    new { matched, total=NativeRaidCodeFingerprint.Violet400.Count });
            }
            else
                Append("elf", "此游戏版本尚无已验证的原生对战构建指令 RVA；不检查或使用《紫》4.0.0 地址。");
            Append("blocked", "HOOK_NOT_INSTALLED: 只读环境检查结束，未截获实际对战构建，未写入任何奖励。");
        }
        catch(OperationCanceledException)
        {
            Append("cancelled","连接超时或用户已停止诊断。");
        }
        catch(Exception ex)
        {
            Append("error",$"实机读取失败：{ex.GetType().Name}：{ex.Message}");
        }
        finally
        {
            if (!IsDisposed && !Disposing) inspect.Enabled = true;
        }
    }
}
