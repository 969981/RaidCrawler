namespace RaidCrawler.Core.Structures;

/// <summary>
/// Session-consistent template update decision, modeled after PokéLDN's
/// one immutable Bootstrap per RaidGroup. No Switch I/O or executable patches.
/// Standard sys-botbase does not prove callback quiescence. Hot updates are
/// allowed only with an independently verified versioned-read-and-rollback
/// trampoline, never merely because the transport can write memory.
/// </summary>
public enum RemoteTemplateUpdateDecision
{
    NoChange,
    PrepareForNewProcess,
    QueueForVerifiedProcessRestart,
    UpdateInVerifiedQuiescence,
    UpdateWithVerifiedEpochFallback,
}

public static class RemoteRewardTemplateUpdatePolicy
{
    /// <summary>
    /// Decides where a correctly encoded snapshot may be applied.
    /// This is a policy result, NOT proof a future installer executed it.
    /// </summary>
    public static RemoteTemplateUpdateDecision Decide(
        ReadOnlySpan<byte> currentlyCommitted,
        ReadOnlySpan<byte> desired,
        RemoteInlineHookPhase hookPhase,
        bool independentlyVerifiedCallbackQuiescence,
        bool safeEpochRollbackInstalled = false)
    {
        if (currentlyCommitted.Length != RaidRewardEditor.RewardBytes ||
            desired.Length != RaidRewardEditor.RewardBytes)
            throw new ArgumentException("Both snapshots must be exactly 720 bytes.");

        // Reject malformed / inconsistent rows before ANY write planning.
        RaidRewardEditor.DecodeSlots(desired);

        if (currentlyCommitted.SequenceEqual(desired))
            return RemoteTemplateUpdateDecision.NoChange;

        // A new/verified game process is the only default release point for
        // a new template when the current console exposes ordinary botbase.
        if (hookPhase == RemoteInlineHookPhase.NotInstalled)
            return RemoteTemplateUpdateDecision.PrepareForNewProcess;

        if (hookPhase is RemoteInlineHookPhase.RecoveryRequired or
            RemoteInlineHookPhase.Staged)
            return RemoteTemplateUpdateDecision.QueueForVerifiedProcessRestart;

        if (safeEpochRollbackInstalled && hookPhase is
            (RemoteInlineHookPhase.Armed or RemoteInlineHookPhase.InstalledDisarmed))
            return RemoteTemplateUpdateDecision.UpdateWithVerifiedEpochFallback;

        if (hookPhase == RemoteInlineHookPhase.Armed)
            return RemoteTemplateUpdateDecision.QueueForVerifiedProcessRestart;

        return independentlyVerifiedCallbackQuiescence
            ? RemoteTemplateUpdateDecision.UpdateInVerifiedQuiescence
            : RemoteTemplateUpdateDecision.QueueForVerifiedProcessRestart;
    }
}
