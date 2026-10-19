# Violet 4.0.0 ELF + PokéLDN：原生奖励 Hook 两个阻断项的复核（2026-10-19）

**范围：只读 ELF / 代码审计 / 离线一致性。没有 Switch 实机跳转执行或发奖验证。**
**约束：继续复用现有 sys-botbase；不安装额外 Switch 插件。**

## 样本直接复核

用户文件库的原始 `Violet4.0.0.elf` 本次已重新读取：74,362,480B，SHA-256 `d17403eaa9d844db82f647fd2f1518806b10c0518d53602fc0ac1766298fdba0`。这是直接二进制检查，不是援引之前的摘要。

`readelf -W -l` 给出：

| Segment | RVA | memsz | Flags |
|---|---|---:|---|
| text / plt | `0x00000000` | `0x343FC90` | **R E** |
| rodata | `0x3440000` | `0xF420A0` | R |
| RW | `0x4383000` | `0x450000` | RW |

4KiB 页面：`[0x343F000, 0x3440000)`。最后一条有效的 `.plt` 指令为 `0x343FC8C: br x17`；紧随其后 `0x343FC90..0x3440000` 剩余 **0x370=880B**。区域和已有可执行 `.plt` **处于同一个 4KiB 页**，不是两种段权限在不同页之间的空闲页。因此候选尾部属于 RX 页的可信度大幅提高。**仍未从实际 NSO 运行时内存映射查询取得 Execute 权限直接证据，且读全零不等于证明运行。**

实机用户 JSONL 已提供 Build ID 前 8 字节匹配、原版 8/8 opcode 匹配、880B 只读全零；没有真实 HOOK_HIT。

## 阻断 1：汇编/重定位的准确结论

已重新反汇编 `0xE32974..0xE329B0`：

```text
E32980  BL memcpy@plt       （把 RaidPoint 后段写入 x23）
E32984  MOV x8,x21          （被候选 inline branch 替代的原指令）
E32988  MOV x0,x20
E3298C  LDR x25,[sp,#0x10]
E32990  MOV x1,x23
E32994  MOV w2,w22
E32998  MOV x3,x19
E3299C  LDP x20,x19,[sp,#0x40]
E329A0  MOV w4,#1
E329A4  LDP x22,x21,[sp,#0x30]
E329A8  LDP x24,x23,[sp,#0x20]
E329AC  LDP x29,x30,[sp],#0x50
E329B0  B 0x1592B7C
```

**关键修正**：前一份报告将「第二次 memcpy 会污染 caller-saved 寄存器」称为必然故障过于武断。这里原函数在 `memcpy` 后明确重新设置 `x0..x4, x8`，且在尾跳前未明显再次读取 `x9..x18`。所以按当前可见活性，旧补丁 **不能仅凭 x9..x11 被修改就判定必然崩溃**；它依然有未经 ABI 和尾调用输入完整验证的风险。较完整保护寄存器并避免第二次 `BL` 是更稳妥的工程选择，但不是已经证明旧代码实际失效的证据。

已对离线候选对象 `stub_checked.o` 重新 objdump：
- `+0x74` = 恢复原来的 `MOV x8,x21`。
- `+0x78` = 返回分支占位。
- `+0x90` = mode，`+0xA0` = 720B reward data。
- `+0x48` = 计算模板地址，`+0x54` = 循环内加载，并**不是**旧 `build_offline_plan.py` 约定的 BL/B 可替换槽。

**已确定并可复现的构建问题**：仅替换 `stub.S` 而沿用旧生成器会产生位置检查失败，若移除检查则可能覆写有效指令。生成器与汇编布局必须同一版本签名、经 objdump 对照再产出二进制。当前没有提交可执行的新跳板或安装器，不能标记为已修复实机问题。

## 阻断 2：为什么只能先做“会话不可变模板”

`969981/pokeldn@research/pokeldn/sv/raid.py`：
- `RaidGroup.__init__` 复制自定义奖励为不可变 tuple；
- `RaidGroup.bootstrap()` 首次构建 `self.plain`，之后直接复用；
- `RaidHost._next()` 调用 `bootstrap_message(self.group.bootstrap())`，压缩并发送同一份会话快照；
- `raid_point()` 将最多 45 行 `<IIII` 放在 `RaidPoint+0xE4`。
所以**一次会话不能混合两版模板**；原生游戏可能调用 serializer 多次，不能从 Python 的缓存行为反推它只执行一次。

`olliz0r/sys-botbase/commands.md` 没有线程暂停、持久调试会话、回调计数、锁操作；`commands.c` 的 `poke` 是 `attach() → writeMem() → detach()`，内部 `svcWriteDebugProcessMemory`，不会等待正在执行的游戏代码返回。一个只读 mode 标志=0，最多只阻止后续新回调应用模板；此前已经读到 mode=1 的回调可能仍在复制。即使 720B 读回 SHA 匹配，也不等于没有一半旧一半新的在途奖励。

因此**仅靠普通 sys-botbase、单个 880B code cave 和无 RW 计数区，无法证明游戏运行中热更新 720B 模板的安全性**。这是能力和同步证据缺口，而不是另一个“通讯写入问题”。

### 可落地的保守策略（无需增加 Switch 插件）

1. 所有编辑先提交到 Windows 本地的新版本快照；当前游戏进程已经启用的 720B 必须保持不可变。
2. 关闭 mode 只影响新的回调；不因此宣称旧回调已退出。
3. 未获得可核实的回调静止证明时，**不进行本进程原地模板替换、擦除 code cave 或释放仍可能使用的缓冲区**。
4. 新奖励设置在完成游戏进程退出／重新启动后成为下一会话的候选模板。验证新进程的原始入口以及尾部全零；重新计算 ASLR base，旧进程偏移和上次安装事务不得复用。
5. 若要求游戏内不用重启、立即热修改，则还需真正的 quiescence 原语或能证明原子读取一个**不可变双缓冲**模板的方案；880B 仅容纳一套 720B，且标准 botbase 不能直接提供这样的同步证明。因此这一增强功能不可冒充已支持。

### 验收层级

| 层级 | 状态 |
|---|---|
| 原始 ELF / Last PLT / 4KiB 同页关系 | 本次直接确认 |
| 新候选汇编 880B 静态布局 | 本地离线确认 |
| 原始 builder 与新汇编偏移一致 | **否，阻断** |
| 安全的在线无重启模板热更新 | **无同步证据，禁止宣称** |
| 游戏端真实 ARM64 跳板执行 | 未实测 |
| Source RaidPoint → Bootstrap → Receiver 筛选 | 上游部分研究；实机尚缺闭环 |
| 发奖 Bag 入库、实际存档 | 未实测 |

引用：
- [PokéLDN raid.py](https://github.com/969981/pokeldn/blob/research/pokeldn/sv/raid.py)
- [PokéLDN S1→S2→S3 一致性研究](https://github.com/969981/pokeldn/blob/research/docs/research/sv/raidpoint-bootstrap-receiver-consistency.md)
- [Atmosphere Mesosphere](https://github.com/Atmosphere-NX/Atmosphere/blob/master/libraries/libmesosphere/source/kern_k_page_table_base.cpp)
- [sys-botbase commands](https://github.com/olliz0r/sys-botbase/blob/master/commands.md)
