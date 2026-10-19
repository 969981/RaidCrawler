# P0 · 本地太晶坑奖励三节点验收矩阵（《紫》4.0.0）

更新：2026-10-20。只读证据与实验计划；**本任务未安装或启用新的 Native Hook，未验证本机动态 Lua 对象**。

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
| N2→N3 | `C889F6553EDFCD011.lua:103-115` | `GameEventBattleResult.onCreate` 从新的事件 `setupData` 建立 `self[18]` | **OBJECT_PROVENANCE_GAP** |
| N3: win | 同文件 `304-357` | 在 `type==2`、`result[1]==1` 分支调用 `RaidResultAddItem` | **NOT_OBSERVED** |
| N3: Bag | 同文件 `1625-1696` | 从 `self[18][92][3]` 迭代，容量检查后调用 `Bag.AddItem` | **NOT_OBSERVED** |

**重要纠错**：N2 的 `self[19][92]` 和 N3 的 `self[18][92]` 看上去字段位置相同，但 N3 是一个后续新事件的 `setupData`。必须确认 `NetworkBattleRaid` 的上下文如何经过战斗系统**交给** `GameEventBattleResult`，不能仅因字段索引 `92` 相同就声称「完全同一对象实例」。可能有复制/序列化，需对比逻辑内容与唯一的 runId/坑位/种子；不能比较跨进程指针数值。

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

不能使用 Windows 每 5 秒采样 **静态 Hook 模板**代替上述 Lua 实例的读取；普通 sys-botbase 没有瞬间的 Lua VM 栈帧和 `PC/x23` 能力。缺乏 4.0.0 自己的 Lua blob 时，以上动态字段均不得标成已证实。

## 4. 操作说明及当前不能越过的边界

一、在本地固定旧版源码并运行静态脚本：

```bash
git clone https://github.com/Martmists-GH/SV-Script-RE.git
cd SV-Script-RE
git checkout 1f843bd2517c707fbb47491dad58856eaa2c2cc7
cd ..
python3 RaidCrawler/tools/research/verify_sv_reward_three_nodes.py SV-Script-RE
```

二、要把结论升级到《紫》4.0.0 **真实游戏**，需要该版本的原始 RomFS 脚本模块或可信运行时跟踪工具，用于确认方法/分支版本一致。在此之前不要实现可写奖励的单人 Native Hook。

三、在正常游戏环境验证结算，可以仅用无修改的一场单人坑，记录少量道具结算 UI 和实际背包变化；但截图/背包差分只能证明 N3 的游戏效果，**不能反向证明 N1/N2 所用的精确对象或指针**。 
