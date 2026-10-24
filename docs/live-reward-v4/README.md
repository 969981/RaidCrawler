# SV Raid Live Reward — Mailbox V4 （独立新分支）

> 新分支 feature/sv-raid-mailbox-v4，来自 RaidCrawler/main @ 6beb6601e95fcd220807eb2bb9b0698dc20d2226。
> 当前交付：独立 C# Mailbox 编解码 + sys-botbase 专用邮箱写入客户端 + C++ 只读观察器 + C#/C++ 离线测试 + CI。
> **没有游戏奖励写入、没有 subsdk9 可部署文件、没有实机验证；这不是现成的线上发奖 MOD。**

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
- .github/workflows/live-reward-v4.yml：Windows .NET 10 单测 + Linux Native 契约测试。**不发布或安装 subsdk9**。

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
| 0x38 | 4 | generation（预留） |
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

## 五、后续 ELF 和 Native 接线

既有《紫》4.0.0 静态研究指出 main+0xE327FC 为 0xAA0 序列化器，main+0xE32980 完成 RaidPoint 后段 memcpy，main+0xE32984 为原版 MOV X8,X21（拟研究的只读 Hook 边界）。X20+0x13C 对应当前调用源奖励，X23+0x79C 对应输出 720B 奖励。

**v4 当前未把此静态 RVA 接入 subsdk9，也未实机证明 hook 能命中真正的房主发包。** 后续单独实施目标版本/Build ID 校验、Hook、只读两端 720B 捕获、邮箱进程内初始化和 Manifest 定位。不能把《紫》的偏移直接套到《朱》。

## 六、测试

Windows .NET 10：dotnet restore RaidCrawler.slnx；dotnet build RaidCrawler.slnx -c Release --no-restore；dotnet test RaidCrawler.Tests/RaidCrawler.Tests.csproj -c Release --no-build --filter FullyQualifiedName~RaidCrawler.Tests.LiveRewardV4.

Linux native：g++ -std=c++17 -Wall -Wextra -Werror -pthread -O2 switch/live-reward-v4/tests/abi_smoke.cpp -o /tmp/mailbox-v4-abi，然后运行 /tmp/mailbox-v4-abi。

仓库 CI 工作流对新分支自动检查。构建测试不能替代 Switch 真机验证；本期不生成、发布、上传或自动安装 mod。

下一阶段详细任务见 TASKS.md。
