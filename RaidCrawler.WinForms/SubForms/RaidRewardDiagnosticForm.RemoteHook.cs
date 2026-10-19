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

    private void ConfigureRemoteHookControls(FlowLayoutPanel buttons,
        IReadOnlyList<RaidRewardEntry?> slots)
    {
        remoteRewardTemplate = RaidRewardEditor.EncodeSlots(slots);
        if (connection is not null)
            remoteController = new RemoteViolet400HookInstaller(
                (rva, length, ct) => connection.ReadBytesMainAsync(rva, length, ct),
                (bytes, rva, ct) => connection.WriteBytesMainAsync(bytes, rva, ct),
                VerifyViolet400IdentityAsync);

        Button Add(string name)
        {
            var button = new Button { Text = name, AutoSize = true, Margin = new Padding(2) };
            buttons.Controls.Add(button);
            remoteHookButtons.Add(button);
            return button;
        }
        var status = Add("Hook 状态");
        var install = Add("安装（默认停用）");
        var enable = Add("启用");
        var update = Add("无重启更新");
        var disable = Add("停用");
        var remove = Add("卸载入口");

        status.Click += async (_, _) => await RunRemoteActionAsync("查询状态",
            async (manager, ct) => Describe(await manager.StatusAsync(ct)), false);
        install.Click += async (_, _) => await RunRemoteActionAsync("安装",
            async (manager, ct) => Describe(await manager.InstallAsync(remoteRewardTemplate!, ct)), true);
        enable.Click += async (_, _) => await RunRemoteActionAsync("启用",
            async (manager, ct) => Describe(await manager.SetEnabledAsync(true, ct)), true);
        update.Click += async (_, _) => await RunRemoteActionAsync("热更新",
            async (manager, ct) =>
            {
                var updated = await manager.UpdateAsync(remoteRewardTemplate!, ct);
                return $"epoch {updated.OldEpoch} → {updated.NewEpoch}; " +
                    $"原状态启用={updated.Armed}; {updated.Detail}";
            }, true);
        disable.Click += async (_, _) => await RunRemoteActionAsync("停用",
            async (manager, ct) => Describe(await manager.SetEnabledAsync(false, ct)), true);
        remove.Click += async (_, _) => await RunRemoteActionAsync("卸载",
            async (manager, ct) => Describe(await manager.UninstallAsync(ct)), true);
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
            throw new InvalidOperationException("仅支持《紫》4.0.0 的精确指令地址。");
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
        bool writes)
    {
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
            Append("hook-memory-confirmed", action + "：" + result,
                new { hookHit = false, rewardReceived = false, saveVerified = false,
                    controlPlaneOnly = true });
        }
        catch (OperationCanceledException)
        {
            Append("hook-error", action + " 超时或取消；请先查询入口/跳板状态，不要盲目重复写入。");
        }
        catch (Exception ex)
        {
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
