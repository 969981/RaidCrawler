> **已被新审计修订：2026-10-20** 此文档含历史研究描述；关于奖励行真实 ABI、自动发现内存邮箱、Subject=5 试验，**统一以 [Violet ELF + donor 重新验证与 N1/N2 最新记录](ELF_REVALIDATION_N1_N2.md) 为准**。旧文档中的 `RaidPoint+0xE0`、`Marker/Item/Quantity/Reserved`、SD 导出和 v1 邮箱均已过时，禁止据此修改游戏内存。

> **2026-10-20 N1.1 升级：** 本文件保留原始 SD 导出/静态 ELF 研究过程作为历史记录。当前 `host` 最新实现已改为 [C# + sys-botbase 共享内存邮箱](../../switch/host-probe/README.md)：无需 SD 文件，C# 主窗口新增 Host Probe，原生源代码和构建步骤见其 README。首次获取邮箱地址仍依赖当前进程 `[RC-N1] mailbox=0x...` 调试日志；自动发现跟踪 [Issue #5](https://github.com/969981/RaidCrawler/issues/5)。两端编译不代表真实 Switch UAT 通过。

# N1：真实房主只读捕获（研究入口与验收）

> 2026-10-20；状态：**ARM64_EXLAUNCH_BUILT / REAL_SWITCH_CAPTURE_PENDING**。
> 不修改游戏内存、代码页、LZ4 帧、SysBot.Base，也不发送自定义网络数据。此文档不证明官方互联网模式生效。

## 新进展：Switch 侧 probe 已交叉编译（但尚未实机验证）

原生代码已放入 [`switch/host-probe/`](../../switch/host-probe/README.md)。基于 [exlaunch@f9f4b0d](https://github.com/shadowninja108/exlaunch/commit/f9f4b0dd07b68f97958cb9c79228bbca22ca80d5)，安装 `main+0xE32984` Inline Hook，只读复制完整奖励行区 `x20+0x138` 和 `x23+0x798` 各 **720 B**（原先的 `+0x13C` / `+0x79C` 实为第 0 行 item 字段，不能作为行起点）；后台线程在明确 SD 启用标记存在时尝试导出数据。

- [Native Actions 成功构建](https://github.com/969981/RaidCrawler/actions/runs/39368084041)（仅 ARM64 构建 + artifact，不含 Switch UAT）。
- 原来提到的“代码不修改游戏代码页”需要严格修正：**Inline Hook 为实现观察需要安装代码插桩，但回调不写奖励、背包或网络报文。**
- 新 CLI 用 `--reward-regions` 处理两份 720B 输出；这样不会收集完整 bootstrap 内可能包含的玩家身份和宝可梦数据。
- 完整 Build ID 仍要用户在实机独立确认；模块当前只做 Title ID 目标构建和 12 条 opcode 匹配。SD Debug mount 的实际权限亦未经过实机验证。

## 已提交的 PC 侧只读分析模块

- `RaidCrawler.Core/Host/RaidPointLayout.cs`: 强校验长度，区分 `RaidPoint` 0x3E8 与 `Bootstrap plaintext` 0xAA0，保证两者从正确相对位置提取同样的 720 B。
- `RaidCrawler.Core/Host/HostCaptureVerifier.cs`: `CopySourceRewards`、`CopyBootstrapRewards` 与 `Compare(sourceRaidPoint, decodedBootstrap)`。本 API **仅分析已经拿到的两段明文快照**；不负责从 Switch 读取，不能当成 Native Hook 已就绪。
- `RaidCrawler.Tests/Host/HostCaptureVerifierTests.cs`: 同源一致、字段级差异、错误长度拒绝、拷贝后不别名化瞬时内存。

## 本地命令行比对：已提供可运行实现

在使用合法、受控的只读原生探针得到**同一场**两份文件后：

```powershell
python tools/host/compare_capture.py `
  --source source_raidpoint.bin `
  --bootstrap outgoing_bootstrap_plain.bin `
  --session local-test-001 `
  --build-id 709bfd66115298640155fcc4979dba151c7cc79a `
  --jsonl host-capture.jsonl
```

- `source_raidpoint.bin` 必须恰好 **1000 B / 0x3E8**。
- `outgoing_bootstrap_plain.bin` 必须恰好 **2720 B / 0xAA0**，**不能直接输入 LZ4 压缩包**。
- 比对 `source[0xE0:0x3B0]` 与 `bootstrap[0x798:0xA68]`，总长 720 B。相同返回退出码 0，发现差异返回退出码 2，并输出首个不同槽位与字段。
- `--session` 与 `--build-id` 是操作者提供的标签，命令行无法验证它们的真实性。因此即便结果 `equal: true`，证据级别仍是 **USER_SUPPLIED_SNAPSHOTS_UNVERIFIED**，不是实机 Hook / 接收端 PASS。
- Python 纯标准库测试：`python tools/host/test_compare_capture.py`；测试数据是合成字节，不作为零售实证。

## Native 捕获对象及版本要求

研究基线：已有《紫》4.0.0 原始 ELF 审计 SHA-256 `d17403eaa9d844db82f647fd2f1518806b10c0518d53602fc0ac1766298fdba0`，Build ID `709bfd66115298640155fcc4979dba151c7cc79a`。**必须在当前实际 Switch 游戏进程中核实 title ID/Build ID/模块基址后才使用 4.0.0 RVA；不默认其他补丁版适用。**

待验证地址和角色（来自既有静态研究，不是本轮实机命中）：

| RVA | 证据/用途 | 动态观测目标 |
|---|---|---|
| `main+0xE327FC` | Native `0x2F` 明文构造 | 同一场、同一调用的源对象/目标明文 |
| `main+0xE329B4` | 5 个固定子对象复制的帮助函数 | 辨别子对象与 RaidPoint，不误取错误指针 |
| `main+0x1592B7C` | 压缩封装链 | 验证在 LZ4 之前已经得到 `0xAA0`，此处**不写压缩数据** |
| `main+0x1BAE790` → `0x1BAFF98` | 原生 RaidPoint getter | 确认正确类型、指针状态与对象生命期 |
| `main+0x1BB0018` | RaidPoint 相关复制 | 记录源/目标拷贝关系，而非认定固定地址 |

由既有 ELF 审计记录的 serializer 寄存器相关相对位置仅可作为**验证假设**：

```text
source reward row i: x20 + 0x58 + 0xE0 + 0x10*i
target plain row i: x23 + 0x6B8 + 0xE0 + 0x10*i
i = 0..44
```

绝不可假设某个绝对 RAM 地址永久指向奖励，不能从一个静态解引用推断它跨过函数返回后仍有效。**先记录 4.0.0 原版一次真实流程的上下文与起止边界**，再判断这两个表达式在捕获处是否匹配。

## 实机 N1 精确工作顺序

1. `ReadVersion`：记录主程序 Title ID（Scarlet/Violet）、显示的游戏版本、内核取得的主模块 Build ID、模块主基址；不匹配直接禁用探针/标记 `UNSUPPORTED_BUILD`。
2. `BeginSession`：从用户的「创建房间」动作开始记录匿名的 run ID，不带 Nintendo ID/玩家名。区分准备大厅、所有参与者就绪、实际触发 Bootstrap 的时点；若房间退出或重建，要立即过期。
3. `ObserveLottery`：寻找 `FieldRaidJewelry.LotteryRewardItem` 已生成完整奖励而还未写入 45 槽的节点。若无法验证对应 Lua chunk，可先跳过 S1，绝不能用 C# 预测取代真实 S1。
4. `ObserveSource`：在真实房主复制原版 RaidPoint 时**同步只读拷贝整段 0x3E8 字节**，不跨帧缓存裸指针；同时记录匿名 session ID + 态标记 + 45 行区域 SHA-256。
5. `ObservePlainBootstrap`：同一次 `0x2F` 编码调用，取得**压缩前**的 0xAA0 原始明文快照。不是可靠分片上的 LZ4 压缩数据。
6. `Compare`：`HostCaptureVerifier.Compare` 给出原版 45 槽是否字节一致、首个不同的行/字段/偏移、两个 SHA-256。**匹配只是本机 S2/S3，不是参与者实际收到包的证明**。
7. `Repeat`：普通 3–6 星坑及活动坑至少各采一次；还要验证撤销开战、断线、再次开坑不会读取上场失效对象。
8. `ReceiverFollowup`：另开参与者只读观测，判断 `S4_RECEIVER` 与房主 S3 相同，并在将来区分本地 LDN 与原生 NSO 互联网匹配。不要把客户端奖励 UI 结果当成原始收包字节。

## JSONL 日志约定（数据仅是 schema，非实测）

```json
{
  "schema_version": 1,
  "evidence": "RETAIL_LOCAL_READONLY_PENDING",
  "session_id": "local-nonce-not-account-id",
  "build_id": "current-runtime-actual-build-id",
  "event": "SOURCE_BOOTSTRAP_COMPARE",
  "plain_len": 2720,
  "source_len": 1000,
  "reward_bytes": 720,
  "source_sha256": "<computed>",
  "bootstrap_sha256": "<computed>",
  "equal": null,
  "first_mismatch_row": null,
  "first_mismatch_field": null
}
```

字段以真实 runtime 采集为准，`equal` 必须从两段同场快照中计算；未取得真实数据时填 `null` 而非伪造 true。默认只存摘要，不存整帧、Nintendo Account、认证密钥及个人身份信息。原始 0x3E8/0xAA0 dump 仅在受控调试场景短期保存。

## Go / No-Go

- **N1 GO**：真实房主同场 Source 与 plaintext Bootstrap 的 720 B 完全一致；能解释实际奖励行数/尾部零槽的含义；观察到序列化前后不会被其他逻辑无声覆盖，并区分是否多个 0x2F 发送周期。
- **N1 NO-GO**：未知版本、来源指针寿命不明、同场哈希不一致、存在中间空槽但未解码其含义、或抓到的仅为预测奖励/旧 donor 模板。即使 C# 单测全绿仍不能转入 Native 写入。
- **N2 入口决策**：比较 **Lua LotteryRewardItem 后置返回对象追加** 与 **Native pre-serializer 安全原始对象修改**；前者需 4.0.0 main.blua 原始字节/Prototype PC 或合适的 Lua VM late-trigger，后者需验证正确对象与调用顺序。优先稳定、可撤销、不会因入坑流程提前删除 `subject=5` 的入口。

## 现阶段阻塞

本 GitHub 分支和 PC 工作环境没有运行中的 Switch 游戏进程、调试权限或用户实机读数；**不能在这里制造真实 N1 日志、宣布 Hook 命中或者声称官方联网合法性检查已经通过**。本阶段已经交付的是可供 Native 侧同步采样调用的严格数据契约与只读比对器。

参考：[`pokeldn/research` ELF 原生调用审计](https://github.com/969981/pokeldn/blob/research/docs/research/sv/violet400-elf-raid-bootstrap-audit.md)、[Lua 后置追加窗口](https://github.com/969981/pokeldn/blob/research/docs/research/sv/sv400-lua-post-lottery-append-hook.md)。
