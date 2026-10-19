namespace RaidCrawler.Core.Structures;

/// <summary>
/// Pure lifecycle gate for a future remotely controlled inline Hook.
/// This class never reads/writes Switch memory or emits executable code.
/// Its evidence fields are assertions to be independently verified by a
/// separate runtime implementation; they must not be inferred from UI state.
/// </summary>
public enum RemoteInlineHookPhase
{
    NotInstalled,
    Staged,
    InstalledDisarmed,
    Armed,
    RecoveryRequired,
}

public enum RemoteInlineHookAction
{
    Stage,
    ConfirmInstall,
    Enable,
    Disable,
    UpdateTemplate,
    ConfirmUninstall,
    ReportFailure,
    ConfirmRecovery,
}

public sealed record RemoteInlineHookEvidence
{
    public bool GameIdentityVerified { get; init; }
    public bool OriginalInstructionVerified { get; init; }
    public bool MappingExecutableVerified { get; init; }
    public bool PayloadReadbackVerified { get; init; }
    public bool EntryReadbackVerified { get; init; }
    public bool CallbacksQuiescentVerified { get; init; }
    public bool TemplateReadbackVerified { get; init; }
    // Callers must read and compare the expected mode word for each operation.
    public bool ModeReadbackVerified { get; init; }
    public bool OriginalEntryRestoredVerified { get; init; }
}

public sealed class RemoteInlineHookLifecycle
{
    public RemoteInlineHookPhase Phase { get; private set; } =
        RemoteInlineHookPhase.NotInstalled;

    public bool TryAdvance(
        RemoteInlineHookAction action,
        RemoteInlineHookEvidence evidence,
        out string reason)
    {
        ArgumentNullException.ThrowIfNull(evidence);
        bool common = evidence.GameIdentityVerified;
        bool quiescent = evidence.CallbacksQuiescentVerified;

        bool allowed = action switch
        {
            RemoteInlineHookAction.Stage =>
                Phase == RemoteInlineHookPhase.NotInstalled &&
                common && evidence.OriginalInstructionVerified &&
                evidence.MappingExecutableVerified && quiescent &&
                evidence.PayloadReadbackVerified &&
                evidence.ModeReadbackVerified,

            RemoteInlineHookAction.ConfirmInstall =>
                Phase == RemoteInlineHookPhase.Staged &&
                common && quiescent && evidence.EntryReadbackVerified &&
                evidence.ModeReadbackVerified,

            RemoteInlineHookAction.Enable =>
                Phase == RemoteInlineHookPhase.InstalledDisarmed &&
                common && quiescent && evidence.TemplateReadbackVerified &&
                evidence.EntryReadbackVerified && evidence.ModeReadbackVerified,

            RemoteInlineHookAction.Disable =>
                Phase == RemoteInlineHookPhase.Armed &&
                common && quiescent && evidence.ModeReadbackVerified,

            RemoteInlineHookAction.UpdateTemplate =>
                Phase == RemoteInlineHookPhase.InstalledDisarmed &&
                common && quiescent && evidence.TemplateReadbackVerified &&
                evidence.ModeReadbackVerified,

            RemoteInlineHookAction.ConfirmUninstall =>
                Phase == RemoteInlineHookPhase.InstalledDisarmed &&
                common && quiescent && evidence.OriginalEntryRestoredVerified,

            RemoteInlineHookAction.ReportFailure => true,

            RemoteInlineHookAction.ConfirmRecovery =>
                Phase == RemoteInlineHookPhase.RecoveryRequired &&
                common && quiescent && evidence.OriginalEntryRestoredVerified,

            _ => false,
        };

        if (!allowed)
        {
            reason = $"拒绝 {action}: 当前状态 {Phase}，或缺少实机核验证据。";
            return false;
        }

        Phase = action switch
        {
            RemoteInlineHookAction.Stage => RemoteInlineHookPhase.Staged,
            RemoteInlineHookAction.ConfirmInstall =>
                RemoteInlineHookPhase.InstalledDisarmed,
            RemoteInlineHookAction.Enable => RemoteInlineHookPhase.Armed,
            RemoteInlineHookAction.Disable =>
                RemoteInlineHookPhase.InstalledDisarmed,
            RemoteInlineHookAction.UpdateTemplate => Phase,
            RemoteInlineHookAction.ConfirmUninstall =>
                RemoteInlineHookPhase.NotInstalled,
            RemoteInlineHookAction.ReportFailure =>
                RemoteInlineHookPhase.RecoveryRequired,
            RemoteInlineHookAction.ConfirmRecovery =>
                RemoteInlineHookPhase.NotInstalled,
            _ => Phase,
        };
        reason = $"状态已更新为 {Phase}（逻辑模型，非 Switch 执行确认）。";
        return true;
    }
}
