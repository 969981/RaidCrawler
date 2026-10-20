# 方案 A：仅用 sys-botbase 的真实房主 RaidPoint 直接内存路线

> **2026-10-20 重要勘误（覆盖本页旧结论）**：PokéLDN 4.0.0 donor 的完整奖励记录从 `RaidPoint+0xE0` / `Bootstrap+0x798` 开始，四列为 `[condition_or_source, item_id, quantity, reserved]`；`+0xE4/+0x79C` 只是记录内部的道具 ID，不是完整行起点。以下旧版 `+0xE4 记录起点 / [Item,Quantity,Rare,Subject]` 的叙述**仅作为历史错误调查记录保存，不可作为写入指导**。最新版布局、subject0/5 和验证限制统一参见 [POKELDN_SUBJECT0_RETAIL_LAYOUT_TEST.md](POKELDN_SUBJECT0_RETAIL_LAYOUT_TEST.md)。


> **2026-10-20 当前结论：纯 sys-botbase 读内存诊断已实现；尚未证明可得到“当前房主真正用于下一次序列化”的稳定指针。没有任何实机发奖写入。方案 B 暂不开发。**
>
> 本路线不安装 `subsdk9`、`main.npdm`、任何 exlaunch 插件，不启用 Hook，也不需要 `mailbox-locator.json`。仅使用原有 `ISwitchConnectionAsync` 的 `GetTitleID`、`GetGameInfo("version")`、`ReadBytesMainAsync`、`ReadBytesAbsoluteAsync` 与 `GetHeapBaseAsync`。

## 本轮新增：基于真实 ELF 重定位表确认序列化类实例指纹

在 **Violet4.0.0.elf 原始字节** 的 `.rela.dyn`（282,761 条重定位）里，找到了唯一指向原生 Serializer `main+0xE327FC` 的 **AArch64 RELATIVE** 项（类型 `0x403`）：

| 重定位写入位置 | 目标代码地址（ELF RVA） | 解释 |
|---|---|---|
| `main+0x4459278` | `main+0x1E631F8` | 主虚函数表起点的第一个析构器槽 |
| **`main+0x44592E0`** | **`main+0xE327FC`** | **虚表 +0x68 是目标序列化函数** |
| `main+0x4459308` | `main+0xA6855C` | 第二虚表首项 |
| `main+0x4459358` | `main+0xA68570` | 第三虚表首项 |

原始 ARM64 指令证实 `main+0x1E63204` 创建主虚表引用 `0x4459278`、`+0x1E6320C` 向 `[x0]` 存入、`+0x1E63210/+0x1E63214` 计算 `+0x90/+0xE0`、`+0x1E6321C` 把两张虚表地址存至 `[x0+0x20]`。这些代码位于对象析构/虚表切换路径中，**能够证实虚表布局，但不能据此推断构造/存活时刻**。

因此可在**不使用 Hook** 的情况下，由 C# 获取当前 `mainBase`、确认 `[mainBase+0x44592E0] == mainBase+0xE327FC`，再在事先限定的堆范围里寻找同时符合：

```text
[owner+0x00] == mainBase + 0x4459278
[owner+0x20] == mainBase + 0x4459308
[owner+0x28] == mainBase + 0x4459358
RaidPoint  == owner+0x58
RaidPoint.name begins "RaidPoint_"
rewardRow0 == owner+0x13C
```

此组合比在全堆里仅搜 ASCII `RaidPoint_` 更能排除伪阳性，但**只能标记 `ELF_VTABLE_TYPE_ONLY_NOT_ACTIVE_RAID`**。实例仍可能是未使用对象、另一个房间的对象，或根本不在前1MiB堆区域。也无法通过这组证据直接得出长期稳定的全局指针或奖励生成完成后的可写窗口。

本轮实际工作：
- [ELF 重定位与指令核验脚本](../../tools/host/audit_violet_serializer_vtable.py)：仅使用用户本地 ELF、导出 JSON 结构化结论，不包含游戏字节。
- [纯 sys-botbase C# 类型扫描](../../RaidCrawler.Core/Host/SysbotOnlyRaidPointInspector.cs)：新增 `ScanSerializerObjectsAsync`、三虚表匹配、运行时 vtable slot 检查，默认界面只读扫描 **堆起始1MiB**，函数最大8MiB。找到唯一类型实例可填入 C# 地址框进行继续只读核验，但不能写奖励。
- [中文观察窗口](../../RaidCrawler.WinForms/SubForms/SysbotOnlyHostWindow.cs)：虚表定位按钮及 S0～S4 的人工阶段标记；比较本轮地址、奖励 SHA、行数是否随房间进度变化。
- [离线结构测试](../../RaidCrawler.Tests/Host/SysbotOnlyRaidPointInspectorTests.cs)：拒绝仅有 ASCII 名称的伪对象、验证虚表三元组、`0x68` 函数槽偏移。

可复现的本地静态验证：

```bash
python tools/host/audit_violet_serializer_vtable.py /path/to/Violet4.0.0.elf --out vtable-audit.json
```

`type_identity=ELF_RELOCATION_VERIFIED` **只说明 ELF 静态结构**，不等于找到了活跃对象或已验证写入。

## 与 PokéLDN 研究的时序交叉验证

在 [PokéLDN`pokeldn/sv/raid.py`](https://github.com/969981/pokeldn/blob/research/pokeldn/sv/raid.py#L171-L235)：

- `raid_point()` 按 `0xE4+i*16` 生成45条四 `u32` 奖励；`bootstrap()` 在生成完队伍后，才把 `RaidPoint` 拼进完整 `0xAA0`。
- `RaidGroup.__init__()` 先只做参数与结构验证；`RaidGroup.bootstrap()` 第一次真正调用生成函数时设置 `self.plain`，之后缓存复用。
- 这证明 **PokéLDN 模拟房主** 可在 bootstrap 生成时控制奖励数据，但**并不证明真实 Switch 房主会在开房期间保留同一份长期可写的 C++ RaidPoint**。
- [Lot​​tery 后置 Hook 研究](https://github.com/969981/pokeldn/blob/research/docs/research/sv/sv400-lua-post-lottery-append-hook.md) 指出原版 Lua 的奖励可能在后续初始化/资格处理时变化，若修改过早，则 Subject=5 行可能再次被移除；且公开 Lua 基线与4.0.0不完全对应。要确认真实宿主窗口仍需运行时序证据。

**本轮结论**：静态证实 **“能可靠区分序列化类实例”**，但未静态证明 **“能持续访问当前即将发包的那个实例”**。后两项不能靠更激进的扫描或猜测地址来宣称解决。保留方案A只读直到当前实例归属、变化和时间窗可证实。

## 原始 ELF 的直接结论

ELF 来源：用户 Library 的 `Violet4.0.0.elf`，`size=74,362,480`，`SHA256=d17403eaa9d844db82f647fd2f1518806b10c0518d53602fc0ac1766298fdba0`，`Build ID=709bfd66115298640155fcc4979dba151c7cc79a`。本轮在工具环境里直接对该 ELF 执行了 `llvm-objdump --start-address=0xE327FC --stop-address=0xE329B4` 和 ARM64 BL/B xref 统计；具体 N1 指令见 [ELF_REVALIDATION_N1_N2.md](ELF_REVALIDATION_N1_N2.md)。

| 位置 | 原版指令 | 意义 |
|---|---|---|
| `main+0xE32814` | `mov x20,x0` | 当前 **serializer 输入对象的指针** 位于寄存器 `x0`，被复制到 `x20` |
| `main+0xE32964` | `ldp x11,x10,[x20,#0x58]` | RaidPoint 的前两个 8 字节来源于此对象内嵌区域 |
| `main+0xE32968` | `add x1,x20,#0x78` | 剩余的 0x3C8 字节的拷贝来源 |
| `main+0xE32970` | `str x11,[x23,#0x6B8]` | 输出缓冲区中的 RaidPoint 起点 |
| `main+0xE32980` | `bl memcpy` | 将输入对象的 RaidPoint 内容复制到输出 |
| `main+0xE32984` | `mov x8,x21` | 原版序列化完成拷贝；但这个位置是代码地址，并不是指向 RaidPoint 的指针 |
| `main+0xE329B0` | `b 0x1592B7C` | 后续封装/压缩处理路径 |

从上述指令可以准确导出：

```text
ownerObjectPtr = runtime x0 / x20            ← 当前还没有稳定指针链
raidPointStart = ownerObjectPtr + 0x58
rewardRow0     = ownerObjectPtr + 0x13C
bootstrapRows  = runtime x23 + 0x79C

45 rows * 16 bytes, columns = ItemID, Quantity, RareFlag, Subject
```

针对 `.text` 的 **ARM64 直接 B/BL 静态交叉引用**统计：到 `main+0xE327FC` 的直接跳转 0 个，但 ELF 中可以找到它作为函数指针被注册在静态元数据表。这提示其调用可能经过间接分发，**不能证明其对象有固定长期生命周期，也不能据此假设是全局变量**。因此现有 `RaidBlockPointerBase`（太晶坑存档数据块）和这里的 `ownerObjectPtr` 必须严格区分。

## 已实现：纯 C# 只读诊断

- [`SysbotOnlyRaidPointInspector.cs`](../../RaidCrawler.Core/Host/SysbotOnlyRaidPointInspector.cs)：验证 Title ID 与版本、12 条 ELF 指令指纹；接受**有证据来源的当前绝对地址**，支持“对象起点 x0”或“内嵌 RaidPoint 起点”两种输入；两次间隔 150ms 读取 0x3E8，并核验名称 `RaidPoint_`、45×16 奖励结构、源数据 hash 与只读离线追加预览。
- [`SysbotOnlyHostWindow.cs`](../../RaidCrawler.WinForms/SubForms/SysbotOnlyHostWindow.cs)：主窗口“纯sys-botbase”按钮，不需要 Native 模块和 mailbox。
- 仅作为候选发现：用户显式确认后，**只读扫描堆起始 1MiB**（方法本身最大允许 8MiB），寻找符合 `RaidPoint_` 前缀及奖励字段的内存副本；找到的结果必须标记 `POSSIBLE_HEAP_COPY_ONLY`。未发现也不等于游戏中没有对象。**禁止将候选地址直接作为可以修改的证明**。
- [`SysbotOnlyRaidPointInspectorTests.cs`](../../RaidCrawler.Tests/Host/SysbotOnlyRaidPointInspectorTests.cs) 对地址计算、12 条指令和结构签名测试。只有静态/合成测试，不冒充实机。

## 如何验证是否满足方案 A 的写入先决条件

在具有恢复能力的测试 Switch（仅使用现有 sys-botbase）依次记录：

1. 《紫》4.0.0 游戏处于主菜单、房主开放团体战房间**之前**的候选池数据。仅观察，不写入。
2. 房主进入具体太晶坑、开放房间、参与者加入、准备开战这几个阶段，各读取**同一候选地址**。记录名称、有效奖励行数、SHA256、有无变化、是否消失。也可以在静止期间试验限定范围的只读堆签名搜索。
3. 对多个坑位、不同地图和活动坑测试：该对象是否每次更新为**当前**坑对应数据？是否有数份看起来正确但其实过期的副本？如果重启后改变地址，能否从已知且经过 ELF 独立验证的稳定指针根追踪它？
4. 通过真正的接收端观察或者受控的原版发送端证据，验证所找到对象的奖励确实成为下一次 Bootstrap 的原版奖励，而不只是 RaidCrawler 自己预测的一份表。
5. 只有上述实时关联被真实证明，并且可以确定“奖励生成完成→序列化前”**存在足够长且不会覆写数据的窗口**，才讨论基于 sys-botbase 的受控 `Read→Compare→Write→ReadBack` 事务。**该写入代码本轮没有实现**，原因是目标地址尚未证实。

方案 A 的 GO 标准：**稳定可复现的当前对象定位方式 + 当前房间/坑/时间线关联 + 已生成完成且序列化前不被覆盖的窗口 + 可恢复的 fail-closed 事务**。单纯在堆里找到 `RaidPoint_` 或者连续两次读取相同，严格不足以达到 GO。

## 若方案 A 的根指针/时间窗口无法证实

再评估方案 B：利用 sys-botbase 临时内存指令插桩，不向 SD 卡部署模块。方案 B 本质还是机器码 Hook，额外需要代码可写/可执行权限、指令缓存一致性、ARM64 跳板、并发与还原保障。**目前没有证明标准 sys-botbase 能在所有 Switch 系统版本完成安全的代码插桩，不能直接把先前 exlaunch subsdk9 当成纯内存 Hook 使用。**

## 安装与构建区分

默认 [host Windows CI](https://github.com/969981/RaidCrawler/actions/workflows/host.yml) 现在只上传 `RaidCrawler-host-sysbot-only`，不会再携带 `Native/subsdk9`、`main.npdm` 或 `mailbox-locator.json`。旧版 Native 调试工具只保留在单独 [host-native-probe.yml](https://github.com/969981/RaidCrawler/actions/workflows/host-native-probe.yml) 的研究工作流中。请不要为了方案 A 将该 Native 构建放入 Switch。
