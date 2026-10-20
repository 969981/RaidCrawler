# 方案 A：房主 RaidPoint 自动候选发现（无需手填地址的只读诊断）

日期：2026-10-20；代码分支：`host`；游戏：仅《紫》4.0.0；连接：**现有 sys-botbase**（不安装 `subsdk9`、`main.npdm`）。

## 为何旧版一直要求手填地址

旧版“虚表定位（只读）”只检查堆前1MiB，若失败再扩到8MiB，并把 [`main+0x4459278`](https://github.com/969981/RaidCrawler/blob/host/RaidCrawler.Core/Host/SysbotOnlyRaidPointInspector.cs) / 0x4459308 / 0x4459358 这三个虚表**同时精确匹配**作为筛选条件。

直接反汇编用户 Library `Violet4.0.0.elf`（SHA256 `d17403eaa9d844db82f647fd2f1518806b10c0518d53602fc0ac1766298fdba0`）：

```asm
main+0x1E63204: adrp x8, 0x4459000
main+0x1E63208: add  x8, x8, #0x278
main+0x1E6320C: str  x8, [x0]
main+0x1E6321C: stp  x9, x8, [x0, #0x20]
main+0x1E63220: ldr  x0, [x0, #0x458]
main+0x1E6322C: ldaxr w8, [x9]
...
```

该函数随后递减资源引用计数并做释放处理，属于**析构路径**。这一组虚表虽包含序列化方法 `main+0xE327FC`，却不能当成所有**活跃**实例的唯一类型签名。旧版找到0个候选完全可能是假阴性。

## 来自 PokéLDN 的完整对象校验

固定 [Decryptu/pokeldn@8b2c19c 的 SV_RAIDPOINT_REVERSE_ENGINEERING.md](https://github.com/Decryptu/pokeldn/blob/8b2c19c480400a6805eeeff266e13ded20c97475/docs/research/sv/SV_RAIDPOINT_REVERSE_ENGINEERING.md)，三份零售版 Bootstrap 实测显示：

| RaidPoint 偏移 | 原版意义 |
|---|---|
| +0x00 | `RaidPoint_*` ASCII 名称 |
| +0x18 | 3份样本里为 `0x40`（不强制当作所有实例的必要条件） |
| +0x20 | 星级 |
| +0x2C | Boss 等级 |
| +0xE0 | 首条16B奖励记录 `[source,item,qty,reserved]` |
| +0x3B8 | 与 +0x20 重复的星级 |
| +0x3BC | Boss 图鉴物种 |
| +0x3C8 | 与 +0x2C 重复的等级 |

而原 ELF `main+0xE327FC` 序列化函数将 `owner+0x58` 的 `RaidPoint` 复制到 `Bootstrap+0x6B8`。所以可以从堆中找到上述结构候选 `raidPointAddress`，计算 `ownerCandidate = raidPointAddress - 0x58`。这**只是候选内存关系**，并不是能从固定指针根证明该对象已被房主发包程序选用。

## 已实现算法

[`HostRaidPointAutoLocator.cs`](../../RaidCrawler.Core/Host/HostRaidPointAutoLocator.cs)：

1. 读取当前 Title ID、版本；验证《紫》4.0.0 的序列化指令和虚函数入口、主模块与堆基址。
2. 通过现有 sys-botbase 对堆起始区域进行**有上限的只读分块扫描**；每块16KiB，不猜测/修改任意地址，不扫描其他进程。默认8MiB，用户许可后扩到32MiB和64MiB；无法读取的页面计入失败计数，不当作已完整扫描。
3. 找到8字节对齐的 `RaidPoint_` 前缀后，读取完整 `0x3E8`；核对星级、等级及尾部重复字段、物种范围、名称；间隔120ms重复读取验证稳定。
4. 从 `RaidPoint-0x58` 回推 owner，读取第一、第二、第三个虚表或槽入口，把证据分级为 `None`、`MainStaticPointerOnly`、`SerializerSlotPointsToE327FC` 或 `DestructorVtableTriple`；**不把活跃类必须持有析构期三虚表当成筛选条件**。
5. 单独执行保守的原版45奖励行追加规划，但**即使原版奖励中存在料理或未知来源行导致无法追加，仍保留结构候选**以便研究；绝不因为未知行就覆盖原有奖励。
6. 记录候选 owner地址、RaidPoint地址、名称、奖励SHA、是否满足离线规划、类型证据、可读/不可读块数量。最多24个候选，支持关闭窗口取消。
7. 当前如果**恰有一个**候选且能在 owner 关联到原版序列化函数，会自动填入地址框；否则不猜测，不启用自动写入。

[主窗口 `SysbotOnlyHostWindow.cs`](../../RaidCrawler.WinForms/SubForms/SysbotOnlyHostWindow.cs) 现在走结构识别，并在无候选时询问是否扩大只读范围。[预设窗口 `SysbotOnlyRewardWriteWindow.cs`](../../RaidCrawler.WinForms/SubForms/SysbotOnlyRewardWriteWindow.cs) 新增 **“自动定位”**，可先在预设窗口搜堆前8MiB，无唯一可信候选则提示使用主窗口进一步诊断。

## 有哪些尚未解决的问题

- 一个 RaidPoint 结构/有效星级和等级重复字段正确，仅证明该内存副本形式像零售版实例。游戏可能在 heap 之外分配、保存多个副本、更新后释放旧对象，或为不同房间保留缓存。
- 当前活动中的派生类可能覆写 serializer 方法，`vtable+0x68` 不一定直接指向 `E327FC`；无法通过时**不得擅自认定不存在房主对象**。
- sys-botbase 没有“获取当前寄存器x20”的通用命令，不能仅用已知 `E327FC` RVA 推导实时 `x20`。
- 自动填入绝对地址 **不等于已经确认当前房主发送链**。现有 `PrepareAsync` 仍会对当前对象及版本执行完整检查，失败则拒绝。该迭代**没有取消任何写入前门禁**。
- 纯 C# 内存轮询不能证明满足“LotteryRewardItem生成后且Bootstrap复制前”的瞬间时序，最终仍须多场真实本地多人团体战日志证明。
- 单行 `subject=0` 先机球×500只读规划、候选RAM写入成功、参与者奖励到账和存档持久化是四个**分开的**验收指标，不得混为一谈。

## 测试操作

1. [构建最新版 host](https://github.com/969981/RaidCrawler/actions/workflows/host.yml) 并连接《紫》4.0.0 sys-botbase。
2. `纯sys-botbase → ① 自动查找候选`（新版改为结构搜索）。先在开房前搜一次，再在房主**已开房、参与者已加入**时搜一次。
3. 若搜索结果为0，可根据弹窗按需扩大范围；Wi-Fi读取可能需较长时间，留意不可读块和错误。保存完整日志。
4. 有唯一带有效序列化槽的对象才会自动填入 owner；然后用“②验证找到的地址”记录奖励SHA及数量变化。
5. 在单行奖励预设窗口也可点击“自动定位”执行同样的只读初筛。
6. 只有能够跨阶段证明对象属于当前房主且准备检查全部通过才考虑显式的试验写入。程序从不把“找到候选”当作“参与者已领到奖励”。

本轮 GitHub 自动化可证明编译和**合成数据结构判断**，但不能代替真实 Switch 内存和联机实机结果。
