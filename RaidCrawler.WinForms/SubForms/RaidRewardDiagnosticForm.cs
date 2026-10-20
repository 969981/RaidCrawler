using System.Security.Cryptography;
using System.Text;
using RaidCrawler.Core.Structures;
using SysBot.Base;

namespace RaidCrawler.WinForms.SubForms;

/// <summary>
/// Experimental native-hook diagnostics with a compact everyday toolbar.
/// Installation, activation and memory writes remain explicit user actions.
/// </summary>
public sealed partial class RaidRewardDiagnosticForm : Form
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
    private readonly Button testWrite = new()
    {
        Text = "验证写入并恢复", AutoSize = true,
    };
    private readonly CancellationTokenSource stop = new();
    private readonly FlowLayoutPanel advancedActions = new()
    {
        Dock = DockStyle.Fill, AutoScroll = true, WrapContents = true,
        FlowDirection = FlowDirection.LeftToRight,
        Padding = new Padding(2),
        Visible = false,
    };
    private TableLayoutPanel? diagnosticLayout;
    private Button? advancedToggle;
    private bool closing;

    public RaidRewardDiagnosticForm(ISwitchConnectionAsync? connection,
        IReadOnlyList<RaidRewardEntry?> slots)
    {
        this.connection = connection;
        Text = "太晶坑奖励写入／Hook 实机诊断";
        StartPosition = FormStartPosition.CenterParent;
        MinimumSize = new Size(740, 440);
        Size = new Size(960, 550);

        var panel = new TableLayoutPanel { Dock = DockStyle.Fill,
            Padding = new Padding(10), ColumnCount = 1, RowCount = 4 };
        diagnosticLayout = panel;
        panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 64));
        panel.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 58));
        panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 0));
        Controls.Add(panel);

        var header = new Label
        {
            Dock = DockStyle.Fill,
            Text = "RaidCrawler / 《紫》4.0.0  ·  晶耀护符追加配置（仅保存到本机）" +
                Environment.NewLine +
                "原版奖励保留；追加运行时接入未实现。旧网络覆盖实验已移至高级工具并默认锁定。",
            ForeColor = Color.DarkRed, AutoEllipsis = true,
            TextAlign = ContentAlignment.MiddleLeft,
        };
        panel.Controls.Add(header, 0, 0);
        panel.Controls.Add(logText, 0, 1);

        var toolbar = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill, WrapContents = true, AutoScroll = true,
            FlowDirection = FlowDirection.LeftToRight,
            Padding = new Padding(2)
        };
        panel.Controls.Add(toolbar, 0, 2);
        panel.Controls.Add(advancedActions, 0, 3);

        ConfigureRemoteHookControls(toolbar, advancedActions, slots);
        // Routine diagnostic actions no longer occupy the everyday toolbar.
        advancedActions.Controls.Add(inspect);
        ConfigureLuaVmRootProbe(advancedActions);
        advancedActions.Controls.Add(testWrite);

        var openFolder = new Button { Text = "打开日志目录", AutoSize = true };
        advancedActions.Controls.Add(openFolder);
        ConfigureRewardEvidenceControls(advancedActions);
        ConfigureAutomaticRaidPhaseMonitor(toolbar, advancedActions);

        advancedToggle = new Button { Text = "高级工具 ▾", AutoSize = true };
        toolbar.Controls.Add(advancedToggle);
        advancedToggle.Click += (_, _) => ToggleAdvancedActions();

        openFolder.Click += (_, _) =>
        {
            try
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = System.IO.Path.GetDirectoryName(trace.Path)!,
                    UseShellExecute = true
                });
            }
            catch (Exception ex)
            {
                Append("error", "打开日志目录失败：" + ex.GetType().Name);
            }
        };

        var raw = RaidRewardEditor.EncodeSlots(slots);
        var sha = Convert.ToHexString(SHA256.HashData(raw));
        var configured = slots.Count(x => x is not null);
        Append("config", $"45槽校验通过：{configured}/45 已配置；SHA256={sha}", new
        {
            configured, empty=45-configured, payloadLength=raw.Length, sha256=sha,
            subjects=slots.Where(s => s is not null).GroupBy(s => s!.Subject)
                .ToDictionary(g=>g.Key.ToString(),g=>g.Count()),
        });
        Append("scope", "当前是独立的晶耀追加配置预览；旧 Hook 是 LDN 0x2F 出站整表覆盖，默认锁定且绝非追加模式。单人实机追加未实现。",
            new { profileScope="global", nativeHookCoverage="LDN-bootstrap-serializer",
                  verifiedSolo=false, verifiedOfficialInternet=false,
                  actualBattleSeed=(uint?)null, targetDen=(int?)null });
        Append("charm-assumption",
            "晶耀护符资格：用户已经确认持有物品 2481，本轮不再将缺少护符作为失败原因。追加奖励运行时未实现，当前无 A/B/C 实机捕获。",
            new { subject5 = slots.Count(x => x?.Subject == 5),
                  charmOwnershipUserConfirmed = true,
                  onConsoleRechecked = false,
                  runtimeAppend = "NOT_IMPLEMENTED",
                  legacyOverwriteIsAppend = false });
        Append("hook-status", "自动监控会读取 Hook 状态，但不会自动安装或启用。");
        Append("log", "诊断日志：" + trace.Path);

        inspect.Click += async (_,_) => await RunInspectionAsync();
        testWrite.Click += async (_,_) => await RunWriteProbeAsync();
        FormClosing += (_,_) => {
            closing = true;
            StopAutomaticPhaseMonitor("WINDOW_CLOSING");
            autoPhaseTimer.Dispose();
            integrityTimer.Stop();
            integrityTimer.Dispose();
            stop.Cancel();
            trace.Dispose();
        };
    }

    private void ToggleAdvancedActions()
    {
        if (diagnosticLayout is null || advancedToggle is null) return;
        bool show = !advancedActions.Visible;
        diagnosticLayout.SuspendLayout();
        diagnosticLayout.RowStyles[3].Height = show ? 140 : 0;
        advancedActions.Visible = show;
        advancedToggle.Text = show ? "收起高级工具 ▴" : "高级工具 ▾";
        diagnosticLayout.ResumeLayout(true);
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

    private async Task RunWriteProbeAsync()
    {
        if (remoteBusy) return;
        if (connection is null)
        {
            Append("write-probe-blocked", "未连接 Switch；拒绝代码段测试。");
            return;
        }
        if (MessageBox.Show(this,
                "此操作仅在《紫》4.0.0 的候选全零代码间隔临时写入 4B ARM64 NOP，" +
                "立即读取并尝试恢复为 0。不修改 Hook 入口，也不发放奖励。\r\n" +
                "代码写入仍存在游戏崩溃风险；建议先备份存档、退出联网对战。\r\n" +
                "确定使用现有 sys-botbase 进行一次写入测试吗？",
                "确认直接通信测试", MessageBoxButtons.YesNo,
                MessageBoxIcon.Warning) != DialogResult.Yes)
            return;

        inspect.Enabled = false;
        testWrite.Enabled = false;
        remoteBusy = true;
        remoteHookButtons.ForEach(x => x.Enabled = false);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(stop.Token);
        timeout.CancelAfter(TimeSpan.FromSeconds(25));
        try
        {
            var ct = timeout.Token;
            var title = (await connection.GetTitleID(ct)).Trim();
            var version = (await connection.GetGameInfo("version", ct)).Trim();
            if (title != Offsets.VioletID || version != "4.0.0")
            {
                Append("write-probe-blocked", "只支持《紫》4.0.0，当前版本不匹配。");
                return;
            }
            bool useCrlf = connection is SwitchSocketAsync;
            byte[] rawBuild = await connection.ReadRaw(SwitchCommand.GetBuildID(useCrlf), 17, ct);
            string build = Encoding.ASCII.GetString(rawBuild).Trim('\0','\r','\n',' ');
            if (!build.Equals(RaidRewardHookContract.VerifiedViolet400BuildId[..16],
                    StringComparison.OrdinalIgnoreCase))
            {
                Append("write-probe-blocked", "NSO Build ID 前缀不匹配，禁止写入。");
                return;
            }
            ulong beforeBase = await connection.GetMainNsoBaseAsync(ct);
            foreach (var anchor in NativeRaidCodeFingerprint.Violet400)
            {
                byte[] actual = await connection.ReadBytesMainAsync(anchor.Rva, 4, ct);
                if (!NativeRaidCodeFingerprint.Check(anchor, actual).Matches)
                {
                    Append("write-probe-blocked",
                        $"main+0x{anchor.Rva:X} 指令不同于原版，避免与已有 Hook 冲突，拒绝写入。");
                    return;
                }
            }
            ulong afterBase = await connection.GetMainNsoBaseAsync(ct);
            if (afterBase != beforeBase)
            {
                Append("write-probe-blocked", "Main NSO base 在检查期间变化，拒绝写入。");
                return;
            }
            Append("write-probe-start",
                "版本、Build ID 前缀和 8/8 指令指纹通过；即将验证候选全零区域并临时写入 NOP。",
                new { mainBase=$"0x{beforeBase:X}",
                      probeRva=$"0x{RemoteCodeGapWriteProbe.ProbeRva:X}",
                      modifiesHookEntry=false, fullBuildConfirmed=false });
            var result = await RemoteCodeGapWriteProbe.RunAsync(
                (offset,size,token) => connection.ReadBytesMainAsync(offset,size,token),
                (data,offset,token) => connection.WriteBytesMainAsync(data,offset,token),
                ct);
            Append("write-probe-result", result.Reason,
                new {result.Accepted, result.WriteVerified, result.RestoreVerified,
                     executableVerified=false, hookInstalled=false});
        }
        catch (OperationCanceledException)
        {
            Append("write-probe-error", "连接超时或取消；若测试已开始，重新进入游戏后应复查全零区域。");
        }
        catch (Exception ex)
        {
            Append("write-probe-error", "远程测试异常：" + ex.GetType().Name + "：" + ex.Message);
        }
        finally
        {
            remoteBusy = false;
            if (!IsDisposed && !Disposing)
            {
                inspect.Enabled = true;
                testWrite.Enabled = true;
                remoteHookButtons.ForEach(x => x.Enabled = true);
            }
        }
    }

    private async Task RunInspectionAsync()
    {
        if (remoteBusy) return;
        if (connection is null)
        {
            Append("disconnected", "没有已连接的 Switch。请回主界面连接并读取坑位后重试。");
            return;
        }
        inspect.Enabled = false;
        testWrite.Enabled = false;
        remoteBusy = true;
        remoteHookButtons.ForEach(x => x.Enabled = false);
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
                "方案A：调试断点协议仍不支持；普通 botbase 理论上可修补 ARM64 代码段，但需实机验证 880B 空间与恢复机制。",
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
                // Extra read-only evidence from the exact ELF: the 880B
                // candidate shares its 4KiB page with the last .plt instruction.
                // No sys-botbase API here queries actual runtime X permissions.
                var pltBytes = await connection.ReadBytesMainAsync(
                    NativeRaidCodeFingerprint.LastPltInstructionRva, 4, token);
                bool pltMatches = NativeRaidCodeFingerprint.Check(
                    new NativeRaidCodeFingerprint.Anchor(
                        NativeRaidCodeFingerprint.LastPltInstructionRva,
                        NativeRaidCodeFingerprint.LastPltInstructionOpcode,
                        "Last executable PLT br x17"), pltBytes).Matches;
                Append("rx-page-layout",
                    $"末尾 PLT 指令匹配={pltMatches}；候选区域与已有 RX .plt 同属 4KiB 页=" +
                    NativeRaidCodeFingerprint.CandidateSharesLastPltPage +
                    "。这是 ELF/地址布局推断，不等于实机 X 权限查询。",
                    new { pltMatches,
                        same4KPage=NativeRaidCodeFingerprint.CandidateSharesLastPltPage,
                        executableVerified=false, runtimePermissionQueried=false });
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
            Append("probe-complete", "Windows 只读检查完成。可另行人工启动写入/恢复测试；仍未证明候选区可执行或 Hook 已命中。");
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
            remoteBusy = false;
            if (!IsDisposed && !Disposing)
            {
                inspect.Enabled = true;
                testWrite.Enabled = true;
                remoteHookButtons.ForEach(x => x.Enabled = true);
            }
        }
    }
}
