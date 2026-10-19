# 原生官方联网奖励 Hook：实机诊断与 JSONL 追踪（2026-10-19）

## 当前交付状态

本分支的 **「写入奖励」** 入口已从单纯的「尚未就绪」信息框改为一个真实可操作的 **实机诊断窗口**：
- 直接复用 RaidCrawler 已连接的 sys-botbase **Wi-Fi/USB 连接**，用户主动点击「读取实机信息（只读）」时调用 `GetTitleID`、`GetGameInfo("version")`、`GetBotbaseVersion`。
- 自动记录用户当前选中的 `Seed/Area/Den/Species/Stars/Map/IsEvent`、全部 45 物理槽中非空行数、各 Subject 数量、720 字节奖励配置 SHA-256。
- 手动事件标记：进入房间、开战、结算。标记**不是**代码 Hook 命中；只提供时间锚点，不能用来证明内存已被写入。
- 每次开启诊断窗口生成独立 UTF-8 **JSON Lines** 文件，自动逐条 flush，保存在 `%LOCALAPPDATA%\RaidCrawler\raid-diagnostics\raid-YYYYMMDD-HHMMSS-XXXXXXXX.jsonl`；「打开日志目录」可直接查看。每行包含 `utc/session/stage/message/detail`。
- 为减少不必要的敏感信息，日志**不记录**任天堂账号令牌、好友代码、其他玩家 ID、原始战斗网络包、完整游戏内存或程序操作地址。分享日志前仍建议审查选中坑位的 Seed 等内容。
- 超时（18 秒）、意外断开、非朱紫游戏、朱紫版本切换及游戏版本与静态 ELF 样本不符都会单独记录状态与原因。

## 已确证及未确证的边界

已由 [PokéLDN](https://github.com/969981/pokeldn/tree/research) 的 `0x2F` bootstrap 研究与《紫》4.0.0 ELF 静态反汇编确认数据布局：明文 `0xAA0`，RaidPoint `0x3E8`，奖品区 `RaidPoint+0xE4`，共 `45 × 16 = 720` 字节。通过 [SV-Script-RE](https://github.com/Martmists-GH/SV-Script-RE/blob/1f843bd2517c707fbb47491dad58856eaa2c2cc7/decompiled/C07C34FC3B976E5A7.lua#L183-L321) 可以观察接收端按 Subject 过滤（`subject=5` 要有道具 2481）；[GameEventBattleResult](https://github.com/Martmists-GH/SV-Script-RE/blob/1f843bd2517c707fbb47491dad58856eaa2c2cc7/decompiled/C889F6553EDFCD011.lua#L1625-L1697) 负责结算背包。

**仍未确证**：官方互联网模式中，本机作为房主的发送回调是否经原始 ELF 的 `main+0xE327FC` 以及实际本场 `RaidPoint` 的对象生命周期；`x20/x23` 不是永久地址；`ReadBytesAbsoluteAsync/WriteBytesAbsoluteAsync` 无法替代原生 ARM64 执行时刻 Hook。故本版本**不安装 native Hook，也不会调用写入 API**，更不声称官方互联网参与者能获取异常奖励。日志仅记录 real-console **只读 preflight**，不是已安装 Hook 的运行日志。

## 如何收集可复现的实机日志

1. 在 RaidCrawler 主窗口连接自己的 Switch，读取太晶坑并选中准备主持的目标场次。
2. 点击「奖励编辑」，修改 45 槽、数量和 Subject 后选择「写入奖励」。当前该按钮**打开诊断窗，而不是执行写入**。
3. 在诊断窗口点击 **读取实机信息（只读）**，确认能获取标题 ID、游戏版本和 sys-botbase 版本；同时查看 Config SHA。
4. 按自己正常流程在游戏中开房/准备/战斗；对应阶段按「标记：已开房／已开战／已结算」，确保同步时间戳。不会干预 Switch 游戏输入。
5. 在窗口点击「打开日志目录」，取最新 JSONL 进行复核。**若日志没有真实 Hook callback event，就不能把手动标记当作该回调的证明。**

### 日志例子（示意，不是本轮实机输出）

```jsonl
{"utc":"2026-10-19T11:15:00Z","session":"example","stage":"config","message":"45槽校验通过；20/45 已配置","detail":{"configured":20,"payloadLength":720}}
{"utc":"2026-10-19T11:15:02Z","session":"example","stage":"console","message":"TitleID=...; game=4.0.0; botbase=..."}
{"utc":"2026-10-19T11:15:04Z","session":"example","stage":"hook","message":"原生 Hook：未安装；不具备官方联网奖励写入能力。"}
{"utc":"2026-10-19T11:16:00Z","session":"example","stage":"manual-lobby","message":"用户手动标记：已进入官方联网房间。并非 Hook 触发证据。"}
```

## 真正实现原生 Hook 的前置技术任务

| 项目 | 现状 | 必要证据 |
|---|---|---|
| 发送端 ELF serializer RVA | 《紫》4.0.0 静态指令已复核 | 实机确认正在主持官方互联网房间时的真实执行调用 |
| 房间／场次归属 | 目前只有 RaidCrawler 选中坑快照 | 运行时可靠 session ID、host role、Seed 比较 |
| 安全的可修改对象 | 0xAA0 未压缩布局已知 | 精确发送前目标、缓冲区所有权、线程一致性及复入保护 |
| 真正的 Hook & 原字节备份 | **尚未实现** | 在合法可恢复的环境下通过签名检查、写入/读回测试、失败回滚 |
| 真实多人奖励 | **尚未实测** | 相同 Subject 在 host/guest、有无护符时的入包结果 |
| 异步存档持久化 | 尚未实测 | 完成结果、重启前后道具增量、错误处理与版本适配 |

不能对未知构建 ID 使用静态 RVA；不能靠循环写入随机 0xAA0 片段碰运气；官方联网对异常奖励是否存在额外处理仍需实证。

## 相关代码

- [RaidRewardDiagnosticForm.cs](../../RaidCrawler.WinForms/SubForms/RaidRewardDiagnosticForm.cs)
- [RaidRewardDiagnosticLog.cs](../../RaidCrawler.WinForms/SubForms/RaidRewardDiagnosticLog.cs)
- [RewardEditorForm.cs](../../RaidCrawler.WinForms/SubForms/RewardEditorForm.cs)
- [RaidRewardHookContract.cs](../../RaidCrawler.Core/Structures/RaidRewardHookContract.cs)
- [官方联网原始研究](official-online-host-reward-write-plan.md)
