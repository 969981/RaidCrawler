## Windows 远程安装（方案 A）最新核验

当前标准 botbase 的 `pokeMain` 仍只对应 `svcWriteDebugProcessMemory`。前置诊断新增 `getBuildID` **前 8 字节前缀**和 `remote-hook-a` 能力缺口报告；这不是 Hook 安装证据。详细研究：[windows-remote-hook-feasibility.md](windows-remote-hook-feasibility.md)。

# 全局奖励模板与任意场次的战斗构建 Hook（2026-10-19）

## 当前实现

- RaidCrawler 的奖励编辑器已从「选中一个坑位」解耦：即使没有读取坑位、没有选择 Seed，也能打开编辑器，配置 45 个物理奖励槽位。
- 模板存储位置：`%LOCALAPPDATA%\\RaidCrawler\\global-raid-rewards.json`。点击编辑器「写入奖励」会先保存这个全局配置，正常关闭编辑器也会持久保存；切换地图、扫描其他 Seed、关闭并重新启动程序后仍保留。默认仍为 20 行 999×Subject 5，余下 25 槽保持真正空白。
- 当前「写入奖励」的实际行为是 **准备模板 + 启动诊断窗口**，不是执行 Switch 内存写入。此状态必须维持到实机证据验证。JSON/BIN 数据保持原 45×16 字节布局。
- 实机日志不再把 UI 的 `seed=321EE013/den=7` 当作目标：新的 `scope=all-raids` 日志表明模板适用于未来实际创建的对战。 `actualBattleSeed=null` 只表示当前没有原生回调，不是永远不存在 Seed。
- `RaidRewardHookContract.TryStageOffline` 不再要求 `SelectedSeed=ObservedSeed`，也不要求本地 LDN 或官方互联网的某个预设模式。仍要求：匹配的 4.0.0 Build ID、独立 SessionId、本机房主角色真实确认、正确的**发送方向**、还未压缩的 0xAA0、合法 RaidPoint 前缀，以及同一场只处理一次。
- `NativeRaidCodeFingerprint` 包含原始 ELF 已核对的 8 条机器指令（其中 `main+0xE32984` 位于尾部复制之后、后续处理之前）。实机诊断只在标题为**紫**且游戏版本等于 4.0.0 时，用 SysBot 读取这 8 组指令并给出 `MATCH/MISMATCH`。**这是静态代码身份确认，不是 Hook 命中。**

## 原始 ELF 对真实 Hook 的价值与限制

经 `llvm-objdump` 再次检查原始 `Violet4.0.0.elf`：
```text
main+0xE327FC  serializer entry
main+0xE32814  mov x20,x0          ; 输入对象
main+0xE32828  mov w22,#0xAA0      ; 明文长度
main+0xE3283C  mov x23,x0          ; 输出明文缓冲区
main+0xE32968  add x1,x20,#0x78   ; RaidPoint 的剩余结构字段
main+0xE3296C  mov w2,#0x3C8
main+0xE32980  bl memcpy          ; 复制尾部
main+0xE32984  mov x8,x21          ; 复制之后的候选观察时刻
main+0xE32990  mov x1,x23
main+0xE32994  mov w2,w22
main+0xE329B0  b 0x1592B7C       ; 后续压缩/打包链
```
`x20+0x13C` 是输入对象第 0 行奖励；`x23+0x79C` 是输出缓冲区第 0 行奖励，总计 `0x2D0` 字节。**这些寄存器仅在本次函数执行中有意义**；对其运行时修改需要真实 Switch 侧 ARM64 Hook，不能通过多次 sys-botbase 轮询或写入某个固定数值来代替。

## 如何从现有版本升级到真实 Hook

1. 仅在自己的合法研究环境，先对单人／本地 LDN 运行**只读执行级追踪**，检查构建对战时 `E32984` 是否触发及正在处理的当前对象。必须与发送方向与实际主机角色关联，避免把接收端对象当成主持数据。
2. 有可靠数据后，在 Switch 侧使用具备安全暂停／恢复能力的原生执行拦截模块，于目标缓冲区生命周期内调用 45 槽替换；检查对象长度、`RaidPoint_`、实际会话 ID、指令指纹、线程及重入状态。
3. 注入的目标是**当时正在创建的那一场对战**，与 RaidCrawler 曾浏览的坑位无关。只有 Native Callback 能确认新对战后才标记 `HOOK_HIT`、`PATCH_APPLIED`、`PATCH_VERIFIED`，失败必须回滚；UI 不得自行猜测。
4. 对单人、本地 LDN 和互联网连接模式分别验证客户端实际收到的配置，及结算后的道具增量与存档。显示 999 仍可能受到道具 `SlotMaxNum` 的限制。

## 功能验收

- [x] 任意坑位均可打开全局奖励编辑器，不需读取／选坑
- [x] 45 槽全局配置以独立 JSON 原子保存／加载
- [x] 选择坑与真实对战 Seed 不一致时不再被离线 Hook 契约拒绝
- [x] 本地／互联网模式不再作为离线契约的硬阻断条件
- [x] 实机静态 8 条 opcode 只读检测及 JSONL 记录
- [ ] **Switch 侧原生执行级 Hook**：尚未安装，当前没有原生 `HOOK_HIT`
- [ ] 本机作为房主时的真实战斗构建回调证明
- [ ] 将写入结果通知给其他参战玩家，并验证入包／保存

**这份更新并未完成真实游戏里的 Hook 或奖励内存写入。** 若日志只有 `scope / config / opcode / fingerprint / manual-*`，不能将其解释为真实领奖成功。


---

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
