> **2026-10-20 新版主路径：** [Bootstrap 发送副本重构](BOOTSTRAP_SEND_COPY_REFACTOR.md)。当前 `host` 代码以 `RaidPoint+0xE4` 为第一条完整奖励行，字段为 `[ItemId, Quantity, RareFlag, SubjectType]`；`+0xE0` 不再作为写入依据。历史章节与先前的自相矛盾修订保留供追溯，**不代表目前代码 ABI**。默认只读。\n\n# 《朱／紫》4.0.0 RaidPoint 实证与 N1/N2 实施记录

> **2026-10-20 重要勘误（覆盖本页旧结论）**：PokéLDN 4.0.0 donor 的完整奖励记录从 `RaidPoint+0xE0` / `Bootstrap+0x798` 开始，四列为 `[condition_or_source, item_id, quantity, reserved]`；`+0xE4/+0x79C` 只是记录内部的道具 ID，不是完整行起点。以下旧版 `+0xE4 记录起点 / [Item,Quantity,Rare,Subject]` 的叙述**仅作为历史错误调查记录保存，不可作为写入指导**。最新版布局、subject0/5 和验证限制统一参见 [POKELDN_SUBJECT0_RETAIL_LAYOUT_TEST.md](POKELDN_SUBJECT0_RETAIL_LAYOUT_TEST.md)。


> 2026-10-20 · 证据层级：紫 ELF/上游 donor 字节 **已静态核验**；Native/WinForms **CI 编译可测**；Switch 运行与网络参与者 **尚未验证**。
> **旧文档以 RaidPoint+0xE0 为行起始以及 Marker/Item/Quantity/Reserved 的解释已经证伪，不能用于改写游戏奖励！**

## 输入的真实文件与可追溯指纹

- Library 中取得的 **Violet4.0.0.elf**，74,362,480 字节，SHA256 `d17403eaa9d844db82f647fd2f1518806b10c0518d53602fc0ac1766298fdba0`，GNU Build ID `709bfd66115298640155fcc4979dba151c7cc79a`。工具实际执行 `readelf -n` 与 `llvm-objdump -d`，不只引用别人分析。
- 在本次 Library 的大型文件列表中**仅找到紫版 ELF，没有找到朱版 4.0.0 ELF**。不能将紫版 RVA、ABI 断言和 Build ID 冒充为已验证的朱版。朱版 Native 接入保持 NO-GO，直到重新取得 ELF 并独立分析。
- PokéLDN 固定提交 `8b2c19c480400a6805eeeff266e13ded20c97475` 的 [sv_raid_reward_donor.bin](https://github.com/Decryptu/pokeldn/blob/8b2c19c480400a6805eeeff266e13ded20c97475/docs/research/sv/fixtures/sv_raid_reward_donor.bin) 经本轮直接 Raw-LZ4 解码：应用报文 1707B，解压 0xAA0 / 2720B。
- PokéLDN [encoder](https://github.com/Decryptu/pokeldn/blob/8b2c19c480400a6805eeeff266e13ded20c97475/bin/sv_raid_bootstrap_codec.py) 与 Lua reward filtering 文档是上游逻辑依据；模拟房主的修改不自动证明零售 Switch 房主、NSO 互联网和持久化有效。

## 1. 直接从紫 ELF 查到的序列化窗口

```asm
main+0xE3295C  ldp x8,x9,[x20,#0x68]
main+0xE32960  add x0,x23,#0x6D8
main+0xE32964  ldp x11,x10,[x20,#0x58]
main+0xE32968  add x1,x20,#0x78
main+0xE3296C  mov w2,#0x3C8
main+0xE32970  str x11,[x23,#0x6B8]
main+0xE32980  bl memcpy@plt
main+0xE32984  mov x8,x21  ; N1/N2 post-copy pre-compression hook
main+0xE32990  mov x1,x23
main+0xE32994  mov w2,w22 ; 0xAA0
main+0xE329B0  b 0x1592B7C
```

ELF 表明 `x23+0x6B8` 是 RaidPoint 的目标明文结构起点，`x20` 是尚未离开序列化函数的运行时输入对象；`x20+0x58` 及 `x20+0x78` 合起来复制 `0x3E8` 的 RaidPoint。此处 `main+RVA` 是代码偏移，**不是长生命周期全局数据地址**。Hook 插入到 `+0xE32984` 可以观察 0xAA0 压缩前的目标缓冲，但实际寄存器及内存生命期仍须实机验证。

## 2. 真正的 45×16B 奖励布局：必读勘误

```
RaidPoint:       bootstrap_plain + 0x6B8         size=0x3E8
reward row 0:    RaidPoint+0xE4   = plain+0x79C
reward row i:    RaidPoint+0xE4+16*i
total:           45 rows ×16=720 bytes
end exclusive:   RaidPoint+0x3B4 = plain+0xA6C
summary:         RaidPoint+0x3B8 and beyond (not reward rows)
struct row:      ItemId:u32 | Quantity:u32 | RareFlag:u32 | Subject:u32
native source:   x20+0x58+0xE4 = x20+0x13C
native output:   x23+0x6B8+0xE4 = x23+0x79C
```

**判定错误的简便方法：** 旧版代码把 `+0xE0` 当作行起点，读取四个 u32 时依次得到 `前缀/上一行 Subject, 本行 ItemId, Quantity, RareFlag`。在 donor 最后一条占用行中读到 `(5,0,0,0)`，是错位四字节造成的“伪特殊行”，不是一个额外的 Subject=5 无道具 metadata。

本轮对真实 donor 从正确的 `+0xE4` 开始解析：**21 条有效行**，第 0 行 `(1127,1,0,0)`，第 1 行 `(1128,1,0,0)`，第 20 行 `(1862,10,0,5)`，第 21~44 行清零，共 **24 个空位**。按 +0xE0 解码得到的 `(5,0,0,0)` 恰好是上一行的 Subject + 下一行零槽。本事实纠正了旧代码中“遇到 marker=5/item=0 必须拒绝”的错误理由（未知行仍需拒绝）。

## 3. N1 + 自动定位（零 SD 文件）

- Native exlaunch（紫 4.0.0）Hook: `main+0xE32984`，同步采样 `x20+0x13C`、`x23+0x79C` 各 720B 放入 sys-botbase 可读内存邮箱。
- 通过 `GetMainNsoBaseAsync` 取得游戏 main 起点，沿被 Patch 的 InlineHook `B` 或 `LDR/BR` 跳板进入固定编译版本 exlaunch JIT Entry；Entry+0x10 的 `m_Callback` 为真实 C++ Callback 地址。由 **同一原生构建**生成 `mailbox-locator.json` 的 `callback_to_mailbox_delta` 推出内存邮箱地址。
- Native 构建脚本运行 `aarch64-none-elf-nm -n -C` 解析 `RaidBootstrapReadOnly::Callback` 与 `rcprobe::g_mailbox` 符号，再打包清单。**必须与同次构建出的 subsdk9 配对**，不能在不同版本间复用 manifest。
- C# Host Probe 的「自动定位」按钮读取该 manifest + 当前 main Hook 指令 + Entry，验证 mailbox `RCHMB02\0`、v2 size、Title ID、hook status。任何异常时不给出猜测地址，不执行写入。
- 自动发现不代表完整 Build ID 已动态读取；当前只在 Native 启动时核对 17 条明确指令（新版包含 0xE32984 Hook 原始指令），另外需要测试机独立确认 Build ID。

## 4. N2 单行 Subject=5 开发（实验）

生产式默认 CI Native 产物**不包含奖励写入指令路径**，但具备配置与预览；手动显式 `HOST_N2_EXPERIMENTAL=1 bash switch/host-probe/build.sh` 才会编译实验写入分支，邮箱能力位上报 1。只有经过 N1 真实房主安全验收的测试机才应考虑这样构建。

```text
RaidCrawler C# 设置 ItemId=15(先机球) / Quantity=1..10 / RareFlag=0 / Subject=5
      ↓ 现有 sys-botbase 写入 mailbox +0x5E4..0x5F3，最后 arm +0x5E0
      ↓ Native 4.0.0 post-memcpy hook：先复制 Source 与 outgoing 两端原文
      ↓ 首先验证两个 720B 字节一致；按 +0xE4 正确解析45行
      ↓ 验证原行连续、字段合法，第一条空位有效，余量≥1
      ↓ mode=1：返回预览结果10，绝不写游戏对象
      ↓ mode=2：仅显式编译的实验 Native 将 <15,qty,0,5> 写入
                   0xAA0 明文中第一条空奖励行
      ↓ 游戏后续自己的 0x1592B7C 压缩和发送（实机尚未证明）
      ↓ C# 读回结果码20及两份720B，检查原版全部字节不变，
                   只有一个正确 subject5 行新增
```

此路径**不修改源 RaidPoint、不修改压缩后 LZ4、不是 PokéLDN 模拟房主**，且默认只读。当前实验品限定先机球 Item15 数量1–10，绝不能因为解析器是 u32 就认为所有物品可以 999。完整主体范围与 Bundle/SlotMaxNum 要后续验证。

N2 mailbox v2 相对原 v1：magic `RCHMB02\0`、version2、原头/两份720字节区不变；`+0x24` Native 构建能力位；`+0x5E0` 请求 mode (0 off,1 preview,2 write)，`+0x5E4` 四个 u32 请求字段，`+0x5F4` 结果码，`+0x5F8` 实验追加次数，`+0x5FC` 原版奖励行数。N2 不自动开启：mode=0，CaptureEnabled=0。

状态码：1 原版双端一致；10 预览可用；11 容量已满；12 原行布局不合法；13 请求参数不合法；14 双端不一致；15 当前二进制不具备写入能力；20 实验单行已写到发包**明文缓冲**（不证明接收者实际获得道具）。

## 5. 严格验收关卡

| 关卡 | 验收要求 | 现状 |
|---|---|---|
| A：静态与供体 | 紫 ELF 指令、Build ID、45行行起点、真实 donor row 20 的 Subject=5 | **完成静态核验** |
| B：编译和回归 | Windows & ARM64 CI；原奖励不变/校验失败不写入；自动定位跳板解析测试 | **可自动运行，不代表硬件测试** |
| C：N1 真实房主 | Native 能加载；自动定位正确；同场 Source 与 Bootstrap 原版奖励720B 完全一致 | **PENDING** |
| D：N2 preview | 一局实机普通坑，预览结果10、原版奖励完整 | **PENDING** |
| E：N2 最小写入 | 测试机原版全部保留、只额外一个 Subject=5，道具类型/数量正确 | **PENDING** |
| F：接收者/保存 | 双机，有/无晶耀护符对照；获胜 UI、Bag、存档重载完整保留 | **PENDING** |
| G：朱版支持 | 取得朱版 ELF，独立 SHA/Build ID/反汇编，不共享紫版未证实 RVA | **BLOCKED：缺 ELF** |
| H：官方 NSO | 单独对官方互联网房间验证合法性和两机数据流程 | **PENDING，不使用 PokéLDN 成功当证明** |

本仓库的代码提交与 GitHub CI 能证明**源码编译和离线字节契约**，无法替代真实 Switch 环境（网络发奖与实际 Bag）。若任一 runtime 证据不一致，立即停在只读/预览状态。
