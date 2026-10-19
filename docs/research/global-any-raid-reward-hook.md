# 全局奖励模板与任意场次的战斗构建 Hook（2026-10-19）

## 当前实现

- RaidCrawler 的奖励编辑器已从「选中一个坑位」解耦：即使没有读取坑位、没有选择 Seed，也能打开编辑器，配置 45 个物理奖励槽位。
- 模板存储位置：`%LOCALAPPDATA%\\RaidCrawler\\global-raid-rewards.json`。点击编辑器「写入奖励」会先保存这个全局配置，正常关闭编辑器也会持久保存；切换地图、扫描其他 Seed、关闭并重新启动程序后仍保留。默认仍为 20 行 999×Subject 5，余下 25 槽保持真正空白。
- 当前「写入奖励」的实际行为是 **准备模板 + 启动诊断窗口**，不是执行 Switch 内存写入。此状态必须维持到实机证据验证。JSON/BIN 数据保持原 45×16 字节布局。
- 实机日志不再把 UI 的 `seed=321EE013/den=7` 当作目标：新的 `scope=all-raids` 日志表明模板适用于未来实际创建的对战。 `actualBattleSeed=null` 只表示当前没有原生回调，不是永远不存在 Seed。
- `RaidRewardHookContract.TryStageOffline` 不再要求 `SelectedSeed=ObservedSeed`，也不要求本地 LDN 或官方互联网的某个预设模式。仍要求：匹配的 4.0.0 Build ID、独立 SessionId、本机房主角色真实确认、正确的**发送方向**、还未压缩的 0xAA0、合法 RaidPoint 前缀，以及同一场只处理一次。
- `NativeRaidCodeFingerprint` 包含原始 ELF 已核对的 8 条机器指令（其中 `main+0xE32984` 位于尾部复制之后、后续处理之前）。实机诊断只在标题为**紫**且游戏版本等于 4.0.0 时，用 SysBot 读取这 8 组指令并给出 `MATCH/MISMATCH`。**这是静态代码身份确认，不是 Hook 命中。**

## 原始 ELF 对真实 Hook 的价值与限制

经 `llvm-objdump` 再次检查原始 `Violet4.0.0.elf`：
```text
main+0xE327FC  serializer entry
main+0xE32814  mov x20,x0          ; 输入对象
main+0xE32828  mov w22,#0xAA0      ; 明文长度
main+0xE3283C  mov x23,x0          ; 输出明文缓冲区
main+0xE32968  add x1,x20,#0x78   ; RaidPoint 的剩余结构字段
main+0xE3296C  mov w2,#0x3C8
main+0xE32980  bl memcpy          ; 复制尾部
main+0xE32984  mov x8,x21          ; 复制之后的候选观察时刻
main+0xE32990  mov x1,x23
main+0xE32994  mov w2,w22
main+0xE329B0  b 0x1592B7C       ; 后续压缩/打包链
```
`x20+0x13C` 是输入对象第 0 行奖励；`x23+0x79C` 是输出缓冲区第 0 行奖励，总计 `0x2D0` 字节。**这些寄存器仅在本次函数执行中有意义**；对其运行时修改需要真实 Switch 侧 ARM64 Hook，不能通过多次 sys-botbase 轮询或写入某个固定数值来代替。

## 如何从现有版本升级到真实 Hook

1. 仅在自己的合法研究环境，先对单人／本地 LDN 运行**只读执行级追踪**，检查构建对战时 `E32984` 是否触发及正在处理的当前对象。必须与发送方向与实际主机角色关联，避免把接收端对象当成主持数据。
2. 有可靠数据后，在 Switch 侧使用具备安全暂停／恢复能力的原生执行拦截模块，于目标缓冲区生命周期内调用 45 槽替换；检查对象长度、`RaidPoint_`、实际会话 ID、指令指纹、线程及重入状态。
3. 注入的目标是**当时正在创建的那一场对战**，与 RaidCrawler 曾浏览的坑位无关。只有 Native Callback 能确认新对战后才标记 `HOOK_HIT`、`PATCH_APPLIED`、`PATCH_VERIFIED`，失败必须回滚；UI 不得自行猜测。
4. 对单人、本地 LDN 和互联网连接模式分别验证客户端实际收到的配置，及结算后的道具增量与存档。显示 999 仍可能受到道具 `SlotMaxNum` 的限制。

## 功能验收

- [x] 任意坑位均可打开全局奖励编辑器，不需读取／选坑
- [x] 45 槽全局配置以独立 JSON 原子保存／加载
- [x] 选择坑与真实对战 Seed 不一致时不再被离线 Hook 契约拒绝
- [x] 本地／互联网模式不再作为离线契约的硬阻断条件
- [x] 实机静态 8 条 opcode 只读检测及 JSONL 记录
- [ ] **Switch 侧原生执行级 Hook**：尚未安装，当前没有原生 `HOOK_HIT`
- [ ] 本机作为房主时的真实战斗构建回调证明
- [ ] 将写入结果通知给其他参战玩家，并验证入包／保存

**这份更新并未完成真实游戏里的 Hook 或奖励内存写入。** 若日志只有 `scope / config / opcode / fingerprint / manual-*`，不能将其解释为真实领奖成功。
