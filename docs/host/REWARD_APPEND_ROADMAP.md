# RaidCrawler `host`：正版 Switch 房主太晶坑奖励追加开发规划

> 状态：**PLAN / N0 待实机验证** · 2026-10-20  
> 基线：`969981/RaidCrawler@main` 创建的独立 `host` 分支。本文是路线图，**没有声称实现、实机验证或官方互联网模式可用**。  
> 目标：正版 Switch 《朱/紫》房主开任意**适用且能正常联机的**太晶团体战，待参与者就绪、战斗加载前，在**不减损任何原版奖励**的前提下追加若干 `subject=5` 道具奖励，由**每台参与者的正版游戏**按晶耀护符（重要物品 ID 2481）资格过滤、领奖和存档。优先小数量验证；20 行、999 数量仅作为受容量与道具上限约束的后续目标。

## 0. 证据等级与重要澄清

- **当前 RaidCrawler**：`RaidCrawler.Core/Connection/ConnectionWrapper.cs` 用 `SysBot.Base` 通过 USB/Wi-Fi 读游戏存档块/太晶坑，`Offsets.cs` 保存旧的固定指针，`RaidExtensions.ReadAllRaids` 由种子和奖励表推算预览；`RewardsView.cs` 仅展示预测 `(item, count, subject)`。**没有现成的 `0x2F` 真实房主捕获、游戏内 Native/Lua Hook 或参与者实机反馈通道。**预测的奖励并不是发包前真实 `RaidPoint`。
- **PokéLDN 研究**：PokéLDN 模拟房主发送 `0x2F` （内层序列化消息类型 `0x012F`）到正版 LDN 参与者；已实测「替换」奖励，比如先机球 500。**不等于**真实 Switch 房主「追加」、不等于官方互联网游戏会话成功。
- **《紫》4.0.0 ELF**：既有独立静态审计支持 `main+0xE327FC` 把 `RaidPoint` 放进 `0xAA0` 明文 bootstrap、`0x1592B7C` 之后的序列化压缩链。**不提供**可跨版本直接复用的 RAM 绝对地址，也没直接证实 4.0.0 Lua 函数的运行时入口。
- **Lua 语义证据**：公开 SV-Script-RE 资料的基线为 3.0.1，支持「`FieldRaidJewelry.LotteryRewardItem` 生成完整奖励 → 45 槽写入 → 接收端按 `subject=5` + Bag.GetNumItem(2481) 过滤 → Bag.AddItem」的设计推断。**4.0.0 同一 Lua 模块的调用点与重入行为需再验。**
- **触发时序**：需求文字“玩家进入战斗后发奖”应拆为：参与者就绪后、**进入战斗画面之前**发送战斗 bootstrap；胜利后客户端发奖。不能等正式战斗已开始才修改一次性 bootstrap。
- **联网范围**：本地 LDN、远程 LDN 中继、任天堂原生互联网房间是不同实验目标。即便本地通过，不能据此保证官方互联网同样接受房主奖励或无服务器校验。

## 1. 固定数据契约（目标仅为 Violet 4.0.0 的已知研究基线）

```text
Plain bootstrap: 0xAA0 bytes
  0x000..0x6B7    5 x 0x158 子对象
  0x6B8..0xA9F    RaidPoint = 0x3E8 bytes
RaidPoint:
  +0xE4..+0x3B3   45 x 0x10 = 0x2D0 / 720 B 奖励行
  +0x3B4..+0x3B7  非奖励行，必须不动
  +0x3B8..        摘要区，必须不动
第 i 行 (0<=i<45) 的 bootstrap 明文偏移：
   0x6B8 + 0xE4 + i*0x10 = 0x79C + i*0x10
每行 4 x uint32 little-endian:
   item_id | quantity | rare_type | subject_type
```

- `subject=0` 全体、`1` 房主、`2` 加入者、`3` 一次性资格、`4` 团战力、`5` 自身拥有晶耀护符（Bag Item ID 2481）；不能把 4/5 理解为「稀有级别」或特殊数量。
- 默认追加只能是**尾部的空余奖励槽**。原版已有全部非零行（含对外不可见、料理、一次性与原生晶耀碎块）必须原样保留；禁止清空或替换。不能只根据「战后 UI 可见行数」推断剩余容量。
- **需要验证**真实源数据是否会在中间留空、使用固定哨兵、重复初始化或重排；在明确有效长度或填充模式前，不允许对“第一个全零行”直接下写。
- 若原版有效槽数为 `n`，最多追加 `45-n` 行。20 行仅在 `n<=25` 时可能完整追加。默认请求超容时**整单拒绝追加、继续原版流程**，可选“明确授权部分追加”留待后期开发。
- `quantity <= 999` 不是对所有道具的安全持久化保证：`SlotMaxNum`、当前库存、物品合法性和实际结算逻辑可能裁剪。默认实验限 1–3 个、1–5 个数量。
- 不修改 LZ4 压缩字节，也不重放 PokéLDN donor 报文；真实房主首选在明文对象生成后、原版 `0x2F` 序列化前完成操作。

## 2. 推荐架构

```text
RaidCrawler.WinForms（PC 配置、预览、只读诊断、实验状态）
    |
    v
RaidCrawler.Core.Host（纯逻辑 / 配置版本 / 45 槽 AppendPlan / 审计）
    |
    +--- HostBridge（PC ↔ Switch 的受控协议；只传配置和脱敏事件，
    |               不直接把 SysBot.Base 的轮询读内存当成实时 Hook）
    |
    v
Switch Native Probe / Hook （单独开发、按 Game Build ID 精确匹配）
    |
    +-- N0/N1: 只读捕获 Lottery 输出 / 源 RaidPoint / 0x2F 前明文
    +-- N2: 验证后在原版奖励生成完成的安全窗口一次性追加
    +-- N3: 原版 serializer 发送；参与者原版 subject=5 筛选
    |
    v
参与者正版 Switch（独立资格过滤、战后背包、保存/重启）
```

**为何不直接“PC 写内存”代替 Hook？** RaidCrawler 当前 SysBot.Base 连接能读块，但无法保证抓到团战瞬时序列化窗口；某个地址可能由后续游戏逻辑覆盖。PC 轮询适用于静态扫描和辅助验证，不代替**房主 Switch 游戏进程内的同步触发**。

**Native 实现两条备选**：
- **A（优先研究）**：独立 ExeFS / ARM64 模块，精准捕获 4.0.0 `main+0xE327FC` 的参数与源/目的对象；结合真实只读日志判断是否能够在发送前安全追加。无需替换 `sys-botbase`，但需要在 Switch 侧部署经过授权的模块，并证明不破坏指令、寄存器、栈和原版时序。
- **B（语义最清晰）**：在匹配版本的 Lua `LotteryRewardItem` 返回后、45 槽 serializer 消费前包装返回对象并 append。**纯 ExeFS 也可通过 Lua VM 初始化后 late trigger 安装 wrapper**；不能在主 Lua 尚未加载时强行包装，不能从后台线程直接调用 Lua state。若选 bytecode 路线，必须先核验真实 4.0.0 main.blua SHA 与 Prototype/PC，3.0.1 不能直接移植。
- 选路条件：N1 观测到 S1/S2 的实际时间、原始行数、对象生命周期、Native 是否存在对所有坑都稳定的窗口。**在结果出来之前不强行选某个固定 RVA 当作写入 Hook**。

### 主机状态机 / 防重入

`DISABLED → IDLE → LOBBY → READY → CAPTURED → (APPEND_ONCE) → BOOTSTRAP_SEEN → BATTLE → RESULT → RESET`

- 每场以进程内 `session_nonce` + Raid 标识/种子 + 游戏版本关联，同局只生效一次；遇到重连、取消开战、读错会话、来源更换、奖励数不明、版本不匹配均**保留原版、禁用追加**。
- 在 Switch 原游戏线程捕获，数据以安全的拷贝/摘要传给 PC；绝不保留过期栈指针。
- 非识别版本默认只读/禁止写入；Switch 模块异常自动退回原版流程。配置热更不能改变已经进入 bootstrap 的同一场。
- 收集的 telemetry 只存阶段、时间、Build ID、匿名 session id、Raid 类别、原始/追加行、SHA256、原因码；不收集 Nintendo Account、连接密钥、完整玩家档案。

## 3. 具体开发阶段与完成定义

| 阶段 | 优先级 | 关键工作 | 明确交付 / Go-NoGo |
|---|---|---|---|
| **N0 静态契约与离线测试** | P0 | Core.Host `RewardRow`、`RaidPointSnapshot`、`AppendPlan`；45 槽、Little Endian、保留字段、最小容量/合法性检测；导入 PokéLDN 脱敏明文 fixture 的测试 | `dotnet test`；无修改原版前缀/末尾；超容 100% fail-closed |
| **N1 真实房主只读链** | **P0 / 第一关键实机任务** | Switch 侧定位房主在参与者就绪后生成的 Lottery 输出、源 `RaidPoint`、复制到 bootstrap 前/后的 45 行；对齐 `0x2F` 与 session | 每场 S1→S2→S3 精确逐字段对齐；至少普通坑、活动坑各有一致性日志；不写游戏内存 |
| **N2 单行追加实验** | P0 | 选择稳定窗口，开启配置开关后将“普通合法道具×1、subject=5”追加到空槽；检查后续覆盖/二次调用 | 原版 45 槽前缀逐字节一致；1 次追加；有护符接收者看到、无护符者看不到；其他奖励不变 |
| **N3 领奖/持久化闭环** | P1 | 同版本受控两机对照：UI、胜利领取、Bag 净增、存档完成、完全退出重开 | 原版应得数量不下降、附加道具实际净增、完全重载仍保留；若库存上限阻碍则记限制，不强称通过 |
| **N4 任意坑覆盖矩阵** | P1 | 帕底亚/北上乡/蓝莓学院、1–7 星、6 星黑坑、活动/最强之证；单人/双人/多人的时序与异常 | 按坑类型形成 Supported/Unsupported/Needs Capture 表，不把「任意坑」当作未测试即成立 |
| **N5 配置编辑器与 20 行目标** | P2 | UI 允许配置道具 ID/数量/`subject=5`；检测表内合法性、槽位预算、导入导出、preview、Dry Run、Kill Switch | `n+extras<=45`；原版与料理/护符行先保留；不足容量给清晰拒绝原因；默认禁用 |
| **N6 官方互联网独立验证** | P2 / 高风险独立门槛 | 区分官方 NSO / 本地 LDN / 远程中继，先只读确认实机房主与参与者双方是否走同一 bootstrap；再经授权小规模对照 | 不混用 LDN 结论；若官方服务端拒绝/复算或存在账号风险，标记 Unsupported 并停止写入实验 |

### 最小对照矩阵

| Case | 场景 | 追加 | 预期 |
|---|---|---|---|
| C00 | 基线，无 Hook | 0 | 原版奖励与背包/保存正常 |
| C01 | 有护符参与者 | 1 行 ×1，subject 5 | 原版不变；额外行显示、发放并重启保留 |
| C02 | 无护符参与者 | 同一行 | 原版不变；额外行被过滤 |
| C03 | 同一局有/无护符两人 | 同一发送对象 | 接收到相同源行，结算差异仅按资格 |
| C04 | 料理 subject 4 + 原生护符 subject 5 | 追加 1 行 | 原有序号、行顺序与全部条件不变 |
| C05 | 45 槽已满 / 无法识别长度 | 1 行 | 0 修改、无越界、原版照常进行 |
| C06 | 退出/重进/重开房间 | 1 行 | 没有上一局状态污染或重复追加 |
| C07 | 多奖励、库存接近上限 | 少量梯度试验 | 显示/背包/重启分别记录，不虚报 999 |
| C08 | 版本/Build ID 不匹配 | 1 行 | 自动禁用写入，产生可审计错误 |
| C09 | 各类坑/地图/特殊活动 | 1 行 | 独立地积累“适用坑”证据，未测保持 Pending |

## 4. 只读观测格式与判定逻辑

```json
{
  "schema": "raidcrawler.host.trace.v1",
  "mode": "observe",
  "session": "<anonymized local nonce>",
  "game": "Violet",
  "game_version": "4.0.0",
  "build_id": "<full actual Build ID>",
  "raid_kind": "standard|event|black|unknown",
  "stage": "S1_LOTTERY|S2_RAIDPOINT_SOURCE|S3_BOOTSTRAP_PLAIN|S4_RECEIVER|S5_FILTERED|S6_BAG|S7_RELOAD",
  "reward_count": 0,
  "rows_sha256": "<computed sha256>",
  "row_bytes_length": 720,
  "original_unchanged": null,
  "result": "OBSERVED|MATCH|MISMATCH|MISSING",
  "reason": null
}
```

阶段对齐：`S1` 原始 Lottery 结果（若能定位）；`S2` 原始 `RaidPoint+0xE4` 的 720 B；`S3` 源游戏 bootstrap **解压前的明文** `[0x79C,0xA6C)` 对应 720 B；`S4` 接收端所见 720 B；`S5` 接收端资格筛选后；`S6` 战后背包；`S7` 重启后背包。S2/S3 比较完整区域与保留字节，差异时输出首个不同 slot/字段；S4/S5 不能只凭房主日志声称 PASS。日志仅记录结构化摘要，原始 dump 需在用户主动开启的诊断模式中限时保存且脱敏。

**Go 条件**：实机捕获同一场的 `S2 == S3`，且行数语义明确、时机稳定；在此之前，N2 写入开关不得可用。**终极 PASS**：C01+C02+C03，并满足「原版不减少 + 新奖励到账 + 重启保留」。

## 5. 代码分层与建议文件（待开发，不表示已存在）

- `RaidCrawler.Core/Host/RewardRow.cs`：4×u32、序列化/反序列化；原版行不可变快照。
- `RaidCrawler.Core/Host/RaidPointLayout.cs`：`0x3E8`、`0xE4`、45×16、`0x3B4`、`0x3B8` 常量断言。
- `RaidCrawler.Core/Host/RewardAppendPlanner.cs`：`Plan(original, requested)`，保留原版，计算容量，拒绝未知/超限/无效物品。
- `RaidCrawler.Core/Host/HostSession.cs`：状态机、断线重置、一次性令牌。
- `RaidCrawler.Core/Host/HostTrace.cs`：只读日志、匿名化、字段差分/摘要。
- `RaidCrawler.Core/Host/IHostBridge.cs`：PC ↔ Switch 受控通信契约，先做本地 FakeBridge。
- `RaidCrawler.Tests/Host/*Tests.cs`：布局/全满/未知/重复/跨会话/对照 fixture。
- `RaidCrawler.WinForms/SubForms/HostRewardsView.*`：在 N1/N2 后才开放实验 UI；初版仅 Observe/Preview，写入默认禁用。
- `switch/host-hook/` **仅在决定 Native 路线后**新增独立工程；明确版本匹配、符号/指令断言、卸载/故障回退。不得直接改写发布版的 `SysBot.Base.dll`。

## 6. 测试环境与发布守则

1. 先确认房主游戏标题 ID、版本、真实 **Build ID** 与主模块 SHA；《紫》4.0.0 已有审计 Build ID `709bfd66115298640155fcc4979dba151c7cc79a`，但目标实机必须**当场重新读取验证**；《朱》和其他补丁各自独立做 fingerprint。
2. N0 的 .NET 框架为 `net10.0`，仓库已有 `RaidCrawler.Tests`；先 `dotnet test RaidCrawler.slnx -c Release` 与 `dotnet build RaidCrawler.slnx -c Release`。
3. 当前 workflow `.github/workflows/dotnet-desktop.yml` 只监听 `main` 的 push/PR，`host` 的测试和构建需要**新增单独 workflow 或调整 triggers**，不要因为推送 host 自动误发正式 Release。推荐 host CI 仅 build/test/upload 不自动发布。
4. 优先用户控制的两台正版机、本地受控模式；官方互联网测试前先评估账户、服务条款及封号风险。不要采集、上传个人账号标识或会话密钥。原始 ELF、main.blua、未公开商业工具、ROM/RomFS 及未授权资源不提交仓库。
5. 对任意未完成阶段在 issue/报告写 `PENDING`；必须标注 **OFFLINE_STATIC / RETAIL_LOCAL / RETAIL_OFFICIAL_ONLINE** 的证据类型。任何未经过参与者真实收包、领奖和重启存档的结果，都不得标为 end-to-end completed。

## 7. 直接开始的工作（依赖顺序）

1. **N0**：实现不可写的 45 槽结构、append 计划、完整保留断言和假桥接回归测试；先打通 `host` 独立 CI。
2. **N1（最高优先级实机）**：在**房主本人**的真实开始战斗流程里，只读 Hook/trace `main+0xE327FC` 附近输入和输出，采集一次 `RaidPoint+0xE4` / `0x2F` 明文，配对 run_id、数据 SHA；不要复用模拟房主替代它。
3. **决策点 D1**：确认「Source → native copy → bootstrap」原版一致和对象生命周期，再选择 Lua post-lottery wrapper 或 native pre-serializer 原位追加的具体入口。
4. **N2/N3**：仅加入一行小数量 subject=5 完成 C01/C02、背包净增及重载闭环，再逐步扩展容量、地图和互联网覆盖。

## 8. 参考资料（固定引用，避免随上游更新漂移）

- [PokéLDN Bootstrap Encoder 实机替换奖励证据](https://github.com/969981/pokeldn/blob/research/docs/research/sv/references/SV_RAID_BOOTSTRAP_ENCODER.md)
- [PokéLDN 研究：晶耀护符资格与 `subject=5`](https://github.com/969981/pokeldn/blob/research/docs/research/sv/glimmering-charm-raid-rewards.md)
- [PokéLDN 研究：源 RaidPoint → Bootstrap → 接收端](https://github.com/969981/pokeldn/blob/research/docs/research/sv/raidpoint-bootstrap-receiver-consistency.md)
- [PokéLDN 研究：LotteryRewardItem 后置追加](https://github.com/969981/pokeldn/blob/research/docs/research/sv/sv400-lua-post-lottery-append-hook.md)
- [PokéLDN 研究：原版 ELF 发送前复制路径](https://github.com/969981/pokeldn/blob/research/docs/research/sv/violet400-elf-raid-bootstrap-audit.md)
- [RaidCrawler WinForms 原始入口](https://github.com/969981/RaidCrawler/blob/main/RaidCrawler.WinForms/MainWindow.cs)
- [RaidCrawler 当前连接层](https://github.com/969981/RaidCrawler/blob/main/RaidCrawler.Core/Connection/ConnectionWrapper.cs)

**版本声明**：这些链接指向当前仓库分支（可能未来改变）；具体代码开发时应在 issue / 实验结果中另行记录当次读取的 SHA、构建与工具版本，以便复现。
