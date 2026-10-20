# 方案 A：参考 hexbyt3/SVRaidBot 自动识别房主阶段（纯 sys-botbase）

> 2026-10-20。**实现了被动读取和自动状态记录，不存在内存写入或额外 Switch 插件。** 本项目在《紫》4.0.0 上尚未有真实游戏状态转移的实机录制，不能把“可编译”当作状态偏移已验证。

## 原版实现交叉核查

固定上游提交 [hexbyt3/SVRaidBot@9c1407c](https://github.com/hexbyt3/SVRaidBot/tree/9c1407c765bb0ff01bf5fc8160c62d363c0a1547)：

- [`RotatingRaidBotSV.cs`](https://github.com/hexbyt3/SVRaidBot/blob/9c1407c765bb0ff01bf5fc8160c62d363c0a1547/SysBot.Pokemon/SV/BotRaid/RotatingRaidBotSV.cs#L3181-L3205)：
  - `IsConnectedToLobby()` → `ReadBytesMainAsync(Offsets.TeraLobbyIsConnected, 1)`，字节非零为已连接。
  - `IsInRaid()` → `ReadBytesMainAsync(Offsets.LoadedIntoDesiredState, 1)`，字节 `0x02` 为战斗中。
  - [`ReadTrainers()`](https://github.com/hexbyt3/SVRaidBot/blob/9c1407c765bb0ff01bf5fc8160c62d363c0a1547/SysBot.Pokemon/SV/BotRaid/RotatingRaidBotSV.cs#L3065-L3120) 读取 3 个 NID 槽位，非零代表对应连接者存在；**不是 ready 状态**。
  - [`GetLobbyReady()`](https://github.com/hexbyt3/SVRaidBot/blob/9c1407c765bb0ff01bf5fc8160c62d363c0a1547/SysBot.Pokemon/SV/BotRaid/RotatingRaidBotSV.cs#L2993-L3030) 包括机器人主动点击 A，不能把其成功理解为无需输入即可由单一状态字节确定所有人准备完毕。
- [`PokeDataOffsetsSV.cs`](https://github.com/hexbyt3/SVRaidBot/blob/9c1407c765bb0ff01bf5fc8160c62d363c0a1547/SysBot.Pokemon/SV/Vision/PokeDataOffsetsSV.cs#L14-L29) **直接注释为 3.0.0**：`TeraLobbyIsConnected=0x043DF430`，`LoadedIntoDesiredState=0x047D2020`，参与者 NID 指针链 `[0x475EA28,0xF8,0x8]`。
- [Eppin/Sysbot.NET 的 4.0.0 配置](https://github.com/Eppin/Sysbot.NET/blob/main/SysBot.Pokemon/SV/Vision/PokeDataOffsetsSV.cs) 提供野外指针 `[0x473ADE0,0x160,0xE8,0x28]`，其 [`IsOnOverworld`](https://github.com/Eppin/Sysbot.NET/blob/main/SysBot.Pokemon/SV/PokeRoutineExecutor9SV.cs) 使用 `data[0]==0x11`。这是 4.0.0 *公开源码* 的支持证据，仍需实机读取确认。
- **绝不能**直接把 hexbyt3 的 3.0.0 lobby/battle/NID 内存地址认定为《紫》4.0.0 已核验 RVA。这些字段在本工程被明确称作“实验旧版候选信号”，需用户确认才读取。

## 接入的源码

- [`SvRaidLifecycleReader.cs`](../../RaidCrawler.Core/Host/SvRaidLifecycleReader.cs)：仅通过现有 `ISwitchConnectionAsync` 被动读取，Title ID 和 Game Version 必须与《紫》4.0.0 一致；分别读野外、实验旧版候选房间、战斗、三个 NID 槽位（只计数量，不记录任何 NID/玩家身份）；读取错误不冒充否定值。
- [`SvRaidLifecycleClassifier`](../../RaidCrawler.Core/Host/SvRaidLifecycleReader.cs)：连续两次轮询同状态才提交一次阶段变更，未知值不会自动假设状态；下述 S3 一定带“推断”。
- [`SvRaidAutoMonitorWindow.cs`](../../RaidCrawler.WinForms/SubForms/SvRaidAutoMonitorWindow.cs)：纯 sys-botbase 中文窗口、启动/停止 1000ms 轮询、每30次重新核对游戏版本、阶段变更自动读取可选 `RaidPoint` 对象并对照奖励哈希。只读，无手柄输入，无 RAM Hook。
- [`SysbotOnlyHostWindow.cs`](../../RaidCrawler.WinForms/SubForms/SysbotOnlyHostWindow.cs)：新增“自动状态监测”入口。
- [`SvRaidLifecycleReaderTests.cs`](../../RaidCrawler.Tests/Host/SvRaidLifecycleReaderTests.cs)：稳定两次才判定、未验证地址禁止默认解释、S3 过渡推断、多状态优先级与读取异常退化。

## 自动阶段含义和可信度

| 状态 | 被动信号 | 准确度说明 |
|---|---|---|
| S0 野外 | Eppin 4.0.0 野外指针 `==0x11` | 有 4.0.0 开源实现，仍需用户实机确认 |
| S1 房间打开 | hexbyt3 旧版 `LobbyFlag!=0` | 仅实验候选，4.0.0 未核对 |
| S2 参与者加入 | `LobbyFlag!=0` 且三个NID槽至少一个非零 | 仅实验候选；玩家可能已离开但槽位未清除 |
| S3 疑似房间到开战过渡 | 前一状态为 S1/S2，现 lobby=false、未到野外、battle!=2 | **推断**，不代表所有人已准备，不证明已形成网络包 |
| S4 团体战 | 旧版 `BattleState==0x02` | 仅实验候选，4.0.0 尚未确认 |

对每个阶段，只在稳定连续2轮采样时记录一次，防止重复日志与网络读波动。S0 与 S4 优先按原始字节严格匹配；缺失数据保持 Unknown，而非假装成功。

## 如何使用

1. 下载 [纯 sys-botbase Windows host 构建](https://github.com/969981/RaidCrawler/actions/workflows/host.yml)，无需下载或安装 Switch 原生模块。
2. 打开《紫》4.0.0，正常连接 sys-botbase，主窗口点“纯sys-botbase”→“自动状态监测”。
3. **默认**实验旧版候选关闭，只对野外 S0 读数。若希望观察 S1-S4 的旧版候选信号，主动勾选并确认黄色警告，**只允许作为观察指标**。如有稳定候选 RaidPoint 地址，可输入后再开始；否则只记录状态。
4. 开房→让玩家加入→开始战斗，程序每秒读取一次，在状态变化时自动写入**PC 窗口日志**，并尝试读取同一候选 RaidPoint 的 SHA/行数。无需再人工点击 S0～S4。点击 **“保存 PC 诊断日志”** 可导出本机 TXT 文件；不会在 Switch 的 SD 卡保存文件。
5. 标注错误、未知和候选异常；若字段始终不变/反常，则退出实验，暂不建立奖励写入逻辑。游戏版本改变/连接失败时程序自动停止监测。

## 当前需要验证的关键阻塞

- **4.0.0 地址校准**：hexbyt3 的 3.0.0 `0x043DF430`、`0x047D2020` 和 NID 链是否在这台真实 Switch 的《紫》4.0.0 正确变化？不能只靠旧版源码作结论。
- **状态关联**：至少记录2～3次真实房主开房、参与者加入、开战、退出的完整自动日志；需要能够复现的信号序列才能把实验地址升级成可信4.0.0状态观测。
- **对象归属**：经过 ELF 三虚表筛选的对象是不是本次将发送的 RaidPoint？状态自动识别只能帮助同步采样，不能自动填补这个证明。
- **样本间隔**：1秒 sys-botbase 跨网轮询不能证明在瞬间序列化前必定命中目标；也不保证一个真实的写入窗口存在。
- **并发**：避免同时运行 RaidCrawler 其他自动读坑和时间推进任务，以免同一个 sys-botbase 连接争用。

当前路线仍是**方案 A 的被动验证**。方案 B RAM Hook 以及直接追加道具均未进入实机实现，不应把本轮状态机视为联网奖励已发放。
