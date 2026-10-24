# RaidCrawler SV Mailbox V4 — 技术设计与源码说明

> 适用分支：`feature/sv-raid-mailbox-v4`，从原始 `main` 独立开发。  
> 适用游戏：**Pokémon Violet /《宝可梦 紫》4.0.0**，Title ID `01008F6008C5E000`。  
> 预期原始 Build ID：`709BFD66115298640155FCC4979DBA151C7CC79A`。  
> 当前交付：**N1 只读原生模块 + PC 热配置通信 + 离线自动化测试**。无真实奖励修改、无联机发奖或存档修改能力；**尚无 Switch 实机验证结果**。

本文是开发者技术文档，实际操作请看 [USER_GUIDE.md](USER_GUIDE.md)，现有总体概览见 [README.md](README.md)，分期任务见 [TASKS.md](TASKS.md)。

## 1. 目标、系统边界与设计原则

最终希望实现“原生模块首次加载后，PC 端可在游戏运行时更新奖励配置，不必为每次改配置重新替换文件”，但**本阶段只验证通信与只读观察链路**：

~~~text
Windows RaidCrawler / LiveRewardV4Window
  │ 显式选择道具 ID、数量；点击发布
  ▼
MailboxV4Client + SysBot.Base (Wi-Fi / USB)
  │ 只在已验证的专用 Mailbox 里写配置
  ▼
运行中游戏的 subsdk9 / raidv4::MailboxV4
  ├─ Header: PublishedRevision / ObservedRevision
  ├─ Slot A、Slot B: 45 × 16 B 配置候选
  └─ Tail: 捕获序号、源与输出奖励区哈希
                 ▲
                 │ 仅在房主序列化 Hook 命中时 ObserveReadOnly
                 │
Violet main+0xE32984（原版 memcpy 之后）
  ├─ 读取 X20 所指源 RaidPoint
  ├─ 读取 X23 所指 0xAA0 Bootstrap 中的 RaidPoint
  ├─ 核对整块 0x3E8 与两段 720 B 哈希
  └─ 不写源/目标奖励数据；继续原版流程
~~~

**运行时写操作限定在 Native 自己声明的 Mailbox 配置区，以及 Native 自己的观测计数／状态区**。Hook 不修改游戏本身的 RaidPoint、Bootstrap、背包、联网数据和存档。PC 不安装 Hook，也不扫描全 Heap、写未知游戏内存。

## 2. 代码目录及职责

| 文件 | 说明 |
|---|---|
| `RaidCrawler.Core/LiveRewardV4/MailboxV4.cs` | V4 Wire ABI、小端编码、字段约束、FNV-1a、序列化和解析 |
| `MailboxV4Client.cs` | `IMailboxV4Transport` 抽象、SysBot.Base 适配、A/B 配置提交、回读和确认 |
| `MailboxV4Locator.cs` | Title ID / 版本 / 原版指令检查，沿 exlaunch Hook 寻找当前 Mailbox |
| `MailboxV4ArtifactVerifier.cs` | 本地 `subsdk9` NSO 文件与配对 JSON 的 SHA-256 核对 |
| `MailboxV4Observation.cs` | Header + Tail 一致性读取，解析 Native 观测计数和状态 |
| `RaidCrawler.WinForms/SubForms/LiveRewardV4Window.cs` | 可视化道具行、提交、自动定位、观察输出 |
| `RaidCrawler.WinForms/MainWindow.cs` | 主界面“V4 只读”入口 |
| `switch/live-reward-v4/include/raid_mailbox_v4.hpp` | 同构 C++17 ABI、静态偏移断言、Native 只读 Observer |
| `switch/live-reward-v4/source/main.cpp` | exlaunch N1 只读 Inline Hook、上下文验证、哈希及遥测 |
| `switch/live-reward-v4/build.sh` | 固定 exlaunch 版本编译 `subsdk9` 与 NPDM，不自动部署 |
| `switch/live-reward-v4/gen_manifest.py` | 从同次构建 ELF 符号生成配对 locator JSON |
| `switch/live-reward-v4/tests/audit_violet400_elf.py` | 离线检查自行提取的原始 ELF SHA-256 + 17 个指令字 |
| `RaidCrawler.Tests/LiveRewardV4/*`、`switch/live-reward-v4/tests/*` | C# 单元测试、C++ ABI 与 Manifest 校验测试 |

未继承旧 `host` 分支的实验奖励写入模块，只有历史地址研究作为参考。

## 3. 静态 ELF 分析：为什么选择这个 Hook 点

对已审计的《紫》4.0.0 ELF（SHA-256：`d17403eaa9d844db82f647fd2f1518806b10c0518d53602fc0ac1766298fdba0`），相关数据及偏移：

| 名称 | 地址／偏移 | 长度 | 备注 |
|---|---|---:|---|
| 序列化函数 | `main+0xE327FC` | — | 接收 X0，保存对象指针到 X20 |
| 本次源 RaidPoint | `X20+0x58` | `0x3E8=1000` B | **当前调用上下文**的地址，不是静态绝对地址 |
| 源奖励 45 行 | `X20+0x13C` | `0x2D0=720` B | RaidPoint 内偏移 `0xE4` |
| 整份输出 Bootstrap | `X23` | `0xAA0=2720` B | 游戏本次构造的发送前明文 |
| 输出 RaidPoint | `X23+0x6B8` | `0x3E8` B | Bootstrap 内偏移 |
| 输出奖励 45 行 | `X23+0x79C` | `0x2D0` B | `0x6B8+0xE4` |
| 原版 memcpy | `main+0xE32980` | — | 复制 RaidPoint 后 968 B |
| N1 Hook 点 | `main+0xE32984` | — | 原指令 `MOV X8,X21`，后续恢复原流程 |

Native `main.cpp` 的 `kFingerprint` 在安装 Hook 前检查 17 条原版指令，**不匹配则将自己的状态设为 2，拒绝安装 Hook**。PC locator 还会核对 Hook 周围九条未被覆盖的指令。两者都不等于设备实际 Build ID 的独立读取证明；必须人工核实游戏版本与 Build ID，不得给《朱》或其他版本复用该 RVA。

N1 Hook 先检查寄存器上下文中 `X20`、`X23` 的基本地址及 `X22` 低位传输长度，随后要求输出中的 `RaidPoint_` 标识匹配，且源与目标整块 `0x3E8` 字节一致。通过后才计算两份 720 B 奖励 FNV32 并调用 `ObserveReadOnly`。这些检查是**当前实现的过滤假设，仍须实机确认**；不能因为编译通过就断言所有真实房主场景都会命中。

## 4. Mailbox V4 的二进制 ABI

所有多字节数字使用 **Little Endian**。总长度 `0x800=2048` B。

### 4.1 顶层布局

| 全局偏移 | 大小 | 字段 | 写入方／语义 |
|---|---:|---|---|
| `+0x000` | 8 | `RCHMB04\0` | Native 初始化 Magic |
| `+0x008` | 4 | Version=4 | Native |
| `+0x00C` | 4 | Length=`0x800` | Native |
| `+0x010` | 4 | Capabilities=1 | Native，表示**只读游戏数据观察** |
| `+0x014` | 4 | Status=1/2 | 1 Ready，2 不兼容/不安装 Hook |
| `+0x018` | 4 | ActiveSlot | `0`、`1` 或 `0xFFFFFFFF` 未发布 |
| `+0x01C` | 4 | Reserved | 保留 |
| `+0x020` | 8 | PublishedRevision | PC 提交序号 |
| `+0x028` | 8 | ObservedRevision | Native **实际读过有效配置**才更新 |
| `+0x030` | 4 | ObservationCount | Native 尝试观察配置的次数 |
| `+0x034` | 4 | LastObservationStatus | 1 有效，2 无配置，3 无效，4 并发 |
| `+0x038` | 4 | Generation | Native 遥测写入前后奇偶递增 |
| `+0x03C` | 4 | Reserved | 保留 |
| `+0x040` | `0x3C0` | Config Slot A | PC 写入未激活槽 |
| `+0x400` | `0x3C0` | Config Slot B | PC 写入未激活槽 |
| `+0x7C0` | `0x40` | Native Tail | 捕获日志及预留空间 |

### 4.2 单个配置槽，0x3C0 B

| 槽内偏移 | 长度 | 内容 |
|---|---:|---|
| `+0x00` | 8 | Revision，非零 `u64` |
| `+0x08` | 4 | Enabled：`0/1` |
| `+0x0C` | 4 | Mode：当前仅 `1=Append` |
| `+0x10` | 4 | Policy：当前仅 `2=Persistent` |
| `+0x14` | 4 | RewardCount：`0..45`，Enabled 时至少 1 |
| `+0x18` | 4 | `FNV-1a-32` 校验值 |
| `+0x1C` | 4 | Reserved=0 |
| `+0x20` | `0x2D0` | 45 行、每行 16 B |
| `+0x2F0` | `0xD0` | 必须为零的保留区 |

每行四个小端无符号 32 位值：`ItemId`、`Quantity`、`RareFlag`、`SubjectType`。当前代码的协议规则：

- `ItemId`：1–65535，行之间不允许重复；**不是经过游戏合法性证明的 ID 白名单**。
- `Quantity`：1–999；默认创建行时 999。
- `RareFlag=0`，`SubjectType=5`；这是当前只读候选格式限制，不代表这些规则在所有领奖情况下有效。
- 未使用奖励行、槽保留空间必须全零，Enabled=true 时必须选至少一行。
- 配置的 Mode=Append、Policy=Persistent 是**记录用户意图**，N1 不执行任何 Append。

校验使用对整个 `0x3C0` B 的 FNV-1a 32，起始值 `2166136261`、乘数 `16777619`，计算时先将校验字段 `+0x18..+0x1B` 当作零。它是意外损坏检测，不是身份认证或防攻击签名。

### 4.3 发布顺序与观察顺序

~~~text
PC: 检查 Magic/版本/Ready/Capability
     → 找未激活槽
     → 序列化全部 0x3C0 B
     → sys-botbase 写到未激活槽
     → 全量读回并逐字节比对
     → 再次检查旧 ActiveSlot/PublishedRevision 没变化
     → 先写新 PublishedRevision (8 B)
     → 最后写 ActiveSlot (4 B)
     → 回读 Header 确认 published

Native（每次通过本次 Hook 上下文验证后）：
     → 读 ActiveSlot、PublishedRevision
     → 拷贝槽到局部 Snapshot
     → 再读 ActiveSlot、PublishedRevision
     → CRC + 行约束校验
     → 成功才更新 ObservedRevision / LastObservationStatus
     → 不修改任何游戏奖励
~~~

PC 提交时的 `SemaphoreSlim` 只排除**同一个 MailboxV4Client 实例**中的并发请求；Native 的快照双读与校验只能在不一致时拒绝，并**不能**提供完整跨进程事务、写者排他或强原子多字节提交保证。没有 ACK/租约，连续极高速发布时可能跳过中间版本；N1 设计为失效关闭。未来若准备打开写入逻辑，须先重新审核同步协议。

## 5. Native 只读遥测字段、状态含义

Native Hook 使用 `g_captureBusy` 抑制重入。Tail 前 16 B：

| 偏移 | 长度 | 说明 |
|---|---:|---|
| `+0x7C0` | 4 | CaptureSerial，本进程内递增 |
| `+0x7C4` | 4 | ContextResult |
| `+0x7C8` | 4 | 源奖励 FNV32 |
| `+0x7CC` | 4 | 目标奖励 FNV32 |

`ContextResult` 枚举：

| 值 | 解释 | ObservedRevision 是否会更新 |
|---|---|---|
| 0 | 初始未产生捕获 | 否 |
| 2 | X20/X23 或长度不符合检查 | 否 |
| 3 | `RaidPoint_` 标识或整块源/目标数据不匹配 | 否 |
| 4 | 数据相等但没有已发布配置 | 否 |
| 5 | 数据相等但配置校验/并发失败 | 否 |
| 6 | 数据相等，成功读取有效配置 | **是** |

`ObservationCount` 只统计实际进入 `ObserveReadOnly` 的次数，而 `CaptureSerial` 是调用 `Report` 的次数，两者不必相等；重入直接被略过不产生记录。若状态 2/3，源/目标哈希会是 0，**不能将两个 0 相等误判成内容相等**。UI 的 `RewardHashesMatch` 只有在状态 4/5/6 时才有意义。

PC 的 `MailboxV4ObservationReader` 读取 Header、Tail、Header，以 `Generation` 奇偶变化以及观察序号保护快照；始终只提供**本地数据一致性**证据，不能证明网络包已发出、客户端已接受或背包存档持久化。

## 6. 自动定位流程与配对验证

本次构建生成 `subsdk9`、`main.npdm`、`mailbox-v4-locator.json`、`SHA256SUMS.txt`。Manifest 来自同次 **未剥离符号的 exlaunch ELF**：

1. `aarch64-none-elf-nm -n -C` 找到 `RaidV4ReadOnly::Callback` 和 `rcv4::g_mailbox`；
2. 计算 `callback_to_mailbox_delta = mailbox_symbol - callback_symbol`；
3. 将 NSO 的 SHA-256、期望 Title ID、版本、Hook RVA、只读标记写到 JSON；
4. Windows 端先核对**本地同目录**的 `subsdk9` 与 Manifest SHA-256，再检查运行时 Title ID / 版本与邻近原版 Opcode；
5. 从 `main+0xE32984` 已安装的 exlaunch 跳转寻找 Entry，验证 Entry 指令及 Callback 地址；用编译清单中的 delta 计算 Mailbox 绝对地址；
6. 在候选地址读取 `RCHMB04`、版本、长度、状态及只读 Capabilities；失败则拒绝，**不退回到猜测地址或 Heap 全量扫描**。

**重要限制：**本地 NSO/Manifest 校验不能证明 Switch 当前实际加载的 NSO 一定是那份文件；也没有在流程中动态读取并比对设备真正的游戏 Build ID。只有通过真机上版本、Hook、Mailbox 和行为等多项检查后，才能提高可信度。退出/重启游戏后旧邮箱地址可能失效；PC 记录 Main NSO 基址，发现变化就要求重新定位，但相同基址重用也不等于证明进程未重启。

## 7. 构建、CI、可复现性

### 7.1 PC / .NET 10

~~~powershell
dotnet restore RaidCrawler.slnx
dotnet build RaidCrawler.slnx -c Release --no-restore
dotnet test RaidCrawler.Tests/RaidCrawler.Tests.csproj -c Release --no-build --filter "FullyQualifiedName~RaidCrawler.Tests.LiveRewardV4."
dotnet publish RaidCrawler.WinForms/RaidCrawler.WinForms.csproj -c Release -r win-x64 --self-contained false
~~~

运行环境需有兼容的 .NET 10 Windows Desktop Runtime，程序使用现有 SysBot.Base 依赖。CI 文件：`.github/workflows/live-reward-v4.yml`，分别执行 C++ ABI 冒烟及 Windows 编译/测试/发布。

### 7.2 Native / exlaunch subsdk9

~~~bash
export DEVKITPRO=/opt/devkitpro
bash switch/live-reward-v4/build.sh
~~~

构建脚本固定 exlaunch 提交 `f9f4b0dd07b68f97958cb9c79228bbca22ca80d5`，要求 devkitPro/devkitA64/libnx、Git、Make、Python3，从源码复制 `main.cpp` 和 v4 ABI header 到固定的 exlaunch 工作树；构建生成 `switch/live-reward-v4/out/`。CI 文件：`.github/workflows/live-reward-v4-native.yml`，在固定 devkitPro 容器中构建并上传标注 `READONLY-UNVERIFIED` 的研究附件。**它不自动部署到 SD 卡，不构建写奖励版本。**

### 7.3 本地 ELF 离线校验

~~~bash
python3 switch/live-reward-v4/tests/audit_violet400_elf.py --elf /path/to/Violet4.0.0.elf
~~~

离线校验完整 ELF SHA-256 与 17 个指令字；原始游戏 ELF **不在本仓库内**。此检查与运行时 Hook 验收是两回事。

## 8. 已知限制、潜在缺陷与后续工程门槛

1. **实机尚未验收**：编译与单元测试通过不能证明 Nintendo Switch 会正常加载这份 NPDM/NSO，也不能证明真实开房点击开始后能命中并通过过滤。
2. **进程权限和插件冲突**：若已存在 `subsdk9`、`main.npdm` 或其他 Hook，必须先备份并核对；当前没有多插件 Hook 链冲突检测。
3. **同步协议限制**：单实例发送锁、非原子的两阶段发布、C++ 原子字段与外部内存写入并非严格 C++ 同线程同步；当前靠一致性检查失效关闭，需在真正写入前重新验证。
4. **捕获假设**：Native 的地址有效性仅为基本数值/对齐检查，没有映射区可读性查询；若上下文不符仍存在崩溃风险。先在可恢复的离线测试机上验证。
5. **版本和构建识别**：Opcode Guard 不是完整的 Build ID 验证；Scarlet 与其他版本需要独立移植、重新审核。
6. **Hash 非真实性证明**：FNV32 碰撞可能存在；若需强证据，后续可以增加受限的只读完整 Bootstrap 快照、日志及本地接收侧对照。
7. **配置含义 ≠ 游戏能力**：45 行是二进制格式容量；Append + 999 + Persistent 当前只是配置数据，不代表 45 行都能合法追加或实际领取。
8. **联网与存档**：没有执行在线发奖、参与者收货与存档持久化测试，不应将本研究构建当成可投入线上多人环境的版本。

后续的实机 N1 验收与日志模板、错误排查请参阅 [USER_GUIDE.md](USER_GUIDE.md)；任何真实奖励写入须在独立阶段经过充分的本地兼容性和失败恢复验证。
