using RaidCrawler.Core.Structures;
using Xunit;

namespace RaidCrawler.Tests;

public sealed class RemoteInlineHookLifecycleTests
{
    private static RemoteInlineHookEvidence Verified => new()
    {
        GameIdentityVerified = true,
        OriginalInstructionVerified = true,
        MappingExecutableVerified = true,
        PayloadReadbackVerified = true,
        EntryReadbackVerified = true,
        CallbacksQuiescentVerified = true,
        TemplateReadbackVerified = true,
        ModeReadbackVerified = true,
        OriginalEntryRestoredVerified = true,
    };

    [Fact]
    public void NoExecutableEvidenceMeansNoStage()
    {
        var machine = new RemoteInlineHookLifecycle();
        var incomplete = Verified with { MappingExecutableVerified = false };
        Assert.False(machine.TryAdvance(RemoteInlineHookAction.Stage,
            incomplete, out _));
        Assert.Equal(RemoteInlineHookPhase.NotInstalled, machine.Phase);
    }

    [Fact]
    public void CannotClaimEnabledBeforeInstallConfirmed()
    {
        var machine = new RemoteInlineHookLifecycle();
        Assert.False(machine.TryAdvance(RemoteInlineHookAction.Enable, Verified, out _));
        Assert.True(machine.TryAdvance(RemoteInlineHookAction.Stage, Verified, out _));
        Assert.False(machine.TryAdvance(RemoteInlineHookAction.Enable, Verified, out _));
        Assert.True(machine.TryAdvance(RemoteInlineHookAction.ConfirmInstall, Verified, out _));
        Assert.True(machine.TryAdvance(RemoteInlineHookAction.Enable, Verified, out _));
        Assert.Equal(RemoteInlineHookPhase.Armed, machine.Phase);
    }

    [Fact]
    public void DisallowsLiveTemplateEditAndLiveUninstall()
    {
        var machine = new RemoteInlineHookLifecycle();
        Assert.True(machine.TryAdvance(RemoteInlineHookAction.Stage, Verified, out _));
        Assert.True(machine.TryAdvance(RemoteInlineHookAction.ConfirmInstall, Verified, out _));
        Assert.True(machine.TryAdvance(RemoteInlineHookAction.Enable, Verified, out _));
        Assert.False(machine.TryAdvance(RemoteInlineHookAction.UpdateTemplate, Verified, out _));
        Assert.False(machine.TryAdvance(RemoteInlineHookAction.ConfirmUninstall, Verified, out _));
        Assert.True(machine.TryAdvance(RemoteInlineHookAction.Disable, Verified, out _));
        Assert.True(machine.TryAdvance(RemoteInlineHookAction.UpdateTemplate, Verified, out _));
        Assert.True(machine.TryAdvance(RemoteInlineHookAction.ConfirmUninstall, Verified, out _));
        Assert.Equal(RemoteInlineHookPhase.NotInstalled, machine.Phase);
    }

    [Fact]
    public void RefusesTransitionsWithoutQuiescence()
    {
        var machine = new RemoteInlineHookLifecycle();
        var busy = Verified with { CallbacksQuiescentVerified = false };
        Assert.False(machine.TryAdvance(RemoteInlineHookAction.Stage, busy, out _));
        Assert.True(machine.TryAdvance(RemoteInlineHookAction.Stage, Verified, out _));
        Assert.False(machine.TryAdvance(RemoteInlineHookAction.ConfirmInstall, busy, out _));
    }

    [Fact]
    public void ModeMustBeReadBackBeforeArmingOrDisarming()
    {
        var machine = new RemoteInlineHookLifecycle();
        Assert.True(machine.TryAdvance(RemoteInlineHookAction.Stage, Verified, out _));
        Assert.True(machine.TryAdvance(RemoteInlineHookAction.ConfirmInstall, Verified, out _));
        var unknownMode = Verified with { ModeReadbackVerified = false };
        Assert.False(machine.TryAdvance(RemoteInlineHookAction.Enable, unknownMode, out _));
        Assert.True(machine.TryAdvance(RemoteInlineHookAction.Enable, Verified, out _));
        Assert.False(machine.TryAdvance(RemoteInlineHookAction.Disable, unknownMode, out _));
        Assert.Equal(RemoteInlineHookPhase.Armed, machine.Phase);
    }

    [Fact]
    public void FailureRequiresVerifiedRecovery()
    {
        var machine = new RemoteInlineHookLifecycle();
        Assert.True(machine.TryAdvance(RemoteInlineHookAction.ReportFailure,
            new RemoteInlineHookEvidence(), out _));
        Assert.Equal(RemoteInlineHookPhase.RecoveryRequired, machine.Phase);
        Assert.False(machine.TryAdvance(RemoteInlineHookAction.Enable, Verified, out _));
        Assert.False(machine.TryAdvance(RemoteInlineHookAction.ConfirmRecovery,
            Verified with { OriginalEntryRestoredVerified = false }, out _));
        Assert.True(machine.TryAdvance(RemoteInlineHookAction.ConfirmRecovery,
            Verified, out _));
        Assert.Equal(RemoteInlineHookPhase.NotInstalled, machine.Phase);
    }
}
