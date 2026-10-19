# RaidCrawler · 《紫》4.0.0 原生战斗奖励 Hook（实验）

> **交付范围：已实现 Skyline ARM64 inline callback 源码；尚未用实际 Switch 验证联机／本地结算。** 只有 `HOOK_HIT` 能证明执行时命中，不代表其他玩家已收到奖励。旧的 Windows 程序只有 sys-botbase 读写能力，不能单独安装原生 Hook。

## 1. 精确的 ELF 位置

只适配原始 `Violet4.0.0.elf`（SHA256 `d17403eaa9d844db82f647fd2f1518806b10c0518d53602fc0ac1766298fdba0`；Build ID `709bfd66115298640155fcc4979dba151c7cc79a`；Title ID `01008F6008C5E000`）。

已从原始 ELF 直接核对：

```asm
main+0xE32960  add x0,x23,#0x6d8    ; output RaidPoint remaining 0x3C8 bytes
main+0xE32968  add x1,x20,#0x78
main+0xE3296C  mov w2,#0x3c8
main+0xE32980  bl  memcpy
main+0xE32984  mov x8,x21           ; INLINE HOOK (last memcpy completed)
main+0xE32990  mov x1,x23
main+0xE32994  mov w2,w22           ; 0xAA0 bytes of plaintext
main+0xE329B0  b   0x1592B7C       ; next serialization/compression chain
```

Hook 在 `main+0xE32984`，从 `InlineCtx.registers[23]` 获取仅**本次函数执行时有效**的 `0xAA0` 输出缓冲区（不可缓存指针），检查 `[x23+0x6B8]` 的 `RaidPoint_` 标志，定位 `[x23+0x79C, x23+0xA6C)` 共 `0x2D0=720` 奖励字节。复制 45 槽配置、读回对比，不一致则恢复备份。除奖励区以外没有预期修改，原版继续从 `0xE32984` 执行。

此指令位于**一种战斗初始化序列化路径**，不是已经完成的跨模式／跨补丁版本 Hook 兼容证明。不是所有游戏版本甚至所有模式都保证执行此函数。高层对战奖励 API 路径及 Nintendo 官方服务端行为仍需实机核查。

## 2. 配置和启动

源码位于 `native/sv_raid_reward_hook`。需要 Atmosphère、兼容的 Skyline loader、Rust + `cargo-skyline`（以及正常使用的插件启动环境）。**不要用于非《紫》4.0.0 或未核验的升级版本。**

```bash
cargo install cargo-skyline
cd native/sv_raid_reward_hook
cargo skyline update-std
cargo skyline build --release
```

得到 `.nro` 后按 Skyline 的插件部署规则复制到 SD：

```text
/atmosphere/contents/01008F6008C5E000/romfs/skyline/plugins/raidcrawler_sv_reward_hook.nro
/switch/raidcrawler/rewards.bin
/switch/raidcrawler/hook-mode.txt
```

PC 端新版 **奖励编辑器 → 写入奖励** 会准备 `%LOCALAPPDATA%\RaidCrawler\native-hook\rewards.bin` 和 `hook-mode.txt`。将两个文件复制到 Switch SD 卡的 `switch/raidcrawler/`，然后**重启游戏**使原生插件读取配置。若已运行的插件需要切换配置，必须重启游戏；不会从 Windows 程序自动推送到 Switch。运行时不会从任意公网 URL 更新配置。

- `hook-mode.txt = observe`（默认）：安装 Hook，遇到构建事件仅记录 `HOOK_HIT`，不写入。
- `hook-mode.txt = apply`：显式授权当前游戏版本与当前实验，把 720 字节奖励表写入**正在序列化的这场对战**；不是固定地图或 Seed。
- 删除或改回 `observe`，并**重启游戏**即可取消写入。若签名、`RaidPoint_` 标志或 720 字节格式不匹配，则不会安装或应用 Hook。

插件日志（尽力写入，另有 Skyline 输出）：

```text
/switch/raidcrawler/hook.log
```

重要事件：`START`、`ELF_FINGERPRINT`、`CONFIG_VALID`、`MODE observe/apply`、`NATIVE_HOOK_INSTALLED`、`HOOK_HIT`、`PATCH_VERIFIED`、`PATCH_VERIFY_FAILED`、`PATCH_REJECTED`。**只有 `PATCH_VERIFIED` 才表示本地输出缓冲区字节核对通过；并不等于参与者收到数据或保存成功。**

## 3. 两阶段实机验收

先**仅观察**单人及本地 LDN：

1. `observe` 启动 `《紫》4.0.0`。检查 `ELF_FINGERPRINT verified 8/8`、`NATIVE_HOOK_INSTALLED`。
2. 任选普通坑（不必让 RaidCrawler 选中同一坑），开启团体战。若命中，应看到 `HOOK_HIT seq=... mode=observe`。若未命中，即这个游戏路径没有经过已确定的序列化函数，需要另寻构建入口，**不应盲写内存**。
3. 只有观察后，在自己授权的测试环境改为 `apply` 并重启。配置初期建议只填一行小数量的非稀有道具，不宜直接用 20 行 ×999 作为首次验证。
4. 查 `PATCH_VERIFIED` 和实际结算、背包增量、保存再读取。再用无护符/持有晶耀护符以及 Subject 0/1/2/3/4/5 分别对照。
5. 最后单独评估官方互联网房主是否触发相同 serializer，以及其他参与者实际获得的内容。不要推断任天堂服务端已接受或绕过任何校验。

**恢复：** 有异常时退出游戏，删除插件 NRO（或设为 `observe` 并重启）即可禁用。插件只改运行时序列化输出，不触碰太晶坑持久化 Seed 记录；但游戏崩溃、异常奖励影响存档等风险仍需提前备份并谨慎实测。

## 4. 开发限制

- 日志写入失败不会影响基础游戏逻辑，但在即时 Hook 中写 SD 会增加延迟，正式版需要进一步把日志写入异步环形缓冲区。
- 本版本没有区分所有网络传输路径，也没有确认该回调每场必触发；仅使用**准确的原版《紫》4.0.0 ELF 指纹**。
- PC 发起的模板准备与 Switch `.nro` 之间目前使用**手动 SD 文件传输**；未实现通过 sys-botbase 写入 SD。
- 与其他在同一 RVA 安装 Hook 的插件可能冲突；不要叠加未知 EXEFS 补丁。
- 需要通过 native CI 的 cargo-skyline 构建与实机测试才能把目前的源码升级成已验收二进制。

## 5. 代码与研究

- `src/lib.rs`：Skyline A64InlineHook、构建时 x23 指针、8 条指纹、备份／替换／读回／回滚以及日志。
- `src/format.rs`：独立可测试的 45×16 字节解码、字段约束及 0xAA0 差分替换。
- `../../docs/research/global-any-raid-reward-hook.md`：为何要用全局模板，而不是选中 Seed。
- `../../docs/research/live-raid-hook-diagnostics.md`：PC 端只读环境诊断；不能替代游戏内原生 Hook。
