# 方案 A：Wi-Fi sys-botbase 扫描 8 MiB 长时间停在 0% 的性能修复

2026-10-20。仅改 Windows/C# 内存**只读扫描**，不安装新 Switch 模块，不改变奖励写入参数，也不把候选视为当前房主的确定对象。

## 现场故障

真实《紫》4.0.0 房主开房但尚未开战时，用户启动“① 自动查找候选”，屏幕始终显示 `进度 0/8MiB，候选0，不可读块0`，超过两分钟无有效进度；同时 UI 不允许点击“自动状态监测”。这属于扫描性能与 UI 反馈设计缺陷，不是“开房未打”阶段自动导致的。

## 已核对的性能根因

参考 `kwsch/SysBot.NET` [`SwitchSocket.cs`](https://github.com/kwsch/SysBot.NET/blob/master/SysBot.Base/Connection/Switch/Wireless/SwitchSocket.cs) 与 [`SwitchSocketAsync.cs`](https://github.com/kwsch/SysBot.NET/blob/master/SysBot.Base/Connection/Switch/Wireless/SwitchSocketAsync.cs)：

- Wi-Fi 客户端公开源码的默认 `MaximumTransferSize = 0x1C0`，即 448 字节；
- 默认 `BaseDelay = 64ms`、`DelayFactor=256`；
- 一次 `ReadBytesAbsoluteAsync(16KiB)` 会继续拆成多个更小的请求，在每个请求后执行 `Task.Delay(MaximumTransferSize/DelayFactor + BaseDelay)`；
- 8MiB/448 ≈ **18,725 个底层块**；单纯附加延迟约 18,725×65ms ≈ **20分钟**，不包括 Wi-Fi 网络 RTT、游戏/客户端处理时间；
- 原先外层 UI 只在每32个16KiB块回调一次，还对已读字节执行**整数 MiB 截断**；因此日志长时间保持0/8MiB。

**限制：** 上述默认值是上游公开源码，实际 RaidCrawler 附带 `SysBot.Base.dll` 的参数可能不同；最新版会在进度日志中输出**当前运行时**的传输块大小和调整前后每块延迟，避免把估算当成实测。

## 修复

1. [`HostRaidPointAutoLocator.cs`](../../RaidCrawler.Core/Host/HostRaidPointAutoLocator.cs) 在扫描生命周期使用 `ScopedWifiScanTuning`：仅对现有的 `SwitchSocketAsync`，把 `BaseDelay` 降至不高于2ms，`DelayFactor` 提高至至少1024。**不改变 `MaximumTransferSize`**，因为该上游读命令可能用一次 TCP Receive 接收十六进制响应，贸然放大请求有分包截断风险。使用 `using` 在完成、异常、主动取消时恢复原参数。
2. 完整扫描仍用16KiB外层块，保护上限最多64MiB；实际外层每处理4块（64KiB）或发现候选即上报**真实字节数**和耗时，而不是只报整数 MiB。
3. [`SysbotOnlyHostWindow.cs`](../../RaidCrawler.WinForms/SubForms/SysbotOnlyHostWindow.cs)：标题栏显示 `已读 KiB/总 KiB、百分比、KiB/s`，日志约每2.2秒报告速度、剩余时间、候选数、不可读块数。
4. 新增**停止扫描**按钮：取消当前专用 scan CTS，不再需要关闭整个程序；完成/取消后恢复可用按钮和窗口标题。
5. 每个外层16KiB请求有12秒单次超时；超时/连续读取失败停止并提示检查或重新连接，避免卡住几分钟。即使 SysBot.Base 在内部把取消读转换为 `byte[0]`，本层会再次检查取消标记。

## 验收

- 新版连接《紫》4.0.0 后，在**本地网络连接稳定**的阶段点击“①自动查找”，日志首先输出实际 Wi-Fi chunk/delay 参数，然后开始**以KiB递增**显示；即使最后找到0个 RaidPoint 副本，也应有准确的已读字节和耗时。
- 点击“停止扫描”后应在当前 SysBot 读命令结束或取消后解除禁用，并恢复原版连接参数。
- 如果 12 秒内一个16KiB读取都无法完成，则发出有意义的超时提示，不要再用相同连接连续提交数千条请求；如已发生连接异常，先断开重新连接。
- **扫描快不等于自动识别本局实际发送对象。** LDN 本地无线切换可能切断 Wi-Fi sys-botbase 的 TCP 连接，属于网络链路问题，不是扫描延迟能解决的。
- 编译/Host 单测保证 C# API 可用；首次真实 Switch 性能依然要用进度日志测量，不能预先承诺8MiB一定几秒完成。
