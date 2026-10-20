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

    private readonly Button luaVmFrameProbeButton = new()
    {
        Text = "P1-B2：只读验证 Lua 主状态/调用帧", AutoSize = true,
    };

    private void ConfigureLuaVmRootProbe(FlowLayoutPanel actions)
    {
        actions.Controls.Add(luaVmRootProbeButton);
        actions.Controls.Add(luaVmFrameProbeButton);
        luaVmRootProbeButton.Click += async (_, _) => await RunLuaVmRootProbeAsync();
        luaVmFrameProbeButton.Click += async (_, _) => await RunLuaVmFrameProbeAsync();
    }

    private async Task RunLuaVmFrameProbeAsync()
    {
        if (connection is null || closing || remoteBusy)
        {
            Append("lua-frame-blocked", "Switch 未连接、窗口关闭或已有其他远程操作；跳过只读采样。");
            return;
        }
        remoteBusy = true;
        luaVmFrameProbeButton.Enabled = false;
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(stop.Token);
            timeout.CancelAfter(TimeSpan.FromSeconds(30));
            CancellationToken ct = timeout.Token;
            await VerifyViolet400IdentityAsync(ct);
            ulong baseBefore = await connection.GetMainNsoBaseAsync(ct);
            var result = await Violet400LuaVmFrameProbe.ProbeAsync(
                (rva, len, token) => connection.ReadBytesMainAsync(rva, len, token),
                (address, len, token) => connection.ReadBytesAbsoluteAsync(address, len, token),
                ct);
            ulong baseAfter = await connection.GetMainNsoBaseAsync(ct);
            bool stableBase = baseAfter == baseBefore;
            string status = stableBase ? result.Status : "MAIN_BASE_CHANGED_DURING_SAMPLE";
            Append("lua-frame-readonly",
                "P1-B2 Lua5.4 主状态/CallInfo 只读结构检查：" + status +
                "；即使发现 Lua 闭包 TValue 标记，也不能证明执行 LotteryRewardItem。",
                new
                {
                    status, mainBaseStable = stableBase, root = result.RootStatus,
                    mainStateLayout = result.MainStateLayoutMatched,
                    stackRange = result.StackPointersPlausible,
                    activeCallInfoCandidate = stableBase && result.SnapshotStable &&
                        result.ActiveCallInfoCandidate,
                    functionSlotType = result.FunctionSlotClassification,
                    luaClosureTagCandidate = result.LuaClosureValueTagObserved,
                    snapshotStable = stableBase && result.SnapshotStable,
                    luaProtoIdentity = "NOT_VERIFIED",
                    vmCpuHit = "NOT_OBSERVED", abcRewardData = "NOT_CAPTURED",
                    stateOrHeapPointersLogged = false,
                    gameWrites = false, hookInstalled = false,
                });
        }
        catch (OperationCanceledException)
        {
            Append("lua-frame-error", "P1-B2 只读采样超时或取消，游戏未被修改。");
        }
        catch (Exception ex)
        {
            Append("lua-frame-error", "P1-B2 只读采样失败：" + ex.GetType().Name +
                "；未修改游戏，不能判定 Lua VM 或奖励执行状态。");
        }
        finally
        {
            remoteBusy = false;
            if (!closing) luaVmFrameProbeButton.Enabled = true;
        }
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
