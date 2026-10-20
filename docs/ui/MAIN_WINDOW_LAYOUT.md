# RaidCrawler 主窗口布局优化（WinForms）

> 2026-10-20 · `host` 分支 · 仅涉及桌面程序 UI，**不修改** sys-botbase 协议、数据块解析、N1/N2 内存逻辑、事件回调、原版太晶坑功能。

## 原界面问题

原版 `MainWindow.Designer.cs` 使用绝对坐标，原始 `ClientSize=580×357`、`FixedDialog`、`MaximizeBox=false`。在中文界面加入 Host Probe、纯 sys-botbase 后，新增按钮位置分别位于 X=222、368，和右侧数据区重叠；中文标签及高 DPI 放大可能使部分文本溢出。宝可梦信息仅占右侧约 280px，招式区碰到左边栏。

## 最新实现

[MainWindow.ResponsiveLayout.cs](../../RaidCrawler.WinForms/MainWindow.ResponsiveLayout.cs) 将已有 WinForms 控件**重新挂到 TableLayoutPanel/FlowLayoutPanel**，不替换控件或事件：

| 区域 | 布局 |
|---|---|
| 左侧「设备连接」 | Switch IP / USB 端口根据已有 Protocol Visible 逻辑切换；连接/断开同一行 |
| 左侧「坑位浏览」 | 匹配数量、上一坑/下拉索引/下一坑 |
| 左侧「太晶坑操作」 | 读坑、导出、截图、活动数据、地图、奖励等6个原功能 |
| 左侧「筛选与自动化」 | 编辑筛选、开关、团战力、日期推进/停止、时间工具、设置 |
| 左侧「房主研究工具」 | 纯 sys-botbase 优先，Native 仅标注为需插件的研究工具 |
| 右侧「当前坑位」 | 当前活动标签、太晶属性图标、宝可梦图像与标题 |
| 右侧「宝可梦与战斗信息」 | 种子、EC、PID、宝可梦、特性、太晶属性、星级、性别、性格、个体值、区域、4个招式 |
| 底部 | 原版 `StatusStrip` 与日期推进状态保留 |

- 窗口可缩放/最大化，默认逻辑尺寸约 1070×690；左栏和详情区各自可滚动，详情随可用宽度拉伸，避免标签挤在输入框上。
- 使用 Windows DPI 缩放的 `DeviceDpi` 计算默认尺寸和行高；使用 `Microsoft YaHei UI` 与统一留白、对齐和淡色背景。
- `InputSwitchIP`、原 `ButtonConnect`、`groupBox1` 的6个按钮、`Seed`、`Species`、`Move1..4` 等都**还是原控件实例**，从而保留所有 `.Click`、`.Visible`、`.Enabled` 更新。
- `MainWindow.Designer.cs` 保持不变（便于与 upstream 合并），只在 `MainWindow.cs` 构造函数添加一行 `InitializeResponsiveMainLayout(...)`。

## 验收清单（运行前后必须测试）

1. Windows 100%、125%、150% 文本/屏幕缩放；最小窗口、默认尺寸、最大化及左右栏目滚动。
2. WiFi 与 USB 切换：IP/USB 标签、输入框及连接按钮的显示、自动保存/连接状态。
3. 没连接时不可执行的操作保持禁用；连接后读取太晶坑可用，其他6个按钮响应同原版。
4. 种子/EC/PID、宝可梦种族、性格、特性、晶属性、4个招式、活动坑图标在读坑前后的位置与文本正确，不再遮挡。
5. `StopAdvance_Button` 和 `ButtonAdvanceDate` 使用同一个可见槽位，日期推进中应能正确显示停止按钮，停止后恢复。
6. 纯 sys-botbase 入口不要求 Native 模块；Native 探针按钮单独保留，不能混淆两条实现路线。
7. 此次 GitHub Windows CI 的 Build/Host 单测验证**源码可编译**；真正窗口的 UI 与缩放仍建议在桌面实机截图核对。

若有高 DPI 特定机型的控件错位，记录 Windows 缩放比例与窗口尺寸，并调整 `MainWindow.ResponsiveLayout.cs`。无需触碰游戏内存 Hook、奖励数据或原版 C# 业务事件。
