# RaidCrawler V4 N1 使用说明（Windows + Switch，简体中文）

> 版本类型：**实验性只读观察器**，不是能够在线刷道具的正式修改器。  
> 游戏范围：**《宝可梦 紫》4.0.0**；暂不支持《朱》、其他游戏版本及不明 Build ID。  
> 本文基于 `feature/sv-raid-mailbox-v4` 当前源码、Windows / Native CI 编译结果撰写。**尚未取得 Switch 实机成功记录**，因此步骤中的“预期结果”不代表已经实机通过。

技术细节参阅 [TECHNICAL_ARCHITECTURE.md](TECHNICAL_ARCHITECTURE.md)，协议字段见 [README.md](README.md)。

## 一、当前可以做什么，不能做什么

**现有可测试功能**：在 RaidCrawler 界面填写 1～45 行道具 ID 与数量、选择是否启用配置；将其通过 sys-botbase 发送到已安装 `subsdk9` 的**专用配置邮箱**；下一次符合条件的房主序列化 Hook 触发后，读取 Native 是否观察到对应配置版本，并查看原版源/输出奖励数据的哈希。

**现阶段不能做**：改变太晶坑掉落、修改联网奖励、向其他参与者发送自定义道具、确认背包到账、修改存档、热安装 `subsdk9`，也不能仅凭“配置已发布”证明游戏 Hook 运行成功。

### 配置默认值

| 项目 | 本版默认／实际作用 |
|---|---|
| 启用 | 复选框默认勾选（仅表示候选配置启用） |
| 奖励模式 | Append / 无损追加（**只记录配置，不真正追加**） |
| 数量 | 新增行默认为 `999`，允许 1～999 |
| 生效策略 | Persistent / 持续（**Native 每次合适的 Hook 都可以读该版本**） |
| 行数 | 允许最多 45 种；提交时会校验 |
| 领取字段 | `RareFlag=0`、`SubjectType=5` 固定 |
| 示例行 | 界面初始演示 `ItemId=492, Quantity=999`；这只是示例数字，不代表已核对的游戏道具名称或合法性 |

推荐先将示例行替换为你自己从游戏资料核实的目标道具 ID，再手动点击“发布只读配置”。程序不会因打开窗口而自动发布。

## 二、准备条件

- Windows PC；能够运行原版 RaidCrawler 的已连接 Switch 环境，安装 **.NET 10 Windows Desktop Runtime**（若尚未安装）。
- 可自行备份和恢复的 Switch 测试机，Atmosphère / ExeFS 模块加载条件已具备；已有匹配的 sys-botbase 或兼容 SysBot.Base 的 USB/TCP 访问方式。
- 《宝可梦 紫》4.0.0，Title ID `01008F6008C5E000`；**预期** Build ID `709BFD66115298640155FCC4979DBA151C7CC79A`。
- 记录当前 SD 卡所有 ExeFS 覆盖文件、已有 `subsdk9` / `main.npdm` 和相关插件；**不要直接覆盖陌生的同名文件**。
- 首次安装前建议备份存档和可恢复环境，并先在不连接官方线上服务的本地场景验证。

**重要：**固件、系统插件、CFW 和游戏更新版本之间可能不兼容。本工具是研究版，没有对 Switch 上的安装效果作保证。

## 三、下载两份 GitHub Actions 构建

本项目分别生成 PC 程序与 Native 模块，**不能只下载一份**。

### A. Windows PC 工具

工作流：[SV Raid Mailbox V4 (read-only foundation)](../../.github/workflows/live-reward-v4.yml)

1. 在 GitHub 仓库打开 **Actions**，选择上述工作流。
2. 找到 `feature/sv-raid-mailbox-v4` 上**成功（绿色）**的执行记录。
3. 打开运行详情页面底部的 **Artifacts**，下载：
   `RaidCrawler-V4-N1-PC-READONLY-UNVERIFIED`。
4. 解压到 Windows 独立测试文件夹，例如 `D:\Tools\RaidCrawler-V4-N1\`，保留原有 DLL / 资源文件，不要只拿 EXE。

### B. Native N1 只读构建

工作流：[SV Raid V4 N1 subsdk9 (read-only research)](../../.github/workflows/live-reward-v4-native.yml)

1. 在该工作流找**成功**的执行，下载 Artifacts：
   `RaidCrawler-V4-N1-Violet400-READONLY-UNVERIFIED`。
2. 解压后核对这些文件确实存在：

~~~text
subsdk9
main.npdm
mailbox-v4-locator.json
SHA256SUMS.txt
README-INSTALL.txt
~~~

**Artifact 不是 GitHub Release**，下载可能需要登录 GitHub；当前工作流附件保留期为 **7 天**。如果过期，需要在仓库 Actions 重新触发 `workflow_dispatch` 构建。两份附件来自同一分支，但 **Manifest 与 subsdk9 必须来自同一次 Native 构建**，不能拿旧版 JSON 配新版 NSO。

## 四、PC 文件怎样放置

将 Native 包内原样的 `subsdk9` 和 `mailbox-v4-locator.json` 复制到 Windows RaidCrawler 主程序旁边，供本地校验：

~~~text
D:\Tools\RaidCrawler-V4-N1\
├─ RaidCrawler.WinForms.exe        # Windows 构建产物；可能带依赖文件
├─ mailbox-v4-locator.json         # Native 包提供
├─ subsdk9                         # Native 包提供；PC 本地仅做 SHA-256 校验
└─ ...                             # Windows 构建自带的其他文件
~~~

Windows 文件夹内的 `subsdk9` **不会自动注入 Switch**。其作用是让“自动定位 N1”先核验本地文件与 JSON 的 SHA-256 完全一致。

在 PowerShell 可人工核对：

~~~powershell
Get-FileHash .\subsdk9 -Algorithm SHA256
Get-Content .\mailbox-v4-locator.json
~~~

其中第一条的结果应与 JSON 的 `subsdk9_sha256` 一致。**这只证明 PC 的本地配对，不证明 Switch 实际装载了同一文件。**

## 五、Switch 测试机安装步骤（手动，仅实验环境）

1. **彻底退出《宝可梦 紫》游戏**，不要只退到 Switch 主菜单挂起。
2. 先备份现有目录 `sd:/atmosphere/contents/01008F6008C5E000/`，尤其是 `exefs/subsdk9`、`exefs/main.npdm` 等覆盖及其他可能 Hook 的插件。
3. 若没有冲突，按照你的 Atmosphère / exlaunch 实际加载方式，放入匹配的 Native `subsdk9`：

~~~text
sd:/atmosphere/contents/
└─ 01008F6008C5E000/
   └─ exefs/
      └─ subsdk9
~~~

4. **不要未经核对直接复制或覆盖 `main.npdm`**。它可能影响进程元数据与加载行为；必须先确认装载方案是否实际需要它、并保证与原有配置兼容。如果不明确，停止安装并先处理加载兼容问题。
5. 确认目标游戏确为《紫》4.0.0，且构建标识和本研究版本匹配；若不同，停止而不是尝试复用偏移。
6. 完全重新启动游戏，确认没有启动崩溃、黑屏或其它明显异常。

**更改/更新 `subsdk9` 本身需要完全退出游戏再加载。** 只有以后更改 Mailbox 配置才可能在游戏运行期间热更新。

## 六、第一次使用 PC 的 V4 只读控制台

1. 打开 `RaidCrawler.WinForms.exe`，在原有连接界面使用 Wi-Fi 或 USB **连接 Switch**，确认连接成功。
2. 主界面底部找到 **“V4 只读”** 按钮，打开独立控制台。
3. 在对话框中先点击 **“自动定位 N1”**。
4. 程序会依次检查本地 NSO 与 JSON 是否配对、运行时 Title ID、游戏版本、原版 ARM64 指令、已安装 exlaunch Hook、Mailbox `RCHMB04` 和只读能力。
5. 只有全部通过，日志才会显示类似：

~~~text
已定位 N1 邮箱：0x...；只读能力=1。
~~~

**这个地址是每次游戏进程的运行时地址，不是固定的 HEX/金手指地址。禁止将示例或上次运行的地址粘贴到其它工具直接写内存。**

如果定位失败，先对照第九节排错；不建议手动猜测邮箱地址。

## 七、发布配置与读取 Native 观察

### 7.1 创建配置 A

在 V4 窗口表格里，把初始示例改成你自行确认的道具 ID。每行填写两列：“道具 ID (1–65535)”和“数量 (1–999)”。

- 点“增加道具”可增加一行，默认数量是 999。
- 选中一行或多行后，“移除选中”会删除所选行；最多 45 行。
- 当前模式固定 **Append**，当前策略固定 **Persistent**，这些并非 UI 可切换的实际发奖模式。
- 同一个道具 ID 重复、数量为 0/1000、ID 越界、空输入或启用但没有有效行都会被拒绝。

点击 **“发布只读配置”**，成功后看到：

~~~text
配置已发布 Revision=N，等待 Native 下次只读观察。不会修改奖励。
~~~

这是通过 sys-botbase 写入**Native 自己的配置区**。该消息**不表示自定义道具已被游戏发出**。

### 7.2 触发只读捕获

在本地测试环境中以自己为房主进入太晶团体战，按照通常流程点击 **“开始挑战”**。目标序列化函数如果执行到了已安装且匹配的 Hook，Native 才会记录此次观察。

返回 PC 点击 **“读取 Native 观察”**：

~~~text
调用记录=1，上下文状态=6，
已发布/已观察 Revision=N/N，
源/输出 FNV=xxxxxxxx/xxxxxxxx，
奖励区一致=True，配置观察成功=True。
~~~

以上是**预期示例格式，并非已取得的真机日志**。

字段含义：

| 字段 | 怎样看 |
|---|---|
| 调用记录 / CaptureSerial | 本进程报告次数；增加代表有新的 Hook 诊断记录 |
| 上下文状态=2 | 寄存器地址或长度不符合检查 |
| 上下文状态=3 | RaidPoint 标识或整块源／目标数据不匹配 |
| 上下文状态=4 | 源／目标一致，但还没有配置 |
| 上下文状态=5 | 源／目标一致，但当前配置损坏／竞态 |
| 上下文状态=6 | 源／目标一致，Native 成功**只读观察**本次配置 |
| PublishedRevision | PC 最近成功提交的版本 |
| ObservedRevision | Native 最近成功观察的版本；可能落后 |
| 奖励区一致=True | 限于本地只读快照校验，不代表发奖 |
| 配置观察成功=True | 必须状态 6，且发布/观察版本相同且非零 |

注意：未触发 Hook 时，已发布版本可能是 N，而观察版本仍是 0 或上一版；**这是正常且重要的区分**。

### 7.3 游戏不重启，切换配置 B

1. 保持游戏运行，回到 V4 窗口。
2. 编辑另一组道具 ID 和数量，点击“发布只读配置”。
3. 检查 `PublishedRevision` 递增。此时 `ObservedRevision` 可能还没有改变。
4. 在可控的下一场测试中再次触发房主开始挑战，点击“读取 Native 观察”。
5. 若新旧 Revision 相同、CaptureSerial 增长、上下文状态为 6，说明**这一次配置热切换被只读 Hook 观察到**；仍然没有执行奖励修改。

不支持在第一场已经序列化完成后，通过修改 Mailbox 追溯改变已发送的数据。

## 八、取消启用与彻底恢复

**只停用候选配置**：取消窗口的“启用配置（只读）”，保留需要的表格行，再点“发布只读配置”。这样 Native 下次可观察到 Enabled=0 的配置，但不会卸载 Hook。对于目前 N1 来说，启用与关闭都不会修改游戏奖励。

**彻底卸载研究模块**：

1. 完全退出游戏。
2. 从 Switch SD 卡的对应游戏 ExeFS 目录撤下本次 N1 `subsdk9`；仅恢复你亲自备份的原始文件，不要误删其他 MOD。
3. 如曾改动 NPDM，恢复原有正确版本；若从未改动则不要碰。
4. 重启游戏，确认原版行为正常。PC 的 `mailbox-v4-locator.json` 和本地校验副本可保留作资料，但将不再能够定位运行中的 N1 邮箱。

不要试图在游戏运行时直接删除 SD 卡中的 NSO 文件来“卸载”已加载模块；那不等价于停止已加载代码。

## 九、故障排查

| 现象／错误 | 常见原因与处理 |
|---|---|
| 主界面找不到“V4 只读” | 下载了原版 main 或旧 Windows Artifact；确认从 V4 新分支成功 CI 下载 |
| `FileNotFoundException` / 找不到 locator | `mailbox-v4-locator.json` 没有放到 EXE 同目录 |
| 找不到 `subsdk9` | 必须同时把**Native 包的 NSO 副本**放在 PC EXE 目录作配对校验 |
| `V4_MANIFEST_NSO_SHA256_MISMATCH` | Manifest 与本地 NSO 来自不同次构建、文件被修改；重新放入同一 Native 包中的原样文件 |
| `V4_ONLY_VIOLET_400` | 当前连接不是《紫》4.0.0；确认 Title ID/游戏版本 |
| `V4_ELF_FINGERPRINT_MISMATCH` | 原始指令不匹配，可能游戏版本不同或与其它补丁冲突；不要强行安装 |
| `V4_HOOK_NOT_INSTALLED` | 游戏中未加载本 N1 Hook，或实际 Hook 格式不匹配；检查 SD 路径/模块冲突/重启 |
| `V4_EXLAUNCH_ENTRY_MISMATCH` | 找到的指令不符合固定 exlaunch Entry 布局；停止自动定位，核对构建 |
| `MAILBOX_V4_HEADER_MISMATCH` | 推导的地址不是本版邮箱，或 Native 未准备好；不要猜地址 |
| `MAILBOX_V4_NOT_READONLY_READY` | Native 不在 Ready 且只读能力精确等于 1 的状态 |
| `MAILBOX_V4_SLOT_READBACK_MISMATCH` | 配置区写后读回不同；暂停实验，确认连接和对象地址有效 |
| `MAILBOX_V4_COMMIT_UNCERTAIN` | 发布过程不完整，不能把这次当作成功；检查连接，重新读取完整状态 |
| `ObservedRevision` 不增长 | Hook 未命中、上下文不合格、未有效提交；先查 CaptureSerial 和 ContextResult |
| ContextResult=2/3 | 现场数据或版本过滤假设不成立；记录并停止，不通过取消校验强行跳过 |
| Wi-Fi 断连 | 某些 LDN/无线状态会影响连接；优先保证环境稳定，必要时使用已验证的 USB 链路 |
| 游戏黑屏、闪退、插件冲突 | **停止测试并卸载研究覆盖**，恢复备份；不要反复尝试线上连接 |

### 推荐记录的实验表

| 场次 | 游戏版本/Build ID | 配置版本 | CaptureSerial 前→后 | ContextResult | Published/Observed | 源/目标 Hash | 是否通过 |
|---|---|---|---|---|---|---|---|
| A | 实机填写 | N | 实机填写 | 实机填写 | 实机填写 | 实机填写 | 待验证 |
| B | 实机填写 | N+1 | 实机填写 | 实机填写 | 实机填写 | 实机填写 | 待验证 |

如果要提交问题报告，最好包含：Windows CI Run 链接、Native CI Run 链接、Manifest（可以不发完整机器信息）、具体错误文本、是否存在其它 ExeFS 插件以及上表对应日志。不要公开设备敏感标识、账号凭据或私人联机记录。

## 十、离线编译与研究人员检查命令

Windows PowerShell 中使用 .NET 10：

~~~powershell
dotnet restore RaidCrawler.slnx
dotnet build RaidCrawler.slnx -c Release --no-restore
dotnet test RaidCrawler.Tests/RaidCrawler.Tests.csproj -c Release --no-build --filter "FullyQualifiedName~RaidCrawler.Tests.LiveRewardV4."
dotnet publish RaidCrawler.WinForms/RaidCrawler.WinForms.csproj -c Release -r win-x64 --self-contained false
~~~

Native 使用包含 devkitA64/libnx 的 devkitPro 环境：

~~~bash
export DEVKITPRO=/opt/devkitpro
bash switch/live-reward-v4/build.sh
~~~

独立核对自行提取的原始 ELF（仓库不带游戏 ELF）：

~~~bash
python3 switch/live-reward-v4/tests/audit_violet400_elf.py --elf /path/to/Violet4.0.0.elf
~~~

完整原理、CRC/位移及风险见 [TECHNICAL_ARCHITECTURE.md](TECHNICAL_ARCHITECTURE.md)。**目前唯一可认定的通过状态是“编译/离线测试”，Switch 实机仍需按第七节测试并保存证据。**
