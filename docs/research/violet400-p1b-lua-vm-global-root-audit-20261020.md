# 《紫》4.0.0 P1-B：Lua VM 主状态创建与全局引用链（只读验证）

状态：**ELF 静态候选已精确验证；实机读取、Lua VM 调度、BattleRewardData A/B/C 均未验证**。2026-10-20。实现分支：`RaidCrawler/benefit`。

详细的 ELF 程序头分段、19 处 ARM64 指令原码、三个 BL 目标、全局 BSS 槽位置及可复现 objdump 命令，已另行整理为 [《紫》4.0.0 ELF — Lua VM 状态指针逆向技术文档](violet400-elf-lua-vm-state-pointer-reverse-engineering-20261020.md)。本文保留为 P1-B 功能状态与实机验收入口。

## 1. 用户提供的精确原始输入

- `Violet4.0.0.elf` 74,362,480B；SHA256 `d17403eaa9d844db82f647fd2f1518806b10c0518d53602fc0ac1766298fdba0`。
- `main.blua` 12,912,264B；SHA256 `c1a4f4e2625912cf0b739a642a79ba3357df0557a544b92da22a6cb139de2815`。
- `LotteryRewardItem = root/2899`、`F316... = root/4870`、`RaidResultAddItem = root/3756` 的精确 Lua Prototype 静态绑定见 [上一阶段](violet400-exact-main-blua-reward-p1-20261020.md)。字节码 PC 不是 AArch64 运行时指令地址。

## 2. 本次由真实 ELF 原始机器码交叉确认的新静态链

| AArch64 main+RVA | 实际指令/行为 | 证据意义 |
|---|---|---|
| `0x3B420` | 初始状态创建函数，间接调用分配器，大小 `0x658` | **Lua 状态构造候选**，不表示已捕获活动状态 |
| `0x3B050` | 分配 `0x2D0`，将返回地址写到 `[x19+0x30]` 和 `[x19+0x40]`，按 16B 初始化槽 | **Lua 栈初始化候选**；这是 720B *stack*，不是奖励表 720B |
| `0x3B5DC` | `BL 0x3B050` | 主 Lua 状态初始化上下文 |
| `0x26076B8` | `BL 0x3B050`；所在函数前面分配了 `0xD0` 的对象 | 协程/线程创建路径也会初始化栈 |
| `0x1107A64` | `MOV X22,X19` | 脚本环境对象起点的别名 |
| `0x1107A84` | `STR XZR,[X22,#0x88]!` | X22 变成环境对象 `+0x88` |
| `0x1107AF4` | `BL 0x3B420` | 游戏脚本环境创建 Lua 主状态 |
| `0x1107B00` | `STR X0,[X19,#0x70]` | 状态返回指针写入脚本环境 `+0x70` |
| `0x1107B34..38` | `ADRP X8,0x4759000; STR X22,[X8,#0xEB0]` | `main+0x4759EB0` 全局槽 = 环境对象 `+0x88` |
| `0x2607874..88` | 从上述槽读指针，与 `context+0x88` 比较，一致才写零 | 清理阶段的匹配/失效验证，证明全局引用有生命周期 |

此表中的原生指令通过 `tools/research/audit_violet400_lua_vm_root.py` 的 19 个 opcode 指纹与 BL 目标校验得到；**不能把这些静态偏移直接当作被证实稳定的 Switch 虚拟地址**。

因此给出待实机验证的状态候选只读链：

```text
reference = *(uint64*)(MainBase + 0x4759EB0)
scriptContextCandidate = reference - 0x88
luaStateCandidate = *(uint64*)(reference - 0x18)
# 0x18 = 0x88 - 0x70
```

- 这里 `reference` **不是 Lua 状态指针**，而是一个脚本环境内部地址候选；先减偏移才能推导 Lua 状态字段。
- 即使指针稳定，也不能证明 `lua_State` 在当前主线程执行、某个 Proto 已装载、更不能证明 A/B/C 捕获。
- 程序只能在**检查准确 Violet TitleID、4.0.0、Build ID、Main NSO 基址**后读取。拒绝空指针/未对齐/改动中的数据；没有这些条件不允许盲扫堆。
- 解析 Lua 运行时 Table/TValue/Closure 仍需另外的生命周期和 GC 安全研究，不能用类似 `[3]` 等 Lua 索引直接叠加到 Native 指针。

## 3. 代码与 UI 接入

新增：
- `RaidCrawler.Core/Structures/Violet400LuaVmRootProbe.cs`：每次最多两次 Main 槽读取 + 两次 Absolute 状态字段读取（各 8B）。检测 NULL、非法/未对齐、状态变更，全程只读。对稳定但未经验证的指针输出 `CANDIDATE_POINTER_STABLE_NOT_VM_VERIFIED`。
- `RaidCrawler.Tests/Violet400LuaVmRootProbeTests.cs`：模拟设备读取，测试正常、空指针、未对齐、字段空、采样期间改变、短读和取消。作为 Windows 构建阻断单测。
- `RaidCrawler.WinForms/SubForms/RaidRewardDiagnosticForm.LuaVm.cs`：连接当前诊断窗口，新增 **高级工具 → P1-B：只读探测 Lua 状态候选**。沿用前置 `VerifyViolet400IdentityAsync`，核对 MainBase 采样前后不变。日志只给出状态，不记录完整堆指针。
- `tools/research/audit_violet400_lua_vm_root.py` 及配套测试：离线 exact ELF 指令/BL 审计。

**实际运行方式：** Windows RaidCrawler 连接自己的 Switch → 奖励编辑 → 诊断 → 高级工具 → P1-B 只读探测。一次点击既不装新插件，也不更改游戏内存、奖励表或存档。输出 `lua-root-readonly` 状态记录。不具备连接时将显示阻断/异常，而不是假造结果。

请注意：此时收到 `CANDIDATE_POINTER_STABLE_NOT_VM_VERIFIED`，最多说明预期位置当前存在稳定、可读的指针链；**不能显示 Hook 命中**，也不能标记 P1-C 完成。原 sys-botbase 2.5 无标准 Lua 调用帧捕获接口；A/B/C 仍无法仅靠这个功能自动采集。

## 4. 可复核的离线命令

```bash
python tools/research/audit_violet400_lua_vm_root.py /path/to/Violet4.0.0.elf \
  --output violet400-vm-root-static.json
python -m unittest discover -s tools/research \
  -p 'test_audit_violet400_lua_vm_root.py' -v
```

精确版本 ELF 不上传至仓库；若用 CI 无原始游戏文件，SHA 门控的完整 ELF 单测显式跳过，不宣称在 CI 中拥有原游戏文件。

## 5. 下一道 Gate：Lua VM 调用层身份与单人 A/B/C

**P1-B1（当前只读）**：实机测量全局引用链非空/稳定；验证游戏进出、关闭或版本不同情况下能正确回退/拒绝。此项暂未得到你的实机日志。

**P1-B2（尚未实现）**：从单独受控 Lua VM 执行观测中确认活动 Lua 状态属于某个实际运行的 closure/Proto，定位 `LotteryRewardItem` 返回、`F316...` 过滤返回及 `RaidResultAddItem` 调用；正确处理 thread/coroutine 与 GC。必须是可信执行时点，不能用窗口每几秒读取代替。

**P1-C（尚未实现）**：在同一个 runId 记录 A0 全部原版行、B 新的 `[3]`、C 当前结算的 `[18][92][3]`；借助 [ABC 证据契约](violet400-abc-runtime-capture-contract.md) 对照。尚缺可安全执行的采集适配器。

**P1-D/E（尚未实现）**：只有动态证据闭环后，先一条×1/subject5→背包实际增加→重新载入保留。以后才可尝试 20×999、多地图、本地联机和其他联网模式。

**本轮不安装新 Hook，不写入游戏，不声称你本人/其他联机玩家获得了奖励。**
