# P0 · 本地太晶坑奖励三节点验收矩阵（《紫》4.0.0）

更新：2026-10-20。只读证据与实验计划；**本任务未安装或启用新的 Native Hook，未验证本机动态 Lua 对象**。

## 版本兼容性依据（2026-10-20 校正）

**优先采用 SV-Script-RE 的 3.0.1 Lua 分析 4.0.0 的奖励业务流程，不再把重新获取 4.0.0 Lua RomFS 当作推进 N1/N2/N3 的硬性前置条件。**

- 《紫》3.0.1 与 4.0.0 使用相同的 Main NSO Build ID 前 8 字节 `709BFD6611529864`；用户 4.0.0 实机也回读该标识和匹配的 AArch64 指令。社区 SV cheat 维护者明确确认两个版本沿用相同 Build ID 和代码偏移：
  - https://gbatemp.net/threads/pokemon-scarlet-violet-cheat-database.621563/page-510
  - https://gbatemp.net/threads/pokemon-scarlet-violet-cheat-database.621563/
- 任天堂 4.0.0 官方发布内容主要是 Switch 2 显示分辨率和帧率优化，而非太晶坑业务重构：
  - https://en-americas-support.nintendo.com/app/answers/detail/a_id/60277/p/988/c/950
- `Martmists-GH/SV-Script-RE` 公开仓库最新版本是 `1f843bd`（2024-10-29），README 标注 3.0.1；三个研究文件均来自该版本。**仓库没有独立的 4.0.0 文件版本或两代文件 SHA 对比，故无法从此仓库单独证明 RomFS Lua 字节完全相同。**
- 证据强度区别：**Native Build ID/地址兼容：已受社区与本机验证；Lua 业务调用链沿用：高置信研究假设，可直接作为工作基线；三个 Lua 文件的逐字节一致：尚无双版本样本验证；4.0.0 运行时命中及对象同一性：仍须测试。**
- **N2→N3 的静态实参调用已经通过 `GameEventNetworkBattleRaid` 第 1987–1995 行找到，后续 P0 应聚焦本机 4.0.0 是否实际走过这条单人模式分支，以及过滤后的 `ItemNo/ItemNum` 是否真正被 `Bag.AddItem` 消费**；不应再以“先获得全部 4.0.0 Lua”为由暂停分析。

## 0. 验证目标：严格区分静态链与实机结果

1. **N1 奖励生成**：FieldRaidJewelry.LotteryRewardItem 被单人模式实际调用，输出 `BattleRewardData[3]`。
2. **N2 奖励传递**：对应战斗的 `raidBattleRewardData` 进入 `GameEventNetworkBattleRaid` 上下文 `[92]`，经 `F316609DEA110601D` 过滤，形成可领奖列表。
3. **N3 实际入包**：`GameEventBattleResult.RaidResultAddItem` 从事件 `setupData[92][3]` 取同一次战斗的过滤列表，根据 `SlotMaxNum` 计算并调用 `Bag.AddItem`。

对源码静态证据的认定必须与实机动态命中、内存身份和实际背包差分分开。

## 1. 最新源码断言（SV-Script-RE@1f843bd，约 3.0.1）

| 验收点 | 对应 Lua 行号 | 已确认的静态事实 | 4.0.0 实机状态 |
|---|---|---|---|
| N1: local object | `C81390F82EADEF632.lua:469-485` | 调用 `LotteryRewardItem`，返回 `L16_2` | **NOT_OBSERVED** |
| N1: rows | 同文件 `820-1335` | 新建 `BattleRewardData`；固定表最多 15、随机表最多 30，最终 subject=5 额外行有分支 | **NOT_OBSERVED** |
| N1→N2 | 同文件 `684-701` 和 `777-791` | 奖励对象进入 `SE9713F112CDA989C`，并作为 raid event 参数传递 | **NOT_OBSERVED** |
| N2: event | `C849FF496D0E93636.lua:54-95` | `raidBattleRewardData = A8_2`，同时独立保存 `isComm=A1_2` | **NOT_OBSERVED** |
| N2: filter | 同文件 `1512-1549` | 参数封装为 `self[19][92]` 并调用 `F316609DEA110601D` | **NOT_OBSERVED** |
| N2→N3 | `C849FF496D0E93636.lua:1987-1995` + `C889F6553EDFCD011.lua:65-115` | `GameEventNetworkBattleRaid` 直接把筛选上下文 `self[19]` 作为 `GameEventBattleResult` 的 `setupData` 传入，后者再建立 `self[18]` | **STATIC_ARGUMENT_HANDOFF_CONFIRMED; RUNTIME_NOT_OBSERVED** |
| N3: win | 同文件 `304-357` | 在 `type==2`、`result[1]==1` 分支调用 `RaidResultAddItem` | **NOT_OBSERVED** |
| N3: Bag | 同文件 `1625-1696` | 从 `self[18][92][3]` 迭代，容量检查后调用 `Bag.AddItem` | **NOT_OBSERVED** |

**新闭环（直接旧 Lua 调用证据，2026-10-20）**：继续检索 `GameEventNetworkBattleRaid` 调用方，在 `C849FF496D0E93636.lua:1987-1995` 找到它**直接调用** `GameEventBattleResult.SFCE091807173F6E9`：`L40_2 = 2`，`L41_2 = self[19]`（当前经过过滤的上下文），`L42_2 = self[18].F357A65DD82CA566E(...)`（战斗结果数据），最终 `SFCE...(2, self[19], result...)`。而 `GameEventBattleResult.SFCE...` 的 `setupData=A1_2`、`resultData=A2_2`（`C889F6553EDFCD011.lua:65-95`），其 `onCreate` 将 `setupData` 包装进新事件 `self[18]`（约 103–115 行）。最后 `RaidResultAddItem` 从 `self[18][92][3]` 逐行读取并调用 Bag。**所以 N2→N3 的源码实参传递已闭合，必须取消“无调用方”的静态阻断项。**

**仍须区分**：`self[19]` 传递与后续 `self[18]` 封装可能产生新对象；源码证明**逻辑数据从本次筛选上下文传至结算事件**，但不能证明物理对象地址完全不变，更不能冒充 4.0.0 真机执行事件。

代码实证工具：`tools/research/verify_sv_reward_three_nodes.py`，要求 checkout 原始固定 commit；仅验证代码片段顺序，明确输出所有 runtime = `NOT_OBSERVED`。它不是修改器、不会解析 Lua 虚拟机的运行时内存。

## 2. 与 exact ELF 已有的证据

- 已直接核验的 4.0.0 ELF Native 方法注册（来自 `.rela.dyn`）：
  - `f21AF1B35 → main+0x1AF24F0`：随机奖励表初始化桥接。
  - `f66894ABE → main+0x1AF2670`：固定奖励表初始化桥接。
  - `f859EFF3C → main+0x1A27FC0`：管理器获取实例。
- 这些是 Native 函数包装入口，不是 Lua `LotteryRewardItem` 的函数入口，也不是 `BattleRewardData[3]` 指针。
- `main+0xE32984` 仍是出站 bootstrap 副本 Hook；64 次战斗快照里模板 SHA 均一致，但 CPU 命中未知；**不能因为补丁存在就判断 N1–N3 任一节点已动态命中**。

## 3. 下一次受控实机需要的最小证据

仅在**备份好存档、未联网**的单人测试中采集。默认不修改额外奖励，只核对正常原始流程；观察失败不能用任意新的 Hook 地址盲目修补。

| 证据阶段 | 最低需要记录的内容 | 足以推出的结论 |
|---|---|---|
| N1 create | 当前 Build ID、runId、坑位/种子标识、Lua 模块版本、调用进入/返回、生成奖励行的 item/qty/rare/subject 与数量 | 本次单人确实构造了 RewardData |
| N2 enter / exit | 相同 runId，事件 `isComm`，进入前与过滤后列表，是否持道具 2481，角色与餐力 | 列表到达并通过/未通过过滤 |
| N3 result | 相同 runId，`type`、胜利结果，`setupData[92][3]` 内容与 N2 比对 | 结算消费是否采用 N2 的数据（允许复制但内容应对得上） |
| N3 bag | 道具 ID、入包前数量、`SlotMaxNum`、调用 `AddItem` 的增量、入包后数量 | 是否真正修改运行时库存 |
| Save (independent) | 保存请求、实际 Commit 返回码、退出后重进存档背包差分 | 是否持久保存，和 N3 不混为一谈 |

不能使用 Windows 每 5 秒采样 **静态 Hook 模板**代替上述 Lua 实例的读取；普通 sys-botbase 没有瞬间的 Lua VM 栈帧和 `PC/x23` 能力。**动态字段必须靠实际运行时观察才能标成已证实**，但 4.0.0 Lua blob 并非开展旧源码逻辑研究与对象传递审计的必要条件。

## 4. 操作说明及当前不能越过的边界

一、在本地固定旧版源码并运行静态脚本：

```bash
git clone https://github.com/Martmists-GH/SV-Script-RE.git
cd SV-Script-RE
git checkout 1f843bd2517c707fbb47491dad58856eaa2c2cc7
cd ..
python3 RaidCrawler/tools/research/verify_sv_reward_three_nodes.py SV-Script-RE
```

二、**直接按现有 3.0.1/4.0.0 兼容基线**研究三节点对象传递，并在离线受控的 4.0.0 实机上采集运行时证据。若以后取得两版对应 Lua/RomFS，可追加逐字节哈希核验；但这属于版本校验强化项，不阻塞现阶段的调用链分析。尚未验证动态生命周期前，不要直接部署新的可写单人奖励 Hook。

三、在正常游戏环境验证结算，可以仅用无修改的一场单人坑，记录少量道具结算 UI 和实际背包变化；但截图/背包差分只能证明 N3 的游戏效果，**不能反向证明 N1/N2 所用的精确对象或指针**。 
