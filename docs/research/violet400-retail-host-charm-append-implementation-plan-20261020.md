# 《紫》4.0.0 真机房主晶耀护符奖励追加：优先 LDN 实机验证的实施路线

2026-10-20，仓库：`969981/RaidCrawler`，目标分支 `benefit`。
状态：**技术规划/待开发；未安装新 Hook，未证实接收方获得自定义 subject=5 奖励。**
研究目标：用真实 Switch 当房主、任意支持的星级/Seed/地图开本地联机团体战，将原本正常奖励保留，向**持有晶耀护符（重要物品 ID 2481）**的原版参与者额外提供用户选择的奖励（最终愿望为 20 类道具，各请求数量 999）。

## 一、证据级别与不能混淆的实验

A. [上游 PokéLDN 归档的真实参与 Switch 奖励实验](https://github.com/969981/pokeldn/blob/research/docs/research/sv/references/SV_RAID_REWARD_PROFILES.md)：模拟房主发送修改后的 `0x2F` Bootstrap。正常零售参与端曾收到先机球 ×500、也曾收到按原来数量重映射的高级球/经验糖果，表明**本地 LDN 中接收方会消费部分由房主控制的奖励行**。这些实验验证的是**替换整个奖励列表**，不是保留原版之后再追加 20 行，不是 `subject=5` ×999，更不是官方 Nintendo 互联网房间。
B. [上游发送时序分析](https://github.com/969981/pokeldn/blob/research/docs/research/SV_Raid_Bootstrap_Reward_Timing_2026-10-20.md)：大厅 `0x2C/0x2D/0x2E/0x30` → 房主发 `0x0C` 请求开始 → 参与者 `0x0D` 确认 → 之后发送 `0x2F` Bootstrap → 加载 → 开战。因此**不是玩家入房就发奖励**；真正处理的时间是战斗准备阶段创建/序列化 Bootstrap 时，领取则在战斗胜利结算时。
C. 本人有精确匹配的《紫》4.0.0 ELF、main.blua、P1-B1 实机 Lua 全局状态指针链稳定记录。它们支持后续本地单人/房主**自己的**领奖机制研究，但**不要求先完成 P1-B2 的 CallInfo 结构读取才能研究联机房主对接收方的 0x2F 发送**。
D. 当前 RaidCrawler `native/sv_raid_reward_hook` 的 `main+0xE32984` 是 x23 指向解压前 `0xAA0` Bootstrap 输出副本的序列化后拷贝时点，有 Skyline inline Hook 源码。**目前无真正的实机 HOOK_HIT**；原有 `apply` 是`720B copy_from_slice(template)`，是整表替换，不能用于本需求。

## 二、网络奖励的真实格式：使用新版核验，不继承旧归档错误

| 范围 | 长度 | 意义 |
| --- | ---: | --- |
| `Bootstrap[0x000..0x6B8)` | `5×0x158` | 四人 PK9 + Boss |
| `Bootstrap[0x6B8..0xAA0)` | `0x3E8` | 已生成的 RaidPoint |
| `RaidPoint[0xE4..0x3B4)` | `0x2D0 = 45×16` | 真正的 45 个奖励记录 |
| `Bootstrap[0x79C..0xA6C)` | 同上 | 发出的原版 + 待追加奖励 |
| `RaidPoint[0x3B4..0x3B8)` | 4B | 不能误当第 46 行 |
| `RaidPoint[0x3B8..)` | 摘要等 | 不能覆盖 |

每条记录 `<u32 ItemNo, u32 ItemNum, u32 rareType, u32 subjectType>`，little-endian；新增行选用 `rareType=0, subjectType=5`，接收端原版 Lua 资格判断使用 `Bag.GetNumItem(2481)>0`。注意 [历史归档的 RewardRecord 写法](https://github.com/969981/pokeldn/blob/research/docs/research/sv/references/SV_RAID_BOOTSTRAP_ENCODER.md) 将奖励区起点标为 `0x798`、字段标为 `marker,item,quantity,reserved`，与当前 4.0.0 源码和 ELF 交叉校验结果**不一致**；实现应以 [新布局审计](https://github.com/969981/pokeldn/blob/research/docs/research/sv/sv400-custom-reward-pointer-patch.md) 为准，而不是拷贝旧实验的硬编码结构。

## 三、分两条路径：先帮助正版联机参与者，再考虑房主/单人自己的奖励

### 路线 A：真实 Switch 房主向本地 LDN 参与者提供额外奖励（**第一优先**）

执行节点和实现位置：

1. 在用户授权的 Violet 4.0.0 游戏环境中用原有 `native/sv_raid_reward_hook` 的 **observe-only** 功能，验证 `main+0xE32984` 执行命中。Hook 上下文中 `x20` 是序列化输入对象、`x23` 是此次序列化输出明文缓冲。源奖励行相对 `x20+0x13C`；输出奖励相对 `x23+0x79C`。保留原始 `0xAA0` 中非奖励区域不变。
2. **必须先在真实房主开坑时抓到 HOOK_HIT**，并能确认消息方向/角色是房主 outbound `0x2F`，而不是其它序列化、缓存或读回。读出**真正本场** 45 条中的有效行和空位。记录 runId、BuildID 校验、场景阶段、baseRowCount、subject 分布、row SHA256；不写 Switch 存档，不记录完整参与者 PK9/账户数据。
3. 只有 A1 原版网络发奖候选成立后，新开发一个明确独立的 `append-charm` 模式（**不要启用旧 `apply`**）。创建新的固定大小 45 行写入缓冲，**先复制原版整块奖励区**，仅在经过验证的未使用尾部区域添加自选 `(ItemNo,ItemNum,0,5)`；原版顺序、item、count、rare、subject **逐字节不变**。任何格式不明/跨界/空间不足时，保留原始 720B 并跳过改动；日志输出容量限制。不允许清空普通固定/随机/团体战料理/原版晶耀护符太晶碎块行。
4. 在有效的 Serializer 现场、LZ4 压缩前完成补充，校验添加前后 diff 仅命中**原本已经确认为空**的完整 16B 奖励槽。网络包无需 PC 重压 LZ4；修改原版 Native 明文缓冲后，让游戏按正常函数路径继续压缩和发送。
5. 下一步在**另一台未修改的正版 Switch**、同一场次对照接收 Bootstrap 的行摘要、游戏奖励界面、领奖者背包变动和重启存档持久化。房主客户端自身可否同时收到新增奖励必须**单独测**：修改的是出站消息副本，不等于房主自己结算使用的 Lua RewardData。
6. P0 验证时只测试本地 LDN。跨互联网的 LDN 中继是否沿用相同路径需要独立验收。Nintendo 官方在线房间的服务器/会话路径不能由 LDN 成功推导为成功；仍待调查。

**强制容量门槛：** `45` 是整个奖励表容量，**不是“额外给用户留的 45 条”**。目前编辑器 `20/45` 只说明自定义模板用了 20 槽，并不能证明真实坑还留有 20 个空槽。完整 20 条自定义奖励只有在原版本场实际奖励占用 `≤25` 个完整槽且尾部追加可行时才具备不覆盖原版的网络格式容量；超过则明确显示 `CAPACITY_EXCEEDED`，不 silently trim、合并、更改原始奖励或覆盖 `+0x3B4` 间隙。奖励数组若有中途空洞/内部元数据，须另外证明填洞安全，否则拒绝。

### 路线 B：房主单人/房主自己也得奖励（**第二优先**）

`main.blua` 的 `LotteryRewardItem (root/2899)` → `BattleRewardData.F316... (root/4870)` → `RaidResultAddItem (root/3756)` 链是本地领奖来源。尤其过滤在 PC134 **写入新的 [3] 列表**，结算从 `[18][92][3]` 取最终列表。单人模式可能根本不会触发 outbound `0x2F`，因此单靠 Serializer Hook 不可靠。

继续 Lua VM 审计/执行级采样，直至捕获同一场次的 A0 原版、B 过滤新 [3]、C 结算；确认安全 Lua VM 线程/GC 操作窗口后再新增一条、再扩展。**不能把房主端 Lua 列表修改和房主对参与者的网络 0x2F 数据修改当作同一个写入。**

## 四、分阶段验收/测试矩阵

| 阶段 | 最小变更 | 应有真实可核验日志 | 通过判据 |
| --- | --- | --- | --- |
| N0 观测 | 原生 sidecar Hook `observe`；不改奖励 | `ELF_FINGERPRINT`, `NATIVE_HOOK_INSTALLED`, 本场 `HOOK_HIT`, 原版奖励行摘要 | 真人 Switch 房主至少一个真实 outbound 0x2F 现场 |
| N1 数据完整 | 只读对比源 x20 与输出 x23 的 45 槽 | 角色/方向、原版行数、非零行类型、尾部空位、source/output hash | 建立本场“原版奖励+可用空间” |
| N2 最小追加 | **一条正常道具×1, subject=5**，默认仅 LDN | `APPEND_APPLIED`, diff/CRC, `0x2F` 发出 | 原版行顺序和值全保留、接收方实际多出一行 |
| N3 资格验证 | 另一台原版零售 Switch，持有与不持有 2481 对照 | 同场行/结果、before/after Bag、reboot 读回 | 持护符方取得新项，未持有者不应取得 subject=5 额外行；原版奖励保留 |
| N4 扩容 | 1→5→最多 20 行，数量 1→小量→999 | 实际 `baseCount`、容量校验、所有道具 `SlotMaxNum` 和入包差分 | 没有遗漏原版条目；不够容量就跳过/明确降级，不虚报全部成功 |
| N5 范围 | 普通/活动、各地图、不同星级、LDN 中继、官方互联网 **分别** 验证 | 模式真实证据和独立对端领取日志 | 只有已验证的模式才宣称兼容 |

测试开始前先备份，避免错误奖励注入造成房间异常；正式软件 **默认 observe-only**，apply 需要独立明确授权且支持关闭/撤回。原版奖励数量和功能不可退化，失败不删除原版数组。Hook 回调中不可做阻塞磁盘 IO/大数组分配/任意 Lua GC 解引用；记录最小计数至有界缓冲，非实时线程传送给 RaidCrawler。

### 最高优先级的下一件事

**N0：实机启动原生 observe-only Hook，确认真人 Switch 房主实际发送 0x2F 时的 HOOK_HIT。** 现有 Windows P1-B/P1-B2 指针工具（`CANDIDATE_POINTER_STABLE`、`STATE_FIELDS_CHANGED`）与这个网络方向执行点无关；继续点击它们不会自然得到玩家接收的奖励列表。

如果 N0 没命中，必须定位真实房主 Serializer/Native 发包路径；**不能贸然把旧 `apply` 当作追加实现**。如果 N0 命中，立刻着手 N1 从这一场真实源/输出读取奖励，复用原版副本形成安全 N2。

## 五、与源码差异（需新增工作，当前未实施）

- `native/sv_raid_reward_hook/src/lib.rs`：保留 observe 并引入出站方向判别、原版奖励行计数、受保护的 `append-charm` 状态、非阻塞遥测；现有 `mode=apply` **整表覆盖**不符合“保留正常奖励”，不能复用。
- `native/sv_raid_reward_hook/src/format.rs`：增加 `append_charm(original, additions)` 的**完全无损失败关闭**检查/合成；空间不足返回原版，包含多种 sparse/空槽/非标准行边界测试。
- `RaidCrawler.Core/Structures/RewardAppendComposer.cs`：只作 UI/离线合同检查，不能代替原生实机原版行的即时读取。
- `RaidCrawler.WinForms/SubForms/RaidRewardDiagnosticForm.*`：显示同场 N0/N1/N2 真实来源的 `baseCount,freeSlots,appendCount,overflow,sourceSha,patchedSha,receiverReceipt`；UI 表示`NOT_OBSERVED`直到实机原生日志/客机反馈到达。
- `docs/research/violet400-p1b2-reward-table-diagnostics-20261020.md` 中的 37×基础资源 JSON 是**静态资料浏览**；不能作为真实正在开坑的奖励行输入。

资料：
- [历史可领奖实验](https://github.com/969981/pokeldn/blob/research/docs/research/sv/references/SV_RAID_REWARD_PROFILES.md)
- [历史初始化数据逆向](https://github.com/969981/pokeldn/blob/research/docs/research/sv/references/SV_RAIDPOINT_REVERSE_ENGINEERING.md)（早期行偏移须以上述新版勘误为准）
- [新版指针/奖励布局](https://github.com/969981/pokeldn/blob/research/docs/research/sv/sv400-custom-reward-pointer-patch.md)
- [开始时间与房间消息](https://github.com/969981/pokeldn/blob/research/docs/research/SV_Raid_Bootstrap_Reward_Timing_2026-10-20.md)
- [当前原生 Hook 源码](../../native/sv_raid_reward_hook/src/lib.rs)
- [当前整表覆盖实现](../../native/sv_raid_reward_hook/src/format.rs)
