# 《紫》4.0.0 单人团战奖励源头：FieldRaidJewelry → BattleRewardData → Bag

日期：2026-10-20；分支：`benefit`；用途：**只读静态研究及后续受控实机证据规划**，不向 Switch 写入新 Hook，不修改任何用户的 ELF、存档或奖励数据。

## 一、为什么不继续调 main+0xE32984

实机 `raid-20261019-162502-2f64d2a2.jsonl` 的 64 个只读周期全都显示 `EXPECTED_BRANCH`、`V2_CODE_AND_HEADER_MATCH`、`epoch=2`、720B 的 SHA 匹配；但 `nativeHookHit=NOT_OBSERVED`，玩家**持有晶耀护符**且单人结算奖励不变。这个结果只证明**写入持续存在**，没有证明 CPU 进过 serializer。

实际 4.0.0 ELF 的 `main+0xE327FC` / `main+0xE32984`，位于 RaidPoint 向网络 Bootstrap 复制的逻辑中，目标是 `x23+0x79C` 的**输出副本**，而不是单人发奖源对象。下一阶段应转向更上游的 `BattleRewardData` 生成和消费链路。

## 二、新定位：旧版 Lua 中有直接创建 BattleRewardData 的 FieldRaidJewelry

证据来源：公开 [SV-Script-RE 固定 commit 1f843bd](https://github.com/Martmists-GH/SV-Script-RE/tree/1f843bd2517c707fbb47491dad58856eaa2c2cc7)，README 约 3.0.1。**目前没有《紫》4.0.0 实际 RomFS Lua 原件，不把旧 Lua 当成新 ELF 已独立验证的 Lua 代码。**

### 2.1 本地事件初始化 → 生成奖励

[FieldRaidJewelry / C81390F82EADEF632.lua](https://github.com/Martmists-GH/SV-Script-RE/blob/1f843bd2517c707fbb47491dad58856eaa2c2cc7/decompiled/C81390F82EADEF632.lua)：

| 代码行 | 可重复核对的事实 |
|---|---|
| 417–419 | 先调用 `C07C34FC3B976E5A7.new()`，即 `BattleRewardData.new()` |
| 469–485 | 调用 `self.FE77BCB620AB2EDCA(...)`（方法标注 `LotteryRewardItem`），以难度、表 ID、掉落表和 RNG 参数构造奖励；返回值记入 `L16_2` |
| 684–701 | 组装本次团战的对象，其中 `SE9713F112CDA989C = L16_2`，保存生成的奖励对象 |
| 777–791 | 调用 `GameEventNetworkBattleRaid.SFCE091807173F6E9(...)`，**第 9 个函数实参（0-based A8_2）是 `L47_2 = L16_2` 奖励对象** |
| 740–755 | 使用房间状态返回码决定布尔参数 `L27_2/L28_2`，说明这是一个带模式分支的共同事件调用处；尚未独立标定每个状态整数的精确语义 |

注：`GameEventNetworkBattleRaid` 是**Lua 事件类名**，并不是“必经 LDN 0x2F native 序列化”的证明。事件参数里单独有 `isComm` 位，支持区分本地与联机分支。当前尚未完整实机证明这个脚本入口在《紫》4.0.0 单人团战中的覆盖率。

### 2.2 LotteryRewardItem 构造的是对象，而不只是 720B 网络数据

同一文件 [第 820–1335 行](https://github.com/Martmists-GH/SV-Script-RE/blob/1f843bd2517c707fbb47491dad58856eaa2c2cc7/decompiled/C81390F82EADEF632.lua#L820-L1335)：

- 821–825：函数内新建 `BattleRewardData` 对象。
- 828–840：读取当前难度、固定掉落表、随机掉落表及槽位表。
- 852–862：检查 `reward_item_00` 是否存在；缺失时返回空奖励对象。
- 955–1095：**最多遍历 15 个固定表条目**，提取 `itemID`、`category`、`subject_type`、`num`，写入 `[3]` 的 `ItemNo/ItemNum/rareType/subjectType` 行。
- 1096–1300：遍历最多 30 个随机表条目，以 `rate`、`num`、`rare_item_flag` 和随机数决定进入 `[3]` 的奖励。
- 1302–1334：依据 `difficulty_%d` 生成一条 `subjectType=5` 的额外奖励行，最后返回 `BattleRewardData`；**是否每次添加、行数上限及版本差异需另核对**。

这说明在公开旧版 Lua 中，生成的奖励列表具有本地游戏逻辑可直接消费的**对象形态**。因此继续修改网络 Bootstrap 输出缓冲不会必然影响同一实例的本地结算对象。

### 2.3 对象传递、资格过滤、真正入包

[GameEventNetworkBattleRaid C849FF496D0E93636.lua](https://github.com/Martmists-GH/SV-Script-RE/blob/1f843bd2517c707fbb47491dad58856eaa2c2cc7/decompiled/C849FF496D0E93636.lua)：
- 第 54–95 行：事件参数独立包含 `isComm=A1_2` 和 `raidBattleRewardData=A8_2`。
- 第 1512–1517 行：`self[17].raidBattleRewardData` 变成当前战斗上下文的 `self[19][92]`。
- 第 1533–1549 行：调用 `BattleRewardData.F316609DEA110601D` 进行 subject、角色、餐力、护符等条件过滤。用户有晶耀护符，不能把本例失败归因于缺护符。
- 后续由 `GameEventBattleResult` 消费相关的战斗上下文。

[GameEventBattleResult C889F6553EDFCD011.lua](https://github.com/Martmists-GH/SV-Script-RE/blob/1f843bd2517c707fbb47491dad58856eaa2c2cc7/decompiled/C889F6553EDFCD011.lua)：
- 第 305–357 行：获胜分支调用 `RaidResultAddItem`。
- 第 1625–1697 行：从 `self[18][92][3]` 取过滤后的奖励，依据 `SlotMaxNum` 调整实际能入包的 `ItemNum`，调用 `Bag.AddItem`。
- 该类第 530–535 行请求 `SaveLoadManager.RequestAutoSave(0)`；它并不等于保存已经刷盘。

因而公开旧版 Lua 的完整主干是：

```text
FieldRaidJewelry
    └─ LotteryRewardItem(difficulty, dropTableFix, dropTableRandom, RNG)
         └─ BattleRewardData[3]  ← 在这里已构建本地奖励列表
              └─ GameEventNetworkBattleRaid(param.raidBattleRewardData)
                   └─ context[92] → subject filter
                        └─ GameEventBattleResult.RaidResultAddItem
                             └─ Bag.AddItem → BagSaveAccessor
                                  └─ RequestAutoSave → StartSaveProcess
```

**这条 Lua 路径比 LDN serializer `main+0xE32984` 更适合调查单人奖励的来源。**

## 三、新的《紫》4.0.0 ELF 实证：三个原生 Lua 桥接

本次直接读取用户的原始 `Violet4.0.0.elf`：SHA256 `d17403eaa9d844db82f647fd2f1518806b10c0518d53602fc0ac1766298fdba0`，ELF64 AArch64。

在 `.rela.dyn` 中找到**相邻的两条 R_AARCH64_RELATIVE 重定位**，分别初始化一个 Lua 方法名指针与随后相邻的 Native 回调指针（名字所在 RVA 由 ELF .rodata `fileoffset-0x568` 得出）：

| 方法哈希 | 公开旧 Lua 用途 | 名称对应目标指针 RVA | Native 包装函数 RVA |
|---|---|---|---|
| `f21AF1B35` | `EncountRaidManager.InitializeLotteryRewardItem` 调用 | `0x4429760` | **`0x1AF24F0`** |
| `f66894ABE` | `EncountRaidManager.InitializeFixedRewardItem` 调用 | `0x4429770` | **`0x1AF2670`** |
| `f859EFF3C` | 获取原生数据管理器的实例 | `0x441F100` | **`0x1A27FC0`** |

注意：
1. 上述 ELF 文件偏移处是 **ELF RELA 重定位记录**，**不是**可直接读写的游戏运行时函数表。重定位实际把名字指针和函数指针写入所列 `r_offset` 目标位置。
2. 对三组记录，名称槽与函数槽都相距 8B，原生函数入口的 AArch64 序言也已直接核验。研究脚本仅校验真实重定位及机器码，无任何内存修改操作。
3. 这三个 Native 包装方法与 `LotteryRewardItem` 的生成表初始化相关，**尚非已证实的单人奖励最终生成/发放 Hook 点**。Lua `FieldRaidJewelry` 本身的调用边界不必对应一个可直接替换的 AArch64 函数地址。
4. 当前仍缺少《紫》4.0.0 的 `C81390F82EADEF632` 等**原始 Lua/RomFS 字节**及实机对象生命周期证据，不能在这里直接给出安全、稳定、覆盖单人的奖励对象指针。

已提交工具：
- [只读 ELF Native bridge 审计器](../../tools/research/audit_violet400_reward_source_bridges.py)
- [离线单元测试](../../tools/research/test_audit_violet400_reward_source_bridges.py)

本地使用（自有 ELF，不上传游戏二进制）：

```bash
python3 tools/research/audit_violet400_reward_source_bridges.py /path/to/Violet4.0.0.elf
```

## 四、下一阶段验证次序（不能跳过）

1. **P0 / Lua 版本闭合**：核对用户自有 4.0.0 RomFS 中 `FieldRaidJewelry`、`BattleRewardData`、`GameEventNetworkBattleRaid`、`GameEventBattleResult` 的模块哈希/实际脚本。若无法取得，保留「3.0.1 Lua 与 4.0.0 ELF 跨版本映射」的证据等级。
2. **P0 / 单人对象链**：在受控离线测试中分别观察 FieldRaidJewelry 构造结果、传给事件的 `raidBattleRewardData`、过滤后的 `context[92][3]`，确认它们是否为同一次战斗、是否确实包含预期原始奖励。不能以网络 `x23+0x79C` 作为单人对象的代替。
3. **P0 / 本地结算**：确认 `GameEventBattleResult.RaidResultAddItem` 实际遍历的对象及 `Bag.AddItem` 的实测数量；玩家已经有晶耀护符，不需重复“有无护符”归因。
4. **P1 / 保存独立验收**：将入包、存档访问器调用、自动保存请求、Native Commit 结果及重载验证分层。
5. **P1 / 最终修改阶段**：**只有上述链路闭合**，才能评估是否值得做原生 Hook 或 Lua 事件级拦截；暂时不替换未知地址，不把当前 LDN 0x2F serializer 称为单人发奖入口。

### 需要防止的两个错误

- “找到了 `BattleRewardData` 类名 ⇒ 已找到《紫》4.0.0 的 RAM 指针”：**不成立**。
- “找到 `f21AF1B35` 的 Native 地址 ⇒ 这里就是本地发奖时刻”：**也不成立**，这只是管理器初始化接口的一层桥接，是否在每场奖励产生时调用需要独立证据。

参考：[PokéLDN 奖励入包与保存链](https://github.com/969981/pokeldn/blob/research/docs/research/sv/raid-reward-to-bag-save-chain.md) · [PokéLDN Lua/ELF 交叉版本研究](https://github.com/969981/pokeldn/blob/research/docs/research/sv/battle-reward-lua-cross-version.md)。
