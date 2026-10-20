# PokéLDN 全体奖励（subject=0）对照与 RaidCrawler 纯 sys-botbase 实验

时间：2026-10-20 · `host` · 适配《紫》4.0.0，仅测试机验证 · **未证明实际参与者到账**

## 公开实验与原理

参考固定源 [Decryptu/pokeldn @ 8b2c19c](https://github.com/Decryptu/pokeldn/tree/8b2c19c480400a6805eeeff266e13ded20c97475/docs/research/sv)：

- [SV_RAID_BOOTSTRAP_ENCODER.md](https://github.com/Decryptu/pokeldn/blob/8b2c19c480400a6805eeeff266e13ded20c97475/docs/research/sv/SV_RAID_BOOTSTRAP_ENCODER.md)：PokéLDN 自有房主/无线链路重组 Pia 序列 11/12、解开 Raw-LZ4 后修改完整 `0xAA0` 字节明文，重新压缩并下发 `0x012F`。这不是正版 Switch 房主的原地写内存。
- [SV_RAID_REWARD_PROFILES.md](https://github.com/Decryptu/pokeldn/blob/8b2c19c480400a6805eeeff266e13ded20c97475/docs/research/sv/SV_RAID_REWARD_PROFILES.md)：其历史 donor exact-list 编辑器先核对 19 条关联记录及 2 条独立奖励，再**清除这些记录并重建**；采用 `{source=0, item_id, quantity, reserved=0}`。文档报告零售机曾实际收到先机球×500，还报告过切换为高级球／经验糖果 XL 各42个的实验，但均是 PokéLDN 虚拟房主重放模式。
- 公开研究认为 source/condition 字段 `0` 是无需晶耀护符的常规奖励来源，其他来源可能包含参与者/房主/料理/晶耀护符筛选。具体实际网络接收路径尚未由本项目实机重现，因此本项目 UI 称 **`subject=0` 全体候选、`subject=5` 晶耀护符候选**，不是已经通过真实网络端到端验证的主机事实。

## 致命布局勘误（2026-10-20）

旧版 RaidCrawler 一度误把 **第一个道具 ID 在 `RaidPoint+0xE4`** 当成**第一个完整记录起点**，进而错误写出 `[ItemId, Quantity, RareFlag, Subject]`。但 PokéLDN 的独立实机供体显示实际记录为：

```c
// Violet4.0.0 retail donor, first record in decoded 0xAA0 Bootstrap
// RaidPoint begins at decoded+0x6B8
// FIRST RECORD starts at RaidPoint+0xE0 = decoded+0x798
struct RewardRecord {
    uint32_t condition_or_source; // subject (0 all-player candidate; 5 charm candidate)
    uint32_t item_id;             // at RaidPoint+0xE4 = decoded+0x79C
    uint32_t quantity;
    uint32_t reserved;            // zero in validated donor records
}; // sizeof 16
```

**注意重叠陷阱：** 把源字段当作上一记录的尾字段，可能错误地认为最后的 source=5/item=0 条目只是“上一条奖励 subject=5”，而 PokéLDN 研究里某些来源标记／独立奖励／定义记录有特殊用途；**不能在未能准确判定所有原有记录的情况下把它当成空槽覆盖。**

旧文档中所谓“`RaidPoint+0xE4` 是记录起点”的表和测试不再具有写入指导意义。已修正生产路径：

| 项目 | 正确地址 |
|---|---|
| RaidPoint 在 Bootstrap 中的开始 | `+0x6B8` |
| 第1个 16B 记录开始（source） | RaidPoint `+0xE0` = Bootstrap `+0x798` |
| 第1个 item_id | RaidPoint `+0xE4` = Bootstrap `+0x79C` |
| 45×16 B 边界（独占） | RaidPoint `+0x3B0` = Bootstrap `+0xA68` |
| 序列化 owner 内嵌 RaidPoint | `x20+0x58` |
| 序列化 owner 内奖励记录 | `x20+0x138`（不是 item_id 的 `x20+0x13C`） |

修正见 [RaidPointLayout.cs](../../RaidCrawler.Core/Host/RaidPointLayout.cs)、[RewardAppendPlanner.cs](../../RaidCrawler.Core/Host/RewardAppendPlanner.cs)、[HostCaptureVerifier.cs](../../RaidCrawler.Core/Host/HostCaptureVerifier.cs)、[compare_capture.py](../../tools/host/compare_capture.py)，含 C# 和 Python 字节级单测。可选研究用 Native 文件也改了 capture 偏移，但**默认 Windows 程序不包含 Native 插件，仍推荐纯 sys-botbase**。

## 已开发两组 user-triggered 写入实验

**A. 20种截图道具×999 全体候选**

主程序：`纯sys-botbase → 截图奖励20种×999（实验） → 选择 全体玩家（subject=0）`。完整20×16=320B，数据行格式 `[0, ItemId, 999, 0]`。`subject=5` 原模式也保留。只要原奖励中有 source-only 模糊行、洞、或剩余不足20条就**整个拒绝、不写入**；没有删除原版料理或奖励定义。这与 PokéLDN 的 donor-specific exact-list **删除重排原始21条**的实现有意不同，避免对真实房主未知内容做破坏。

**B. PokéLDN 单行对照：先机球×500，subject=0**

`纯sys-botbase → 受控奖励写入（实验） → 全体玩家（subject=0） → 先机球×500`。只改第一个真实空槽 16 B：`00 00 00 00 0F 00 00 00 F4 01 00 00 00 00 00 00`。这是**测试参数复刻**，而非网络模拟主机的原版 exact-list 算法复制。`subject=5` 单行测试仍限1～10。

两者都用同一现有 sys-botbase 的 `ReadBytesAbsoluteAsync` / `WriteBytesAbsoluteAsync`：4.0.0 title/version/opcode+owner虚表核查、原始对象双快照、游戏房间双状态检查、15秒凭证、写前完整比对、写后完整读回、仅完全一致才能有条件恢复。**没有原子 compare-and-swap、写入依旧可能与游戏线程竞争。**

## 下一步真实零售房主验收

- 非公开、可恢复本地测试 Switch，两台参与端分别无晶耀护符/有晶耀护符，对同一坑记录状态 S0～S4。
- 先测试*单条*先机球×500／subject0，原版奖励保持不变；若模糊记录导致拒绝，不要强制启用覆盖模式。
- 必须确认地址是**当前房主下一次用来序列化的 source RaidPoint**，原版可能在开始战斗前重新生成覆盖这个候选实例。
- 最终在真实接收者验证奖励出现、背包实际增加的数量及保存重启后保留；仅内存读回成功 ≠ 游戏发出 ≠ 客户端接受 ≠ 背包持久化。
- 只有单条闭环通过，才尝试20条全体×999；它可能碰到数量限制或奖励UI/背包上限。

截止本次开发，仅完成 GitHub 代码/CI 离线验证，**没有实际控制用户的 Switch，也未证明任何奖励已经到账**。
