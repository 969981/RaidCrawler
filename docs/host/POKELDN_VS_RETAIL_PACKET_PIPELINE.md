> 注意：本页为历史分析，旧版的奖励行 `+0xE0` 解释已被证伪。请优先阅读 [最新 ELF 与 donor 验证](ELF_REVALIDATION_N1_N2.md)：奖励行起始为 `+0xE4`，字段顺序 ItemID/Quantity/RareFlag/Subject。\n\n# PokéLDN 修改 `0x012F` 与真实 Switch 房主的差别（2026-10-20）

## 结论

**PokéLDN 能先改解压后的 Bootstrap，再重压缩/重封包，因为它就是网络会话的主持端。** 真实游戏 Switch 作房主时，通过 `sys-botbase` 的 `SendAsync` 发的是控制服务命令，而不是给玩家的 Pia 网络应用消息，不能把磁盘上的修改后 `0x012F` 字节直接送到原版房间。

真实 Switch 想绕过奖励生成函数捕获，必须在以下方案中选择一种：

1. **事先改动态 RaidPoint 源对象**：在序列化之前使用 sys-botbase 的合法内存写入命令修改原版对象；无需在奖励生成瞬间捕获寄存器，但需要**可靠的指针来源、写入窗口以及确认后续不覆盖**。原版对象地址尚无版本稳定的链，因此目前不可作为可靠落地方案。切勿将下文 donor 的地址当作进程绝对地址。
2. **最小 ExeFS 捕获/传输桥接（推荐研究方案）**：Switch Native 模块只负责在发送路径捕获或替换当前调用关联的结构；C# 通过现有 sys-botbase TCP/USB 连接读取状态、把配置写进严格定义的内存邮箱。C# 是唯一操作入口，**仍需要一次 Native 模块安装**，但无需改 sys-botbase 自身。
3. **PC 完全充当房主（PokéLDN 模式）**：由 PC 自己生成 RaidPoint、玩家队伍及可靠流、分片、会话与 ACK；此时并非真实游戏 Switch 作房主。PokéLDN 的 ESP32/LDN 连接和任天堂官方互联网房间不能混为一谈。
4. **代理/中继层修改**：仅对自己完全控制且能解密、重封协议、维持可靠流状态的 LDN 链路成立。普通 sys-botbase 的控制连接不是这个代理。任天堂官方 NSO 模式能否工作没有由该 donor 证明。

## 在用户指定提交上复核的精确证据

固定来源：
- [原始 PokéLDN 快照](https://github.com/Decryptu/pokeldn/tree/8b2c19c480400a6805eeeff266e13ded20c97475)；
- [Bootstrap 逆向文档](https://github.com/Decryptu/pokeldn/blob/8b2c19c480400a6805eeeff266e13ded20c97475/docs/research/sv/SV_RAID_BOOTSTRAP_ENCODER.md)；
- [导出的 1707 B donor](https://github.com/Decryptu/pokeldn/blob/8b2c19c480400a6805eeeff266e13ded20c97475/docs/research/sv/fixtures/sv_raid_reward_donor.bin)；
- [实际编码器源码](https://github.com/Decryptu/pokeldn/blob/8b2c19c480400a6805eeeff266e13ded20c97475/bin/sv_raid_bootstrap_codec.py)；
- [生成房主流程源码](https://github.com/Decryptu/pokeldn/blob/8b2c19c480400a6805eeeff266e13ded20c97475/pokeldn/sv/raid.py)。

**本轮实际解码 donor：**

```
application bytes = 1707
header (18B) = 80 33 2f 01 13 00 02 00 00 00 a0 0a 00 00 00 00 28 3a
prefix = 0x3380 (on-wire bytes 80 33)
type = 0x012F
LZ4 block starts at application+0x12
decoded plaintext = 2720 bytes (0xAA0)
RaidPoint begins at plaintext+0x6B8
RaidPoint name = RaidPoint_13_1_1
reward records start at RaidPoint+0xE0 = plaintext+0x798
row width 16; four little-endian uint32 words: marker, item_id, quantity, reserved
```

示例真实 donor 解压字节：

```text
plain+0x798 : (marker 0, item 1127, amount 1, reserved 0)
plain+0x7A8 : (marker 0, item 1128, amount 1, reserved 0)
plain+0x7E8 : (marker 2, item 1862, amount 2, reserved 0)
plain+0x808 : (marker 1, item 2064, amount 2, reserved 0)
plain+0x8B8 : (marker 4, item 89, amount 1, reserved 0)
plain+0x8C8 : (marker 4, item 92, amount 1, reserved 0)
plain+0x8D8 : (marker 4, item 1862, amount 10, reserved 0)
plain+0x8E8 : (marker 5, item 0, amount 0, reserved 0)  <-- 特殊元数据/终止样式，不能当空行
```

**重大勘误：** 过去 N0 以 `+0xE4` 为奖励行起始及 `item,quantity,rare,subject` 的假设与**这个实际 donor**不符：`+0xE4` 实际上是第 0 行**item 字段地址**，其前四字节 `+0xE0` 是 marker。现在 `host` 已把奖励行起点修正到 `+0xE0`，更新解析/比较/Native 只读探针。由于 `marker=5,item=0` 的语义尚有疑点，离线追加规划器遇到它必须拒绝，不能把其后的 0 字节区域视为可安全追加的普通奖励列表。

注意：在项目不同分支、文档时点和旧版 Lua 研究中，字段可能被描述为 `subject`、`source` 或 `rare`。在当前**固定 commit 的 donor**中，确定的是**4×u32 实际字节顺序**，不等同于完全还原每个 marker 的玩家资格语义。

## PokéLDN 如何编码并发送

完整的**自有模拟房主**发包链：

```text
生成 Seed/Encounter/PK9 与完整 RaidPoint
    或载入 donor/replay 取出已完整重组的 0x012F application
   → raw-LZ4 解压，得到 0xAA0 明文
   → 按验证过的 reward rows 修改
   → raw-LZ4 重新压缩
   → 保留/生成 0x12 字节应用消息头
   → 在 1395 B 左右切分（历史 donor seq11/seq12；新生成消息可动态切分）
   → 对带 FLAG_ZLIB 的分段再执行一层 zlib
   → 重新建立 Pia reliable-stream 消息头、流水序号、ACK/重传
   → PokéLDN transport.send() 经自己管理的房主通信链路发送
   → 零售参与者解码并处理
```

两个独立压缩层不能混用：

| 层 | 管理者 | 用途 |
|---|---|---|
| 应用 Raw-LZ4 | `sv_raid_bootstrap_codec.py` | 0xAA0 RaidPoint + 5个队伍对象 |
| Pia 可靠分片 zlib（选用） | `pokeldn.sv.streams` | 按可靠分片 flags 封装，不是整个应用都 zlib |

在固定提交的 `bin/sv_raid_bootstrap_codec.py` 中，`encode_reward_profile_application` 采用的是**替换奖励清单**：清空 19 个相互关联的奖励行以及 2 个额外 bonus 行，再写入新清单；**不是无损追加原版全部奖励**。

已在真实零售参与者验证的案例包含：42 超级球、42 经验糖果 XL、先机球 ×500；但该 donor 测试**不证明**可以保留所有原版奖励 + 任意追加 20×999、也不证明 Switch 原生 NSO 互联网房主允许外部报文替换。固定提交中的常用房主已经改成生成整个 RaidPoint、独立主持 `seq 1..20` 的开战流程；donor 是研究夹具，而非生产必需文件。

## C# + sys-botbase 的可实施改造路线

建议把 `RaidCrawler.WinForms` 作为**唯一控制 UI**，但把“生成字节”和“投递字节”视为两个独立模块：

**PC C# 离线管线**：只解析/预览已授权得到的完整 `0x012F` 应用对象；按版本化 schema 读出真实 marker/item/count；保留 source 未知字节；由 Raw-LZ4 库重新编码，校验 `decode(encode(raw))==raw`。若需要修改房主奖励，必须优先保持原奖励行和条件行；不要把 donor 的 `exact` 清单替换误用成 append。

**C# → Switch 控制管线（需支持发送端）**：sys-botbase 只承载配置、状态与少量验证数据（例如 `SET_ENABLED`、`GET_STATUS`、`SET_EXTRAS`、`FETCH_CAPTURE`）；真正修改源对象/发送前 buffer 的动作仍应由具有正确时序的 Switch 模块执行。可以把控制接口实现为游戏内固定大小的共享内存邮箱，C# 通过 `ReadBytesAbsoluteAsync` 等接口读状态（写入 API 和邮箱地址分配需单独核实）；不能凭空声称现有 `SwitchCommand` 原生支持上述命令名称。

**准入门槛**：N1 实机拿到源/明文完全一致后，再确认原版行数、marker=5 特殊行处理、追加位置与 native 枚举方式，最后才允许 N2 小数量单行追加。若不能证明投递通道可控，则只能离线生成候选消息而不能宣称参与者已收到。

## 建议路线

对于“**真 Switch 房主 + C# 界面 + sys-botbase 唯一通信连接**”的约束，首选 **C# 控制 + Native 最小只读/可控模块**，逐步把前一阶段的 SD 文件导出改为 sys-botbase 读共享邮箱。只通过默认 sys-botbase 而不在 Switch 上安装任何原生模块，**目前没有已证实的发包替换入口**；模拟房主的 PokéLDN 做法不能直接等价迁移。
