# Violet 4.0.0 首次实机无效果：ELF × PokéLDN 定位（2026-10-19）

## 判定与原始证据

用户提供的 `raid-20261019-151633-74168ab3.jsonl` 显示：
- 《紫》4.0.0，TitleID `01008F6008C5E000`，NSO Build ID 8 字节前缀 `709bfd6611529864`；
- Main NSO base `0x127FE04000`，原 8/8 指令匹配；
- 880B 区域映射可读、全零，最后一条 PLT 指令匹配；
- `Empty → InstalledDisarmed (epoch=1) → InstalledArmed (epoch=2)`，且入口跳转与完整跳板字节回读正确。
- 当时奖励配置 20/45 行，完整 720B SHA256 `7693893F94360FDBFE8AE77616F5CAA819525A77EA4CDE1D1A46914804C4D435`，20 行全为 `subject=5`。
- 用户确认**持有晶耀护符**；五星梦妖魔战斗截图仍是常规奖励。
- 上传 JSONL 共 28 条事件，最后一个是启用，**没有进入战斗期间的 Hook 命中或分组采样**；此前界面的布尔 `hookHit:false` 表示“未被确认”，不是实机上查到了零次命中。

**结论**：这只证明 Windows 调试通道写入成功，不证明 CPU 执行补丁，也不能据此判断发奖阶段何处失效。不要重复安装或盲目写入其他内存。

## 本次直接扫描原始 ELF

原始 `Violet4.0.0.elf` SHA256 `d17403eaa9d844db82f647fd2f1518806b10c0518d53602fc0ac1766298fdba0`，大小 74,362,480 字节。

- `main+0xE327FC`（序列化函数）：`main+0xE32828` 分配 `0xAA0`；`main+0xE32960..0xE32980` 将 `x20` 消息对象的数据复制进 `x23` 明文缓冲。
- `main+0xE32964` 读取 `x20+0x58` 的 RaidPoint 前段；写入 `x23+0x6B8`；
  `main+0xE32968` 以 `x20+0x78` 为源，连续复制 `0x3C8` 字节至 `x23+0x6D8`。
- 实际奖励区：RaidPoint `+0xE4` = `x20+0x13C`；Bootstrap `+0x79C` = `x23+0x79C`；45 行 × 16B。
- Hook `main+0xE32984` 正位于 memcpy 返回后，因此就算 Hook 确实进入，**当前替换的也是发送端的 `x23` 缓冲，非持久修改 `x20` 源对象／结算列表／背包**。
- 对整个 0x343FC90 字节代码段扫描，**没有直接 AArch64 `BL` 或 `B` 指向 `0xE327FC`**；在原始 ELF file offset `0x359C720`（映射 RVA `0x359C1B8`）发现唯一对齐的 `u64 0xE327FC` 函数指针。该区连续存有类似三元数据 `0x403, function_pointer, metadata_pointer`，是函数表间接分发的依据，但**尚未证明全部触发条件以及表项语义**。
- 原始 ELF 没有找到 ASCII 字节序列 `RaidPoint_`，这本身**不证明零售 RaidPoint 名称不同**（名称可能来自外部资源／动态生成）；却意味着不能通过 ELF 字符串存在性来认证跳板的字符串保护分支。
- 当前跳板在版本号检查后，还要求 `x23+0x6B8` 的开头 8 字节为 `RaidPoin`，否则**静默跳过**。这是比此前想象更重要的可能性，尚没有运行期证据证明零售数据满足条件。

## 与 PokéLDN 的源代码和实际测试对照

[`pokeldn/sv/raid.py`](https://github.com/969981/pokeldn/blob/research/pokeldn/sv/raid.py) 构建 `RaidPoint` (`0x3E8`) 和 `0xAA0` Bootstrap：
- `POINT_NAME="RaidPoint_POKELDN_0"` 是 PokéLDN 自己构造的名字；名字要求以 `RaidPoint_` 开头是 PokéLDN 的生成器约束，**不能当作零售游戏所有 RaidPoint 必定满足的运行期证据**。
- `POINT_REWARDS=0xE4`，`POINT_SUMMARY=0x3B8`，正好 45×16B；
- `RaidGroup.bootstrap()` 仅首次创建并缓存 `self.plain`；`RaidHost._next()` 将同一明文按 `kind=0x2F` 发给参与者。这是本地 LDN 出站协议模型，并非单人发奖链路。
- [`docs/sv_raid.md`](https://github.com/969981/pokeldn/blob/research/docs/sv_raid.md) 给出零售 4.0.0 的 Lua 筛选函数 `C07C34FC3B976E5A7.F316609DEA110601D`，其中 subject 5 必须要接收者持有道具 ID 2481「晶耀护符」。PokéLDN 还实际测试过持护符的接收者保留了 `subject=5` 行，并证明按修改过的 RaidPoint 接收发奖是可能的。因此用户已有晶耀护符时，**不能把本次未看到奖励归因于缺护符**。
- 过滤发生在参与者自己的 Lua/原生链路；此外，房主与接收者可能使用不同数据来源，单凭房主自己的奖励截图无法证明其出站 Bootstrap 修改有没有发生。
- `pokeldn/sv/raid_reward_trace.py` 已能独立记录 `S1_SOURCE`、`S2_HOST_BOOTSTRAP` 和 `S2_WIRE_LOOPBACK`；这些**来自 PokéLDN 进程**，不是零售 Switch 内部 `x23` 命中证据。

## 需要分别采集的证据与失败定位

| 节点 | 最低证明 | 目前状态 |
|---|---|---|
| A. Windows 补丁持久存在 | 前后回读入口、完整 reader、epoch、720B SHA | 安装时确认；战斗中需采 |
| B. ARM64 reader 真实执行 | 实际 CPU PC/执行观察或可信的原生事件 | **NOT_OBSERVED** |
| C. reader 内的 RaidPoint 前缀检查通过 | 运行期 `x23+0x6B8` 前缀或独立条件观察 | **NOT_OBSERVED** |
| D. 源 RaidPoint → Bootstrap | 与战斗同一会话的独立 S1/S2 字节哈希 | **NOT_OBSERVED** |
| E. 接收端的 0x2F 数据 | 解压后 45 行字节/哈希；同 run/站点关联 | **NOT_OBSERVED** |
| F. Lua 筛选 | 实际筛选列表及是否有护符/其他条件 | **NOT_OBSERVED** |
| G. 发奖与保存 | 结算、背包增量、重新读取存档 | 当前截图为普通奖励；无持久化证据 |

**重要限制**：普通 botbase 的静态读取和定时快照不提供 PC、临时 `x23` 或安全的代码段写计数；不能因为 snapshot 稳定就将 B/C 标成成功或失败。此前没有游戏侧 telemetry 的缺口仍然存在。

## 已开发的诊断功能

- `RemoteHookIntegritySnapshot`：只读核对入口、完整 reader、epoch 前后稳定性、45 行 SHA、subject 分布、与本地编辑器 SHA。结果命名 `CONTROL_PLANE_INTEGRITY_ONLY`，**绝不记录虚构的 HOOK_HIT**。
- 诊断窗口每次团战手动标记产生独立 `runId`，记录模式（单人、LDN 房主/参与者、官方互联网）、护符三态（有/无/未确认），战斗期间每 5 秒自动只读采样直至结算／最多 160 次。
- `RaidRewardTraceEvidence`：导入 PokéLDN 的 S1/S2 JSONL，对每个 run 独立计算 45 行完整 720B SHA，检查源→构建→wire loopback；`S2_receiver`、`S3_filter` 无独立数据即显示 `NOT_OBSERVED`。导入来源标记 `EXTERNAL_POKELDN_JSONL`，**不能冒充当前零售游戏 Hook**。
- 手动背包数量对照：记录 itemId、团战前/后数量和增量，字段固定为 `USER_REPORTED`，不能与自动背包扫描混淆。

## 下一步静态研究和实机验证

1. 先用本次完善的日志确定单人/LDN/官方互联网模式，确认游戏不在联网状态下进行受控实验。
2. 优先确定**实际函数是否进入、`RaidPoin` 保护条件是否满足**，之后才研究发奖函数和背包；不应该在这两个未知情况下盲目换地址或改大量道具。
3. 若目标确实为**单人离线奖励**，继续在 ELF 和 Lua 资源中定位本地 `RaidPoint` 生成、Lua 筛选及物品添加的回调链；现有 `0xE32984` 是出站 Bootstrap 序列化点，覆盖不到单人模式仍是合理可能。
4. 如果目标是**LDN 房主 → 参与者**，分别抓真实发包和接收对象，确认 `S1/S2/S3` 一致性；当前自动日志只能证明 A 节点。

最后再次说明：用户持有护符已确认。任何“没护符导致失败”的结论在此测试中都没有依据。
