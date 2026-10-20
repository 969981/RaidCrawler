using System.Text;
using RaidCrawler.Core.Structures;
using SysBot.Base;

namespace RaidCrawler.WinForms.SubForms;

public sealed partial class RaidRewardDiagnosticForm
{
    private readonly List<Button> remoteHookButtons = [];
    private RemoteViolet400HookInstaller? remoteController;
    private byte[]? remoteRewardTemplate;
    private ulong? remoteMainBase;
    private bool remoteBusy;
    private bool legacySerializerExperimentUnlocked;
    private readonly Label hookStatusLabel = new()
    {
        AutoSize = false, Width = 178, Height = 30,
        Text = "旧网络 Hook：检测中", TextAlign = ContentAlignment.MiddleLeft,
        AutoEllipsis = true
    };
    private Button? armToggleButton;

    private void ConfigureRemoteHookControls(
        FlowLayoutPanel primary, FlowLayoutPanel advanced,
        IReadOnlyList<RaidRewardEntry?> slots)
    {
        remoteRewardTemplate = RaidRewardEditor.EncodeSlots(slots);
        if (connection is not null)
            remoteController = new RemoteViolet400HookInstaller(
                (rva, length, ct) => connection.ReadBytesMainAsync(rva, length, ct),
                (bytes, rva, ct) => connection.WriteBytesMainAsync(bytes, rva, ct),
                VerifyViolet400IdentityAsync);

        primary.Controls.Add(hookStatusLabel);

        Button Add(FlowLayoutPanel target, string name)
        {
            var button = new Button
            {
                Text = name, AutoSize = true, Margin = new Padding(3),
            };
            target.Controls.Add(button);
            remoteHookButtons.Add(button);
            return button;
        }
        // The old experiment OVERWRITES all 45 outbound network rows. It is
        // incompatible with the new preserve-and-append contract, and has
        // never been shown to affect solo Lua reward objects.
        var unlockLegacy = Add(advanced, "解锁 Legacy 网络整表替换实验");
        var install = Add(advanced, "Legacy：安装旧覆盖 Hook");
        armToggleButton = Add(advanced, "Legacy：启用旧覆盖 Hook");
        var update = Add(advanced, "Legacy：更新整表覆盖模板");
        unlockLegacy.Click += (_, _) =>
        {
            if (legacySerializerExperimentUnlocked) return;
            if (MessageBox.Show(this,
                "这是旧版 LDN 出站45槽整表覆盖实验，并非晶耀护符奖励追加。\\r\\n" +
                "它可能清除原版固定／随机奖励；单人是否命中未经证明。\\r\\n" +
                "只有在已备份存档且明确需要研究旧版网络路径时才解锁。是否继续？",
                "解锁 Legacy 实验", MessageBoxButtons.YesNo,
                MessageBoxIcon.Warning) != DialogResult.Yes) return;
            legacySerializerExperimentUnlocked = true;
            unlockLegacy.Text = "Legacy 实验已解锁（仅本窗口）";
            Append("legacy-experiment-unlocked",
                "仅本窗口允许手工执行旧版网络整表替换；新晶耀追加模式始终不会调用旧写入接口。");
        };
        var status = Add(advanced, "刷新 Hook 状态");
        var remove = Add(advanced, "卸载入口");

        status.Click += async (_, _) => await RunRemoteActionAsync("查询状态",
            async (manager, ct) => Describe(await manager.StatusAsync(ct)), false);
        install.Click += async (_, _) => await RunRemoteActionAsync("安装",
            async (manager, ct) => Describe(await manager.InstallAsync(remoteRewardTemplate!, ct)), true, requiresLegacyUnlock:true);
        armToggleButton.Click += async (_, _) =>
            await RunRemoteActionAsync("切换 Hook 启停", async (manager, ct) =>
            {
                var previous = await manager.StatusAsync(ct);
                bool enable = previous.Installation switch
                {
                    RemoteViolet400HookInstaller.InstallState.InstalledDisarmed => true,
                    RemoteViolet400HookInstaller.InstallState.InstalledArmed => false,
                    _ => throw new InvalidOperationException("请先检查或安装 Hook；当前状态不能切换。")
                };
                return Describe(await manager.SetEnabledAsync(enable, ct));
            }, true, requiresLegacyUnlock:true);
        update.Click += async (_, _) => await RunRemoteActionAsync("无重启应用奖励",
            async (manager, ct) =>
            {
                var updated = await manager.UpdateAsync(remoteRewardTemplate!, ct);
                return $"epoch {updated.OldEpoch} → {updated.NewEpoch}; " +
                    $"原状态启用={updated.Armed}; {updated.Detail}";
            }, true, requiresLegacyUnlock:true);
        remove.Click += async (_, _) => await RunRemoteActionAsync("卸载",
            async (manager, ct) => Describe(await manager.UninstallAsync(ct)), true);
    }

    private void UpdateVisualHookState(RemoteViolet400HookInstaller.State state)
    {
        hookStatusLabel.Text = state.Installation switch
        {
            RemoteViolet400HookInstaller.InstallState.Empty => "Hook：未安装",
            RemoteViolet400HookInstaller.InstallState.InstalledArmed =>
                $"Hook：已启用 ({state.Epoch})",
            RemoteViolet400HookInstaller.InstallState.InstalledDisarmed =>
                $"Hook：已停用 ({state.Epoch})",
            RemoteViolet400HookInstaller.InstallState.DetachedCodePresent =>
                "Hook：已卸载入口",
            _ => "Hook：状态异常"
        };
        if (armToggleButton is not null)
        {
            armToggleButton.Text = state.Installation ==
                RemoteViolet400HookInstaller.InstallState.InstalledArmed
                ? "Legacy：停用旧覆盖 Hook" : "Legacy：启用旧覆盖 Hook";
        }
    }

    private async Task RefreshHookStatusOnOpenAsync()
    {
        await RunRemoteActionAsync("自动刷新状态",
            async (manager, ct) => Describe(await manager.StatusAsync(ct)), false);
    }

    private static string Describe(RemoteViolet400HookInstaller.State state) =>
        $"{state.Installation}, epoch={state.Epoch?.ToString() ?? "N/A"}; {state.Detail}";

    private async Task VerifyViolet400IdentityAsync(CancellationToken ct)
    {
        if (connection is null)
            throw new InvalidOperationException("Switch 未连接。");
        string title = (await connection.GetTitleID(ct)).Trim();
        string version = (await connection.GetGameInfo("version", ct)).Trim();
        if (title != Offsets.VioletID || version != "4.0.0")
            throw new InvalidOperationException(
                $"目标游戏进程身份已变化：TitleID={title}；version={version}。" +
                " 本窗口不再允许使用旧地址。");
        bool crlf = connection is SwitchSocketAsync;
        byte[] raw = await connection.ReadRaw(SwitchCommand.GetBuildID(crlf), 17, ct);
        string build = Encoding.ASCII.GetString(raw).Trim('\0', '\r', '\n', ' ');
        if (!build.Equals(RaidRewardHookContract.VerifiedViolet400BuildId[..16],
                StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("NSO Build ID 前 8 字节不匹配。");
        ulong before = await connection.GetMainNsoBaseAsync(ct);
        if (remoteMainBase is ulong recorded && before != recorded)
            throw new InvalidOperationException("Main NSO 基址已改变：停止操作，重新打开诊断。");

        foreach (var anchor in NativeRaidCodeFingerprint.Violet400)
        {
            if (anchor.Rva == NativeRaidCodeFingerprint.SerializerHookRva)
                continue; // installed entry is intentionally a B instruction
            byte[] actual = await connection.ReadBytesMainAsync(anchor.Rva, 4, ct);
            if (!NativeRaidCodeFingerprint.Check(anchor, actual).Matches)
                throw new InvalidOperationException(
                    $"main+0x{anchor.Rva:X} 指纹不匹配：禁止代码修改。");
        }
        ulong after = await connection.GetMainNsoBaseAsync(ct);
        if (before != after)
            throw new InvalidOperationException("NSO 基址在检测期间发生变化。");
        remoteMainBase ??= before;
    }

    private async Task RunRemoteActionAsync(string action,
        Func<RemoteViolet400HookInstaller, CancellationToken, Task<string>> operation,
        bool writes, bool requiresLegacyUnlock = false)
    {
        if (requiresLegacyUnlock && !legacySerializerExperimentUnlocked)
        {
            Append("legacy-experiment-blocked",
                "晶耀追加配置不会执行旧45槽整表覆盖；如需研究旧版网络实验，请在高级工具明确解锁 Legacy。");
            return;
        }
        if (remoteBusy)
            return;
        if (remoteController is null)
        {
            Append("hook-blocked", "请先连接 Switch，然后重新打开本窗口。");
            return;
        }
        if (writes && MessageBox.Show(this,
                $"将执行实验性操作：{action}。\r\n" +
                "仅限《紫》4.0.0、已备份存档、未进入团体战且退出互联网联机的研究设备。\r\n" +
                "操作会修改正在运行的游戏代码/数据，可能使游戏崩溃。\r\n" +
                "卸载只恢复入口，不清空仍可能被 CPU 执行的代码区。\r\n" +
                "确认继续吗？",
                "远程 ARM64 Hook 实验确认",
                MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes)
            return;

        remoteBusy = true;
        remoteHookButtons.ForEach(x => x.Enabled = false);
        inspect.Enabled = false;
        testWrite.Enabled = false;
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(stop.Token);
        deadline.CancelAfter(TimeSpan.FromSeconds(90));
        try
        {
            Append("hook-operation", "开始：" + action);
            string result = await operation(remoteController, deadline.Token);
            var state = await remoteController.StatusAsync(deadline.Token);
            UpdateVisualHookState(state);
            Append("hook-memory-confirmed", action + "：" + result,
                new { nativeHookHit = "NOT_OBSERVED",
                    receiverReward = "NOT_OBSERVED",
                    bagChange = "NOT_OBSERVED",
                    savePersistence = "NOT_OBSERVED",
                    controlPlaneOnly = true });
        }
        catch (OperationCanceledException)
        {
            hookStatusLabel.Text = "Hook：状态未知";
            Append("hook-error", action + " 超时或取消；请先查询入口/跳板状态，不要盲目重复写入。");
        }
        catch (Exception ex)
        {
            hookStatusLabel.Text = "Hook：请检查日志";
            Append("hook-error", action + " 失败：" + ex.GetType().Name + "：" +
                ex.Message + "；未知状态下禁止继续修改。");
        }
        finally
        {
            remoteBusy = false;
            if (!IsDisposed && !Disposing)
            {
                remoteHookButtons.ForEach(x => x.Enabled = true);
                inspect.Enabled = true;
                testWrite.Enabled = true;
            }
        }
    }
}
