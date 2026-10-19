# 《紫》4.0.0 — RaidCrawler Windows 原生奖励 Hook 实机测试

**状态：实验性。ELF/LLVM 静态检查已完成；在你的 Switch 上的执行命中、奖励领取及保存仍未验证。**
**限制：现有 sys-botbase 即可；无需 Skyline/NRO；不要求重启才能修改下一场奖励。**

## 准备
1. 给研究机的游戏存档做独立备份；退出官方互联网联机及任何正在进行的团战。关闭其他向同一进程写内存的软件。
2. 从 [benefit 的 GitHub Actions](https://github.com/969981/RaidCrawler/actions?query=branch%3Abenefit) 选择**最新成功**的 .NET Core Desktop 构建，下载 Artifacts 的 RaidCrawler Windows 程序。勿使用旧 Skyline 发布包。
3. 启动《紫》4.0.0（TitleID 01008F6008C5E000），原版 sys-botbase 2.5 保持连接。Build ID 前缀必须为 709bfd6611529864。
4. 打开全局奖励编辑器。首次建议只配置一条合法的常见物品奖励，Quantity 5、Rare 0、Subject 0。Subject 5 要求有晶耀护符，首次测试不建议选 45 行 ×999。

## 安装和启用
1. 点奖励编辑器的 **写入奖励**，保存本地模板并打开诊断窗口（不会自动修改 Switch）。
2. 先点 **Hook 状态**；初次应显示 Empty。如显示 ConflictOrIncomplete，停止，不要强制覆盖。
3. 在游戏没有开房、开战或结算时，点 **安装（默认停用）**，确认风险。成功应显示 InstalledDisarmed，epoch=1。表示 880B 跳板和 4B 入口已核对，**尚不是 Hook 命中证明**。
4. 点 **启用**，应显示 InstalledArmed，epoch=2。再点状态复核。

## 首次验证
1. 离线开始一次能完成的本地团体战，核对奖励画面中是否出现新设定的物品；提前记录该物品背包原数量，战斗结束后比较。
2. 正常保存并在适当时机重新载入游戏，再看数量是否保留。奖励画面、背包增量、重新加载后保留，是三个不同的验证。
3. 当前 880B 跳板没有安全的可写命中计数，RaidCrawler 的 hook-memory-confirmed 日志**只表示内存读写成功，不表示 HOOK_HIT**。如果没出现奖励，仍不能断定哪一段没生效；可记录玩法、时间、日志及前后数量继续排查。
4. 官方互联网房间和参与者是否会采用房主提供的奖励数据尚未验证；不建议在正式联网场景进行首次试验。

## 无重启热更新
1. 游戏保持运行。关闭诊断窗口，回到奖励编辑器修改奖励，然后再点 **写入奖励**；打开新的诊断窗口。
2. 点 **Hook 状态**，确认仍为 InstalledArmed。
3. 点 **无重启更新**。Windows 先发布奇数 epoch，写入并回读 720B，再发布新偶数；示例 2→4。下一次走到相应 serializer 的团战才可能采用新模板。
4. 更新同时发生的在途回调由原生代码检测 epoch 变化并恢复源 RaidPoint 奖励。该并发机制只做了静态核验，尚需实机测试。不要让其他工具同时写入这块内存。

## 停用、卸载与故障恢复
- **停用**：epoch 切为奇数；新回调跳过自定义奖励。要继续编辑/使用时再启用。
- **卸载入口**：先停用并还原 main+0xE32984 的原始指令。为保护可能仍在执行的 CPU，刻意保留 880B 跳板；同一个游戏进程卸载后不得再点安装，重启才能清除这部分残留。想反复开关请用停用/启用，不要卸载。
- 操作超时、游戏闪退或显示 ConflictOrIncomplete 时，**不要反复安装**。保存诊断 JSONL 日志和屏幕信息；必要时重新启动游戏恢复原始进程。
- 诊断窗口会显示日志路径，或点 **打开诊断目录**。

## 代码和验收范围
- native/sv_remote_code_patch/stub.S：游戏端 ARM64 v2 读者、epoch 前后校验、冲突回滚。
- native/sv_remote_code_patch/build_offline_plan.py：精确 ELF SHA/入口、+0x8C 返回跳转修正。
- RaidCrawler.Core/Structures/RemoteViolet400HookPayload.cs：WinForms 内嵌对应机器码。
- RaidCrawler.Core/Structures/RemoteViolet400HookInstaller.cs：安装、启停、状态、恢复入口。
- RaidCrawler.Core/Structures/RemoteRewardEpochUpdater.cs：720B 版本保护热更新。
- RaidCrawler.WinForms/SubForms/RaidRewardDiagnosticForm.RemoteHook.cs：用户主动点击的控制界面。

**完成验收至少要分别核验**：跳板/入口读回、真实游戏回调执行、接收端奖励列表、奖励领取、背包增量、存档保留。当前程序只自动完成第一类检验。
