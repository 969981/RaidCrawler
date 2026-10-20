# P1 开始执行：单人晶耀护符「原版＋一条自定义」分段闭环

更新：2026-10-20；分支：`benefit`；状态：**P1 证据闸门已编码，Lua 运行时注入及实机到账未完成**。

> 边界：`P1` 不再尝试在原 LDN 发送副本 `main+0xE32984` 上追加单人奖励。旧 Hook 修改的是网络出站 Bootstrap `x23+0x79C` 的数据副本，不是 Lua `BattleRewardData[3]`。原版 45 槽的网络上限也不能外推成 Lua 数组的实际容量上限。

## 1. 新确认的 PokéLDN 时序与单人差异

当前 [`pokeldn/sv/raid.py`](https://github.com/969981/pokeldn/blob/research/pokeldn/sv/raid.py) 将 `STATE_START=0x0C`、`STATE_STARTED=0x0D`、`KIND_BOOTSTRAP=0x2F` 分开处理；只有开战握手完成、满足接收方条件后，Host 才构造和发送 `0x2F` Bootstrap。**因此现有出站 Hook 既不是“入房立即触发”，也不是胜利后发奖 Hook**。

- **接收方多人路径**：房主通过 LDN 发送 `0x2F` 时附带 `RaidPoint`，`0x6B8+0xE4=0x79C` 的 45×16B 是网络原始奖励。PokéLDN 通过修改这些行，已有零售版接收方领取先机球 ×500 的研究记录；那次实测验证的是接收方对房主奖励行的消费，不代表旧 RaidCrawler Hook 已命中真实 Switch。
- **单人路径**：现阶段目标是 `FieldRaidJewelry.LotteryRewardItem` 完成全部原版固定、随机、团体战力和晶耀护符碎块后、`BattleRewardData.F316609DEA110601D` 执行之前，在**实际 Lua 对象**里追加一条 `subject=5`。不能由网络 serializer 是否命中代替。
- 新版 PokéLDN 的 `raid_point(..., rewards)` **支持为自定义行提供 Subject 5**（0/5 两类），但自定义 `rewards` 参数仍然是**替换** `raid.rows`，不是“原版+追加”；因此不直接作为 P1 的追加算法。

## 2. Lua 静态调用锚点（兼容源码）

固定 [SV-Script-RE commit `1f843bd`](https://github.com/Martmists-GH/SV-Script-RE/tree/1f843bd2517c707fbb47491dad58856eaa2c2cc7):

1. **A0／原版完成**：`decompiled/C81390F82EADEF632.lua:1302-1334`。原版 `LotteryRewardItem` 对 `L8_2[3]` 最后追加一条 `ItemNo=原版属性碎块, ItemNum=difficulty_*, rareType=0, subjectType=5`，随后 `return L8_2`。这条 `return` 的**逻辑位置**是追加自定义 Subject 5 最合适的研究入口，不是已证实可写的 ARM64 入口。
2. **A0→事件**：`C81390F82EADEF632.lua:469-485, 777-791`，奖励数据通过 `GameEventNetworkBattleRaid` 参数传给事件。事件类名包含 Network 不代表单人奖励必经 `0x2F` 发送。
3. **B／过滤**：`decompiled/C849FF496D0E93636.lua:1512-1549` 调用 `C07C34FC3B976E5A7.F316609DEA110601D`，后者从 `Bag.GetNumItem(2481)>0` 判断 `subject=5`，在返回前**新建并替换** `self[3]` 数组。用户已确认持有护符，不能拿“无护符”解释失败。
4. **B→C／交接**：`C849FF496D0E93636.lua:1987-1995` 将 `self[19]` 作为 `setupData` 参数交给 `GameEventBattleResult`；其 `onCreate` 把参数挂到新事件中。
5. **C→背包**：`C889F6553EDFCD011.lua:1625-1696` 从 `self[18][92][3]` 遍历奖励行，检查每条道具的可添加数量，再执行 `Bag.AddItem`。因此 C 有奖励不能代替确认背包数量增加。

**运行时定位尚未闭合**：提供的 ELF 为固定版本 SHA256 `d17403eaa9d844db82f647fd2f1518806b10c0518d53602fc0ac1766298fdba0`，含 Lua 5.4，但不含可直接导出 `LotteryRewardItem` Lua 方法的独立 `A64 RVA` 或经验证的当前 Lua `self` 地址。没法把其 Lua 数组 `[3]` 当作 `obj+3` 字节，也不能直接拿 `0x1AF24F0` / `0x1AF2670` 两个原生奖励表初始化包装函数作为单人追加入口。

## 3. 本次已实现：六点内容证据闸门

文件：
- [`RaidCrawler.Core/Structures/SoloCharmAppendEvidenceGate.cs`](../../RaidCrawler.Core/Structures/SoloCharmAppendEvidenceGate.cs)
- [`RaidCrawler.Tests/SoloCharmAppendEvidenceGateTests.cs`](../../RaidCrawler.Tests/SoloCharmAppendEvidenceGateTests.cs)

首轮强制试验配置为**只启用一条**`(item_id, quantity=1, rare=0, subject=5)`，而不是一开始同时发 20 条或每条 999 个。最宜选可安全领取、**与本次原版奖励 ItemNo 不相同**的道具，才能把背包差额唯一归因于新增奖励。

| 时点 | 未来实际采集的数据 | 验收条件 |
|---|---|---|
| A0 原版完成 | `LotteryRewardItem` 返回前的本次奖励行 | 原版固定、随机、餐力、原版护符行完整 |
| A1 追加完成 | 逻辑 `BattleRewardData[3]`，在资格过滤之前 | `A1=A0+[bonus]`，原版行次序/内容逐条不变 |
| B 过滤之后 | 原对象 **新** `[3]` | 本机已持护符，追加行应保留 |
| C 开始结算 | `setupData[92][3]` | C 与 B 内容一致 |
| Bag 实际库存 | 测试道具领取前／后数量 | 前后差额 ≥ 请求数量，且原版没有同 ID 道具 |
| 保存并重新载入 | 同一测试道具数量 | 重载后数量未低于确认入包时的数量 |

代码可判断异常阶段：原版行丢失／追加未生效／资格筛选丢失／结算交接丢失／背包未到账／保存未保留。**这是同场次数据一致性校验算法，不是采集器**：输入来源即便带有正确 SHA256，也不能证明来自 Switch。API 永远输出 `RuntimeAttestation=NOT_ATTESTED_BY_THIS_CLASS`、`HookCpuHit=NOT_OBSERVED_BY_THIS_CLASS`，不得用其 `ContentConsistent` 宣称已经实机成功。

阻断性 CI 已纳入 `SoloCharmAppendEvidenceGateTests`；覆盖完整正例、各异常阶段、缺失数据、没有护符对照、多人误用、配置关闭、原版已有同种道具导致的库存归因歧义。

## 4. 真实单人接入尚缺的工程任务

### P1-01：获得经过验证的 Lua Prototype 和活动对象根

- KM-Editor 的 `Lua54BinaryChunk` 已证明可对《朱／紫》4.0.0 `romfs/script/lua/bin/release/main/main.blua` 做结构化解析，并按 SHA 和 Prototype 指纹精确锁定脚本位置。
- 下一轮请使用**用户自己 4.0.0 RomFS 中的 `main.blua` 文件**核对 A0 return 邻接闭包及 B/C 函数，不能直接把旧版 Lua 反编译的行号当成新版 bytecode PC。
- 仅凭 ELF 内有 Lua 5.4 版本字符串尚不足以定位 `lua_State`、调用帧、GC 安全访问及 event 上下文对象。需要可复核、精确版本对应的执行入口／采集器；不能扫描 720B 网络奖励模板冒充运行中的 Haxe/Lua 实例。
- KM-Editor 代码 GPL-3.0-only；本仓库不能不经许可审查直接复制其实现。

### P1-02：先做无修改遥测，再逐条开放写入

- 在受控单人离线游戏里，先记录 A0/B/C 的同场次内容，确认动态覆盖这些路径；随后验证 A1 是否有安全对象读写生命周期。
- 必须保证和现有 sys-botbase 2.5 兼容，不添加 NRO／Skyline 或替换插件。若研究明确证明必须运行时 ARM64 仪表化，先独立审查指令覆盖、GC 同步、版本指纹、数据缓冲、错误回退与 CPU 命中证据；即使只记录 Lua 内容，**修补执行代码也不是纯只读内存操作**。
- 用户允许 P1 研究，但未提供当前游戏可用的实际连接；本轮没有安装任何新的代码 Hook。

### P1-03：真实一条物品单人试验

1. 备份存档，关闭互联网功能。
2. 先进行同类型原版团战，记录完整掉落及选定测试道具背包数量。
3. 使用确认有效的 Lua A0→A1 附加点；追加**一条数量 1**的自定义 Subject5 行。
4. 对同一场输出 A0/A1/B/C 和背包前后差异；退出重进，确认保存持久化。
5. 若任意节点缺乏完整实机证据，则保持 `P1_RUNTIME_NOT_VERIFIED`，不得发布“支持单人自定义发奖”。

## 5. 与旧 P0 的关系

P0 `RewardAppendComposer.ComposeLogical` 可用于生成**与运行时目标一致的预期列表**，但不会写 Lua VM。只有安全定位游戏真实 `BattleRewardData[3]` 后，才能选择如何把预期结果安全地写入游戏；网络 `ComposeBootstrap` 是离线网络格式验证，不能作为单人 Hook。

原版多人 45 行容量的“全保留或跳过”规则继续生效；**不能假定 Lua 本地数组也固定 45 行**。在单人完成一条后，才进入完整 20 行和多人房主/接收方单独验收。
