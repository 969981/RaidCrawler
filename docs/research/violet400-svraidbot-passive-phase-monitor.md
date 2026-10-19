# 《紫》4.0.0 自动团战阶段观察：Hexbyt3 SVRaidBot × 原始 ELF

**开发分支：`benefit`。方案是纯 Windows/sys-botbase，只读，不安装其他 Switch 插件，不按 A、不改奖励、更不自动保存。**

## 上游实现（实际阅读的源码）

[hexbyt3/SVRaidBot](https://github.com/hexbyt3/SVRaidBot) 的：
- `SysBot.Pokemon/SV/Vision/PokeDataOffsetsSV.cs`：`TeraLobbyIsConnected = 0x043DF430`、`LoadedIntoDesiredState = 0x047D2020`、`OverworldPointer = [0x473ADE0,0x160,0xE8,0x28]`。**注意上游字段注释仍带有 3.0.0，虽然类常量 `SVGameVersion` 是 `4.0.0`。**
- `SysBot.Pokemon/SV/BotRaid/RotatingRaidBotSV.cs`：
  - `IsConnectedToLobby`（约 3181 行）：`ReadBytesMainAsync(TeraLobbyIsConnected,1)`，非零表示连接。
  - `IsInRaid`（约 3200 行）：`ReadBytesMainAsync(LoadedIntoDesiredState,1)`，`data[0] == 0x02`。
  - `GetLobbyReady`（约 2993 行）：循环等到房间连接成立（机器人还会主动按 A，但 RaidCrawler **不复制按键逻辑**）。
  - `EnsureInRaid`（约 1365 行）：等到进入战斗，并检查房间连接仍存在（同样不复制按键逻辑）。
  - `ProcessBattleActions`（约 1527 行）：房间连接消失就返回，视作本轮战斗阶段结束；**这不是领奖画面或背包增加证明**。
  - `FinalizeRaidCompletion`（约 1609 行）：等待回到 overworld，再继续机器人流程。
- `SysBot.Pokemon/SV/PokeRoutineExecutor9SV.cs`（约 287 行）：Resolved Overworld 字节为 `0x11` 时算在野外。

## 在用户原始 ELF 上重新验证

`Violet4.0.0.elf` SHA-256=`d17403eaa9d844db82f647fd2f1518806b10c0518d53602fc0ac1766298fdba0`，长度 74,362,480B。程序段：`R E` 为 `0x0..0x343FC90`；RW 从 `0x4383000` 到 `0x47D3000`。

直接扫描 AArch64 ADRP 和后续访存，发现：
- `0x43DF430`：两处候选同页 ADRP + 精确偏移的 64-bit store 访问，表明该地址被代码使用。
- `0x47D2020`：至少六处候选 ADRP + 精确偏移的 word/qword 读写访问，表明该地址被代码使用。

**这一层只验证偏移是 ELF 中真实访问的地址，不等于证明这些地址所存字节在《紫》4.0.0 的特定时刻仍分别等于 SVRaidBot 的阶段含义。** 已接入的 Windows 监视器必须记录原始字节及其时间变化供实机校准；不接受只看某个非零字节就宣布结算完成。

## RaidCrawler 自动读状态

1. 打开 **奖励 Hook 实机诊断**后，验证 TitleID、游戏版本、Build ID 前 8 字节、原始 ELF 指令锚点和 Main NSO base；通过后自动启动只读监视，2 秒一次。
2. 读取 `main+0x43DF430`、`main+0x47D2020` 及 Overworld 指针（有可能不可用，记录 null）。**不读取 Raid Code / NID / 训练家隐私**。
3. 只有连续两次一致才触发阶段事件；未知值、内存读错误、`Loaded > 5`、同时存在「Overworld+Lobby/Battle」的矛盾值均忽略。
4. `L!=0` → **auto-lobby-open**；`Loaded==2` → **auto-battle-entered**；**仅限本次确实观察到房间存在过**，已进入战斗之后 `L==0` → **auto-battle-connection-ended**（仅表示房间连接结束）；之后 Overworld==`0x11` → **auto-overworld-returned**（仅表示返回野外）。**单人战斗如果整个过程中 L 恒为 0，不得在 loaded=2 时马上报告战斗结束；需等待 loaded 不再是 2 且返回 Overworld=0x11，才能报告“回到野外”，仍不能证实结算。**
5. 若从房间直接回野外且从未进入战斗则 **auto-lobby-closed-no-battle**；窗口在战斗开始后才打开时为 `battle-late-attach`，不伪造本轮开房证据。
6. 对每场生成唯一 `runId`，把自动阶段和此前的 Hook readback / 720B SHA / `Subject` 分布纳入同一份 JSONL。自动检测到战斗时沿用每 5 秒只读 Hook 完整性快照；返回野外后结束本场快照，输出证据结论。
7. 诊断窗口有 **停止自动阶段监控／开始自动阶段监控** 控件，原来的手动标记保留为故障排查备用。

所有自动阶段事件标记 `source=SVRAIDBOT_RAM_FLAGS_INFERRED`、`runtimeCalibrated=false`、`nativeHookHit=NOT_OBSERVED`、`rewardReceived=NOT_OBSERVED`、`saved=NOT_OBSERVED`。

### 重要的语义边界

**「已开房、进入团战、结束连接、返回野外」可被这些状态数据用于自动推断；「奖励结算成功」不能从这三个状态自动确定。** 要确认结算成功，还需另外获得奖励 UI/接收方数据、背包数量变化和实际存档证据。

另外，现有 Inline Hook 是发送端 LDN `0x2F` Bootstrap 序列化；**房间自动监控成功并不意味着这个 Hook 在当前场次被执行了**。没有来自游戏 CPU 的实际 PC、命中计数或可信回调，`HOOK_HIT` 继续输出 `NOT_OBSERVED`。

## 2026-10-19 精简诊断界面

用户实机截图显示原始候选阶段值 `L=255,D=1,O=17`。这不是「已经开房」的证明：在该候选映射中 `O=17` 表示野外，与非零 `L` 同时出现属于语义矛盾，需要 4.0.0 实机重新校准。软件遇此情况仅提示「团战：状态冲突，待校准」，不再自动记录房间成立事件。

根据“自动阶段记录应当取代手动标记”的要求，主窗口改为：

- 常驻工具栏：**Hook 状态、安装 Hook、启用／停用 Hook（一个动态按钮）、应用新奖励、团战当前阶段、高级工具开关**。
- **自动运行**：窗口显示时只读启动 SVRaidBot 候选阶段监控；自动生成 runId、战斗期间 Hook 快照、结束时结论。无需「标记已开房／开战／结算」三个按钮，这些 UI 与旧方法都已移除。
- **高级工具默认折叠**：刷新 Hook 状态、卸载入口、读原始指纹、写入恢复试验、日志目录、选择场景／护符、只读快照、导入 LDN JSONL、手动背包对照、结算截图及暂停自动监控。这些是故障调查和原始证据的补充，不应每次都需要点击。
- **风险操作永不自动执行**：安装／启用／停用／修改奖励／卸载继续显式由玩家确认；打开窗口本身不写入任何游戏内存。
- 自动标志只提供阶段推断，不能从 `auto-overworld-returned` 自动升级为 `HOOK_HIT`、领取成功或存档保存成功。

## 下一轮首次实机验证

- 不要再次安装 Hook 来验证这一纯只读改动；已经安装/运行的可保持现状，但尽量退出联网和战斗，保存诊断 JSONL。
- 在新版本 RaidCrawler 里打开诊断窗口，确认出现 `auto-phase-start` 和 `auto-phase-flags`。
- 观察「在地图 → 打开房间 → 进入战斗 → 战斗房间连接结束 → 返回地图」各个节点时 `L`、`D`、`O` 的真实值以及自动阶段是否一一对应。
- 若实际阶段不同，提交新的 JSONL；**先校准或重新确定 4.0.0 状态偏移**，不要简单将任意数值解释为胜利/领取成功。
