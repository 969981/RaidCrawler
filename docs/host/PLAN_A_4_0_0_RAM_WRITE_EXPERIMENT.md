# 方案 A：4.0.0 状态偏移核对 + 纯 sys-botbase 奖励 RAM 写入实验

> **2026-10-20 重要勘误（覆盖本页旧结论）**：PokéLDN 4.0.0 donor 的完整奖励记录从 `RaidPoint+0xE0` / `Bootstrap+0x798` 开始，四列为 `[condition_or_source, item_id, quantity, reserved]`；`+0xE4/+0x79C` 只是记录内部的道具 ID，不是完整行起点。以下旧版 `+0xE4 记录起点 / [Item,Quantity,Rare,Subject]` 的叙述**仅作为历史错误调查记录保存，不可作为写入指导**。最新版布局、subject0/5 和验证限制统一参见 [POKELDN_SUBJECT0_RETAIL_LAYOUT_TEST.md](POKELDN_SUBJECT0_RETAIL_LAYOUT_TEST.md)。


> 日期：2026-10-20。**本方案已经完成 C# 工程实现及离线字节审计，尚未在真实 Switch 上证明房主实际发送对象、奖励到账或重载存档持久化。**
>
> **不需要** `subsdk9`、`main.npdm`、额外的 Socket 服务、SD 卡模块、ARM64 Hook。所有 Switch 操作仅通过 RaidCrawler 现有的 `SysBot.Base.ISwitchConnectionAsync` 完成。

## 1. Hexbyt3 的版本核对：必须分别看版本声明与偏移注释

2026-10-20 读取 [hexbyt3/SVRaidBot 的最新源码](https://github.com/hexbyt3/SVRaidBot/blob/main/SysBot.Pokemon/SV/Vision/PokeDataOffsetsSV.cs)：

- 声明 `SVGameVersion = "4.0.0"`，所以工程确实已面向 **4.0.0 版本进行游戏校验**。
- 同一文件仍在 `TeraLobbyIsConnected = 0x043DF430`、`LoadedIntoDesiredState = 0x047D2020`、`LinkTradePartnerNIDPointer = [0x475EA28,0xF8,0x8]` 旁标注 **Zyro 3.0.0 / 3.0.0**。这些地址可能沿用至 4.0.0，但仅靠版本常量不能证明其 RVA 在新版本未变。
- [`RotatingRaidBotSV.cs`](https://github.com/hexbyt3/SVRaidBot/blob/main/SysBot.Pokemon/SV/BotRaid/RotatingRaidBotSV.cs) 使用这些地址通过 sys-botbase 读取房间（非零）、战斗（字节=2）、三个 NID 槽（非零）以辅助自身自动开坑和状态管理，不能将这些信号解释为“本局 RaidPoint 已经生成并可安全修改”。

## 2. 《朱》《紫》 ELF 覆盖范围

Library 中取得的用户原始 `Violet4.0.0.elf` 为 74,362,480 字节，Build ID `709bfd66115298640155fcc4979dba151c7cc79a`，SHA-256：

`d17403eaa9d844db82f647fd2f1518806b10c0518d53602fc0ac1766298fdba0`

再次通过 `llvm-objdump` 解码序列化函数：

```asm
main+0xE32814  mov x20, x0
main+0xE32964  ldp x11, x10, [x20,#0x58]
main+0xE32968  add x1, x20, #0x78
main+0xE32980  bl memcpy
main+0xE32984  mov x8, x21
```

因此实际输入运行时对象 `owner=x0/x20`，原生 `RaidPoint=owner+0x58`，`45x16` 奖励行从 `owner+0x13C` 起；输出 Bootstrap 明文奖励从 `x23+0x79C` 起。每行 `ItemId:u32,Quantity:u32,RareFlag:u32,Subject:u32`。该证据是**布局证明**，不证明 `owner` 稳定存活，也不证明直接写该对象会被下次网络序列化读取。

本次 Library 文件清单及额外标题检索中，**没有找到《朱》4.0.0 的 ELF**。紫版的 RVA 和精确 opcode、虚表地址 **绝不能直接用于朱版**。代码对《朱》游戏 Title ID 直接拒绝，不将其伪装为支持双版。

## 3. 已开发的纯 C# 实验写入

- [`SysbotOnlyRewardWriter.cs`](../../RaidCrawler.Core/Host/SysbotOnlyRewardWriter.cs)：`PrepareAsync → ApplyAsync → RestoreAsync`，从同一个现有 sys-botbase 连接读取、验证、写入及读回；不执行任何游戏中的自动按键或开房操作。
- [`HostPlanAWriteAudit.cs`](../../RaidCrawler.Core/Host/HostPlanAWriteAudit.cs)：纯离线、字节级验证：整个原版 `RaidPoint` 0x3E8 必须不变，只有原始45行第一条空行内的16字节允许改变。
- [`SysbotOnlyRewardWriteWindow.cs`](../../RaidCrawler.WinForms/SubForms/SysbotOnlyRewardWriteWindow.cs)：主界面→纯sys-botbase→**受控奖励写入（实验）**；独立两项测试机/不确定性确认、点击准备、准备凭证15秒有效、点击二次确认写入，允许手动恢复。
- [`HostPlanAWriteAuditTests.cs`](../../RaidCrawler.Tests/Host/HostPlanAWriteAuditTests.cs)：离线单行内容和前后字节、空槽、错误 subject、非法数量的回归检查。

## 4. Fail-closed 限制

1. 仅《紫》4.0.0，当前程序主模块指令和序列化函数虚表入口必须与 ELF 匹配，候选对象必须满足运行时三虚表类型及 `RaidPoint_` 结构识别。
2. Hexbyt3 候选 lobby/NID/战斗状态需连续两次表示“房间连接 + 至少一位参与者 + 未战斗 + 非野外”，否则拒绝。**这里的房间检测仍是实验旧版偏移，不能当作已证明的权威状态。**
3. 明确提供对象绝对地址；不能将 RaidCrawler 存档坑位块地址当作序列化对象。只对单行 **先机球（Item 15）×1–10 / Rare=0 / Subject=5** 进行准备和实验写入，禁止 `20×999` 或任意 ItemId。
4. 连续两次原版快照完全相同，离线追加规划器通过、原始奖励连续、有剩余槽位，准备到写入不得超过15秒；写前重新校验整个原始对象和实际现场状态。
5. 一次 `WriteBytesAbsoluteAsync` 只写 16 字节的第一条空奖励行，而不是完整覆盖游戏对象；写后再读取0x3E8比较，除这一行之外任何字节改变都不能认定成功。
6. 恢复操作需要整个候选对象**仍与刚才写后的值完全一致**，才会尝试写回原来的16字节；这只能恢复当前RAM对象，不能撤回已经发送的网络数据。
7. sys-botbase 不是对游戏进程的 Compare-and-Swap：写前比对和写后验证之间**依然存在竞态**。即使报告 `ONE_ROW_WRITTEN_TO_CANDIDATE`，也只代表候选RAM写入成功，**不代表接收者有晶耀护符、已经收到道具或重启存档后仍保留**。

## 5. 实机验收 / 开放真实房主发奖的门槛

| 门槛 | 当前证据 |
|---|---|
| 程序面向《紫》4.0.0、Hexbyt3 版本常量确认 | 已核对上游 |
| 原始紫版 ELF 及 `RaidPoint+0xE4` 字段布局 | 已在离线 ELF/donor 复核 |
| C# 只读候选与受控 16B 写入代码 | 已提交，待 Windows CI 及硬件验收 |
| 游戏现场找到**本局将被序列化**的 owner 的稳定指针 | **尚未证明** |
| 写入必须发生在 LotteryRewardItem 生成后、Bootstrap 复制前，且不会被重置 | **尚未证明** |
| 原版输出 Bootstrap 与直接写后的内存奖励一致 | **尚未实机验证** |
| 携带晶耀护符参与者到账，原版奖励不减，重新加载存档后保留 | **尚未实机验证** |
| 《朱》4.0.0 相同语义的代码地址和 ABI | **缺 ELF，拒绝支持** |

推荐先在可恢复的独立测试机的离线/本地环境进行，记录阶段自动日志、候选身份和写入返回码。遇到类型不匹配、原版数据变化或源对象消失，不要强行写入，也不要宣称“方案 A 已实现真正的联机额外奖励发放”。
