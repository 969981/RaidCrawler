# Violet 4.0.0：过滤／结算执行点与 Lua 对象布局原生边界审计

2026-10-20；研究基线：Violet4.0.0.elf 原始 SHA256
`d17403eaa9d844db82f647fd2f1518806b10c0518d53602fc0ac1766298fdba0`；
Build ID `709bfd66115298640155fcc4979dba151c7cc79a`；
SV-Script-RE `1f843bd2517c707fbb47491dad58856eaa2c2cc7`。

**结论：已完成 ELF 静态 Native 注册与 Lua 业务目标的分类；尚未得到 B/C 两个 Lua 方法的专属 Native RVA、lua_State 根指针、运行时 RewardData 实例或任一实机回调记录；不能部署所谓已证实的单人奖励 Hook。**

## 1. 两个实际采样目标是什么

### B：BattleRewardData.F316609DEA110601D，过滤后的瞬时列表

[Lua 实现](https://github.com/Martmists-GH/SV-Script-RE/blob/1f843bd2517c707fbb47491dad58856eaa2c2cc7/decompiled/C07C34FC3B976E5A7.lua#L182-L321)：

- `C07C34FC3B976E5A7.new` 创建 Haxe/Lua 类实例；`super` 用 `L26_1.new` 构造 `self[3]` 作为 Lua 数组。
- `F316...` 从当前对象 `self[3]` 遍历 `ItemNo`、`ItemNum`、`rareType`、`subjectType`，调用 `Bag.GetNumItem(2481)>0` 获得护符资格。
- **在过滤返回前 `self[3] = L14_2` 替换整个数组引用**，而不是要求就地覆盖某个固定长度 720B 原生数组。因此“函数入参 self[3] 指针”和“函数返回后 self[3] 指针”可能不同。
- **B 的采集时机**应是 `F316...` 返回后立即读取同一 Lua `self` 的新 `[3]`。仅拍函数入口或保留旧 `[3]` 的快照不够。
- 用户真机有晶耀护符，此处验证条件按 `hasCharm=True` 设计，不再要求用户重复证明。只有模板先进入原始列表，才谈得上 `subject=5` 资格筛选。

### C：GameEventBattleResult.RaidResultAddItem，实际消费的瞬时列表

[Lua 实现](https://github.com/Martmists-GH/SV-Script-RE/blob/1f843bd2517c707fbb47491dad58856eaa2c2cc7/decompiled/C889F6553EDFCD011.lua#L1625-L1697)：

- `RaidResultAddItem` 先解引用 `self[18][92][3]`，遍历每行 `ItemNo / ItemNum`。
- 内部调用 `Bag.IsFullBagItem` 和 `Bag.GetNumAddItem`（代码哈希分别 `F2001F41E6B757BB1`、`F6C18EEC27ED4BA1E`），最后 `Bag.AddItem`（`FB01A9B29CD4CC6CB`）。后者本身是**Lua 脚本方法**，并不是 ELF 中已找到的固定 A64 RVA。
- **C 的采集时机**是进入 `RaidResultAddItem` 后、遍历/容量调整/添加背包之前，读取 `self[18][92][3]`。这是游戏认为“准备发奖”的原始行，不等于最终实际添加的数量。

### A→B→C 源码对象生命周期（已证实为逻辑参数传递，非物理指针不变）

```text
FieldRaidJewelry.LotteryRewardItem
  return BattleRewardData Lua instance
    [3] = Haxe/Lua list<Row> (ItemNo,ItemNum,rareType,subjectType)
    ↓ FieldRaidJewelry 保存生成的对象、作为事件实参传出
GameEventNetworkBattleRaid.Call(param.raidBattleRewardData)
    ↓ 事件实例 self[17].raidBattleRewardData
    ↓ L58_1 wrapper/转换，存为 self[19][92]
BattleRewardData.F316609DEA110601D(...)
    ↓ 遍历原 self[3]，**新建数组并令 self[3] = 新数组**
    ↓ self[19] 作为 setupData 被传给 GameEventBattleResult.Call
GameEventBattleResult.onCreate
    ↓ L58_1(self[17].setupData) → self[18]
GameEventBattleResult.RaidResultAddItem
    ↓ self[18][92][3] → Bag.IsFull... → GetNumAdd... → Bag.AddItem
    ↓ BagSaveAccessor、Save request、Commit（独立验收）
```

要求采集器记录 `run_id`、脚本闭包/事件实例的**校验后对象身份**、阶段与奖励行哈希；跨 Haxe wrapper 传递不要靠“指针地址应该相同”判断。Lua 5.4/Haxe 的表与对象大小、真实内存成员偏移在此没有可靠运行时校准，不能直接把 `[3]`、`[18]`、`[92]` 当作 Native struct 的 `+3/+18/+92` 字节偏移。

## 2. 直接检查 exact ELF 的结果（不是上次文档的推断）

原始 ELF 为 ELF64/AArch64 小端；`.text` 入口文件偏移 `0x8D8`，`.rela.dyn` file `0x34405D8..0x3AB92B0`，`.rodata` 的 RVA = file offset − `0x568`。

**Lua 运行时**：`.rodata` 文件偏移 `0x3B75156` 直接存在 ASCII `Lua 5.4\0`。但没有通过该版本字符串单独推导 Lua VM 运行时根指针、函数调用 hook 或 LUA 调用链的 ARM64 RVA。

**所研究的脚本方法哈希** `F316609DEA110601D`、`FCBBD4D63B93E4C04`、`FB01A9B29CD4CC6CB` 在整个原始 main ELF 中**没有 ASCII 明文出现**。结合公开的直接 Lua `function ...prototype:METHOD(...)` 定义，现阶段只能把它们认定为 Lua 侧事件/方法入口；**不能从字符串缺失独立证明它们绝不会以其它方式在 Native 代码中实现**。

**确实有可确认的 Native 注册**，以下每组为两条相邻的 `R_AARCH64_RELATIVE` 重定位，分别写入运行时“方法名指针槽”和“Native 回调指针槽”。4 个入口的指令序言也已直接检查：

| 上游 Lua 调用联系 | Native 注册名 | 名称槽 RVA | Native 包装入口 RVA | 证据与边界 |
|---|---|---:|---:|---|
| 奖励随机表初始化 | `f21AF1B35` | `0x4429760` | `0x1AF24F0` | 确认注册；不是 `LotteryRewardItem` 返回时的列表 |
| 奖励固定表初始化 | `f66894ABE` | `0x4429770` | `0x1AF2670` | 确认注册；不是 `F316...` 过滤入口 |
| `BagSaveAccessor` 底层字段访问 | `fFC03AF97` | `0x4420288` | **`0x1A3D940`** | LUA `BagSaveAccessor.SaveNewGetItem` 中向 Native 传入数值；不是团战专用 |
| `BagSaveAccessor` 底层字段访问 | `fFE5328FE` | `0x44202A8` | **`0x1A3DB80`** | LUA `SaveNewGetItem`/其他 accessor 传入物品 ID；不是 `RaidResultAddItem` 或 Lua [92][3] 的入口 |

`fFC03AF97` 和 `fFE5328FE` 仅可作为**后续 BagSaveAccessor 跨语言边界**的候选调查对象；这些回调在非团战玩法也可能调用，**不具备单人奖励事件的唯一性**，无法直接证明 C/Bag 同场次或真实物品到账。

核验脚本：[`audit_violet400_abc_native_boundaries.py`](../../tools/research/audit_violet400_abc_native_boundaries.py)。它只读取本地玩家拥有的 exact ELF，检查 SHA、RELATIVE relocation、Native prologue 与 Lua5.4 字符串。**不修改内存，不提供任意 Hook。**

## 3. 为得到真正的 A/B/C 实机样本，尚需哪些能力

当前 `sys-botbase 2.5` 的 `peek` 可读指定 RAM 地址，**不能从普通定时轮询直接截获 Lua 函数的调用帧与返回时刻**。要在“不替换 sys-botbase、不添加 Switch 系统插件”的原约束下继续推进，技术上还有三项不能省略：

1. **Lua 运行时实例与函数身份确认**：找到经过验证的 `lua_State`/VM 调用入口及对应正在执行的 Lua closure/Proto，识别 B 函数入口/返回和 C 函数入口，而不是将任意 Native reward-table wrapper 误当作这些闭包的函数入口。须证明自己的游戏版本、目标脚本和调用栈与团战事件匹配。当前 `lua_State` 指针、Lua VM dispatch RVA 均为 `NOT_ESTABLISHED`。
2. **Lua/Haxe 对象布局只读解码**：经运行时根定位 `self[19][92]` / `self[18][92][3]`；使用 Lua 5.4 的真实 Table/TValue 等语义读取 `ItemNo`、`ItemNum`、`subjectType`。确认 GC 生命周期、数组 `length`、索引 0、字段字符串、跨事件 wrapper 变化，并在 VM 安全采样点内生成**一致性快照**。Lua script 的 `[3]` 不是裸内存地址 `obj+3`。
3. **可审计的本机捕获传输**：捕获每个 A/B/C 快照，按现有 `RaidRewardAbcEvidence` JSONL 协议送回 Windows；附同场次标识及原始回调次数。若通过直接 ARM64 代码修补实现，仍需单独审查调试能力、可执行内存、专用 RW 缓冲、GC/线程同步、恢复与异常安全；现有 880B 中的 720B 旧奖励模板和仅支持读/写的 botbase **不能被重复用作已验证的 Lua 对象遥测缓冲**。

在上述条件闭合之前，不做盲目全进程 720B 奖励模式扫描，不直接在 `0x1A3D940`、`0x1A3DB80` 或 `0xE32984` 上继续安装奖励替换 Hook。其成功读回只说明代码或模板存在，不等于 CPU 命中，更不能用来推断有晶耀护符的玩家缺少领奖资格。

## 4. 实验验收顺序（《紫》4.0.0，单人离线、护符已确认）

- **E0 — 仅静态**：exact ELF 的 Lua5.4 与 4 处 Native 表注册核验通过，B/C 专属 A64 入口标记 `NOT_ESTABLISHED`。
- **E1 — 调用存在性**：在仅观测、未更改奖励数据的单人测试中证明 B 调用及返回，C 在胜利结算时调用；包含真正的函数/闭包 ID、场次、次数。不能把阶段标志`D=2`叫作 Lua 函数命中。
- **E2 — 行级一致性**：同一场次 A/B/C 三个 `ItemNo/ItemNum/rareType/subjectType` 列表，可导入当前 GUI 对照；B 预期是 A 的保序子集，C 对比 B 实测一致性。
- **E3 — 是否包含用户 20 条**：A 全原版则确认模板没有进入生成阶段；A 有而 B 无定位筛选；B 有而 C 无定位复制/结算上下文；C 有而 Bag 数量无变化才调查容量限制/保存。假设最终只得“原版奖励”，但目前尚未捕获 A/B/C，所以不能提前选择结论。
- **E4 — 独立入包与持久化**：Bag 层入包数量与保存成功需各自证据，不能以 `fFE5328FE` 被调用等同 Bag 入包成功。

本阶段没有 Switch 实机连接，不会声称已完成 E1–E4。后续如果要直接向游戏执行路径植入遥测，属于新的可写代码补丁，需要额外安全审核，不属于本静态研究任务。
