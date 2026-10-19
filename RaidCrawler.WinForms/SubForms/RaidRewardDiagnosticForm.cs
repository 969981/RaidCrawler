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
    private readonly PokeLdnRaidSelection? selection;
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

    public RaidRewardDiagnosticForm(ISwitchConnectionAsync? connection,
        PokeLdnRaidSelection? selection, IReadOnlyList<RaidRewardEntry?> slots)
    {
        this.connection = connection;
        this.selection = selection;
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
            Text = "诊断模式（不写内存）：记录版本、连接状态、当前坑 Seed 和奖励配置哈希。\r\n" +
                "原版官方联网发送端 Hook 尚未安装，也没有奖励写入确认。开房／战斗节点需要手动标记。",
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
        if (selection is not null)
            Append("selection", $"选择快照：{selection.Version}/{selection.Map} " +
                $"seed={selection.Seed:X8}, den={selection.Den}, event={selection.IsEvent}",
                new { selection.Version,selection.Map,selection.Seed,selection.Den,
                    selection.Area,selection.Species,selection.Stars,selection.IsEvent });
        Append("hook", "原生 Hook：未安装；不具备官方联网奖励写入能力。");
        Append("log", "诊断日志：" + trace.Path);

        inspect.Click += async (_,_) => await RunInspectionAsync();
        markLobby.Click += (_,_) => Append("manual-lobby", "用户手动标记：已进入官方联网房间。并非 Hook 触发证据。");
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
        FormClosing += (_,_) => { stop.Cancel(); trace.Dispose(); };
    }

    private void Append(string stage, string message, object? detail=null)
    {
        if (IsDisposed) return;
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
            Append("probe", "开始读取：Title ID / 游戏版本 / botbase 版本。只读，不扫描内存，不进行写入。");
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
            if (selection is not null && (selection.Version=="violet") != (title==Offsets.VioletID))
            {
                Append("blocked", "当前连接的游戏版本与编辑时的朱／紫快照不一致，不能写入。");
                return;
            }
            if (title == Offsets.VioletID && version == "4.0.0")
                Append("elf", "《紫》4.0.0：有静态 ELF 序列化 RVA 参考，但尚未观测到官方在线房主执行这条回调。");
            else
                Append("elf", "此游戏版本尚无受信任的在线奖励 Hook RVA；禁止使用《紫》4.0.0 的静态地址。");
            Append("blocked", "HOOK_NOT_INSTALLED: 只读连接检查结束，没有执行任何游戏内存写入。");
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
