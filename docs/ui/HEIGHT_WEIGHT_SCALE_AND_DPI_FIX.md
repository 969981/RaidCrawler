# 主窗体第二轮布局修复及 develop 体型字段移植

日期：2026-10-20，分支：`host`。

## 复现来源

用户提供的 1338×900 Windows 截图显示：原本已从 580px 固定坐标迁到左右分栏，但“宝可梦与战斗信息”仍在四列共享一个坐标网格，标签与输入框在高 DPI 下容易拥挤或出现视觉重叠。切换额外招式时，旧版 `Move_Clicked` 还会手动修改 `LabelMoves.Location.X`，与新的 `Dock` / `TableLayoutPanel` 布局互相冲突。

## 已修复

- 用左右两个**独立的标签/只读输入框二列子表格**显示每个属性；各字段内部固定足够的标签列宽，并使用百分比填充剩余列，不允许邻居字段抢占标签位置。
- 避免内层表格使用固定行高和 Padding 双重叠加，而改为一行百分比填充，降低 125%/150% DPI 裁切风险。
- `Move_Clicked` 仅在“招式 1：”和“追加招式：”之间切换文字，**不再按绝对坐标移动标签**；新增 118px 逻辑标签列来适配较长中文文本。
- 保持已有 `Seed`、`EC`、`PID`、`Species`、`IVs`、四个招式等控件原始实例及 Click 回调。

## develop 分支体型数据

直接参考 `develop/RaidCrawler.WinForms/MainWindow.cs` 的 `DisplayRaid()`：

```csharp
var scale = blank.Scale;
var height = blank.HeightScalar;
var weight = blank.WeightScalar;
```

旧 develop 曾以 `{height}m / {weight}kg / {scale}` 拼成单个字段，但 PKHeX `PK9.cs` 对这三个属性均明确定义为 **byte**：`HeightScalar`、`WeightScalar`、`Scale`。前两者是原始标量，不是实际米数/千克数。因此本轮以各自独立的只读数值字段显示，并在工具提示中注明“原始0–255标量”，不添加误导性物理单位。

窗口右侧新增“**身高 · 体重 · 体型**”分组，随每次 `DisplayRaid()` 根据本次 `raid.GenerateDataPK9(blank, ...)` 生成的 `blank` 更新。这三项属于当前坑位预测的宝可梦数据，不是 sys-botbase 实时读取宝可梦队伍或 HOME 的尺寸。

## 自测/验证

- GitHub Actions [host Windows 编译](https://github.com/969981/RaidCrawler/actions/workflows/host.yml) 用于确认编译和 Host 单测；**不能替代真实 Windows DPI 与宽度测试**。
- 桌面目测：100% / 125% / 150%；窗口最小化允许宽度、默认、最大化；中文名较长的种族、能力、性格、追加招式。
- 切换普通/追加招式，观察标签是否留在独立的列内。
- 读取两只不同体型的宝可梦，确认右侧三项数据随坑位更新；必须标注为标量，不能直接显示 m/kg。
- 原有“纯sys-botbase”与 Native探针入口、连接/断开、日期推进停止按钮逻辑必须保留。
