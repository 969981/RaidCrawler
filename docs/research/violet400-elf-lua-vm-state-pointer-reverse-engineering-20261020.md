# 《宝可梦 紫》4.0.0 ELF — Lua VM 初始化、状态指针与全局引用逆向分析

> 研究时间：2026-10-20；仓库：`969981/RaidCrawler` 的 `benefit` 分支。**结论范围：已独立复核精确 ELF 的静态 ARM64 指令和指针链；尚未获得 Switch 运行时状态指针、实际 Lua VM 调用帧、A/B/C 奖励数据或发奖成功证据。**
>
> 本文记录**逆向研究证据**，不包含游戏原始 ELF、main.blua、ROM、私有 Ghidra 工程，也不提供未验证的自动注入/奖励写入补丁。

## 1. 样本身份与 ELF 映射基线

- 游戏：《宝可梦 紫》，更新版本 4.0.0，Title ID `01008F6008C5E000`。
- 精确 ELF：`Violet4.0.0.elf`，文件大小 `74,362,480` 字节。
- ELF SHA-256：`d17403eaa9d844db82f647fd2f1518806b10c0518d53602fc0ac1766298fdba0`。
- 与之配套的 Lua 5.4 chunk：`main.blua`，大小 `12,912,264` 字节，SHA-256 `c1a4f4e2625912cf0b739a642a79ba3357df0557a544b92da22a6cb139de2815`。
- 先前确认的 Build ID：`709bfd66115298640155fcc4979dba151c7cc79a`（实机工具读取 Build ID 时可能只返回前 8 字节；不能将前缀等同完整指纹）。
- ELF64 / AArch64 / Little Endian / `ET_DYN`。本文的 `main+RVA` 均以 NSO `.text` 的静态 RVA 表示，不是 ASLR 后的绝对虚拟地址。

直接解析程序头所得的 `PT_LOAD`：

| Segment | Flags | 文件偏移 | 虚拟地址起点 | 文件大小 | 映射内存大小 |
|---|---|---:|---:|---:|---:|
| `.text` | R-X | `0x8D8` | `0x00000000` | `0x343FC90` | `0x343FC90` |
| `.rodata` | R-- | `0x3440568` | `0x3440000` | `0xF420A0` | `0xF420A0` |
| `RW / data+bss` | RW- | `0x4382608` | `0x4383000` | `0x368868` | `0x450000` |

**重要的额外确认：** 全局槽 `main+0x4759EB0` 位于 RW 映射 `[0x4383000,0x47D3000)` 内，且位于该段文件初始化数据末端 `0x46EB868` 之后，所以属于**内存中零初始化区域**。不能拿 ELF 文件中某个相同 RVA 的字节解释它运行后的指针内容；必须对已加载游戏进程做只读采样。

`.text` 内地址到文件偏移的换算为 `file_off = RVA + 0x8D8`；这一关系**不能用于 RW BSS 槽的文件读取**。

## 2. 六个原生位置与所证实的职责

| 位置 | 已复核证据 | 静态判断与保留条件 |
|---|---|---|
| `main+0x3B420` | 函数为内部状态调用分配器请求 `0x658` 字节，随后大量初始化状态字段，并返回 `x0` | Lua 主状态构造函数的**高置信候选**；尚无实机已创建 `lua_State` 的独立鉴别 |
| `main+0x3B050` | `mov w1,#0x2d0` → 分配器调用；`str x0,[x19,#0x30]` 和 `[x19,#0x40]`；逐个 16B 槽初始化 | Lua 栈分配/初始化；`0x2D0=720B` 是**栈缓冲区**，不是 RaidPoint 奖励的 45×16B |
| `main+0x26075E0` | 通过分配器申请 `0xD0`、填入父状态及管理字段，`main+0x26076B8` 再 `BL 0x3B050` | Lua 子线程/协程相关状态构造候选；不能默认这就是当前单人战斗执行的线程 |
| `main+0x1107AF4` | `BL 0x3B420` | 游戏脚本上下文调用上述主状态构造候选 |
| `main+0x1107B00` | `STR X0,[X19,#0x70]` | 构造返回值保存到脚本上下文的 `+0x70` 字段 |
| `main+0x4759EB0` | 初始化指令 `ADRP ...; STR X22,[X8,#0xEB0]`，销毁路径有对照后清零 | 持有 `context+0x88` 的全局槽；**槽内容不是直接的 lua_State 指针** |

上述语义由机器码+调用关系支持；本文件刻意区分了**指令本身可确认**与**对象类型/动态生命周期尚待实机证明**。

## 3. 关键汇编片段、机器码和调用链

### 3.1 初始状态构造

以下为精确 ELF 上的片段（`llvm-objdump -d`）：

```asm
0x3B420:  sub  sp, sp, #0x50
0x3B438:  mov  x21, x0
0x3B43C:  mov  w3, #0x658
0x3B440:  mov  x0, x1
0x3B444:  mov  x1, xzr
0x3B448:  mov  w2, #0x8
0x3B450:  blr  x21              // 机器码 D63F02A0，分配 0x658
0x3B454:  mov  x19, x0
0x3B458:  cbz  x0, 0x3B5A0
...
0x3B5A0:  mov  x0, x19
0x3B5B4:  ret
```

`0x3B5DC: BL 0x3B050` 在其后相关初始化函数中；分支指令编码 `97FFFE9D`。

### 3.2 栈空间的真实 720B 来源

```asm
0x3B050: stp  x29, x30, [sp,#-0x20]!
0x3B060: mov  x19, x0
0x3B064: mov  w1, #0x2d0      // 机器码 52805A01
0x3B068: mov  x0, x8
0x3B06C: mov  w2, wzr
0x3B070: bl   0x2D7B0        // 机器码 97FFC9D0，分配器
0x3B074: str  x0, [x19,#0x30] // 机器码 F9001A60
0x3B078: str  x0, [x19,#0x40] // 机器码 F9002260
0x3B07C: strb wzr, [x0,#0x8]
...
```

`0x26075E0` 一带独立分配 `0xD0` 字节对象，`0x26076B8` 的 `BL 0x3B050`（机器码 `9768CE66`）表明子状态也复用这个栈初始化过程。只看分配大小 `720` 去寻找“太晶坑奖励数组”是明确错误的推断。

### 3.3 游戏脚本上下文与全局引用建立

```asm
0x1107A64: mov x22, x19              // AA1303F6
0x1107A84: str xzr, [x22,#0x88]!     // F8088EDF；写后 x22=context+0x88
...
0x1107AF4: bl  0x3B420              // 97BCCE4B，Lua 主状态创建候选
0x1107B00: str x0, [x19,#0x70]      // F9003A60，保存返回的状态
...
0x1107B34: adrp x8, 0x4759000      // D001B288
0x1107B38: str x22, [x8,#0xeb0]    // F9075916
```

AArch64 `ADRP` 搭配 12-bit 字节偏移，得到 `0x4759000 + 0xEB0 = 0x4759EB0`。这里写入的 `X22` 已因带感叹号的 pre-index `STR` 被更新为 `context + 0x88`。

### 3.4 清理阶段的同一引用比较

```asm
0x2607874: adrp x8, 0x4759000      // D0010A88
0x2607878: ldr  x9, [x8,#0xeb0]    // F9475909
0x260787C: add  x20, x19,#0x88    // 91022274
0x2607880: cmp  x9, x20           // EB14013F
0x2607884: b.ne 0x260788C
0x2607888: str  xzr,[x8,#0xeb0]   // F907591F
```

这证明**同一个全局槽**会在相同上下文对象的清理分支中被有条件置零。由此应避免缓存指针跨游戏重载、存档切换或 Script 环境析构。

## 4. 只读状态候选推导与最低限度防错

令 `M=GetMainNsoBaseAsync()`，全局地址槽 `G=M+0x4759EB0`。

```text
ref = read_u64(G)                  # candidate = context + 0x88
ctx = ref - 0x88                   # DO NOT treat ref itself as lua_State
L   = read_u64(ref - 0x18)         # context + 0x70 = (context+0x88)-0x18

# 仅表达 ELF 静态字段关系，未验证 L 是仍然存活的 lua_State。
```

现有实现 `Violet400LuaVmRootProbe` 只有最多四次 **8B 只读**：Main 全局槽、推导的 State 字段、再读 State、再读 Main；在调用前还做 Title ID、更新版本、Build ID 前缀、原生指令和 NSO 基址检查。只有非零、合理对齐、两次一致的候选才能返回 `CANDIDATE_POINTER_STABLE_NOT_VM_VERIFIED`；否则保持失败关闭。

**不应做的事：**

1. 将这条候选链当作单人战斗 Lua VM 已经执行的证据；
2. 将 Lua 中 `BattleRewardData[3]`、`self[18][92][3]` 解释成原生 C 结构 `base+3` 或 `base+18` 等裸字节偏移；
3. 对尚未确认生命周期的 Table、TValue、Lua GC 对象执行内存修改；
4. 把过去的 `main+0xE32984` 网络出站 `0x012F` 序列化覆盖 Hook 等同于单人本地 Lua 发奖；
5. 根据一条成功内存读取声称“任意坑 20×999 道具已到账”。

## 5. 与 `main.blua` 已验证的三个关键函数关联

先前已完整解析 37,743 个 Lua Prototype，并通过根函数的 `LOADK(method hash) -> OP_CLOSURE -> CALL` 注册序列绑定：

| 业务阶段 | 4.0.0 Lua Proto | 字节码**文件偏移** | 关键逻辑位置 |
|---|---|---|---|
| A0 原版生成：`FieldRaidJewelry.LotteryRewardItem` | `root/2899` | `0x377501` | 主返回 PC475，另有提前返回分支 |
| B 过滤：`F316609DEA110601D` | `root/4870` | `0x410673` | `LOADI 2481` PC8；写新 `[3]` PC134 |
| C 结算：`RaidResultAddItem` | `root/3756` | `0x3B1E39` | `GETI [18]->[92]->[3]`，物品添加调用 PC66 |

**Lua 字节码文件偏移与 ELF AArch64 RVA 是两个不同的地址空间。** 新获得的全局 Lua 环境/状态候选，只是把实际游戏 VM 身份验证提前到了一个可复查的指针起点；尚未建立 A0/B/C 三个活动 closure、Proto/CallInfo、线程与奖励列表的运行时一一对应。

## 6. 复现流程与通过门槛

要求保留自己的 `Violet4.0.0.elf`，不上传二进制：

```bash
sha256sum Violet4.0.0.elf
llvm-objdump -d --start-address=0x3B420 --stop-address=0x3B5B8 Violet4.0.0.elf
llvm-objdump -d --start-address=0x3B050 --stop-address=0x3B0B0 Violet4.0.0.elf
llvm-objdump -d --start-address=0x26075E0 --stop-address=0x26076D0 Violet4.0.0.elf
llvm-objdump -d --start-address=0x1107A64 --stop-address=0x1107B3C Violet4.0.0.elf
llvm-objdump -d --start-address=0x2607874 --stop-address=0x260788C Violet4.0.0.elf
python tools/research/audit_violet400_lua_vm_root.py Violet4.0.0.elf --output lua-root-elf-audit.json
python -m unittest discover -s tools/research -p 'test_audit_violet400_lua_vm_root.py' -v
```

该审计脚本校验 SHA256、**19 个 ARM64 指令原始 opcode** 以及 3 个 BL 目标（`0x1107AF4→0x3B420`、`0x3B5DC→0x3B050`、`0x26076B8→0x3B050`）。本轮已在提供的**原始 ELF** 上重新计算哈希和独立读原始指令：19/19 均匹配，3/3 BL 目标匹配。**这是 ELF 静态证明，并非 Switch 动态执行证明。**

真正的后续验收依次为：读取全局槽时身份/指针稳定；独立判定活动 `lua_State` 与线程/GC 安全；同一场 A0/B/C 的真实列表内容；先单条 `subject=5` 奖励添加、背包增量和重载持久化；最后才轮到 20×999、三地图/活动坑与其他玩家联网验收。

## 7. 关联代码与记录

- [P1-B 只读诊断与指针链方案](violet400-p1b-lua-vm-global-root-audit-20261020.md)
- [Lua 4.0.0 三个业务方法的精确 Proto 绑定](violet400-exact-main-blua-reward-p1-20261020.md)
- [Lua 对象生命周期与 ELF Native 边界](violet400-abc-native-entry-and-lua-lifetime-audit.md)
- [A/B/C 捕获证据协议](violet400-abc-runtime-capture-contract.md)
- [ELF 静态验证脚本](../../tools/research/audit_violet400_lua_vm_root.py)
- [只读状态候选实现](../../RaidCrawler.Core/Structures/Violet400LuaVmRootProbe.cs)
- [P1-B 模拟读取测试](../../RaidCrawler.Tests/Violet400LuaVmRootProbeTests.cs)

**审计结论：这些静态地址与字段推导可信，但“真实活动 Lua 状态”“奖励 Hook 生效”“联网玩家领取 ×999”仍未验证。**
