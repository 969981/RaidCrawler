# RaidCrawler benefit：任意坑晶耀护符奖励 P1 执行记录（2026-10-20）

状态：**已提交离线试验准备代码 + 单测 + CI；没有实现真实 Lua 运行时 Hook，未在 Switch 上发放道具。** 适用目标：用户本人的《紫》4.0.0；先单人离线，再分别验证本地联机和其他联网模式。禁止从静态地址、模板 SHA 或截图推断联网玩家收到奖励。

## 1. 精确目标

任何 Seed、帕底亚／北上乡／蓝莓学院以及普通／活动坑均从实际游戏本场奖励生成处处理；不要求 RaidCrawler 预先选择某个坑。保留**全部**原版固定、随机、团体战料理和原版晶耀护符太晶碎块奖励，另外增加自定义奖励（每行 ItemNo、ItemNum、rareType=0、subjectType=5）。资格由领取者本机的 Bag.GetNumItem(2481)>0 判断；不直接篡改背包/存档来冒充发奖。

图中的 20 种道具已经被 RaidRewardPresets.ScreenshotCharm999() 编码，依次为 ID：

    492 493 494 495 496 497 498 499
    795 796 1 50 1128 581
    1907 1908 1906 1904 1905 1606

每行数量 999、rare=0、subject=5。**999 是请求数量，不是已验证的实际库存净增**；还要验证 SlotMaxNum、既有库存和保存流程。现在的 CharmBonusProfile 和 RewardAppendComposer 只构建离线配置；它们不接管 Lua VM。

## 2. 本次直接复核的《紫》ELF

从用户已保存文件取得 Violet4.0.0.elf，实际字节数 **74,362,480**；SHA-256：

    d17403eaa9d844db82f647fd2f1518806b10c0518d53602fc0ac1766298fdba0

与现有审计的构建相符。ELF 内有 Lua 5.4 版本字符串，也有三处 LotteryRewardItem 文本，但上下文涉及 BCAT/资源文件名称，**并不能定位 Lua FieldRaidJewelry.LotteryRewardItem 的真实可写 Native 回调或当前 BattleRewardData[3] 对象**。未找到对应 Lua 方法 F316609DEA110601D、RaidResultAddItem 的 ASCII 明文，不能据此编造两个固定 AArch64 RVA。

目前文件库没有发现独立的原版 script/lua/bin/release/main/main.blua；SV-Script-RE@1f843bd 对 3.0.1 业务逻辑的公开反编译可作研究假设，但不是 4.0.0 真实运行时 PC/对象地址证明。

特别区分：原 Legacy main+0xE32984 是 0x2F **网络出站副本**的复制后窗口（x23+0x79C），不能作为单人发奖入口，也不是物品实际保存点；此前指令和内存模板回读成功不表示 CPU 曾命中这个入口。

## 3. 已提交的可运行 P1 离线阶段

- tools/research/prepare_violet400_charm_trial.py：可从图片的 20×999 预设，或读取原有 CharmBonusProfile JSON（校验 C# 格式 SHA256），生成**独立一行**测试 JSON + manifest。
- tools/research/test_prepare_violet400_charm_trial.py：验证 20 行 ID、预设未启用、首轮仅单个合法 ItemId×1、subject=5、防篡改及不覆盖原配置。与前期相同逻辑的隔离本机 Python 试验已通过 5 个 unittest；GitHub CI 验证以实际工作流结论为准。
- .github/workflows/charm-p1-trial-checks.yml：仅 Python 标准库的非写入 CI，不构建也不启用 Switch 原生 Hook，不产生发布版“已成功发奖”的误导。

使用：

    python tools/research/prepare_violet400_charm_trial.py --out-dir ./private-p1-trial
    python tools/research/prepare_violet400_charm_trial.py --source /path/to/charm-bonus-append.json --item-id 1 --out-dir ./private-p1-masterball
    python -m unittest discover -s tools/research -p "test_prepare_violet400_charm_trial.py" -v

默认试验为速度球 ID 492 ×1。生成 p1-single-charm-bonus.json（与现有 C# 档案字段/摘要一致）和 p1-trial-manifest.json（包含 ELF/BuildID、资格、阶段要求及 HookInstalled=false、RuntimeVerified=false）。**这些只是开发试验输入：不可直接当作 Switch 插件、RomFS mod 或 sys-botbase 指令使用。** 不写入活动文件 %LOCALAPPDATA%/RaidCrawler/charm-bonus-append.json，不自动改回现有 20×999 模板。

## 4. 真正的实时功能剩余任务与禁止跨越的 Gate

| 优先 | 工作 | 验收与当前状态 |
| --- | --- | --- |
| P1-01 | 在用户拥有/可研究的 4.0.0 代码上确认 Lua LotteryRewardItem 的函数身份、返回时机和 VM 线程/GC 生命周期 | **阻断**：需要真实 main.blua 或可复验的实机 Lua 闭包捕获；不能把原生奖励表初始化 RVA 当成 Lua 事件 |
| P1-02 | 实际单人战斗只读收集 A0：原版生成列表；B：subject 过滤后的新列表；C：结算实际读取列表 | **未完成**：Windows 静态内存扫描/手动标记不构成执行级捕获 |
| P1-03 | 在正确的 A0→B 阶段追加一条 subject=5、数量1，保留每个原版行，记录 A1=A0+1，失败回滚 | **未完成**：必须验证可用 Lua VM 调用上下文，不能对 Lua [3] 直接当 native +3 字节地址写 |
| P1-04 | 实测胜利奖励页面、Bag 前后增量、SlotMaxNum、SaveProcess 提交、重启读回 | **未完成**：仅看 ×999 截图或 RequestAutoSave 不能证明持久化 |
| P2-01 | 从 1→5→20 条，逐步到 ×999；覆盖 3–6星、活动与各地图 | **待 P1 通过**。多人 0x3E8 RaidPoint 上限 45 行；N+M>45 不静默删除原版奖励。Lua 本地列表容量不能沿用网络 45 行上限 |
| P2-02 | 单独验证 LDN 房主、参与者和官方互联网的收发字段、各自护符资格及背包/保存 | **待 P1 通过**；不能以本地主机成功代替另一玩家实收 |

运行时必须具有可检查的 Build ID、执行命中、完整原版奖励 A0、追加 A1、过滤 B、结算 C、Bag 差分和重启持久化，才能宣称“任意坑真正生效”。**当前不能把 20×999 直接启用为已验证功能。**

其他既有文档：violet400-glimmering-charm-append-redesign-plan.md、violet400-p1-solo-charm-append-lifecycle.md、violet400-abc-native-entry-and-lua-lifetime-audit.md；PokéLDN references 是网络协议资料，不是单人 Lua 注入方案。
