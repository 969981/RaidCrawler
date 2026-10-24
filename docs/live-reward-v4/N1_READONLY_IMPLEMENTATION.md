# 第二阶段：V4 N1 只读 subsdk9 — 编译、操作、验证手册

> 范围：仅《宝可梦 紫》4.0.0，Title ID 01008F6008C5E000。
> 预期 Build ID 709bfd66115298640155fcc4979dba151c7cc79a。
> **当前实机状态：未验证。没有修改奖励的代码；仅在 PC 主动提交时写 Native 自有邮箱。**

## 1. 本轮具体完成

- switch/live-reward-v4/source/main.cpp：从 main 重新实现的 exlaunch N1 只读 Hook，不导入旧 host 分支。
- 目标 RVA main+0xE32984（历史 ELF 指令点），启动时验证 17 条指令的 Opcode Guard。
- Hook 只读取本次现场 X20/X23、0x3E8 RaidPoint，验证完整源/输出一致，然后计算两份 720B 奖励行 FNV32。**从不改变这些内存。**
- 调用 V4 ObserveReadOnly 读取当前活动配置槽、CRC/Revision，写入只读观察结果。
- switch/live-reward-v4/build.sh：固定 exlaunch 提交 f9f4b0dd07b68f97958cb9c79228bbca22ca80d5，使用 devkitPro 编译 NSO 和 npdm；**没有可开启发奖的构建开关**。
- gen_manifest.py：从同次编译 ELF 提取 RaidV4ReadOnly::Callback 与 rcv4::g_mailbox 符号差，使用具体 subsdk9 SHA256 约束清单。
- RaidCrawler.Core/LiveRewardV4/MailboxV4Locator.cs：sys-botbase 校验版本、九条未修改的邻近 Opcode、exlaunch Entry、manifest，再读取 RCHMB04 Header；拒绝未知构建。
- MailboxV4Observation.cs：通过 generation 双读解析只读捕获统计和奖励 FNV32，状态不能当作网络发奖成功。
- RaidCrawler.WinForms/SubForms/LiveRewardV4Window.cs：编辑道具、默认数量 999、启用、无损追加、持续观察；自动定位、发布配置、读观察。
- MainWindow.cs：在主窗口底部新增 **V4 只读** 入口；不影响旧 RaidCrawler 功能。

## 2. 游戏端内存及观测数据

原始发送链：main+0xE32980 执行 memcpy → main+0xE32984 Hook → 后续原版路径；只在进入已验证的同步点时读取当次 X20/X23。

- 输入 RaidPoint = X20+0x58，长度 0x3E8。
- 输出 RaidPoint = X23+0x6B8，长度 0x3E8。
- 源奖励 = X20+0x13C，输出奖励 = X23+0x79C，都是 720 B。
- N1 的 main.cpp 只做 strcmp/memcmp/FNV、ObserveReadOnly、写入自身邮箱状态，绝不修改游戏奖励。
- 完整性失败或上下文不合格时只记录拒绝结果。

V4 mailbox 的 Header、双缓冲见 README.md。新增 Footer 只读统计位于 Mailbox+0x7C0：

| 偏移 | 类型 | 说明 |
|---|---|---|
| +0x7C0 | u32 | 捕获序号，仅本进程计数 |
| +0x7C4 | u32 | 捕获状态 |
| +0x7C8 | u32 | 源奖励 FNV-1a 32 |
| +0x7CC | u32 | 输出奖励 FNV-1a 32 |

捕获状态：1 预留/正常；2 现场指针或长度不合格；3 RaidPoint 不匹配；4 当前没有已提交配置；5 配置校验/并发失败；6 只读配置观察成功。

Header 中 ObservedRevision 只由 Native 的 ObserveReadOnly 更新；PC 提交成功并不等于 Native 已观察。Header Generation 位于 +0x38，Native 写 Footer 时奇偶切换；PC 读取 Footer 前后须确认 Generation 与 ObservationCount 未变化。

## 3. 构建

推荐 GitHub Actions [live-reward-v4-native.yml](../../.github/workflows/live-reward-v4-native.yml)。本地编译：

    export DEVKITPRO=/opt/devkitpro
    bash switch/live-reward-v4/build.sh

使用 devkitpro/devkita64 环境、devkitA64、libnx、git、make、Python 3。
成功后 switch/live-reward-v4/out/：

- subsdk9：**只读实验性 Native NSO**。
- main.npdm：生成的配套元数据，**禁止未经对比覆盖现有同名文件**。
- mailbox-v4-locator.json：与该 NSO 同次生成（含具体 SHA256 和地址符号差）。
- SHA256SUMS.txt、README-INSTALL.txt。

CI 不自动上传到 Switch，不包含 FTP 发布功能。Artifacts 标记 READONLY-UNVERIFIED，只能作为实验材料。主项目打包不会把 Native 附件默默混入。

## 4. 自有实验机手动验证

1. 备份数据与现有 ExeFS 覆盖；确认 Switch 游戏是《紫》4.0.0 且 Build ID 精确一致。**仅靠 17 条指令匹配不足以证明真实 Build ID。**
2. 完全退出游戏；检查有无现有 subsdk9 / main.npdm 冲突，不要覆盖 Skyline 或其他插件；如需安装，本地手动将配对 NSO 放在 atmosphere/contents/01008F6008C5E000/exefs/subsdk9。
3. 按 exlaunch/Atmosphère 的实际 Loader 要求单独检查 main.npdm；不应将 CI 产物强制覆盖已有 NPDM。
4. 将同次 Native Artifact 内的 **mailbox-v4-locator.json 与原样的 subsdk9** 一起放在 Windows RaidCrawler.exe 同目录（这里的 subsdk9 仅用于**本地 SHA-256 配对校验**；真正加载到 Switch 的仍是 SD 卡上的模块）。Windows 和 Native 附件是分开下载的；不要混用不同构建批次的 manifest/subsdk9。启动游戏、连接 sys-botbase 后再操作。
5. RaidCrawler 主窗口底部选择「V4 只读」→「自动定位 N1」。若 Title/version/Opcode/Hook/Header 任一不匹配，工具必须拒绝。
6. 先使用少量道具行测试，点「发布只读配置」；界面应显示 PublishedRevision=N、ObservedRevision 尚可能是上一版。
7. 游戏中由自己担任房主，进入团体战并点击开始挑战。之后点「读取 Native 观察」：捕获序号增加、源/输出 FNV 一致，ObservedRevision 应为 N（若本次观察时配置有效）。
8. 不退出游戏，改用道具 B，再次「发布只读配置」，再开另一场。核对 Revision 增长且 Native 读取更新。
9. 恢复原版配置可取消 Enabled 并发布为 Disabled（该协议仍要求传输至少已指定的行才可再次启用）。
10. 退出游戏前备份日志；无论通过多少次只读测试，**均不得宣称实际发奖或可投入官方网络环境**。

## 4.1 两份 CI Artifact 的文件组合

Windows 工作流生成 RaidCrawler-V4-N1-PC-READONLY-UNVERIFIED；Native 工作流生成 RaidCrawler-V4-N1-Violet400-READONLY-UNVERIFIED。它们不是传统意义的正式发布，特别不能把 CI 构建成功当成 Switch 真机兼容性验证。

推荐测试目录：

    PC/RaidCrawler.WinForms.exe
    PC/mailbox-v4-locator.json
    PC/subsdk9                   # 仅用于本地文件配对哈希校验
    SD/atmosphere/contents/01008F6008C5E000/exefs/subsdk9
    SD/.../main.npdm             # 仅在明确确认此文件确实需要且兼容时使用

PC 执行“自动定位 N1”会校验 NSO0 头和本地 SHA-256。**该校验不能证明 SD 卡上的 subsdk9 和 PC 本地副本相同**；这是需要人工确认的安装步骤。后续仍要求校验 Title ID、游戏版本、原版 ELF 指令、exlaunch Hook 入口以及 RCHMB04 邮箱。任何检查失败，必须停止，不允许尝试猜测的地址。

本阶段默认不启动任何后台轮询或自动发布：由操作者手动点发布，点击“读取 Native 观察”获得结果。失去连接或改变游戏后，应重新“自动定位 N1”。

## 5. 已知风险和限制

- Hook 有能力拦截代码执行，**但本次 N1 不包含游戏数据写入**。不能把它误称“无 Hook 纯 sys-botbase”。
- Native 使用已知场景的静态 ABI，未在真实游戏上完成回调命中；如运行中对象不符，可能拒绝采集或触发异常，实机先在可恢复环境验证。
- PC manifest 的 Build ID 是预期值，设备端使用的是指令指纹；若未独立取得实际游戏 Build ID，不可据此推断完全匹配。
- callback 到 Mailbox 的地址依赖配对二进制，重新编译或切换游戏都必须换 manifest；禁止从旧 host v3 抄地址。
- A/B 双缓冲目前 fail-closed，不承诺任意高频/多 PC 并发或严格原子发布。实际奖励修改需要独立的状态机与实机验收。
- 只读 FNV 一致并不证明游戏真的发了包，更不证明远端玩家得到道具。
- 本地 LDN 可能导致 sys-botbase Wi-Fi 网络断开；已提交配置存在于 Native 自己的内存直到游戏进程结束。

## 6. CI 和验收

- Native NSO 编译：.github/workflows/live-reward-v4-native.yml。
- Windows .NET/编解码/原生 ABI 测试：.github/workflows/live-reward-v4.yml。
- 真机 N1 主机命中、两场跨配置热切换：仍需用户实验机实际运行；代码构建成功不等于设备已验收。
