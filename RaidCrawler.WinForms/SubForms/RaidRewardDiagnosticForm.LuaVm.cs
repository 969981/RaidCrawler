using RaidCrawler.Core.Structures;

namespace RaidCrawler.WinForms.SubForms;

/// <summary>
/// P1-B only. Reads the candidate Lua VM root pointer chain from exact
/// Violet 4.0.0. It does not call Lua, access a reward row, or write RAM.
/// </summary>
public sealed partial class RaidRewardDiagnosticForm
{
    private readonly Button luaVmRootProbeButton = new()
    {
        Text = "P1-B：只读探测 Lua 状态候选", AutoSize = true,
    };

    private void ConfigureLuaVmRootProbe(FlowLayoutPanel actions)
    {
        actions.Controls.Add(luaVmRootProbeButton);
        luaVmRootProbeButton.Click += async (_, _) => await RunLuaVmRootProbeAsync();
    }

    private async Task RunLuaVmRootProbeAsync()
    {
        if (connection is null)
        {
            Append("lua-root-blocked", "尚未连接 Switch；没有执行 Lua 状态读取。");
            return;
        }
        if (remoteBusy || closing)
        {
            Append("lua-root-blocked", "其他远程操作正在执行或窗口正在关闭，本次跳过。");
            return;
        }
        remoteBusy = true;
        luaVmRootProbeButton.Enabled = false;
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(stop.Token);
            timeout.CancelAfter(TimeSpan.FromSeconds(18));
            CancellationToken ct = timeout.Token;

            // Includes title, exact update, BuildID prefix, and relevant
            // non-legacy AArch64 fingerprint verification.
            await VerifyViolet400IdentityAsync(ct);
            ulong mainBefore = await connection.GetMainNsoBaseAsync(ct);
            var result = await Violet400LuaVmRootProbe.ProbeAsync(
                (rva, count, token) => connection.ReadBytesMainAsync(rva, count, token),
                (address, count, token) => connection.ReadBytesAbsoluteAsync(address, count, token),
                ct);
            ulong mainAfter = await connection.GetMainNsoBaseAsync(ct);
            bool baseStable = mainBefore == mainAfter;
            string status = baseStable ? result.Status : "MAIN_BASE_CHANGED_DURING_SAMPLE";
            Append("lua-root-readonly",
                "P1-B Lua 状态全局引用只读观测：" + status +
                "；指针稳定不等于 Lua 线程、闭包或团战回调已证实。",
                new
                {
                    status,
                    baseStable,
                    result.GlobalReferencePresent,
                    result.LuaStateCandidatePresent,
                    stable = baseStable && result.StableAcrossReads,
                    vmStateConfirmed = false,
                    runtimeCpuHit = "NOT_OBSERVED",
                    abcCaptured = "NOT_OBSERVED",
                    rewardModified = false,
                    readOnly = true,
                    // Raw heap pointers intentionally excluded from logs.
                    addressesLogged = false
                });
        }
        catch (OperationCanceledException)
        {
            Append("lua-root-error", "Lua 状态只读读取已取消或超时；未写入游戏。");
        }
        catch (Exception e)
        {
            Append("lua-root-error",
                "Lua 状态候选读取失败（未写入游戏）：" + e.GetType().Name +
                "；请重新检查游戏版本和程序状态。");
        }
        finally
        {
            remoteBusy = false;
            if (!closing) luaVmRootProbeButton.Enabled = true;
        }
    }
}
