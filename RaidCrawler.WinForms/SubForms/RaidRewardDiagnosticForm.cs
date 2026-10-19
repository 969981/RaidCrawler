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
            Text = "全局奖励模板（不限地图/Seed）：Windows 不写 Switch 内存。\r\n" +
                "Switch 侧 Skyline Hook 需单独安装；可导入其 hook.log 核对真实命中。",
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
        var openFolder = new Button { Text = "打开诊断目录", AutoSize = true };
        var openNative = new Button { Text = "插件配置包", AutoSize = true };
        var importNative = new Button { Text = "导入Hook日志", AutoSize = true };
        buttons.Controls.AddRange([markLobby, markBattle, markResult, openFolder, openNative, importNative]);
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
        Append("hook-status", "Windows 端无法确定 Skyline Hook 是否已安装；需要 Switch 插件原始日志确认实际命中。");
        Append("log", "诊断日志：" + trace.Path);

        inspect.Click += async (_,_) => await RunInspectionAsync();
        markLobby.Click += (_,_) => Append("manual-lobby", "用户标记：已开房（可能为本地或互联网）。不是实际 Hook 命中。");
        markBattle.Click += (_,_) => Append("manual-battle", "用户手动标记：已进入战斗。并非发送回调证据。");
        markResult.Click += (_,_) => Append("manual-result", "用户手动标记：已结算。实际奖励增量和保存尚需独立核验。");
        openNative.Click += (_,_) =>
        {
            try
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = RaidRewardNativePackage.Folder,
                    UseShellExecute = true,
                });
            }
            catch (Exception ex) { Append("error", "打开插件配置包失败：" + ex.Message); }
        };
        importNative.Click += (_,_) => ImportNativeHookLog();
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

    private void ImportNativeHookLog()
    {
        using var dialog = new OpenFileDialog
        {
            Title = "选择从 Switch SD 卡复制出的 hook.log",
            Filter = "Hook log (*.log;*.txt)|*.log;*.txt|All files (*.*)|*.*"
        };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        try
        {
            var info = new FileInfo(dialog.FileName);
            if (info.Length > 2 * 1024 * 1024)
                throw new InvalidDataException("日志超过 2 MiB，请先截取相关会话。");
            string[] entries = File.ReadAllLines(dialog.FileName);
            int hookHits=entries.Count(x=>x.Contains("HOOK_HIT",StringComparison.Ordinal));
            int verified=entries.Count(x=>x.Contains("PATCH_VERIFIED",StringComparison.Ordinal));
            int rejected=entries.Count(x=>x.Contains("PATCH_REJECTED",StringComparison.Ordinal) ||
                x.Contains("PATCH_VERIFY_FAILED",StringComparison.Ordinal));
            Append("native-log-import",
                $"已导入原生日志：{entries.Length} 行，HOOK_HIT={hookHits}，PATCH_VERIFIED={verified}，错误={rejected}；实际入包和存档仍需验证。",
                new { hookHits, verified, rejected, lines=entries.Length, file=info.Name });
            foreach (string line in entries.Where(x=>x.Contains("HOOK_HIT") ||
                        x.Contains("ELF_FINGERPRINT") || x.Contains("CONFIG_VALID") ||
                        x.Contains("MODE ") || x.Contains("NATIVE_HOOK_INSTALLED") ||
                        x.Contains("DISABLED")).TakeLast(100))
                Append("native-log", line.Length>512?line[..512]:line);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            Append("error", "导入原生 Hook 日志失败：" + ex.Message);
        }
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
            // SysBot's getBuildID command supplies only the first eight NSO Build ID bytes.
            // This helps identify a bad match, but cannot attest a complete build.
            try
            {
                bool useCrlf = connection is SwitchSocketAsync;
                byte[] rawBuild = await connection.ReadRaw(SwitchCommand.GetBuildID(useCrlf), 17, token);
                string buildPrefix = Encoding.ASCII.GetString(rawBuild).Trim('\0', '\r', '\n', ' ');
                if (buildPrefix.Length == 16 && buildPrefix.All(Uri.IsHexDigit))
                {
                    string expected = RaidRewardHookContract.VerifiedViolet400BuildId[..16];
                    bool match = buildPrefix.Equals(expected, StringComparison.OrdinalIgnoreCase);
                    Append("build-id-prefix",
                        $"NSO Build ID 前8字节={buildPrefix}; Violet4.0.0 前缀匹配={match}; 不能代替完整 Build ID 与指纹。",
                        new { first8Bytes=buildPrefix, expectedFirst8Bytes=expected, matches=match, fullBuildConfirmed=false });
                }
                else
                    Append("build-id-prefix", "当前 botbase 未返回可解析的 8 字节 Build ID 前缀。");
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                Append("build-id-prefix", "getBuildID 命令读取不可用："+ex.GetType().Name);
            }
            var capability=RemoteHookCapabilities.StandardSysBotBase;
            Append("remote-hook-a",
                "方案A检查：当前标准 botbase 只支持内存读写，不具备线程上下文、可执行代码管理及安全回滚协议；拒绝安装。",
                new { ready=capability.RemoteInlineHookReady, missing=capability.Missing() });
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
                Append("fingerprint", $"《紫》4.0.0 当前指令匹配 {matched}/{NativeRaidCodeFingerprint.Violet400.Count}。已安装 inline Hook 时补丁位置可能不再匹配，需 Switch 原生日志确认。",
                    new { matched, total=NativeRaidCodeFingerprint.Violet400.Count });
                // The 880-byte gap is not a named ELF allocation. Probe
                // mapping/bytes only. Reading zeroes does NOT prove +X access.
                try
                {
                    byte[] padding=await connection.ReadBytesMainAsync(
                        NativeRaidCodeFingerprint.EndOfTextPaddingRva,
                        NativeRaidCodeFingerprint.EndOfTextPaddingLength, token);
                    bool exact=padding.Length==NativeRaidCodeFingerprint.EndOfTextPaddingLength;
                    bool zero=exact && padding.All(x=>x==0);
                    Append("code-gap",
                        $"0x343FC90..0x3440000：mapped-read={exact}, all-zero={zero}。即使全零，仍不能证明 RX 可执行或安装安全。",
                        new { rva="0x343FC90", requestedBytes=0x370, returnedBytes=padding.Length,
                            allZero=zero, executableVerified=false, safeToWrite=false });
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    Append("code-gap", "代码段末尾候选空间无法读取："+
                        ex.GetType().Name+"；禁止远程注入。");
                }
            }
            else
                Append("elf", "此游戏版本尚无已验证的原生对战构建指令 RVA；不检查或使用《紫》4.0.0 地址。");
            Append("probe-complete", "Windows 只读检查完成；真实 HOOK_HIT/PATCH_VERIFIED 需导入 Switch hook.log 核实。");
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
