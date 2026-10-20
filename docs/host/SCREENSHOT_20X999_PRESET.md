# 用户截图奖励预设：20种各×999（纯 sys-botbase 方案 A）

> **2026-10-20**：完整奖励行已勘误为 `[subject/source,item_id,quantity,reserved]`，原版记录起点 `+0xE0`。新增 `subject=0` 全体玩家候选模式（另保留 `subject=5`）。零售版收奖尚未验证，参见 [subject0 技术记录](POKELDN_SUBJECT0_RETAIL_LAYOUT_TEST.md)。


> 2026-10-20，**代码实现 ≠ Switch 发奖成功**。本预设按截图原顺序忠实编码20条奖励，`RareFlag=0`、`Subject=5`。只有原版奖励占用槽数 **≤25** 时，45行总表才足以完整追加；否则**零修改直接拒绝**。不会覆盖原本奖励，也不会分批投递。
>
> **仅《紫》4.0.0 的实验性候选对象写入**：游戏实时发包对象与地址稳定性尚未实机验证；《朱》 ELF 尚不可核验，当前拒绝朱版内存写入。需要持有的是**晶耀护符（Glimmering Charm）**，并非闪耀护符（Shiny Charm）；仅通过 `Subject=5` 字段不能保证实际筛选/入包生效。

## 截图精确道具清单

| # | 道具（截图） | 英文 | 十进制 Item ID | 数量 | Subject |
|---:|---|---|---:|---:|---:|
| 1 | 速度球 | Fast Ball | 492 | 999 | 5 |
| 2 | 等级球 | Level Ball | 493 | 999 | 5 |
| 3 | 诱饵球 | Lure Ball | 494 | 999 | 5 |
| 4 | 沉重球 | Heavy Ball | 495 | 999 | 5 |
| 5 | 甜蜜球 | Love Ball | 496 | 999 | 5 |
| 6 | 友友球 | Friend Ball | 497 | 999 | 5 |
| 7 | 月亮球 | Moon Ball | 498 | 999 | 5 |
| 8 | 竞赛球 | Sport Ball | 499 | 999 | 5 |
| 9 | 银色王冠 | Bottle Cap | 795 | 999 | 5 |
| 10 | 金色王冠 | Gold Bottle Cap | 796 | 999 | 5 |
| 11 | 大师球 | Master Ball | 1 | 999 | 5 |
| 12 | 神奇糖果 | Rare Candy | 50 | 999 | 5 |
| 13 | 经验糖果 XL | Exp. Candy XL | 1128 | 999 | 5 |
| 14 | 巨大金珠 | Big Nugget | 581 | 999 | 5 |
| 15 | 苦味料 | Bitter Herba Mystica | 1907 | 999 | 5 |
| 16 | 辣味料 | Spicy Herba Mystica | 1908 | 999 | 5 |
| 17 | 酸味料 | Sour Herba Mystica | 1906 | 999 | 5 |
| 18 | 甜味料 | Sweet Herba Mystica | 1904 | 999 | 5 |
| 19 | 咸味料 | Salty Herba Mystica | 1905 | 999 | 5 |
| 20 | 特性膏药 | Ability Patch | 1606 | 999 | 5 |

20条 × 999 = **19,980 个道具的名义总数量**（不是单个物品的实际获奖数量，也不证明能全部入袋）。

**来源校验：**《朱／紫》PKHeX `ItemStorage9SV.cs` 的 Balls/Other/Ingredients 分类、[Bulbapedia Gen IX Index](https://bulbapedia.bulbagarden.net/wiki/List_of_items_by_index_number_in_Generation_IX) 与用户截图原文交叉核对；巨大金珠为 **581**，不要误用普通金珠；经验糖果 XL 为 **1128**，竞赛球为 **499**，五种秘传调味料为 **1904–1908**。

## 编辑器实现位置

- [完整固定预设及45槽规划](../../RaidCrawler.Core/Host/ScreenshotRewardPreset.cs)
- [C# 仅靠 sys-botbase 写入/读回/恢复](../../RaidCrawler.Core/Host/SysbotOnlyScreenshotPresetWriter.cs)
- [简体中文预设窗口](../../RaidCrawler.WinForms/SubForms/SysbotOnlyScreenshotPresetWindow.cs)
- [单元测试](../../RaidCrawler.Tests/Host/ScreenshotRewardPresetTests.cs)

## 点击路径与安全验收

打开 host Windows 程序、连接已有 sys-botbase（USB/Wi-Fi），主界面选：

**纯sys-botbase → 虚表定位（只读） → 截图奖励20种×999（实验）**

在独立测试 Switch 里输入**真实序列化对象的绝对起点**（不是 RaidPoint 内嵌区域起点），勾选两项确认，依次：

1. **只读准备20条**：强制核对《紫》4.0.0、序列化函数12指令、三虚表、房间/参与者/非战斗候选状态，完整读取对象两次并核对一致，再规划20条；如果原有26条及以上、槽位中有碎片/数据不明、或者总容量超过45条，直接拒绝。
2. **写入全部20条（实验）**：准备凭证15秒有效；写前重新读取全部 `0x3E8` 原始字节且重新验证状态，然后一次 `WriteBytesAbsoluteAsync` 写入 **20×16=320B**；写后完整读回，必须与预期 byte-for-byte 相同才报告候选RAM成功。
3. **安全恢复20条**：仅当整个候选对象仍与写后完整映像完全一致才写回原来的320字节，不盲目覆盖游戏后续生成的奖励。恢复并不能撤回已发送的网络包或游戏保存。

## 999 数量的三个独立上限（不要混淆）

- **记录存储宽度**：奖励行 `Quantity` 是 uint32，`999` 在结构上可存储。
- **游戏奖励生成/筛选/发放上限**：实际 `subject=5` 结算是否接受数量999、是否按不同道具类型限额、是否存在某一阶段截断/重算，**尚未验证**。
- **背包单种堆叠上限**：PKHeX 对《朱／紫》多数道具分类公开上限为999；若领取者本来已有该道具，通常不能因为本次奖励数量写为999就超过背包上限。以实机入袋和重启保存为准。

因此 **"RAM对象已按预设修改" 不是 "参与者20种各获得999"**。实机最少核验：同一局真实房主 source 到 Bootstrap 是否使用已修改对象；无晶耀护符 vs 持有晶耀护符对照；各类道具显示/入袋数量与背包现有数量；获奖后保存并重启确认。

所有版本逻辑仍属于方案 A 的风险可控实验，**不安装 SD 卡 Native/subsdk9**。如果游戏在读取前重新生成或覆盖对象，预设不会自动替你 Hook 原生发送函数，仍须继续研究实际对象定位与发送时序。
