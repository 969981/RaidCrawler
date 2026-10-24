# Mailbox V4 从 main 重新开发：任务拆分、先后依赖、验收标准

分支：feature/sv-raid-mailbox-v4；不从 host cherry-pick 旧写入代码。原则：先证明只读，之后才规划真正写入；测试与正式版本严格隔离。

## 完成进度

| 任务 | 状态 | 交付文件/验收 |
|---|---|---|
| T00 新分支从 main 创建 | 已提交 | 基线 main 6beb6601e95f |
| T01 独立 V4 ABI 及 PC Codec | **代码完成；Windows CI 通过** | MailboxV4.cs；45×16B、0x800 内存布局、默认 x999 |
| T02 sys-botbase 配置传输 | **代码完成；Windows CI 通过，实机待验证** | MailboxV4Client.cs；只写专用 Mailbox；写前检查、读回 |
| T03 Native 只读 ABI / Observe | **代码完成；Native ABI CI 通过** | raid_mailbox_v4.hpp；只读，不更改游戏奖励 |
| T04 离线与跨语言合约测试 | **离线测试通过** | C# xUnit / C++ g++ 测试，含确定性校验向量 |
| T05 .NET / GCC CI | **通过；Windows 工具与只读 NSO 均有构建附件** | live-reward-v4.yml |
| T06 subsdk9 最小只读装载 | **代码与 Native CI 构建通过，待实机** | 只读 NSO、配套 manifest、游戏内 V4 Mailbox 初始化 |
| T07 ELF 实机只读 Hook | **静态 Hook 代码已提交，实机未验收** | 游戏版本指纹、Hook 命中、X20/X23 两端奖励比较 |
| T08 PC V4 面板、热提交状态 | **已集成并通过 Windows CI；真机待验证** | 从主界面进入、编辑行、查看 Published/Observed、关闭配置 |
| T09 长时间运行和 A/B 热切换 | 未开始 | 游戏运行中多次提交配置、跨场景检测序号，没有拆包损坏 |
| T10 奖励写入可行性验证 | **独立阶段，默认关闭** | 需先审查目标客户端规则与本地多人测试条件 |
| T11 正式奖励引擎及多模式 | 暂不进入 | 不由本次 v4 只读提交隐含授权 |

## 当前 N1 状态与文档

- **已完成（CI 验证）**：C# Mailbox V4、双缓冲配置客户端、只读 Native Observer、exlaunch N1 Hook 源码、固定构建的 subsdk9、编译产物配对 JSON、PC 自动定位器、WinForms V4 只读入口、Windows 与 Native 自动化测试及 Actions Artifact。
- **已完成（静态设计与文档）**：ELF / 17 条 Opcode 审计与使用说明、故障排查、状态解释、文件配对和回退流程。
- **尚未完成（实机）**：真实 Switch 上验证 N1 Hook 加载与房主序列化命中、A→B 多场切换、无线断联恢复以及长时间运行。
- **当前不存在**：实际修改掉落奖励的 Native 写入逻辑、在线到账验证、正式版本发布。
- 详细技术文档：[TECHNICAL_ARCHITECTURE.md](TECHNICAL_ARCHITECTURE.md)。
- 详细使用手册：[USER_GUIDE.md](USER_GUIDE.md)。

## 第二阶段：subsdk9 纯观察器（依赖 T01..T05 通过）

开发点：
- 用固定 exlaunch 版本，在 switch/live-reward-v4 下**重新创建**独立 NSO 构建入口，不复制 host 分支旧 subsdk9。
- 严格检查《紫》4.0.0 Title ID / GNU Build ID / 原始 0xE32984 指令指纹；与不匹配版本自动拒绝安装 Hook。
- Native 初始化一个独立的 MailboxV4 实例，并生成对应编译产物的地址定位 manifest；PC 必须只连接已核验地址。
- 在 Hook 上下文先做纯只读完整性验证，再调用 ObserveReadOnly；绝不修改 X20 源、X23 目标、rewardRows、存档或网络报文。
- 如果 Native 不能识别/读取对象，则记录拒绝状态和次数，不重试执行危险写入。
- 初期 NSO 仅手工本地安装，不自动替换已存在的 subsdk9/main.npdm；与其他 ExeFS 模块冲突则停止。

验收条件：
1. 游戏能够启动，Hook 启用/禁用逻辑稳定；
2. 真实房主至少两次挑战中捕获计数增长，与实际 0xAA0 数据匹配；
3. PC 发布 revision N 后，Native observedRevision 仅在下一次真实观察更新为 N；
4. A→B→A 配置切换不重启游戏，无旧配置误用；
5. 无任何游戏数据写入。

## 第三阶段：用户界面和热更新（依赖 Native 只读通过）

- 独立 LiveRewardV4Window，不依赖旧 host 控件，主界面后续新增入口；
- 显示原版房主版本、Hook 状态、Mailbox Magic/Revision、读取成功次数、拒绝原因；
- 用户手动增删奖励，默认 Enabled=true / Append / Quantity=999 / Persistent；
- 未选择道具时按钮禁止发布，不自动提交想象中的 25 行；
- 新配置推送到未激活槽，回读相等、更新 Revision 和 ActiveSlot，界面显示 **配置已发布，等待 Native 观察**；
- 观察成功后显示 **Native 已读取配置**，而非“已发奖”；
- 超时、Wi-Fi 中断、未知版本时不得假装成功，显示失败且保持原版；
- 加入取消/恢复原版开关（提交 Disabled 配置），但不通过改代码段来“恢复”。

## 后续第四阶段：独立评估，不默认启用

在本地可恢复测试环境，以最低影响单行修改验证：
- 受控写入只针对**已经证明是当前发送副本**的目标区域；
- 确认 45 槽容量，原始固定/随机/条件奖励保留；
- 验证接收、掉落、背包及重新进入存档各自不同的证据；
- 官方互联网行为另行验收，不能以离线模拟/LDN 结果代替；
- 无覆盖模式/全量替换默认功能，直到真实性和兼容性有证据；
- 任何写入功能都必须独立显式开启、附带恢复/失效保护，不在普通 CI 发布。

## CI 验收命令

Windows：
dotnet restore RaidCrawler.slnx
dotnet build RaidCrawler.slnx -c Release --no-restore
dotnet test RaidCrawler.Tests/RaidCrawler.Tests.csproj -c Release --no-build --filter FullyQualifiedName~RaidCrawler.Tests.LiveRewardV4.

Linux：
g++ -std=c++17 -Wall -Wextra -Werror -pthread -O2 switch/live-reward-v4/tests/abi_smoke.cpp -o /tmp/mailbox-v4-abi
/tmp/mailbox-v4-abi

N1 构建与手工验证见 N1_READONLY_IMPLEMENTATION.md；总体协议见 README.md。合并 main 需要另行决定，本任务**不合并**。
