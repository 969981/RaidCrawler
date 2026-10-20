# 《紫》4.0.0 晶耀护符奖励「保留原版 + 追加自定义」改造计划

状态：**规划／尚未开发运行时追加 Hook**；2026-10-20；执行分支：`benefit`。当前仅确立数据契约、任务与验收；本文件不代表已修改游戏进程、已经命中本地 Lua 或已发放物品。

## 0. 目标、非目标与硬约束

**目标**：游戏正常计算本场 **固定奖励 (subject=0/1/2/3) + 普通随机奖励 (0) + 团体战力额外抽奖 (4) + 原版晶耀护符太晶碎块 (5)**；然后在原有逻辑奖励对象里追加用户自定义的 20 条 `(ItemNo, ItemNum, rareType=0, subjectType=5)`。不覆盖、删除、重排、随机重抽、减少原有任何奖励行。每个最终领取者由原版过滤器独立判断是否持有晶耀护符 (Bag item 2481)。**用户已确认自己持有护符，不再将“缺少护符”作为当前故障假设**。要求单人本地奖励优先，后续多人房主/参与者按各自接收链验证；现有 sys-botbase 2.5 保持不变，Windows RaidCrawler 为控制端，不装新 Switch 插件、不换 sys-botbase；**热更新属于最终目标，尚未证明当前执行边界能实现**。

**非目标**：不通过修改背包、存档或直接调用 `Bag.AddItem` 来冒充正常团战奖励；不改固定/随机奖池概率、不给无护符玩家发自定义奖励；不把单行输入 `999` 宣称为实际获得 `999`；不默认进入官方互联网模式。不尝试扩展未经证实的 45 行网络容量。若做离线 RomFS/Lua 模组，仅用于可行性验证，不作为符合“无需重启”目标的正式实现。

## 1. 已验证的静态事实与必须区分的对象

| 研究依据 | 证据 | 可以做什么 | 不能推导什么 |
|---|---|---|---|
| 用户 exact Violet ELF 74,362,480B，SHA `d17403eaa9d844db82f647fd2f1518806b10c0518d53602fc0ac1766298fdba0`，Build ID `709bfd66115298640155fcc4979dba151c7cc79a` | `main+0xE327FC` 网络出站 serializer；`main+0xE32984` 当前入口；`x23+0x79C` 目标副本，`x20+0x13C` 原始 RaidPoint rows 来源 | 证明旧 Hook 仅写出站副本，旧入口不是单人本地生成入口 | 不能证明单人执行了该 serializer、不能获得 Lua `BattleRewardData` 指针 |
| PokéLDN `pokeldn/sv/raid.py:171-207`、`docs/sv_raid.md` | `RaidPoint+0xE4`，`45×16B`，`<IIII>`；`raid_point(rewards != None)` **整表替换** | 纯离线格式验证、两端 Packet 解析与容量边界 | 45 是网络 RaidPoint 容量，**不是**已证实的 Lua 数组最大容量 |
| `SV-Script-RE` pinned `1f843bd`，`C81390F82EADEF632.lua:820-1334` | `LotteryRewardItem` 先加入固定/随机行，后追加 `subject=4` 抽奖和 **1 条 `subject=5` 原版额外碎块** | 定义新的 *append after construction before filter* 目标语义 | 不能因名为 Lottery 而将护符行视为普通随机奖池条目 |
| `C07C34FC3B976E5A7.lua:183-320` | `F316609DEA110601D` 逐行检查 `subject=5 && Bag.GetNumItem(2481)>0`，返回前 **替换 `self[3]` 为新的过滤数组** | 多条 subject5 应按相同条件被独立筛选；B 阶段须拍过滤后的 **新数组** | 无护符过滤不足以解释本用户失败 |
| `C849FF496D0E93636.lua:1987-1995`、`C889F6553EDFCD011.lua:1625-1696` | 过滤后的 `self[19]` 传给 Result 事件 `setupData`，C 从 `self[18][92][3]` 读取，容量检查后 Bag.AddItem | 定义 A、B、C 三段实际来源与逐场对照 | 不代表已经完成真实运行时捕获、背包添加或持久化 |
| `KM-Editor` | 读写 FlatBuffers 固定/随机奖励资源；可解析/修改 4.0.0 `main.blua` 的 Lua5.4 prototype | 可做离线资源小样与 Lua 闭包指纹/布局研究 | 固定奖励的静态 `sbyte Num` 与 Subject 0–3 枚举 **不是**运行时自定义 subject5/qty999 的约束 |

### 两种结构边界

- **Lua 对象**：`BattleRewardData[3]` 是 Haxe/Lua 列表，每项逻辑字段 `ItemNo/ItemNum/rareType/subjectType`；不是可以用 `object+3` 或 `720B memcpy` 操作的 native struct。需要 Lua VM 安全执行上下文。
- **网络 RaidPoint**：至多 45 行，40? 不做模糊限值，准确为 `45×16=720B`；需要保证报文大小、其余字节和顺序不变。对于“20 条全部追加并参与多人同步”的默认严格模式，原版有效行数 `N` **必须 ≤25**，否则不得把原版行删掉凑容量。具体每场原版有效行数需要运行时获取，不能用设计阶段猜测值替代。

## 2. 目标数据流

```text
FieldRaidJewelry.LotteryRewardItem
  ├─ fixed original (0..3)
  ├─ lottery original (0)
  ├─ raid-meal original (4)
  └─ original charm shard (5)
       ↓ 完整原版 BattleRewardData[3] (A0, immutable snapshot)
  + user charm bonus profile (up to 20 rows, 5, rare=0)
       ↓ 安全 append，保留/顺序不变 (A1)
GameEventNetworkBattleRaid: raidBattleRewardData
       ↓ filter uses each RECIPIENT's Bag.GetNumItem(2481)
  (B) filtered BattleRewardData[3]
       ↓ GameEventBattleResult setupData[92]
  (C) RaidResultAddItem → Bag.AddItem → Save
```

对于多人，还要单独检查**房主发给参与者的 RaidPoint 序列化内容**是否也含有追加行。修改房主本地 Lua 数组并不自动证明参与者收到了同样的行；旧 `x23+0x79C` 路径则不能代替房主单人本地数组。原版奖励与追加奖励都不能重复发放。

## 3. 开发任务（严格依赖推进）

| ID | 优先级 | 任务／代码建议 | 具体验收 |
|---|---|---|---|
| P0-01 | P0 | **冻结不符合目标的整表覆盖**：旧 `main+0xE32984` serializer Hook 重新命名为 Legacy / 网络出站替换实验；默认追加模式绝不沿用 `rewards_data` 45 槽覆盖。当前已安装旧 Hook 的设备只做状态提醒，用户显式停用/卸载，程序不可暗自写内存。 | 默认“晶耀护符追加”不提供可误触的旧一键写入；原始入口指纹校验失败必拒绝写 |
| P0-02 | P0 | **创建追加配置**：`CharmBonusProfile` 只保存 1–20 条用户道具 (ID/数量/rare=0/subject=5)，和 `RaidRewardEditor` 原版 45 槽数据**分离**；旧 JSON 档案兼容迁移但不迁移为空白行覆盖；有效模板热更新版本号、校验摘要与禁用状态。 | 存盘、加载、旧档迁移、显式关闭后恢复**原版**；不要提示用户再验证护符 |
| P0-03 | P0 | **纯函数 RewardAppendComposer**（Core，不读写 Switch）：输入原版 `RaidPoint` 45 行/或 Lua 抽象行、追加配置；保持原版行二进制/顺序/rare/subject，确保重复行不合并；从原版最后一个有效行后追加，不触碰有效中间 0 空洞；严格校验边界。 | `N+M≤45` 成功，`N+M>45` 返回 `INSUFFICIENT_CAPACITY` 且 **完整回退原版**；证明数据块外字节不变；不静默裁剪 |
| P0-04 | P0 | **真实样本与性质测试**：取 PokéLDN 官方可复现的三/六星原版 reward rows，核对序列化 `<IIII>`、`subject4` 顺序、原版 subject5 + M 额外行；校验 `N=0,24,25,26,44,45`、重复道具、不同星级、rare、非法数量与长度，随机 fuzz。 | 无异常截断/越界；`N=25,M=20` 可用；`N=26,M=20` 拒绝修改；所有原版有效行字节级一致 |
| P1-01 | P0 | **ELF / Lua 边界定位**：exact ELF 的 Lua5.4 运行时原生 API/VM 调用点追踪；参考 KM-Editor `Lua54BinaryChunk` 的 prototype 身份识别，对 `LotteryRewardItem` 返回、`F316...` 返回、`RaidResultAddItem` 入口识别。先找准确 lua_State/闭包/GC 安全性，不拿 Native reward-table 初始化 RVA 冒充结果入口。 | 完整 Build ID/原始 A64 指令和调用上下文证据；三个目标只标 `VERIFIED` 或 `NOT_ESTABLISHED`，不得猜地址 |
| P1-02 | P0 | **真实 ABC 捕获适配器**：不修改奖励，读 A0/A1（如无 A1 则只 A0）、B（过滤后新数组）、C（入包前）行及序列、关联同场 runId；接入已有 `RaidRewardAbcEvidence` JSONL。需要经过验证的同一 Lua VM 线程安全读取／受控内存仪表化，sys-botbase 静态定时读不算捕获。 | 离线无修改单人团战连续取得 A0/B/C，显示各行实际 SHA/数据，且真正的原生回调次数可核对；无法捕获就保持 `NOT_OBSERVED` |
| P1-03 | P1 | **Lua/本地奖励追加 PoC**：以单人离线单场为范围，在 `LotteryRewardItem` **完成原版构造后、资格过滤前**安全追加最初仅一条小额 `subject=5`；可用隔离的 RomFS/Lua patch 做开发验证，但正式热更新方案不得依赖重启。 | A0 全原版；A1=A0+1；B 保留符合资格的行；C 同 B；背包前后独立比较；未能验证先不提供可写 Hook |
| P2-01 | P1 | **Native Windows 控制面**：在现有 sys-botbase 2.5 上实装前完成读写地址校验、独立已验证 RW payload、epoched two-phase update、CPU 调用计数、原版回退、异常 fail-closed；不覆盖代码时所有 45 行到全零，不假定 RW 堆对象跨 GC 生命周期有效。 | 同一实际单人场次观察到 A1 后才判成功；关闭配置/冲突/断线/容量不足不影响原版奖励；卸载不在代码可能执行中释放缓冲 |
| P2-02 | P1 | **多人分支单独闭环**：针对本地房主和参与者分别验证 `RaidPoint` 来源、发给客人的 45 行、客人自己的 subject5 护符资格、C→Bag；只在被确认支持的通信模式启用，**官方互联网对战不在默认范围内**。 | 房主/客人各至少一场、持/不持护符对照、原版行不变；网络容量不足则保留原版、不下发残缺自定义奖励 |
| P2-03 | P1 | **热更新、UI 与日志**：主界面改为「原版奖励：保留（只读）」+「自定义晶耀奖励：追加」+「当前活动配置 M 条」，显示预计/实时 `N+M/45`；自动检测实际战斗阶段；高级面板显示 A0/A1/B/C、容量跳过、调用计数、原版 hash、追加 hash、实际背包差。 | UI 不要求手工点击房间/开战/结算；新增配置无重启生效必须有真机有效调用证明，不以 Hook 字节 SHA 替代 |
| P3-01 | P0 gate | **系统验收**：单人 1 条 → 5 条 → 20 条（必须空间够）、无护符对照（若可用）、多人房主/客人、离线联网切换、不同星级、重复奖励、库存接近满额、保存重载。旧功能回归 CI 阻断。 | A0 与原版完全一致；A1只追加；B/C 符合资格、无重复领奖；实际 `Bag.AddItem` 数量与再次加载持久；崩溃/断链回退原版 |
| P3-02 | P1 gate | **预览构建发布**：Windows dotnet 构建、奖励单测阻断、原生 A64 模块精确 RVA/字节指纹、测试文件 SHA。发布说明突出已支持的玩法、未支持的模式与风险。 | 未完成 P1-02/P1-03 真机闭环前只发布 `PREVIEW_DIAGNOSTIC`，不发布“单人护符追加已实现”字样 |

## 4. 模块划分与不该采取的捷径

- `RaidCrawler.Core`：`CharmBonusProfile`、`RewardAppendComposer`、`AppendCapacityPolicy`、`RaidRewardAbcEvidence`；全为**纯离线**逻辑，可以立即开发，不依赖新 Native Hook。
- `RaidCrawler.WinForms`：从“整表替换”转为默认「保留原版+追加」；旧网络替换仅隔离的高级 Legacy 研究入口（有明确警示）；自动分阶段日志与运行时控制。
- `native/sv_remote_code_patch`：现有 `stub.S` 只能记录为 Legacy outbound serializer；**不得简单改 45B 循环为复制几个槽就声称完成**。真正 Lua 列表写入与额外网络复制是不同问题。
- **PokéLDN**：`raid_point(raid, rewards)` 的 `rewards` 参数是**替换**，用于模拟原版+追加时必须先保留 `raid.rows` 再做纯函数合并；不可声称 PokéLDN 会自动叠加。
- **KM-Editor**：FlatBuffers `sbyte` 和静态 `subject enum 0–3` 不支持将 20 条 `subject5` 按原版静态 fixed 行直接录入；其 Lua54 代码启发 prototype 安全定位，但工具本身不能替代运行时 Lua 对象捕获。若移植 GPL-3.0-only 源码，须审核许可证兼容性与仓库分发义务；可优先实现独立接口规格。
- **原始 ROMFS/ELF**：不提交受版权保护的游戏二进制、密钥、完整游戏脚本。研究脚本以用户本地文件验证 SHA + Build ID，离线测试使用合成 fixture 或可授权分发的结果摘要。

## 5. 执行优先级和 Stop/Go 决策

- **第一迭代（可立即实施，无真机 Hook）**：P0-01 ～ P0-04；完成“追加”数据模型、纯函数、UI 明确模式与完整单测。
- **第二迭代（真正的技术关键路径）**：P1-01 → P1-02 → P1-03；静态可定位不等于实时可读取，三者不闭合不得启动可写 Runtime Hook。允许通过用户自有 RomFS 的一次性离线 Lua 模组作可行性对照，但不能将其当作热更新能力。
- **第三迭代（稳定生产化）**：P2-01 → P2-02 / P2-03 → P3-01 → P3-02；单人成功后再进入多人，不把联网工作反推为单人可用。
- **不可绕过的两道 Gate**：A0 原版奖励保留、可观测；实际单人 C→Bag→保存至少 1 条自定义行闭合。任一失败都不得用“Hook installed/armed/template SHA matched”宣称成功。

### 与之前实测的归因

`raid-20261019-162502-2f64d2a2.jsonl` 的 64 次只读快照证明 Legacy Hook 安装状态及 20/45 模板 SHA 一致，但没有 Lua A/B/C 或 native hit。以前单人掉落不变、护符确实存在、旧 Hook 修改网络缓冲与**整表覆盖式**错误，均由当前路线分别处理。

## 6. 来源锚点

- [PokéLDN 的网络 45 行和原版奖励构造](https://github.com/969981/pokeldn/blob/research/docs/sv_raid.md)
- [PokéLDN raid_point() 实现](https://github.com/969981/pokeldn/blob/research/pokeldn/sv/raid.py)
- [FieldRaidJewelry.LotteryRewardItem 固定／随机／额外护符行](https://github.com/Martmists-GH/SV-Script-RE/blob/1f843bd2517c707fbb47491dad58856eaa2c2cc7/decompiled/C81390F82EADEF632.lua#L820-L1335)
- [Lua subject5 过滤](https://github.com/Martmists-GH/SV-Script-RE/blob/1f843bd2517c707fbb47491dad58856eaa2c2cc7/decompiled/C07C34FC3B976E5A7.lua#L182-L320)
- [KM-Editor Lua54 prototype 解析](https://github.com/KotMatrosk1n/KM-Editor/blob/master/src/KM.Formats/Lua/Lua54BinaryChunk.cs)
- [旧 Hook 汇编](../../native/sv_remote_code_patch/stub.S)、
  [目前 A/B/C 运行时证据协议](violet400-abc-runtime-capture-contract.md)。
