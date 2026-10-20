# Violet 4.0.0 P1-B2：字段级竞态日志与内置奖励表浏览

日期：2026-10-20；代码分支：RaidCrawler/benefit。**已实现只读诊断，尚未获取新的游戏实时 Lua 奖励对象。**

## 用户提供的实机观察

10:53:31.952 旧网络 Hook 为 Empty（原版，880B 全零）。
10:53:42.082 P1-B 状态全局引用返回 CANDIDATE_POINTER_STABLE_NOT_VM_VERIFIED，证明实机上双次指针读回稳定。
10:53:45.063 P1-B2 返回 STATE_FIELDS_CHANGED_DURING_READ。旧日志只知道状态字段变化，不能区分当前 CallInfo 正常切换、GC 栈变化或对象生命周期。
10:53:50/52 的 SVRaidBot L=0,D=1,O=18/17 仍标记 Idle，不能当作 Lua 回调命中或背包入包。

## 一、Lua VM 日志改进

文件：

- RaidCrawler.Core/Structures/Violet400LuaVmFrameProbe.cs
- RaidCrawler.WinForms/SubForms/RaidRewardDiagnosticForm.LuaVm.cs
- RaidCrawler.Tests/Violet400LuaVmFrameProbeTests.cs

变化：

1. 逐字段记录两次 0x70 状态头的变化名称：global_State、CallInfo、stack_last、stack_base、tbclist、base_ci.func、base_ci.top。不会导出内存中的真实地址或相关对象。
2. 对 STATE_FIELDS_CHANGED_DURING_READ 采用同次点击最多三次、两次间隔 40ms 的有界只读重试，所有尝试均重新验证全局指针和状态结构；其他错误不重试。
3. lua-frame-readonly 日志记录 ReadAttempts、changedFields、elapsedMs 和 snapshotAtomic=false。即使重试后状态稳定，仍不能证明任一 Lua Proto 当前被执行，更不是 CPU Hook 命中。
4. 不安装 Hook，不修改堆内奖励对象，不改原版奖励和用户 20×999 配置。

例如“尝试=3，变化字段=CallInfo”只是示例输出格式，不是已经实测得出的变化原因。

## 二、内置基础奖励表和本机模板

已核对仓库原有资源：raid_fixed_reward_item_array.json 为 37 张、每张最多 15 行；raid_lottery_reward_item_array.json 为 37 张、每张最多 30 行；共有 49 个独立 TableName，其中 25 个键同时有固定/随机两表。TableName 必须以 **uint64** 无损解析。

新增：

- RaidCrawler.Core/Structures/RaidRewardTableCatalog.cs：读 embedded base JSON，只解析 TableName、Category、ItemID、Num；固定表读 SubjectType；随机表读 Rate 和 RareItemFlag；计算对应资源 SHA256。
- RaidCrawler.WinForms/SubForms/RaidRewardDiagnosticForm.RewardTables.cs：高级工具的“查看奖励表/模板（内置资源，只读）”窗口。支持选择某个 64-bit TableName，逐行显示固定/随机奖励；亦可选择当前窗口的自定义配置，它会标为 TEMPLATE_NOT_APPLIED。
- RaidCrawler.Tests/RaidRewardTableCatalogTests.cs：按原始 JSON 验证表数、来源、精确 64 位 TableName、行字段；纳入阻断构建。

日志来源严格区分：

| 日志类型 | 来源 | 不是 |
| --- | --- | --- |
| reward-table-base-audit / reward-table-base-preview | 程序自带 Base 资源 | Switch 实时抽取、活动 BCAT 表或 Lua [3] |
| reward-table-config-preview | 当前编辑器模板 | 已安装的奖励 Hook 或实际到账 |
| lua-frame-readonly | Switch 候选 VM 内存字段采样 | 某个团战 Lua Proto 的 CPU 回调认证 |
| ABC_A_GENERATED / ABC_B_FILTERED / ABC_C_CONSUMED | 未来可信采集器的同场奖励列表 | 本次已完成的工作 |

## 三、下一阶段

先在单人离线地图场景点击 P1-B2，保存新的 lua-frame-readonly JSONL，确认哪个字段变化、重试是否得到结构上合理的快照。奖励表浏览不需要 Switch 连接，可立即用于核对固定/随机的字段结构和当前本机配置。

实际读取这一次太晶坑的 Lua BattleRewardData[3]、资格过滤后的新 [3]、RaidResultAddItem 消费的 [18][92][3]，仍需要精确 Lua VM 执行时点和 GC 安全访问方式；不能把静态表或网络 45×16B Bootstrap 替代它们。本次未验证游戏原始奖励保留、额外奖励到账、保存重载或其他玩家联网领取。
