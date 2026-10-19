# 紫版 4.0.0：只用 Windows + 原 sys-botbase 的「无重启奖励热更新」协议研究

日期：2026-10-19；`benefit`；**研究与测试阶段；未声称实机 Hook 已安装或命中。**

## 目标

用户不接受为了更改 45 槽、720B 奖励而重启游戏、替换 sys-botbase、复制 NRO/插件至 SD。Windows RaidCrawler 仍使用现有 `ReadBytesMainAsync` / `WriteBytesMainAsync`；已经连接的游戏不必退出；后续**新的**团战构建使用新奖励。若更新正好与一个团战构建重叠，该次构建安全地采用原始奖励，避免混合新旧模板。

## 为什么不是通常的双缓冲？

原 ELF 的 RX `.text/.plt` 段到 `0x343FC90` 结束，`0x3440000` 为下一 R 段的起点。最后 880B 位于已有可执行 `.plt` 同一 4KiB 页。ELF `RW` 段从 `0x4383000` 到 `0x47D3000`；`.bss` 尾端正好等于 `0x47D3000` 页面边界。**这并不意味着不能申请内存**，但原始 ELF 不提供一处经过证明的空闲 `RW 2×720B` 区域；不能把任何全零 .bss 字段擅自当成备用缓冲区。

## 单缓冲版本号设计（seqlock + fallback）

原始 880B 不变，元数据只占用已有 `+0x90..+0x9F`：

| Cave 偏移 | 长度 | 内容 |
|---|---:|---|
| `+0x00..+0x8F` | 144 | 替换为保护现场、两次版本检查及冲突恢复逻辑 |
| `+0x90` | 4 | Little endian `epoch`; **奇数停用/更新中，偶数启用** |
| `+0x94` | 4 | 协议魔数 `RHS2`（小端 0x32534852），拒绝旧模式字 |
| `+0x98` | 8 | 用于校验明文的 `RaidPoin` |
| `+0xA0` | 720 | 45×16B 奖励模板 |

原协议从 `+0x90` 的 0/1 模式位运行，**与此协议不兼容**。不能只更新 Windows 的代码而继续使用旧跳板；新旧两端必须整套版本匹配。

### Reader（由游戏 CPU 在 serializer 回调中执行）

1. 保存 `x9..x16` 和 `NZCV`；`x8` 最终执行入口被覆盖的原指令 `mov x8,x21`。不调用任何额外 `memcpy`。
2. `LDAR` 读取 epoch；奇数直接跳过，使用游戏已复制好的原始奖励。
3. 校验 `x23+0x6B8` 的 `RaidPoin` 前缀；不匹配跳过。
4. 若偶数，复制 `+0xA0` 720B → `x23+0x79C`。
5. 再次 `LDAR` 读取 epoch；若前后相等则接受完整模板。若发生变化，立即从**本次调用的原始消息对象** `x20+0x13C` 恢复 720B 至 `x23+0x79C`。
6. 完整恢复寄存器/状态并返回原 serializer 的下一条指令。

**ELF 直接依据**：`E32968: add x1,x20,#0x78`、`E3296C: mov w2,#0x3C8`、`E32980: bl memcpy`。原始奖励从 `x20+0x13C` 对应 `RaidPoint+0xE4`；副本在 `x23+0x79C`。这提供了“冲突时恢复源数据”的精确依据，而不是臆造一个长期稳定的 RaidPoint 指针。

为验证空间，独立使用 `clang --target=aarch64-none-elf` 和 `llvm-objdump` 做了**离线汇编原型**，大小 880B；`+0x8C` 是返回指令占位，`+0x90` 是 epoch，`+0xA0` 是模板。**原 GitHub `native/sv_remote_code_patch/stub.S` 仍是旧实现；未替换，也未实机执行**。

### Writer（Windows 端）

1. 验证 TitleID、游戏版本、Build ID 前缀、当前 Main NSO base、入口跳转指令以及 **RHS2** 布局签名。拒绝旧版 0/1 模式 Hook。
2. 先把 epoch 写为一个**新奇数**，回读验证后才开始改奖励；整个期间未进入 Hook 的回调使用原始奖励。
3. 写 720B 到固定 `+0xA0`，读回并按字节比较。
4. 若原状态启用，最后提交新的**偶数** epoch；原状态停用则保留奇数。任何传输异常都不应擅自重新启用。
5. 在途的旧版本回调发现 epoch 不匹配，执行 Reader 第 5 步恢复；下次构建直接使用新模板。允许下一团战用新奖励，**无需重启游戏**。

已有 `RaidCrawler.Core/Structures/RemoteRewardEpochUpdater.cs` 编写了 Writer 的传输无关版本及模拟测试，安装器／WinForms 按钮尚未接入，因为旧跳板不理解该协议。该类使用调用者提供的每次进程身份验证；不包含可欺骗式的“已实机验证”标记。

### 缓存顺序与执行可见性

`olliz0r/sys-botbase` 的 `poke` 最终调用 `svcWriteDebugProcessMemory`。Atmosphère/Mesosphère 的 `KPageTableBase::WriteDebugMemory` 在成功复制写入内容后运行 `StoreDataCache`，并 `InvalidateEntireInstructionCache`，允许修补 RX 段；此为**开源实现检查**，不自动证明用户设备的精确版本、实时调度或写入确认。
参考：[Mesosphere 实现](https://github.com/Atmosphere-NX/Atmosphere/blob/master/libraries/libmesosphere/source/kern_k_page_table_base.cpp) 与 [sys-botbase commands.c](https://github.com/olliz0r/sys-botbase/blob/master/sys-botbase/source/commands.c)。

安全前提：单个 Windows 写入者，按照上述阶段严格串行并完整回读，4B aligned version word 在目标 ARMv8 内存模型上可作为原子状态发布。不得使用 `pokeMain` 传输 720B 后直接把未验证的模板视为生效。读回校验和普通 C# 模拟不足以证明跨核缓存和执行时序，必须在受控本机环境采样。

### 竞态结果矩阵

| Reader 观察 | 结果 |
|---|---|
| 进入时 epoch 奇数 | 保留原始奖励 |
| 前后偶数 epoch 相同 | 自定义 720B 完整替换 |
| 复制期间 epoch 变更 | 从原始 `x20` 对象恢复 720B，保留原始奖励 |
| 写入或回读失败 | 维持奇数停用态，不能报告成功 |
| 游戏重启、NSO base 变化或签名不匹配 | 当前内存事务立即失效，拒绝复用旧地址 |
| 两个 Windows 写入者并行，或外部工具同时修改内存 | 当前不支持，必须拒绝或串行化 |

注意：若一个回调先于新 epoch 提交完成，它可能合理使用旧模板；此设计保证一致性而不是回溯修改已经封装/发送的战斗。绝不能把它称作“每场更新强制生效”。

## 尚未完成的关键验收

- 新汇编与构建器一同升级，修正旧脚本中硬编码 `+0x48/+0x54` 的错误重定位槽；新草案返回槽为 `+0x8C`。
- 新跳板内真实 `LDAR` / epoch 校验及回滚路径的 CPU 实机执行证据；尤其要测试 Windows 改模板正好与 45 行复制交错。
- 变更 Main NSO code tail 是否无运行时覆盖、CPU 实际使用同页 RX 的权限确认。
- WinForms 的安装、停用、启用、模板更新及只读状态显示的有权限集成和用户明确触发。
- 三点数据一致性 `源 RaidPoint → Bootstrap → 接收方过滤列表`、道具实际增加和存档保留仍是独立验收。

**结论**：这是适应 880B 限制的、不重启游戏的**可行候选算法**，已具备 Writer 和离线静态布局基础；尚未完成 Hook 实机命中或端到端发奖验收。
