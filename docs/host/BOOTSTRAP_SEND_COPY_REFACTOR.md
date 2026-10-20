# RaidCrawler host：Bootstrap 发送副本优先的重构（2026-10-20）

> **实现已提交；实机收发及保存尚未验证。** 本页覆盖旧的 `+0xE0` 奖励行解释和以堆扫描为主的路线。针对 **Violet 4.0.0**，不可外推其他 Build ID。

## 1. 路线选择：不再扫堆，也不修改 data.bin

真正的房主流程：

```text
FieldRaidJewelry.LotteryRewardItem（原版完整生成奖励）
    -> RaidPoint 存在源对象里
    -> main+0xE327FC 原版创建 0xAA0 Bootstrap 明文
    -> main+0xE32980 原版 memcpy(source RaidPoint -> outgoing copy)
    -> main+0xE32984 Native InlineHook（压缩前；本项目保留的明确入口）
       x20+0x58 = 源 RaidPoint；x23+0x6B8 = 输出 RaidPoint
       仅拷贝快照/检查；实验构建才可能在输出副本空槽追加
    -> main+0x1592B7C 原版序列化/LZ4 -> 0x2F
    -> 接收者按 subject=5 + Bag.GetNumItem(2481) 过滤
    -> 获胜后发奖，存档需单独验证
```

普通 CI Native **只读**。写入需要 `HOST_N2_EXPERIMENTAL=1` 独立编译，PC 端确认、原版只读命中、Native 构建能力位及一次性请求。原代码已有的实验限制不变：先机球 ID 15、数量 1–10、subject=5、最多追加一行。**本次未开放 20×999 实机写入。**

PokéLDN 自己是 PC/ESP32 模拟 LDN 房主，直接构造网络初始化消息；零售 Switch 本机房主不等价，所以沿用 Native 原版 memcpy 后的窗口，不照搬模拟房主的网络发送器。

## 2. 统一布局：修复历史四字节错位

根据当前 `Decryptu/pokeldn/pokeldn/sv/raid.py` 的 `POINT_REWARDS=0xE4`、Lua 的 45 次四字段写入和本仓库的原始 ELF 审计：

| 区域 | 以 RaidPoint 为基址 | 以完整 Bootstrap 为基址 |
|---|---|---|
| RaidPoint 本体 | `0..0x3E7` | `0x6B8..0xA9F` |
| 45 个奖励槽 | `+0xE4..+0x3B3` | `+0x79C..+0xA6B` |
| 非奖励保留区 | `+0x3B4..+0x3B7` | `+0xA6C..+0xA6F` |
| 星级/Boss 摘要 | `+0x3B8..` | `+0xA70..` |

每行 16B、4 个 little-endian u32：`[ItemId, Quantity, RareFlag, SubjectType]`。有些旧文档将 `+0xE0` 误标成奖励行，导致将前一个字段与本行三个字段错组为 `[source,item,qty,reserved]`，甚至把相邻行的 `subject=5` 解释成特殊空行。本次代码和测试已采用新的 ABI；`+0xE0` 仍可能包含**非奖励元数据**，必须保留。

**证据边界：** 此布局有公开 PokéLDN 生成器、Lua 写入侧及 donor 逆向材料交叉支持；这个提交没有新增《紫》4.0.0 真实游戏执行时的 N1 捕获。因此 Native 运行能力与地址有效性仍要由目标测试机验证。不能从纯静态证据声称联机发奖成功。

## 3. 代码改动

- `RaidCrawler.Core/Host/RaidPointLayout.cs`：起点 `0xE4`、终点 `0x3B4`；字段顺序统一成 item/quantity/rare/subject。
- `RaidCrawler.Core/Host/RewardAppendPlanner.cs`：允许原生 RareFlag=1 行；禁止不连续、未知、部分填充行；全部新增条目一次校验，通过后才写候选数组。
- **`RaidCrawler.Core/Host/BootstrapSendCopyPlanner.cs`（新增）**：离线复制完整 `0xAA0` 输出；先要求源 RaidPoint 与输出中整块 `0x3E8` 字节一致，随后仅替换后续空奖励槽；原本队伍记录、Boss、奖励及摘要一字节也不动。
- `switch/host-probe/source/program/main.cpp`：保持 `main+0xE32984` hook，不搜索运行时 owner；捕获位移修正 `x20+0x13C` / `x23+0x79C`，先对照**完整 RaidPoint**，再有条件写入 **仅 Bootstrap 副本**；构建时写入门闩保持默认禁用。
- `RaidCrawler.Core/Host/HostCaptureVerifier.cs`：差异字段映射改为正确 ABI。
- `RaidCrawler.WinForms/SubForms/HostProbeWindow.cs` 与入口标签：把发送副本 Hook 放在主路径，旧 `SysbotOnly` 大范围扫描仅作为独立诊断保留。
- `RaidCrawler.Tests/Host/BootstrapSendCopyPlannerTests.cs`（新增）和受影响的旧回归：测试 45 槽、两行追加、RareFlag=1、Meta/PK9/summary 保留、完整源与发包副本不一致时拒绝、原数组不变。

## 4. 哪些成功，哪些还没成功

| 关卡 | 当前状态 |
|---|---|
| 静态 4.0.0 发送链、已知 Hook RVA | 上游 + 本项目 ELF 证据 |
| Native 主路径已改为只读发送副本及可选试验写入 | 源码修改已提交 |
| 离线完整 Bootstrap 多行追加模型 | 已编写代码和回归测试，需 CI 执行 |
| 自有 Switch 4.0.0 Native Hook 实际命中 | 待实机 |
| 执行实验编译并在 **同局** 读到追加后明文 | 待实机 |
| 0x2F 真正发给了另一台 Switch | 待接收端对照 |
| 护符接收者获得新增道具、原版奖励不减、存档重载保留 | 待双机闭环 |
| 20 行及 999 数量在 Native 端可用 | **尚未开放**，不可将离线原型当作实机支持 |
| 官方互联网 NSO 会话 | **未验证**；LDN 成功不能外推 |

`lastResult=20` 只表示**Native 曾向发包前的内存明文缓冲追加一条**，不是传输、到账或持久化证明。

## 5. 验收顺序

1. 核对 Violet Title ID `01008F6008C5E000`、游戏版本 `4.0.0`、Build ID `709bfd66115298640155fcc4979dba151c7cc79a` 与同次编译的 `mailbox-locator.json`。
2. 主界面使用“Bootstrap 发送副本”，经 `GetMainNsoBaseAsync`、已知 Hook 跳板及 Manifest 定位邮箱；**不执行 heap scan**。
3. 开启原生只读捕获，在真实房主开战时记录计数、源/输出一致、前后摘要；无捕获或地址指纹不匹配即停止。
4. 用只读 preview 确认连续有效行与容量；再在授权、可恢复测试机用单行实验模式进行写入；必须保留所有原始记录。
5. 分别由有/无晶耀护符的参与者验证收到的 `0x2F`，比较奖励展示、Bag 净增、保存完毕、完全退出重新进入后的数量。

## 6. 参考

- [PokéLDN 当前 SV Raid 研究](https://github.com/Decryptu/pokeldn/blob/main/docs/sv_raid.md)
- [PokéLDN raid.py](https://github.com/Decryptu/pokeldn/blob/main/pokeldn/sv/raid.py)
- [本项目 4.0.0 ELF/N1/N2 历史审计](ELF_REVALIDATION_N1_N2.md)
- [本项目旧堆扫描说明（现为诊断，不是推荐主线）](FAST_TCP_AND_POINTER_FIRST.md)
