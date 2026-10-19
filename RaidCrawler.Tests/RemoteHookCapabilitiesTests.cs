using RaidCrawler.Core.Structures;
using Xunit;

namespace RaidCrawler.Tests;

public sealed class RemoteHookCapabilitiesTests
{
    [Fact]
    public void StandardSysBotBaseCannotClaimARealRuntimeHook()
    {
        var caps=RemoteHookCapabilities.StandardSysBotBase;
        Assert.True(caps.GameMemoryRead);
        Assert.True(caps.GameMemoryWrite);
        Assert.True(caps.RuntimeModuleIdentification);
        Assert.False(caps.RemoteInlineHookReady);
        Assert.Contains("线程 x0..x30 寄存器现场", caps.Missing());
        Assert.Contains("指令缓存与数据缓存一致性", caps.Missing());
        Assert.False(RemoteHookInstallationGate.CanInstall(caps,true,true,true,true));
    }

    [Fact]
    public void StandardBotbaseDoesNotSupportPrivilegedHardwareBreakpointHooks()
    {
        var caps = RemoteBreakpointHookCapabilities.StandardSysBotBase;
        Assert.False(caps.Ready);
        Assert.Contains("断点调试事件循环", caps.Missing());
        Assert.Contains("暂停态 x23 等 ARM64 寄存器读取", caps.Missing());
        var enhanced = new RemoteBreakpointHookCapabilities(true,true,true,true,true,true,true);
        Assert.True(enhanced.Ready);
        Assert.Empty(enhanced.Missing());
        Assert.False((enhanced with {GuaranteedThreadContinue = false}).Ready);
    }

    [Fact]
    public void EvenFullyExtendedServiceRequiresAllRuntimeChecks()
    {
        var caps = new RemoteHookCapabilities(true,true,true,true,true,true,true,true,true,true);
        Assert.True(caps.RemoteInlineHookReady);
        Assert.Empty(caps.Missing());
        Assert.False(RemoteHookInstallationGate.CanInstall(caps,false,true,true,true));
        Assert.False(RemoteHookInstallationGate.CanInstall(caps,true,false,true,true));
        Assert.False(RemoteHookInstallationGate.CanInstall(caps,true,true,false,true));
        Assert.False(RemoteHookInstallationGate.CanInstall(caps,true,true,true,false));
        Assert.True(RemoteHookInstallationGate.CanInstall(caps,true,true,true,true));
    }
}
