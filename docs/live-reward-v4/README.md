# SV Raid Live Reward — Mailbox V4 （独立新分支）

> 新分支 feature/sv-raid-mailbox-v4，来自 RaidCrawler/main @ 6beb6601e95fcd220807eb2bb9b0698dc20d2226。
> 当前交付：V4 编解码、sys-botbase 配置客户端、C++ N1 只读 Hook、匹配 manifest 的 subsdk9 构建、自动定位、WinForms 控制台与 CI；真实 Switch 实机仍待验证。
> **没有游戏奖励写入、没有实机验证；本分支已有成功 CI 的只读 subsdk9 与 Windows 工具，但不是线上发奖 MOD。**

## 文档导航

| 文档 | 面向读者 | 内容 |
|---|---|---|
| **[USER_GUIDE.md](USER_GUIDE.md)** | 使用者 | 两份 GitHub Actions 附件下载、PC/SD 目录、手动安装、点击操作、A/B 热配置实机验收、排错与卸载 |
| **[TECHNICAL_ARCHITECTURE.md](TECHNICAL_ARCHITECTURE.md)** | 开发者 / 逆向人员 | ELF / Hook 偏移证据、完整 Mailbox 0x800 ABI、双缓冲协议、定位器原理、状态码、源码映射和 CI |
| [N1_READONLY_IMPLEMENTATION.md](N1_READONLY_IMPLEMENTATION.md) | 实验人员 | 当前 N1 实现的精简流程与实验注意事项 |
| [TASKS.md](TASKS.md) | 维护者 | 已完成/未完成任务及下一阶段验收门槛 |

## 一、目标和明确的不做事项

目标是未来游戏启动一次加载对应 subsdk9 后，PC 可随时向游戏内部 Mailbox 更新配置。当前用户要求的默认值：启用 true、无损追加 Append、每条默认数量 999、持续生效 Persistent。对游戏奖励的实际生效仍需完成后续阶段。

本阶段只写 Mailbox 的**配置区**，绝不写游戏原始 RaidPoint、720 B 奖励区、2,720 B Bootstrap、背包、存档或网络缓冲。Native 当前只读并记录版本号，不能修改奖励。旧 host 分支只作静态研究参考，不导入它的单行写入或 v3 协议。

默认行生成器需要用户提供实际 ItemId，不自动臆造 25/45 行道具列表。Enabled=true 时必须包含至少一条真实选择的行才能发布。

## 二、代码分层

- RaidCrawler.Core/LiveRewardV4/MailboxV4.cs：独立的二进制字段、版本、校验、规范化 45 条奖励行。
- RaidCrawler.Core/LiveRewardV4/MailboxV4Client.cs：注入式 IMailboxV4Transport、SysBot.Base 适配、双缓冲发布、逐字节读回、状态查看。
- RaidCrawler.Tests/LiveRewardV4/MailboxV4Tests.cs：默认数量、45 行边界、CRC、字段拒绝、槽切换、发布失败不激活。
- switch/live-reward-v4/include/raid_mailbox_v4.hpp：C++17 可移植同构内存布局、静态 ABI 断言和纯只读 ObserveReadOnly。
- switch/live-reward-v4/tests/abi_smoke.cpp：GNU C++ 下运行的跨语言固定测试向量及只读观察状态验证。
- .github/workflows/live-reward-v4.yml：Windows .NET 10 单测 + Linux Native 契约测试，并上传 Windows 只读研究工具。
- .github/workflows/live-reward-v4-native.yml：固定 exlaunch 构建只读 subsdk9，生成配对 manifest 并作为独立研究附件上传，**不会自动安装到 Switch**。

## 三、Mailbox v4 字节布局（Little Endian）

固定大小 0x800 B；开头 0x40 B Header，配置槽 A 位于 +0x40（0x3C0 B），配置槽 B 位于 +0x400（0x3C0 B），尾部 +0x7C0（0x40 B）预留。

| Header 偏移 | 长度 | 字段 / 写入方 |
|---|---:|---|
| 0x00 | 8 | RCHMB04 + NUL；Native 初始化 |
| 0x08 | 4 | Version=4 |
| 0x0C | 4 | Length=0x800 |
| 0x10 | 4 | Capabilities=1，仅允许只读观察 |
| 0x14 | 4 | Status=1，Ready |
| 0x18 | 4 | activeSlot=0、1 或 FFFFFFFF；PC 最后发布 |
| 0x1C | 4 | 保留 |
| 0x20 | 8 | publishedRevision；PC 发布 |
| 0x28 | 8 | observedRevision；Native 真正观察后才更新 |
| 0x30 | 4 | observationCount；Native |
| 0x34 | 4 | observationStatus：1 有效、2 未发布、3 数据无效、4 并发变化 |
| 0x38 | 4 | generation（Native 写遥测前后递增的奇偶序号） |
| 0x3C | 4 | 保留 |

| Slot 相对偏移 | 长度 | 内容 |
|---|---:|---|
| 0x00 | 8 | Revision，非零 |
| 0x08 | 4 | Enabled=0/1 |
| 0x0C | 4 | Mode=1，Append |
| 0x10 | 4 | Policy=2，Persistent |
| 0x14 | 4 | RewardCount=0..45（Enabled 时至少1） |
| 0x18 | 4 | FNV-1a 校验，计算时本字段视作全0 |
| 0x1C | 4 | Reserved=0 |
| 0x20 | 720 | 45 × 16 B 奖励行 |
| 0x2F0 | 0xD0 | 全零保留空间 |

一行奖励依次为 4 个 u32：ItemId、Quantity、RareFlag、SubjectType。本期输入约束为 ItemId 1..65535、Quantity 1..999、RareFlag=0、SubjectType=5，禁止重复 ID、禁止非零垃圾尾部。协议校验成功不代表每种道具都可以合法入包或真实到账。

## 四、配置提交与原生只读观察

1. PC 确认 Header magic、version、status=Ready、capabilities=readonly；依据 activeSlot 选未使用的一槽。
2. PC 对完整槽进行序列化，写入 **仅专用 Mailbox**，随后逐字节读回，验证结果。
3. PC 再次检查 activeSlot 和 publishedRevision 未被修改；先写 Revision，再将四字节 activeSlot **最后发布**。
4. Native observer 在调用时两次读取 activeSlot/revision，中间复制槽，校验 CRC/版本/奖励行；任何不一致一律放弃，绝不修改游戏数据。
5. 成功后写 observedRevision / observationCount / observationStatus，供 PC 判断 “配置已发布” 与 “Native 确认观察” 的区别。

注意：该初期协议是 **fail-closed 只读模型**，并不是多进程原子 CAS 或完全无竞态的高频热更新系统。PC 侧用 SemaphoreSlim 限制当前实例并发，Native 用版本双读+校验进行拒绝；后续启用业务写入之前还必须单独设计 ACK、跨进程写者仲裁和读者租约，避免一个配置槽被重用时的读取撕裂。

## 五、已实现的 Native 只读接线（尚未实机确认）

既有《紫》4.0.0 静态研究指出 main+0xE327FC 为 0xAA0 序列化器，main+0xE32980 完成 RaidPoint 后段 memcpy，main+0xE32984 为原版 MOV X8,X21（拟研究的只读 Hook 边界）。X20+0x13C 对应当前调用源奖励，X23+0x79C 对应输出 720B 奖励。

**当前已实现**：Native 源码将 Hook 安装在该 RVA；启动前比对 17 条原生指令，使用 X20/X23 只读比较两份 RaidPoint，并记录奖励区哈希；PC 端使用配对 manifest 自动定位 Mailbox。Native/Windows CI 已编译并进行离线测试。**尚未**在真实 Switch 上证明 Hook 能命中真正的房主发包，亦未独立取得实机 Build ID 并完成联动验收。不能把《紫》的偏移直接套到《朱》。

## 六、测试

Windows .NET 10：dotnet restore RaidCrawler.slnx；dotnet build RaidCrawler.slnx -c Release --no-restore；dotnet test RaidCrawler.Tests/RaidCrawler.Tests.csproj -c Release --no-build --filter FullyQualifiedName~RaidCrawler.Tests.LiveRewardV4.

Linux native：g++ -std=c++17 -Wall -Wextra -Werror -pthread -O2 switch/live-reward-v4/tests/abi_smoke.cpp -o /tmp/mailbox-v4-abi，然后运行 /tmp/mailbox-v4-abi。

仓库 CI 工作流对新分支自动检查；Windows 与 Native 两份研究构建分别作为 Actions Artifacts 上传（默认保留 7 天），但**不自动安装/部署到 Switch，也不产生真实奖励写入功能**。构建测试不能替代 Switch 真机验证。

N1 构建和操作详见 [N1_READONLY_IMPLEMENTATION.md](N1_READONLY_IMPLEMENTATION.md)。剩余任务见 [TASKS.md](TASKS.md)。
