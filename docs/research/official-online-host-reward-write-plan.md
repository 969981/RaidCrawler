## 2026-10-19 增量：SV-Script-RE 与预加载 Hook 契约

研究链路已直接阅读并核对 [SV-Script-RE 固定版本](https://github.com/Martmists-GH/SV-Script-RE/tree/1f843bd2517c707fbb47491dad58856eaa2c2cc7)：

- `C849FF496D0E93636.lua` 第 1512–1549 行：网络战斗事件从 `self[17].raidBattleRewardData` 复制/包装到 `self[19][92]`，随后调用奖励筛选。它是**战斗配置消费阶段**，不能从此直接推断原生互联网主持端网络发送的全部路径。
- `C07C34FC3B976E5A7.lua` 第 183–321 行：`BattleRewardData.F316609DEA110601D` 读取 `Bag.GetNumItem(2481) > 0`；根据 `subjectType=0..5`、房主身份、一次性条件、团体战力顺序进行筛选。**必须保留原始 45 个物理槽位顺序**，特别是 `subject=4`。
- `C889F6553EDFCD011.lua` 第 1625–1697 行：`GameEventBattleResult.RaidResultAddItem` 根据过滤后 `ItemNo/ItemNum` 调用背包增加，不能把 `Quantity=999` 误解成保证净增加 999。
- `C14A8D35D72F56FE9.lua` 第 2820–2835 行：`Bag.GetNumItem` 由背包状态读取 ID 2481 的数量。这不是 Nintendo 官方联网 Hook 的入口。

已创建 [RaidRewardHookContract.cs](../../RaidCrawler.Core/Structures/RaidRewardHookContract.cs) 和 [单元测试](../../RaidCrawler.Tests/RaidRewardHookContractTests.cs)：这是未来 Switch 侧发包前回调的数据契约，要求游戏 Build ID、场次 Session ID、Seed 一致、官方联网模式/本机房主确认、发送方向的 0xAA0 明文且还未压缩，匹配时只创建奖励替换**副本**并保证同一场次只处理一次。该类完全离线、不会安装 AArch64 跳转，也不能直接写入 Switch 进程。

### 为什么不能把现成 ELF RVA 当作有效的在线注入 Hook？

`main+0xE327FC` 在 4.0.0 中经虚表注册为**bootstrap 序列化函数**；`main+0x1E62FE4` 是恢复函数。ELF 静态结构和 PokéLDN 0x2F 模拟确实一致，然而**缺乏本机作为 Nintendo 在线房主时实际走到发送回调的运行时证据**。静态 RVA 不是宿主的一个“监听奖励表加载”的常驻事件，而且 `x20/x23` 的有效期只是序列化函数的一次调用；进入实际联网 Hook 前必须捕获发包模式、当前场次、正确的 buffer ownership 与同步时序。

即便满足上述条件，仍需独立在有授权的测试设备上实现和调试 Switch 侧原生拦截模块，并有严格的重入保护、异常回滚和退出时的恢复。**当前版本绝不自动向 Nintendo 官方联机房间写入奖励，也不绕过服务器校验。**

## 2026-10-19 新奖励编辑器 P0

- PKHeX.Core `ItemStorage9SV` + 简中/英文 `GameInfo.GetStrings` 构建有效道具目录；搜索按精确 ID、中文/英文前缀、子串、非连续字符依次排序。不展示晶耀护符 2481 等重要物品为发放候选。
- 45 个固定槽：点击“奖励道具”弹出带搜索输入框的下拉面板，默认 20 行，其余为空；点击候选自动设置 ID，并在空槽设置 `Quantity=999`、`Subject=5`、`Rare=0`。
- 底部只保留“**写入奖励、导入 JSON、导出 JSON、查看**”。清空单槽 (Delete)、恢复默认 20 行、清空全部、Subject 快捷设置和 720 字节 BIN 处理移至右键菜单。
- 关闭窗口时，在配置正确的情况下自动暂存 45 槽到当前坑本次会话（`MainWindow.RewardPreviews`），不需要一个额外的“应用预览”按钮。
- “查看”展示当前 45 槽与游戏计算奖励两个标签页；导出 JSON 保留全部 45 个位置与 null，Subject 4 顺序不被压缩。
- **“写入奖励”按钮是诚实的诊断入口，不进行写入。** 没有在线 Hook 之前，点击它只提示缺少运行时证明，不会产生错误的“写入成功”。

# 官方互联网手动主持：奖励区写入研究和阶段验收（2026-10-19）

> **当前状态：已开发 20 行晶耀护符默认配置、完整 45 个物理槽位、Subject 0–5 编辑、720 字节编码及离线 RaidPoint/Bootstrap 替换验证；还没有安全可复现的 Nintendo 官方互联网主持端运行时 Hook，因此当前软件**不执行 Switch 官方联网奖励内存写入**。下文区分已证实的数据格式与未证实的在线运行时目标，不能把 GitHub CI 绿色当成联机发奖成功。

## 目标场景

操作人在正版《朱／紫》里手动选择当前坑，点击 **「挑战团体战 → 邀请其他玩家」**，亲自开任天堂官方互联网房间并打坑。RaidCrawler **不接管房间、不启用 PokéLDN 自身 LDN 主持**。用户希望事先配置共 45 个 RewardRow 槽位，然后在**本场真实游戏的发送前数据对象**进行受控替换；其他地图坑与存档不变。

这与 `--raid-reward` 模拟本地 LDN 主持完全不同：LDN 的成功实验只证明 0x2F 明文格式及接收端过滤；**原生官方联网主持是否运行相同发包路径、存在额外服务端处理，尚无可靠观察。**

## 已开发：20 行预设 + 25 个真正空槽

[源码 RaidRewardPresets](../../RaidCrawler.Core/Structures/RaidRewardPresets.cs) 的顺序与用户截图一致（20×999，Rare=0，Subject=5）：

| 槽号 | 名称 | ItemID |
|---:|---|---:|
| 01–08 | 速度球、等级球、诱饵球、沉重球、甜蜜球、友友球、月亮球、竞赛球 | 492、493、494、495、496、497、498、499 |
| 09–10 | 银色王冠、金色王冠 | 795、796 |
| 11 | 大师球 | 1 |
| 12 | 神奇糖果 | 50 |
| 13 | 经验糖果 XL | 1128 |
| 14 | 巨大金珠 | 581 |
| 15–19 | 秘传：苦、辣、酸、甜、咸味料 | 1907、1908、1906、1904、1905 |
| 20 | 特性膏药 | 1606 |
| **21–45** | **真正空白：四个 u32 全 0** | 无 |

[RewardEditorForm](../../RaidCrawler.WinForms/SubForms/RewardEditorForm.cs) 固定展示 45 行，空行可直接编辑；新增奖励默认 999/Subject=5；清空选中槽保持物理索引，**不把后续 subject=4 料理行左移**。导入/导出 JSON 包含 45 个位置（旧版紧凑 JSON 也可加载），720-byte BIN 保持空洞顺序，重新打开当前坑的会话预览亦保持空洞。

[编码器 RaidRewardEditor](../../RaidCrawler.Core/Structures/RaidRewardEditor.cs) 的新增 `EncodeSlots/DecodeSlots` 支持任意槽位空洞；旧版 `Encode/Decode` 仍保留用于紧凑列表和旧测试。全部 45 个槽共享 `Subject 0..5`，**不是每种 subject 各有 45 行**。游戏的 4×u32 小端行结构和上限来自 [PokéLDN](https://github.com/969981/pokeldn/blob/research/pokeldn/sv/raid.py)：

```text
RaidPoint 0x3E8:
  +0x000 .. +0x0E3: raid header / boss parameters
  +0x0E4 .. +0x3B3: 45 × 0x10 RewardRow
    +0x0 ItemID u32 / +0x4 Quantity u32 / +0x8 Rare u32 / +0xC Subject u32
  +0x3B4 .. +0x3B7: not a 46th reward row
  +0x3B8 .. +0x3E7: remaining metadata
Bootstrap 0xAA0: RaidPoint starts at +0x6B8; reward row #0 at +0x79C
```

新增 [RaidPointOfflinePatch](../../RaidCrawler.Core/Structures/RaidPointOfflinePatch.cs) 可严格在**已经合法获得的、未压缩** 0x3E8/0xAA0 文件中替换奖励，要求 `RaidPoint_` 前缀、正确长度，复制原字节后仅替换 720-byte 区域，其余字节保持逐字一致；**没有连接 SysBot，也不处理压缩的 Pia 包或联网通信**。[单元测试](../../RaidCrawler.Tests/RaidPointOfflinePatchTests.cs) 验证首尾元数据不被损坏、Subject 5 的字节编码和空位归零。

## 证据：PokéLDN / ELF / Lua / SVRaidBot

| 证据 | 能确认 | **不能**推导 |
|---|---|---|
| [PokéLDN research](https://github.com/969981/pokeldn/tree/research) | LDN 自建主持在开始战斗交接阶段生成 0xAA0 明文和 0x2F bootstrap；第 0 行位于 0x79C；原版客户端可按 subject 过滤 | Nintendo 官方互联网房主必然调用同一序列化入口，或允许替换字段 |
| [《紫》4.0.0 ELF 序列化](https://github.com/969981/pokeldn/blob/research/docs/research/sv/violet400-elf-raid-bootstrap-audit.md) | `main+0xE327FC` 生成 0xAA0，RaidPoint 从 `+0x6B8`，`main+0xE32984` 附近是 **候选只读观察窗口** | `x20/x23` 是跨场次、跨版本可用的永久指针 |
| [SVRaidBot_CHS 07/09](https://github.com/969981/SVRaidBot_CHS/tree/research/docs) | 同一 vtable `0x44592E0/0x44592E8` 指向发/收 `main+0xE327FC`、`main+0x1E62FE4`；接收对象 `this+0x13C` 为第一奖励行 | 已找到 Nintendo 在线的上层 dispatch 或稳定拦截对象 |
| [SVRaidBot_CHS RotatingRaidBotSV](https://github.com/969981/SVRaidBot_CHS/blob/research/SysBot.Pokemon/SV/BotRaid/RotatingRaidBotSV.cs) | 该项目自动操作**游戏内官方在线连接**与主持 UI，跟踪 `_connectedOffset`、房间及玩家；`RaidMemoryManager.InjectSeed` 写的是 0x20 坑位记录 | 只靠 Seed 注入就能覆盖战斗 bootstrap 的 45 行奖励 |
| [Lua 奖励与背包](https://github.com/969981/pokeldn/blob/research/docs/research/sv/raid-reward-to-bag-save-chain.md) | 游戏按 Subject 0–5 过滤；Subject 5 读取晶耀护符物品 2481；胜利后受 `SlotMaxNum` 和保存流程限制 | 显示 999 必然真实净增 999、存档永久保留 |
| [Library《紫》4.0.0 原生技术文档](https://github.com/969981/SVRaidBot_CHS/blob/research/docs/07-p1-raidpoint-receiver-boss-hp.md) | 前期 ELF 已核验 Build ID `709bfd66115298640155fcc4979dba151c7cc79a`；附有原生反汇编定位 | 本次已经找到/验证了 4.0.0 之外版本的指针 |

ELF 身份：Library 研究记录的 SHA-256 `d17403eaa9d844db82f647fd2f1518806b10c0518d53602fc0ac1766298fdba0`、Build ID `709bfd66115298640155fcc4979dba151c7cc79a`。本轮 Library 列表未发现可再次 materialize 的原始 ELF 本体，因此使用前期**已核验的 ELF 报告**及同一仓库源码交叉对照，没有虚构新的二进制反汇编结果。

### 重点：官方联机发送端尚未被定位

原版 ELF 可显示下面一段数据流，但“正在执行这段代码的进程角色”和“在哪个网络模式中执行”未闭合：

```text
main+0xE327FC                  RaidBootstrap serialize 候选
  x20 = 本次输入对象
  x23 = 本次明文输出对象
  x20+0x13C  第一奖励行（源）
  x23+0x79C  第一奖励行（复制后输出）
main+0xE32984                  memcpy 返回后候选观察点
```

相邻接收 `main+0x1E62FE4` 是**反序列化方法**，其 `this+0x13C` 来源于已经接收的明文。**这不是原生房主发送前的通用写入目标**；在客户端上修改接收对象也不等于给其他玩家发送奖励。

## 官方在线手动主持 P0–P3：必须完成的技术关口

1. **P0 先纯只读：版本及角色确认。** 获取当前游戏 Title ID、Main Build ID、游戏版本、连接方式（SysBot/Base）、在线状态和“本机为房主”标志。以上与实际原生 Hook 条件必须绑定，不得只按 Seed 推测；活动坑、普通坑各自记录状态。
2. **P1 发送路径验证：** 在自己有权研究的环境捕获“在线手动开房 → 玩家 Ready → 战斗启动”期间 **发送方向** `0xE327FC` 函数是否执行，记录实例地址、调用栈和正在发送的 0xAA0 明文摘要。与不在线的本地 LDN 及普通单人模式做对照，确定何种模式触发、是否先经游戏/服务器生成另一份 RaidPoint。**没有此记录绝对不能声称找到了官方联网的注入时机**。
3. **P2 写入方案评审（尚未开发）：** 若已确认房主发送并且消费者接受，需要 Switch 侧受控代码 Hook **挂起当前原生函数**，在复制前的源对象或复制后的本次独立输出明文中，只替换 `0x2D0`，验证对象/缓冲区边界以及一致的房间/Seed/build；记录一次性事务 ID、原数据 SHA、修改后 SHA，失败时不恢复错误帧。现有 RaidCrawler 的 SysBot TCP/USB 读写能力本身**无法提供 CPU 执行寄存器级暂停/回调保证**；靠高频轮询 720 字节很可能错过窗口。
4. **P3 安全的真实测试顺序：** 离线 Golden Fixture → 小额单人模拟 → 本地 LDN 有/无晶耀护符差分 → 再单独验证官方在线房主、发出的配置与客户端实际收到的内容及保存结果。每次复核物种/星级、玩家角色、备份／失败退出条件。不能拿 ×999 的展示截图当作可靠成功证据。

### 暂不提供的危险按钮

本分支的 **「原生联网写入检查」** 按钮只说明当前阻塞原因，不执行虚假的“写入成功”。旧的“PokéLDN 本地主持”作为单独可选路径保留并**明确注明不等于任天堂在线**。官方在线实际写入需要先有 P1 中的实机观测证据；目前不能承诺通用可用。

## 后续交付条件

- [x] 20 行 screenshot preset / 25 空槽、保留 45 槽物理索引
- [x] 所有 Subject 0/1/2/3/4/5 可修改；默认全 5
- [x] 720-byte BIN / 45-position JSON；本地配置 per Seed 会话保留
- [x] 0x3E8/0xAA0 离线替换只改变奖励区（测试）
- [ ] 官方互联网原生房主发送路径的实机只读证据
- [ ] 稳定的发包前处理目标以及安全 Hook/恢复语义
- [ ] 多人实际奖励/入包/保存/重进复测
- [ ] 对每个游戏版本与活动坑的适配、失败保护

**当前实做功能是可靠的模板和离线组包研究基础；任天堂官方互联网修改奖励的最终能力尚未完成。**
