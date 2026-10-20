# N1 实机只读探针：Violet 4.0.0 Native Hook

> **状态（2026-10-20）**：ARM64 Cross-Build **PASS**，exlaunch 构建产物已上传 GitHub Actions；**真实 Switch 加载、Hook 命中、RaidPoint/Bootstrap 字节一致与官方互联网兼容性均 PENDING**。  
> 本模块只观察《紫》4.0.0 的团体战 bootstrap 奖励区，不包含奖励修改、内存写入功能或网络注入功能。**注意：Inline Hook 安装会对代码入口插桩；“只读”指捕获回调对游戏数据只读，而不是进程代码完全不写。**

## 已实现

- [`source/program/main.cpp`](source/program/main.cpp)：基于 [exlaunch@f9f4b0d](https://github.com/shadowninja108/exlaunch/commit/f9f4b0dd07b68f97958cb9c79228bbca22ca80d5)；`HOOK_DEFINE_INLINE` 安装到 `main+0xE32984`（返回复制之后、寄存器恢复之前）。从 `ctx->X[20]+0x13c` 与 `ctx->X[23]+0x79c` **同步只读复制**各 720 B 原版奖励区。
- 只保存奖励字段，不导出 0xAA0 中包含的玩家宝可梦或个人数据；独立后台线程负责 SD 文件写入，Inline 回调不进行文件操作。
- 12 条经过原始 ELF 审计的 ARM64 指令比对；只对 **Violet Title ID `01008F6008C5E000`** 构建。若任一指纹不匹配，**不安装 Hook**。本代码不能自动获取并校验完整 NSO Build ID，因此用户必须单独在目标机器核对。
- 限制并发：固定 8 槽环形快照队列、轻量的 `atomic_flag` 防并发、槽状态发布/归还；后台线程循环导出。超过容量只丢失观测，不阻塞原版序列化。
- 必须能够 `nn::fs::MountSdCardForDebug("rcprobe")` 且 SD 上存在 `/raidcrawler/host-probe.enable` 文件才开启采样。SD 权限缺失时**不起作用**；这类失败必须从调试输出或产物缺失识别，不能说已成功。
- 输出每次命中 2×720 B：`<boot_tick>_<seq>.source-rewards.bin` 和 `<boot_tick>_<seq>.bootstrap-rewards.bin`，另有 `.meta.txt`（指纹、采样 Tick、序号、未验证状态）。`boot_tick` 和 `seq` 是本地标识，不是联网会话 ID。

## 构建与下载（不会自动安装到 Switch）

已经有独立 [ARM64 native probe GitHub Actions](https://github.com/969981/RaidCrawler/actions/workflows/host-native-probe.yml)：

- Docker 编译镜像：`devkitpro/devkita64:20260219`；exlaunch Git 提交固定在 `f9f4b0dd07b68f97958cb9c79228bbca22ca80d5`；
- [构建脚本](build.sh) 在 `.work/` 临时拉取框架并用本目录 `source/program/main.cpp` 覆盖模板入口，编译 `subsdk9` 和伴随 `main.npdm`；**不自动 FTP 部署、不执行 Switch 启动、不修改当前 main 分支**。
- 成功运行记录：[Actions run 39368084041](https://github.com/969981/RaidCrawler/actions/runs/39368084041)；产物名 `RaidCrawler-host-N1-Violet400-UNVERIFIED`。
- 本地同等命令：`bash switch/host-probe/build.sh`，环境需提供 `DEVKITPRO`、devkitA64、libnx、git、make、Python。最终目录 `switch/host-probe/out/`。

**仅限已经有能力安全恢复的授权测试机，在离线、本地测试环境中使用。** 安装前备份原始文件与可恢复存档，并核对实际版本与 Build ID：

```text
Game       Pokémon Violet / 宝可梦 紫
Title ID   01008F6008C5E000
Version    4.0.0
Build ID   709bfd66115298640155fcc4979dba151c7cc79a
```

这里的 Build ID 是**静态审计值**，不是安装时自动验证的运行值。当前构建不能保证其他版本与《朱》可用。不同 Atmosphère / loader 对 `subsdk9` 和 `main.npdm` 的部署细节可能不同，应先确认现有 ExeFS 的加载方式，不能直接覆盖未知的 `subsdk9` 或系统原有 `main.npdm`。

## 如何取得实际观测

1. 在受控测试机中加载研究模块，确认游戏正常启动。首次默认**不采集**。检查是否能访问 debug SD mount；模块通过 `svcOutputDebugString` 输出 `[RC-N1]` 前缀诊断消息。
2. SD 根目录创建 `/raidcrawler/host-probe.enable`（空文件即可）；模块轮询该文件，有时需要稍后才开始记录。不存在、删掉或 SD mount 失败时停止捕获；注意删除时可能仍存在已经入队的快照。
3. 仅使用离线/本地两机测试：房主建立房间、参与者就绪，房主点击开始战斗。探针在**原版 `0x2F` 序列化时**尝试得到一对奖励区快照；不要假定这一定是每局唯一调用，需要和本地时间线对齐。
4. 在 `SD:/raidcrawler/n1/` 取得同一前缀的 2 个文件和可选 `.meta.txt`。如目录为空，说明**未取得观测结果**，优先确认 SD 权限、指纹、运行版本和真正的调用路径，不可虚报 MATCH。
5. PC 上运行：
   ```powershell
   python tools/host/compare_capture.py `
     --source "0011223344556677_00001.source-rewards.bin" `
     --bootstrap "0011223344556677_00001.bootstrap-rewards.bin" `
     --reward-regions `
     --session "local-raid-001" `
     --build-id "709bfd66115298640155fcc4979dba151c7cc79a" `
     --jsonl "n1-results.jsonl"
   ```
   上述文件名前缀纯属占位示例，必须换成 SD 卡上真实的文件名。CLI 仅检查 720 B 等价和首个不同奖励字段，**不独立证明上传数据来自同一真实会话或主机发出的就是接收方接收的消息**。

## N1 阶段验收与已知限制

| 项目 | 2026-10-20 |
|---|---|
| ExeFS Inline Hook 回调代码 | **已实现** |
| 4.0.0 静态指令指纹 | **已实现**（12 opcode），但**未自动验证完整 Build ID** |
| 2×720B 同步只读复制 | **已实现源码，实机未命中** |
| 独立线程写 SD | **已实现源码，实机 SD 权限未测** |
| ARM64 devkitA64 交叉编译 | **PASS** |
| PC CLI 720 B 奖励区比对 | **已实现，合成数据测试** |
| 一场真实房主奖励 `Source == Bootstrap` | **PENDING** |
| 本地参与者收到相同奖励、资格过滤 | **PENDING** |
| 官方互联网模式可用、N2 奖励追加 | **NOT TESTED / NOT IMPLEMENTED** |

**设计边界**：`main+0xE32984` 是精确 4.0.0 审计点，hook 过程由 exlaunch 处理 ARM64 trampoline 和寄存器恢复；从源码能判断读写意图，但**无法在没有实机的情况下排除使用其他模块、加载次序、内存访问和目录授权导致的崩溃**。因此现阶段不要将编译通过等同于实机可靠。

### 问题定位路线

- 启动异常：立即移除该实验模块恢复原版，检查其它 ExeFS 插桩冲突及游戏 Build ID。
- 无调试输出/无文件：查 SD Debug mount 是否允许、`host-probe.enable` 是否存在；再次确认 `subsdk9` 真正被加载。
- 有文件但不是同场：匹配同一个前缀与对应 Tick/人工记录的“点击开战”时刻；对多次命中逐一比较。
- `equal=false`：第一步保存两端 SHA、首个不同 row/field 并检查是否在复制之后出现原版再写入。
- `equal=true`：只允许标记“这两份**捕获的数据**奖励区一致”；仍需会话归属证据和参加者实机接收结果才能升级 N1 最终通过。

## 外部依赖与许可

本目录仅保存 RaidCrawler 研究模块与脚本；构建时拉取官方 exlaunch 框架，遵守其 GPL-2.0 许可，并保留其版权/NOTICE。模块在最终版本中是否能兼容现代 Atmosphère 需要实机验证。参考原始 [PokéLDN ELF 审计](https://github.com/969981/pokeldn/blob/research/docs/research/sv/violet400-elf-raid-bootstrap-audit.md)。
