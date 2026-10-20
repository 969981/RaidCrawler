# RaidCrawler host 简体中文界面说明

此分支的 Windows 客户端已进行简体中文汉化。**只修改用户可见的窗体文案和游戏资料语言，不更改 SysBot.Base 连接协议、太晶坑计算流程、配置文件格式、筛选序列化字段和 GitHub 上游主分支。**

## 已翻译的功能

- 主界面：Switch 连接/断开、读取太晶坑、编辑筛选、日期推进、地图、奖励、截图、USB/Wi-Fi 状态以及运行中提示。
- 程序设置：常规、匹配通知、日期推进、Webhook、实验性选项、关于。
- 筛选设置：宝可梦、形态、性格、星级、闪光、性别、太晶属性、个体值、奖励数量、批量筛选、筛选错误提示。
- 太晶坑详情、奖励列表、道具筛选、内存查看、时间工具、表情配置和版本更新提示。
- Host Probe：内存邮箱地址输入、读取状态、开始/停止捕获、原版奖励区域的比较和错误提示。
- 游戏资料：使用 PKHeX 提供的 `GameInfo.GetStrings("zh-Hans")`，展示简体中文宝可梦、招式、特性、性格、太晶属性和道具名称；地名也进行了中文显示。
- Discord Webhook 推送的宝可梦名称和内容栏标签也进行了汉化，**表情字典的英文键未修改**。

## 下载和使用

从 [`host` 分支 Windows CI](https://github.com/969981/RaidCrawler/actions/workflows/host.yml) 中打开**最新成功**的构建，下载 `RaidCrawler-host-experimental` Artifact，解压后运行其中的 WinForms 程序。该产物来自 `host` 分支，**不是 upstream/main 的正式 Release**。未安装 Windows .NET 桌面运行环境时需自行安装适用版本。

原有 `config.json` 和 `filters.json` 可以继续使用：配置中的 Game 值仍是 `Scarlet` / `Violet`，界面只是用 owner-draw 显示「朱 / 紫」；性别、宝可梦、性格和太晶属性下拉框均保留原始枚举/索引，避免旧筛选器失效。

## 局限和注意

- 当前是 **zh-CN 默认显示版本**，尚未设计英文/中文的运行时语言切换按钮。
- Windows UI、PKHeX 游戏资料和通知文案已覆盖主要路径；外部 sys-botbase、.NET、第三方库异常信息可能仍会返回英文，不属于客户端可保证的翻译文本。
- 仅通过 GitHub Actions 的编译/单测并不能替代手工截图检查。在小屏幕、高 DPI、长项目名称下仍建议检查控件文字是否被裁切。
- **Host Probe 的 N1 实机 Hook 命中、内存邮箱实际通信、联机奖励发放仍未得到实机验收**。界面汉化不等于功能实机通过。
- 本地调试不要同时运行会争用同一 sys-botbase 连接的自动扫描和 Host Probe。

## 维护

请保留代码和游戏协议使用的英文常量（如 `Scarlet`、`Violet`、`ItemId`、`GetTitleID`、`config.json` 和 Discord emoji keys），仅更新显示层与资源。

重要源码：
- [主窗口](../../RaidCrawler.WinForms/MainWindow.cs)
- [程序设置](../../RaidCrawler.WinForms/SubForms/ConfigWindow.cs)
- [筛选设置](../../RaidCrawler.WinForms/SubForms/FilterSettings.cs)
- [Host Probe](../../RaidCrawler.WinForms/SubForms/HostProbeWindow.cs)
- [游戏字符串](../../RaidCrawler.Core/Structures/RaidContainer.cs)
