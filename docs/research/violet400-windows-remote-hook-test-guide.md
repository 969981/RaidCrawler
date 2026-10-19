# 《紫》4.0.0 — RaidCrawler Windows 原生奖励 Hook 实机测试

**状态：实验性。ELF/LLVM 静态检查已完成；在你的 Switch 上的执行命中、奖励领取及保存仍未验证。**
**限制：现有 sys-botbase 即可；无需 Skyline/NRO；不要求重启才能修改下一场奖励。**

## 2026-10-19 首次实机结果与覆盖范围纠正

用户实机日志显示：TitleID/4.0.0/Build ID 前缀与 8 条原版指纹匹配，880B 全零候选已读回；之后状态为 `InstalledDisarmed epoch=1`，再 `InstalledArmed epoch=2`。但同机五星梦妖魔战斗的结算截图仍是正常奖励，**没有 Hook 命中或接收方数据证据**。

本次配置的 20 个非空行全部为 `Subject=5`（需要晶耀护符资格）。因此无法用这次图片证明版本协议或发奖修改逻辑成功/失败。

**关键范围澄清**：`main+0xE32984` 对应 PokéLDN 研究的本地 LDN `kind=0x2F` Battle Bootstrap **发送端序列化缓冲**；绝非所有普通/单人/官方互联网太晶坑都会经过此函数，也非游戏的通用 `Bag.AddItem` 或单人发奖函数。修改的是 `x23` 输出数据，不是原始 `x20` RaidPoint、房主自己的奖励结算列表或存档。即使发包路径命中，房主本机仍显示原始奖励也是合理的可能性，真正要比的是接收端。

首次证伪测试应使用一个合法物品 `Subject=0, Rare=0, Quantity=5`，且**明确记录模式**：单人离线 / 本地 LDN 房主 / 本地 LDN 加入者 / 官方互联网。若研究的是 LDN 发送端，请在受控的本地联机中从**房主安装当前 Hook**，记录**其他参与者**的奖励；从另一台主机的截图判定，不要只看房主结算画面。若目标是单人离线奖励，则必须另做单人生成/发奖路径定位，不能依赖当前发送端跳板或仅改其 `Subject` 解决。

当前无安全的 `HOOK_HIT` 计数和接收端 0x2F 采集，本轮最严格状态仍是 `PATCH_READBACK_PASS / CALLBACK_UNOBSERVED / REWARD_UNVERIFIED`。未经验证的官方互联网奖励不得宣称生效。

## 准备
1. 给研究机的游戏存档做独立备份；退出官方互联网联机及任何正在进行的团战。关闭其他向同一进程写内存的软件。
2. 从 [benefit 的 GitHub Actions](https://github.com/969981/RaidCrawler/actions?query=branch%3Abenefit) 选择**最新成功**的 .NET Core Desktop 构建，下载 Artifacts 的 RaidCrawler Windows 程序。勿使用旧 Skyline 发布包。
3. 启动《紫》4.0.0（TitleID 01008F6008C5E000），原版 sys-botbase 2.5 保持连接。Build ID 前缀必须为 709bfd6611529864。
4. 打开全局奖励编辑器。首次建议只配置一条合法的常见物品奖励，Quantity 5、Rare 0、Subject 0。Subject 5 要求有晶耀护符，首次测试不建议选 45 行 ×999。

## 2026-10-19：新版证据日志使用方法（需更新 Windows 构建）

本次根据用户五星梦妖魔测试完善了原先只记录「安装／启用」的 JSONL。该玩家**持有晶耀护符**；原日志中的 `Subject=5` 并不能证明筛选失败。应优先核对发送端 Hook 是否被调用、是否是房主出站 Bootstrap 路径。

1. 打开新诊断窗口，模式下拉框选择：**单人离线／本地 LDN 房主／本地 LDN 参与者／官方互联网房主／官方互联网参与者**。用户已有晶耀护符则勾选「本角色持有晶耀护符」。未勾选的三态默认值为「未确认」，不等于「没有」。
2. 点 **只读 Hook 快照**：在不写入 Switch 的情况下检查 `main+0xE32984` 入口、完整 144B reader 签名、奇偶 epoch 前后稳定性、720B SHA-256 和 `Subject=0/5` 行数。此项证据是 `CONTROL_PLANE_INTEGRITY_ONLY`，不能自动生成 `HOOK_HIT`。
3. 开房时点 **标记：已开房**，生成同一场战斗的唯一 `runId`；进战斗点 **标记：已开战**，此后每 5 秒自动进行只读快照，最多 160 次／至手动结算。结束时点 **标记：已结算**，停止采样并输出当前证据边界。自动轮询不会冻结 CPU、读取临时寄存器或改变游戏奖励。
4. 可点 **记录背包前后数量**：输入道具 ID、之前及之后数量，生成 `manual-bag-delta`。这是 `USER_REPORTED`，不是游戏内存抓包，亦非自动保存验证。
5. 如果本地 PokéLDN 也开启了 `POKELDN_SV_REWARD_TRACE`，点 **导入 LDN 证据 JSONL**，将 `S1_SOURCE → S2_HOST_BOOTSTRAP → S2_WIRE_LOOPBACK` 用 45 行完整 SHA 逐站点（`station_index`）比较。外部 JSONL 不是零售机器的原生 `HOOK_HIT`，也不能自动与目前 Switch 的 `runId` 关联；导入时会明示 `IMPORTED_NOT_NATIVE_RETAIL`。
6. 结束后点 **输出证据结论**。没有真正采集到的节点固定输出 `NOT_OBSERVED`，不会把内存哈希稳定或玩家手动截图伪造成 Hook 执行。

新增 JSONL 字段：`schemaVersion=2`、递增 `sequence`、`runId`、`scenario`、`playerCharm`、`entryState`、`readerState`、`epoch`、`stableEpoch`、`rewardSha256`、`templateComparison`、`nativeHookHit=NOT_OBSERVED`。日志路径仍位于 `%LOCALAPPDATA%/RaidCrawler/raid-diagnostics/`。

**目前自动诊断能够回答**：补丁是否保持存在、模板是否意外改变、哪场战斗及角色出现了常规奖励、LDN 外部数据构建是否一致。

**目前不能回答**：CPU 是否经过 `E32984`、实际运行期 `RaidPoint_` 检查是否通过、真正的接收方是否使用该段奖励、Lua 最终筛选以及保存。取得这些证据还需安全的原生事件源或独立接收端观测，而不能靠每 5 秒读静态内存猜测。

## 安装和启用
1. 点奖励编辑器的 **写入奖励**，保存本地模板并打开诊断窗口（不会自动修改 Switch）。
2. 先点 **Hook 状态**；初次应显示 Empty。如显示 ConflictOrIncomplete，停止，不要强制覆盖。
3. 在游戏没有开房、开战或结算时，点 **安装（默认停用）**，确认风险。成功应显示 InstalledDisarmed，epoch=1。表示 880B 跳板和 4B 入口已核对，**尚不是 Hook 命中证明**。
4. 点 **启用**，应显示 InstalledArmed，epoch=2。再点状态复核。

## 首次验证
1. 离线开始一次能完成的本地团体战，核对奖励画面中是否出现新设定的物品；提前记录该物品背包原数量，战斗结束后比较。
2. 正常保存并在适当时机重新载入游戏，再看数量是否保留。奖励画面、背包增量、重新加载后保留，是三个不同的验证。
3. 当前 880B 跳板没有安全的可写命中计数，RaidCrawler 的 hook-memory-confirmed 日志**只表示内存读写成功，不表示 HOOK_HIT**。如果没出现奖励，仍不能断定哪一段没生效；可记录玩法、时间、日志及前后数量继续排查。
4. 官方互联网房间和参与者是否会采用房主提供的奖励数据尚未验证；不建议在正式联网场景进行首次试验。

## 无重启热更新
1. 游戏保持运行。关闭诊断窗口，回到奖励编辑器修改奖励，然后再点 **写入奖励**；打开新的诊断窗口。
2. 点 **Hook 状态**，确认仍为 InstalledArmed。
3. 点 **无重启更新**。Windows 先发布奇数 epoch，写入并回读 720B，再发布新偶数；示例 2→4。下一次走到相应 serializer 的团战才可能采用新模板。
4. 更新同时发生的在途回调由原生代码检测 epoch 变化并恢复源 RaidPoint 奖励。该并发机制只做了静态核验，尚需实机测试。不要让其他工具同时写入这块内存。

## 停用、卸载与故障恢复
- **停用**：epoch 切为奇数；新回调跳过自定义奖励。要继续编辑/使用时再启用。
- **卸载入口**：先停用并还原 main+0xE32984 的原始指令。为保护可能仍在执行的 CPU，刻意保留 880B 跳板；同一个游戏进程卸载后不得再点安装，重启才能清除这部分残留。想反复开关请用停用/启用，不要卸载。
- 操作超时、游戏闪退或显示 ConflictOrIncomplete 时，**不要反复安装**。保存诊断 JSONL 日志和屏幕信息；必要时重新启动游戏恢复原始进程。
- 诊断窗口会显示日志路径，或点 **打开诊断目录**。

## 代码和验收范围
- native/sv_remote_code_patch/stub.S：游戏端 ARM64 v2 读者、epoch 前后校验、冲突回滚。
- native/sv_remote_code_patch/build_offline_plan.py：精确 ELF SHA/入口、+0x8C 返回跳转修正。
- RaidCrawler.Core/Structures/RemoteViolet400HookPayload.cs：WinForms 内嵌对应机器码。
- RaidCrawler.Core/Structures/RemoteViolet400HookInstaller.cs：安装、启停、状态、恢复入口。
- RaidCrawler.Core/Structures/RemoteRewardEpochUpdater.cs：720B 版本保护热更新。
- RaidCrawler.WinForms/SubForms/RaidRewardDiagnosticForm.RemoteHook.cs：用户主动点击的控制界面。

**完成验收至少要分别核验**：跳板/入口读回、真实游戏回调执行、接收端奖励列表、奖励领取、背包增量、存档保留。当前程序只自动完成第一类检验。
