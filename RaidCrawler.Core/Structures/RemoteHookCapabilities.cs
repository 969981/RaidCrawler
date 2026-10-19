namespace RaidCrawler.Core.Structures;

/// <summary>
/// Honest, purely descriptive capability evaluation for installing a native
/// AArch64 trampoline from Windows. A raw game-memory write is NOT an installed hook.
/// A future sysmodule extension must explicitly attest each privileged ability.
/// </summary>
public sealed record RemoteHookCapabilities(
    bool GameMemoryRead, bool GameMemoryWrite, bool RuntimeModuleIdentification,
    bool PersistentDebugSession, bool SuspendAndResumeThreads, bool CaptureThreadRegisters,
    bool TargetExecutableAllocationOrVerifiedCodeCave, bool InstructionCacheCoherence,
    bool TransactionalPatchAndRollback, bool HookEventTelemetry)
{
    public static RemoteHookCapabilities StandardSysBotBase => new(
        GameMemoryRead: true, GameMemoryWrite: true, RuntimeModuleIdentification: true,
        PersistentDebugSession: false, SuspendAndResumeThreads: false,
        CaptureThreadRegisters: false, TargetExecutableAllocationOrVerifiedCodeCave: false,
        InstructionCacheCoherence: false, TransactionalPatchAndRollback: false,
        HookEventTelemetry: false);

    public bool RemoteInlineHookReady => GameMemoryRead && GameMemoryWrite &&
        RuntimeModuleIdentification && PersistentDebugSession &&
        SuspendAndResumeThreads && CaptureThreadRegisters &&
        TargetExecutableAllocationOrVerifiedCodeCave &&
        InstructionCacheCoherence && TransactionalPatchAndRollback &&
        HookEventTelemetry;

    public IReadOnlyList<string> Missing()
    {
        var result = new List<string>();
        if (!GameMemoryRead) result.Add("游戏内存读取");
        if (!GameMemoryWrite) result.Add("游戏内存写入");
        if (!RuntimeModuleIdentification) result.Add("Title/Build 与 NSO 身份确认");
        if (!PersistentDebugSession) result.Add("持久化调试会话");
        if (!SuspendAndResumeThreads) result.Add("执行期间线程暂停/恢复");
        if (!CaptureThreadRegisters) result.Add("线程 x0..x30 寄存器现场");
        if (!TargetExecutableAllocationOrVerifiedCodeCave) result.Add("经验证的目标进程可执行内存／代码洞");
        if (!InstructionCacheCoherence) result.Add("指令缓存与数据缓存一致性");
        if (!TransactionalPatchAndRollback) result.Add("安全安装／原字节回滚及状态确认");
        if (!HookEventTelemetry) result.Add("Hook 命中事件及读回结果回传");
        return result;
    }
}

/// <summary>
/// Explicit gate: a Windows process must not attempt live inline patching
/// merely because SysBot.Base exposes WriteBytesMainAsync.
/// </summary>
public static class RemoteHookInstallationGate
{
    public static bool CanInstall(RemoteHookCapabilities capabilities,
        bool exactGameBuildVerified, bool expectedOriginalOpcodeMatches,
        bool confirmedHostProcess, bool userExplicitlyArmed)
    {
        ArgumentNullException.ThrowIfNull(capabilities);
        return capabilities.RemoteInlineHookReady &&
               exactGameBuildVerified &&
               expectedOriginalOpcodeMatches &&
               confirmedHostProcess &&
               userExplicitlyArmed;
    }
}
