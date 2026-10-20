# Violet 4.0.0 N0/N1：真实 Switch 房主 0x2F 奖励序列化只读捕获

> 2026-10-20。仓库 `RaidCrawler/benefit`。阶段：原生 Hook 源码、Skyline NRO 构建、Windows JSONL 导入器和自动化测试已完成；**尚无本次新增采集器的 Switch 实机 HOOK_HIT 或奖励行日志**。旧版 Windows botbase 的内存轮询不等于 Native 回调。

## 目标与技术依据

- 目标只限 N0/N1：真实 Switch 房主开本地 LDN 太晶团战，验证 CPU 命中游戏的 0x012F 序列化入口，并读取生成的原版奖励和发送明文副本。此阶段不修改奖励。
- 已核对《紫》4.0.0 ELF 的 `main+0xE327FC` 序列化函数与拷贝后 `main+0xE32984`。现场 x20 源消息的 `+0x13C` 与 x23 输出明文的 `+0x79C` 各包含 45×16B 奖励。
- 房主发送 0x2F 一般位于大厅 `0x0C` 请求开始、参与者 `0x0D` 确认后，实际战斗加载前。PokéLDN 对原版 Switch 参与者先机球×500 的成功实验是修改模拟房主的网络 Bootstrap，不等于真实 Switch 房主已经可以追加、也不等于 Nintendo 官方互联网会接受。
- 历史 `SV_RAID_BOOTSTRAP_ENCODER.md` 将奖励起点写成 0x798 且字段布局与新版不同；本次以正确的 `0x79C`/`item,quantity,rare,subject` 为准。

## 已提交原生采集器

| 文件 | 用途 |
| --- | --- |
| `native/sv_raid_reward_hook/src/lib.rs` | 强制 observe-only；8 条 ELF 指令门控；实时回调仅读 x20/x23 的两个 720B 奖励区域；16 事件有界队列；独立工作线程写日志 |
| `native/sv_raid_reward_hook/src/observe.rs` | 逐槽 45 行、合法/未知行、空槽候选、subject 分布、FNV64 与 JSONL |
| `native/sv_raid_reward_hook/README.md` | 安装 NRO、实机开坑、日志、旧 apply 禁用及恢复方案 |
| `.github/workflows/native-raid-observer-offline-tests.yml` | 仅 Rust 标准库的解析/摘要测试及 observe-only 代码门控 |
| `.github/workflows/native-sv-hook.yml` | 真实 cargo-skyline `.nro` 构建及构件输出，不能代替真机验收 |

目前 **apply 已强制关闭**：即使 SD `hook-mode.txt` 为 `apply`，也输出 `LEGACY_APPLY_REJECTED`；原来的整表 720B 覆盖不满足「保留全部原版奖励」，故 N0/N1 不提供改奖功能。

Hook 只记录当前 Native 序列化视图：既不会采集 PK9 等玩家信息，也不执行阻塞磁盘 I/O。worker 写到 `/switch/raidcrawler/hook.log` 和 `/switch/raidcrawler/raid-native-rewards.jsonl`。其中每个 `N1_NATIVE_RAID_REWARD_SNAPSHOT` JSON 行包括 45 条 sourceRows 与 45 条 wireRows、nonzero/unrecognized/tailZeros/subjects/哈希、源/输出是否相同，`memoryWritten=false`。

**全零奖励行不一定代表可无损复用的奖励槽。** 需要先建立尾部语义。空行穿插、特殊非零控制字段、活动 Raid 的数据结构差异不应被自动清除或覆盖。

## Windows RaidCrawler 已新增导入和校验

- 新增 `RaidCrawler.Core/Structures/NativeRaidRewardCapture.cs`：每行 45 槽、`uint32` 字段、小端 FNV64 与来源状态审查；拒绝哈希篡改、无效字段及伪称已修改内存/已收奖的事件。
- 新增 `RaidCrawler.WinForms/SubForms/RaidRewardDiagnosticForm.NativeCapture.cs`：诊断窗口主工具栏 **「导入实机 0x2F 奖励快照 JSONL」**，显示当前已导入 hit 的源 45 行与输出 45 行并排信息，旁边明确「未验证接收方」。
- 新增 `RaidCrawler.Tests/NativeRaidRewardCaptureTests.cs`：有效 45 行、源/输出不同、哈希错误、虚假发奖标签和超大文件；已加入 Windows 阻断测试。
- 通过校验的 JSONL 也仅属于外部导入记录，并非硬件证明。需要用户提供该次 Switch 原生 `hook.log` 和所使用插件构建 SHA 以交叉核对。

## 构建事实与实机 Gate

- [Native Skyline NRO 成功构建](https://github.com/969981/RaidCrawler/actions/runs/39364625543)，原生提交 `63cddd75`；可在 Actions 底部下载 `RaidCrawler-SV-Violet400-Skyline-NRO`。这是**真实 Skyline 构建**，不是仅 `rustc --test`。
- [N0/N1 Rust 解析器离线测试通过](https://github.com/969981/RaidCrawler/actions/runs/39364683204)。
- [Windows RaidCrawler 导入器的构建、阻断测试、产物成功](https://github.com/969981/RaidCrawler/actions/runs/39365003075)，当次源码提交 `811889ea`。后续只新增文档。

首轮只在备份好的自有 Switch 和本地 LDN 测试：在正确路径安装 `.nro`，启动《紫》4.0.0，确认 `ELF_FINGERPRINT verified 8/8`、`NATIVE_HOOK_INSTALLED`，再以房主身份开坑、等待参与者 Ready 并真正按开始战斗。看是否出现 `HOOK_HIT seq=... stage=N0_N1`，以及生成了 JSONL。

若没有命中：不能由 Windows 旧 botbase 的写回证明改判；保留加载时日志、游戏版本、网络模式和开始动作，调查实际序列化入口。若有命中：首先检查 source/wire 45 行一致性和实际尾部空槽数；同时由另一台未修改的 Switch 核对接收侧，但本阶段不做任何写入。

## 尚未完成：N2/N3

要继续实现晶耀护符 `subject=5`，需证明真正源 RaidPoint/Bootstrap 对参与者可见、正确识别安全追加位置、留足 45 总槽容量，先只增加单个普通道具×1并由另一台持有护符的原版 Switch 记录结算、Bag 前后和重新载入。房主自己的单人 Lua 奖励源不等于出站 x23 副本；官方互联网不由本地 LDN 成功自动推导。

本轮没有实际写入 Switch 奖励、没有其他玩家收到×999的证据，也没有 Nintendo 官方互联网模式兼容结论。
