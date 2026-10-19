# 方案 A：RaidCrawler Windows 远程安装原生奖励 Hook 的可行性研究

研究日期：2026-10-19 | 代码位置：`benefit` | 状态：**只读能力预检已开发；现有 botbase 尚不能安装 Hook**

## 结论

**有可能做成用户想要的「Windows 选择全局奖励 → 安装／启动 Hook → Switch 游戏内任意坑构建时替换奖励 → Windows 收到 Hook 命中和恢复状态」，但“只靠当前普通 sys-botbase + 一次 pokeMain”不具备可验证的完整实现基础。**

[上游 sys-botbase commands.md](https://github.com/olliz0r/sys-botbase/blob/master/commands.md) 提供 `peekMain / pokeMain / peekAbsolute / pokeAbsolute / pointerPeek / pointerPoke / getMainNsoBase / getBuildID` 等命令；没有可执行内存分配、长期持有调试会话、线程寄存器回调、事件断点和缓存维护的远程协议。

[上游 commands.c](https://github.com/olliz0r/sys-botbase/blob/master/sys-botbase/source/commands.c) 显示游戏内存写操作由 `svcDebugActiveProcess` + `svcWriteDebugProcessMemory` 完成：`poke()` 临时 `attach()` → `writeMem()` → `detach()`，并不保留跨多条请求的调试句柄，亦没有暴露 ARM64 `x23`、调试事件及“安装代码”的协议。**可以写 RAM 不等于能安全注册会在战斗序列化时自动执行的 Hook。**

[SysBot.Base 的 ISwitchConnectionAsync](https://github.com/969981/SVRaidBot_CHS/blob/research/SysBot.Base/Connection/Switch/ISwitchConnectionAsync.cs) 及 [SwitchCommand](https://github.com/969981/SVRaidBot_CHS/blob/research/SysBot.Base/Util/SwitchCommand.cs) 也只抽象现有查询／读写／控制协议，`ReadRaw / SendRaw` 本身没有凭空赋予后端执行代码的能力。Windows USB 与 Wi-Fi 最终受服务端能力约束。

## 三种方式的区别

| 方案 | 原有 sys-botbase 不变？ | 是否需在 Switch 侧先改软件？ | 结论 |
|---|---|---|---|
| A0：只发送 `pokeMain` 代码补丁 | 是 | 无新增模块 | **不建议**：没有已证实的目标可执行代码洞、暂停线程、寄存器回调、缓存同步和可靠回滚；不能证明代码真的执行 |
| A1：Windows + **增强版 sys-botbase sysmodule** | 否（替换既有模块） | 需要安装一次增强版 botbase，**不需要 Skyline 插件** | **推荐研发**：由 Switch sysmodule 自己持有 debug handle、执行安全安装事务，Windows 只管理配置、命令与日志 |
| A2：Windows + Skyline（前一阶段） | 可以保留 | 需要游戏侧 Skyline NRO 插件 | 现有 `.nro` 已编译，可做实机对照，但不是严格的“Windows 直接安装” |

方案 A1 不需要每次安装游戏插件，但**仍需要有相应权限和代码的 Switch 侧 sysmodule**。如果要求 Switch 上除现有原版 sys-botbase 外绝不增添或替换任何软件，则 A0 当前只能做读取、写入和离线验证，**不能宣称拥有安全的自动 Hook 安装能力**。

## 已核验的静态依据

《紫》4.0.0 原始 ELF，Build ID 前 20 字节 `709bfd66115298640155fcc4979dba151c7cc79a`。

- `main+0xE327FC`：某战斗初始化 `0xAA0` 字节明文序列化路径。
- `main+0xE32984`：尾部 `0x3C8` memcpy 完成后的候选 inline Hook 指令位置。
- 此时 `x23` 是该次函数调用的临时输出缓冲区；奖励起点为 `x23+0x79C`，共 `45×16=720` 字节。
- AArch64 的直接 `B` 跳转采用 26 位有符号指令偏移并按 4 字节缩放，**目标距离限制为 ±128 MiB**。没有可验证的附近可执行代码区时，不能用单条 `B` 假设远处任意地址可达；绝对跳转通常需要多条指令与安全搬迁被覆盖的原指令。
- 覆盖 `main+0xE32984` 的原始 `mov x8,x21` 时，执行流必须正确补回它，保留调用约定要求的寄存器／向量寄存器与状态，并在返回时继续原版逻辑。否则游戏可能崩溃或产生微妙的数据错误。
- 正式安装前必须确认代码页权限、线程暂停与恢复、目标可执行地址归属以及指令缓存一致性。**`svcWriteDebugProcessMemory` 写入成功和代码重新读取成功，仍不等于已经排除 I-cache / 并发执行问题。**

[libnx 官方 svc API](https://switchbrew.github.io/libnx/svc_8h.html) 提供 `svcGetDebugEvent`、`svcGetDebugThreadContext`、`svcContinueDebugEvent`、`svcQueryDebugProcessMemory`、`svcMapProcessCodeMemory` 等底层原语；但它们存在权限、进程句柄和调用状态限制，**不能直接推导当前 sys-botbase 具备这些能力**。[libnx ARM cache API](https://switchbrew.github.io/libnx/cache_8h.html) 列有 `armICacheInvalidate` 等操作，也不是当前远程协议的现成命令。

### Build ID 前缀的重要限制

上游 `getBuildID` 命令的 C 实现**只打印前 8 字节**（16 位十六进制字符），而非完整 20/32 字节。因此 RaidCrawler 的只读检查增加了 Build ID **前缀核验**，并且明确标记 `fullBuildConfirmed=false`；完整身份仍必须结合原始 ELF、NSO 和 8 条指令指纹，未来增强服务应提供完整 Build ID 及完整代码段签名。当前只适配《紫》4.0.0，不允许复用到《朱》或其他版本。

## A1 推荐架构（无需 Skyline）

```text
RaidCrawler.exe (Windows，USB/Wi-Fi)
  - 全局 45 槽奖励 JSON / BIN
  - 读完整 BuildID + 8 条 opcode + Main NSO ASLR base
  - 尝试协商「RemoteHookProtocol v1」特性
  - Install / Arm / Disarm / Uninstall / Status
  - 实时显示 hook-hit、patch-applied、patch-verified、错误及恢复
              |
              | 专用版本化协议、transactionId、响应 ack
              v
增强版 sys-botbase（Switch sysmodule，需要安装一次）
  - 长寿命 Debug session，不是每次 poke 都关句柄
  - 获取游戏目标线程的执行事件/寄存器（x23）
  - 验证 Title/完整 BuildID/程序内存段/原始机器指令
  - 安全分配或确认受控 RX 内存；确认可执行跳转可达
  - 停止与恢复所有相关线程；安装/卸载跳转，进行缓存同步
  - 进入回调时读取 0xAA0 明文，核验 RaidPoint_、奖励区域
  - 仅覆盖本次 720 字节，读回验证，必要时恢复
  - 事件队列避免 Hook 内直接 TCP/USB 或等待 Windows
```

> WinForms **不能在战斗序列化的微秒级临时窗口里接到 x23 指针后再慢慢通过 TCP poke**；这一小段代码必须预置在 Switch 进程可执行上下文或拥有执行控制的 Switch 侧服务中。Windows 侧只负责控制与配置。

### 建议的远程 Hook 协议状态机（设计稿，尚未实现）

```text
DISCONNECTED
  → CONNECTED
  → TITLE_BUILD_VERIFIED
  → EXTENSION_CAPABILITIES_VERIFIED
  → THREADS_QUIESCED
  → INSTALL_PREPARED (backup opcode / trampoline verified)
  → INSTALLED_DISARMED
  → ARMED (45 rows checksum + mode)
  → HOOK_HIT (one native callback)
  → PATCH_VERIFIED or PATCH_REJECTED
  → DISARMED → UNINSTALLED (restore opcode/cache/thread state)
```

所有步骤必须由增强 sysmodule 返回**明确的状态码和事务 ID**，拒绝未知 Build ID / 活动进程更换 / 新旧程序启动纪元不一致 / 缺少权限 / 原始指令异常 / 超时 / 已被其他模块 Hook 的地址。失去连接时应 fail-open（恢复未提交安装或自动 disarm），防止临时状态变成永久损坏。

### 拟增命令（建议，不是现有协议）

```text
hookCapabilities                  -> protocolVersion, supportedPrivileges, transport
hookPreflight                     -> fullBuildId, codeSignature, alloc/exec/cache status
hookInstall expectedSignature ... -> transactionId, state=INSTALLED_DISARMED
hookSetRewards 720Bytes checksum  -> ACK only, no live patch yet
hookArm transactionId             -> state=ARMED
hookEvents cursor                 -> HOOK_HIT, PATCH_VERIFIED, PATCH_REJECTED, etc.
hookDisarm transactionId          -> state=DISARMED
hookUninstall transactionId       -> original bytes verified, all related threads resumed
```

不直接复用 `pokeAbsolute` 命令处理上述事务。它没有足够的明确错误确认、权限信息和线程生命周期约束；Windows 端也不能只靠 `WriteBytesMainAsync` 返回 Task 完成便声称 Hook 已安装。

## 源码层面本次已完成的 P0

1. 新增 [RemoteHookCapabilities.cs](../../RaidCrawler.Core/Structures/RemoteHookCapabilities.cs)：标准 botbase 的能力矩阵；`RemoteInlineHookReady=false`；同时要求完整权限、已核实的游戏构建、未被覆盖的原机器码、房主进程身份、用户明确授权才允许将来的安装命令通过。
2. [RemoteHookCapabilitiesTests.cs](../../RaidCrawler.Tests/RemoteHookCapabilitiesTests.cs)：验证普通 botbase 即使可读写 RAM 也必须拒绝 Hook 安装；即使增强服务的全部能力齐全，任何一项运行时条件不成立也拒绝。
3. [RaidRewardDiagnosticForm.cs](../../RaidCrawler.WinForms/SubForms/RaidRewardDiagnosticForm.cs) 增加**只读** `getBuildID` 前 8 字节前缀读取，以及 `remote-hook-a` 缺失能力列表 JSONL 记录。不会将普通 sys-botbase 伪装成增强 sysmodule。

## 后续开发关口（严格阻断）

| 阶段 | 交付物 | 前置条件 |
|---|---|---|
| P1 | 兼容 Wi-Fi/USB 的标准 botbase 只读能力识别 | 连接实机确认 `getBuildID` 前缀、8 指令与 Title/版本 |
| P2 | 增强 sys-botbase 的专用事务及代码执行／调试能力 | 选定原版 sys-botbase 分支、核对 Horizon 系统权限及线程行为 |
| P3 | 真正可回滚的 ARM64 代码重定向与原生 x23 奖励 Hook | 实机验证 `main+0xE32984` 触发与执行线程、代码洞/映射、缓存维护 |
| P4 | 45 行全局奖励配置同步与读回回滚、Hook 事件回传 | 事务和原生拦截全部可靠；可进行最小化道具验证 |
| P5 | 不同对战模式及存档持久化验收 | 原生 HOOK_HIT、PATCH_VERIFIED、接收端/背包增量和保存证据 |

**当前方案 A 的研究结论是“可设计，但必须解决 Switch 侧执行控制”，不是“已实现无插件远程注入”。** 用户若接受替换当前 botbase sysmodule 而不想安装 Skyline NRO，A1 是更可控的方向。若绝不替换/扩展 sysmodule，现有 A0 证据不足以安全部署。
