using RaidCrawler.Core.Structures;
using Xunit;

namespace RaidCrawler.Tests;

public sealed class RemoteRewardTemplateUpdatePolicyTests
{
    private static byte[] Reward(int itemId, int count)
    {
        var rows = new RaidRewardEntry?[RaidRewardEditor.MaxRows];
        rows[0] = new RaidRewardEntry(itemId, count, 0, 0);
        return RaidRewardEditor.EncodeSlots(rows);
    }

    [Fact]
    public void ArmedHookNeverHotSwapsTemplate()
    {
        var a = Reward(15, 100);
        var b = Reward(15, 999);
        Assert.Equal(RemoteTemplateUpdateDecision.QueueForVerifiedProcessRestart,
            RemoteRewardTemplateUpdatePolicy.Decide(a, b,
                RemoteInlineHookPhase.Armed, independentlyVerifiedCallbackQuiescence: false));
        Assert.Equal(RemoteTemplateUpdateDecision.QueueForVerifiedProcessRestart,
            RemoteRewardTemplateUpdatePolicy.Decide(a, b,
                RemoteInlineHookPhase.Armed, independentlyVerifiedCallbackQuiescence: true));
    }

    [Fact]
    public void DisabledModeWithoutProofStillRequiresRestart()
    {
        var a = Reward(15, 100);
        var b = Reward(15, 999);
        Assert.Equal(RemoteTemplateUpdateDecision.QueueForVerifiedProcessRestart,
            RemoteRewardTemplateUpdatePolicy.Decide(a, b,
                RemoteInlineHookPhase.InstalledDisarmed, false));
    }

    [Fact]
    public void IndependentQuiescenceRequiredForImmediateDisarmedUpdate()
    {
        var a = Reward(15, 100);
        var b = Reward(15, 999);
        Assert.Equal(RemoteTemplateUpdateDecision.UpdateInVerifiedQuiescence,
            RemoteRewardTemplateUpdatePolicy.Decide(a, b,
                RemoteInlineHookPhase.InstalledDisarmed, true));
    }

    [Fact]
    public void UninstalledPlansNewProcessSnapshot()
    {
        var a = Reward(15, 100);
        var b = Reward(15, 999);
        Assert.Equal(RemoteTemplateUpdateDecision.PrepareForNewProcess,
            RemoteRewardTemplateUpdatePolicy.Decide(a, b,
                RemoteInlineHookPhase.NotInstalled, false));
    }

    [Fact]
    public void VerifiedEpochRollbackEnablesNoRestartUpdate()
    {
        var a = Reward(15, 100);
        var b = Reward(15, 999);
        Assert.Equal(RemoteTemplateUpdateDecision.UpdateWithVerifiedEpochFallback,
            RemoteRewardTemplateUpdatePolicy.Decide(a, b,
                RemoteInlineHookPhase.Armed,
                independentlyVerifiedCallbackQuiescence: false,
                safeEpochRollbackInstalled: true));
        Assert.Equal(RemoteTemplateUpdateDecision.UpdateWithVerifiedEpochFallback,
            RemoteRewardTemplateUpdatePolicy.Decide(a, b,
                RemoteInlineHookPhase.InstalledDisarmed,
                independentlyVerifiedCallbackQuiescence: false,
                safeEpochRollbackInstalled: true));
    }

    [Fact]
    public void VerifiedProtocolCannotBypassRecoveryRequired()
    {
        var a = Reward(15, 100);
        var b = Reward(15, 999);
        Assert.Equal(RemoteTemplateUpdateDecision.QueueForVerifiedProcessRestart,
            RemoteRewardTemplateUpdatePolicy.Decide(a, b,
                RemoteInlineHookPhase.RecoveryRequired,
                independentlyVerifiedCallbackQuiescence: false,
                safeEpochRollbackInstalled: true));
    }

    [Fact]
    public void SameTemplateNeedsNoWrite()
    {
        var a = Reward(15, 100);
        Assert.Equal(RemoteTemplateUpdateDecision.NoChange,
            RemoteRewardTemplateUpdatePolicy.Decide(a, a,
                RemoteInlineHookPhase.Armed, false));
    }

    [Fact]
    public void RefusesMalformedTemplate()
    {
        var a = Reward(15, 100);
        var corrupt = Reward(15, 999);
        corrupt[4] = 0xff; corrupt[5] = 0xff; corrupt[6] = 0xff;
        Assert.Throws<ArgumentException>(() =>
            RemoteRewardTemplateUpdatePolicy.Decide(a, corrupt,
                RemoteInlineHookPhase.Armed, false));
    }
}
