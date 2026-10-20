# RaidCrawler — Violet 4.0.0 实机 N0/N1 原生奖励观察器

**当前只观察，不修改奖励。** 这是针对真实 Switch 房主的 `0x012F` Bootstrap 序列化现场的研究代码；不会让参与者立刻得到新道具，也没有完成 N2 晶耀护符追加。必须搭配受支持的 Skyline 原生插件环境，单独运行 Windows 程序不构成 CPU 级 Hook。

## 已确认的版本、地址与工作方式

- 游戏：Pokémon Violet 4.0.0，Title ID `01008F6008C5E000`；ELF SHA-256 `d17403eaa9d844db82f647fd2f1518806b10c0518d53602fc0ac1766298fdba0`，Build ID `709bfd66115298640155fcc4979dba151c7cc79a`。
- AArch64 入口 `main+0xE32984`，插件启动时检查周边八处原始机器码；校验失败则不安装 Hook。原游戏会继续按正常逻辑发送数据。
- `x20+0x13C`：源消息中的 45×16B 原版奖励；`x23+0x79C`：复制到 `0xAA0` 明文 Bootstrap 中的 45×16B 奖励；`x23+0x6B8` 需以 `RaidPoint_` 开头。
- 每行字段：小端 `itemId(u32),quantity(u32),rare(u32),subject(u32)`。`main.blua` 静态研究已支持 `subject=5` 与晶耀护符重要物品 ID 2481 的资格联系。
- 采集数据逐槽记录 **全部 45 行**，包括非标准非零行、全零候选槽、各 subject 计数、原版/输出字节是否相同及 FNV64。全零槽不等于已证明可以安全追加。

## 和旧插件最重要的区别

**N0/N1 强制 `observe-only`。** 不管 `hook-mode.txt` 是否为 `apply`，都不会对游戏内存写入奖励。旧版 `apply` 是整块 720B 覆盖，可能破坏原版固定/随机/料理/护符奖励，现已在此阶段拒绝。`rewards.bin` 本阶段不使用。源码中保留的旧 `format.rs` 覆盖工具仅作离线测试，不被实时 Hook 调用。

## 编译及安装

原生 `.nro` 构建入口：[Native SV reward Hook workflow](../../.github/workflows/native-sv-hook.yml)。另有 [N0/N1 Rust 独立测试](../../.github/workflows/native-raid-observer-offline-tests.yml)；只有真正的 Skyline 原生构建成功才会输出可部署 `.nro`。若本地已有对应 Switch Rust 工具链，可在 `native/sv_raid_reward_hook` 运行 `cargo skyline build --release`。

将 **对应当前 N0/N1 提交的 `.nro`** 放到 Switch SD：

```text
/atmosphere/contents/01008F6008C5E000/romfs/skyline/plugins/raidcrawler_sv_reward_hook.nro
/switch/raidcrawler/hook-mode.txt     # 可选；observe，apply 会被强制拒绝
```

先创建 `/switch/raidcrawler/` 文件夹，关闭可能与同一 RVA 冲突的插件，然后重启游戏。实验前备份存档。

## 真实房主 N0/N1 操作

1. 启动匹配的《紫》4.0.0，检查 Switch SD 的 `/switch/raidcrawler/hook.log`：应能看到 `START N0_N1 OBSERVER`、`ELF_FINGERPRINT verified 8/8`、`MODE observe-only forced`、`NATIVE_HOOK_INSTALLED`。
2. 使用这台 Switch **当房主**，与另一台授权的 Switch 开一场本地 LDN 太晶团体战。从大厅准备到正式点击开始战斗，等待开始握手完成。PokéLDN 已记录 `0x0C`/`0x0D` 后、开战前发送 `0x2F`；玩家刚进入大厅时不应期待已发送。
3. 若 CPU 命中，`hook.log` 会记录 `HOOK_HIT seq=... stage=N0_N1`；同时 `/switch/raidcrawler/raid-native-rewards.jsonl` 生成一条含源/输出各 45 行的 JSON。若没有命中则保留日志，不启用旧整表覆盖。
4. 将两份日志拷到 Windows。在 RaidCrawler 的诊断窗口点击 **「导入实机 0x2F 奖励快照 JSONL」**，逐行比较原版 `x20` 与输出 `x23`。导入器会核验每行顺序、FNV64、数量统计和 `observe-only` 来源标志。
5. 记录本次是本地 LDN、是否为房主、星级及开战时刻（用户手工说明）。仅凭原生序列化回调不能自动判断 Nintendo 官方互联网方向，更不能确认参与者收包、实际背包增量或保存。

## 安全、隐私及运行限制

- 回调内部只有固定长度内存复制和 **有界通道 `try_send`**；不执行 SD 文件写入、JSON 格式化或 Lua 堆扫描。独立工作线程负责写日志。通道满时有 `N1_EVENTS_DROPPED`。
- JSONL 只含数字奖励行，不记录参与者 PK9、名称、账户或明文网络完整帧；FNV64 仅用于发现传输/复制不一致，不是取证签名。
- Snapshot 表明的是“加载的原生插件报告了一个执行现场”，不能从复制出的 JSONL 自动证明回调真实来源或玩家实收。
- 遇到异常退出游戏、删除该 `.nro` 并重启即可停止实验。仅适配同一版本和 Build ID；不要向其他版本移植硬编码 RVA。

## 后续 N2 仍需研究

只有获得真实房主 `HOOK_HIT`、原版/输出 45 行数据与有效空尾槽证据后，才研究新增单条 `subject=5, quantity=1` 并让第二台原版机验证；保留全部原版行且不跨越 45 行上限。**当前既未实测追加，也未修改任何道具。**

相关技术方案：[真实 Switch 房主晶耀护符奖励追加](../../docs/research/violet400-retail-host-charm-append-implementation-plan-20261020.md)。
