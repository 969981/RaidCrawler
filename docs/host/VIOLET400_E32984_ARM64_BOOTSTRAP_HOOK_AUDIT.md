# Violet 4.0.0 `main+0xE32984` ARM64 指令级审计：RaidPoint → Bootstrap → 压缩前 Hook

> **审计日期：2026-10-20；范围：真实《宝可梦 紫》4.0.0 ELF 的静态代码及当前 `RaidCrawler/host`。** 本文区分原生反汇编能直接证明的事实、PokéLDN 的已知联网协议研究、以及尚需真机验证的行为。**未修改游戏资源 data.bin，也未在本次工作中运行实际 Switch 房主。**

## 0. 核心结论

`main+0xE32984` 本身只有一条指令：**`mov x8, x21`**（机器码 **`AA1503E8`**）。它**不是奖励生成器、LZ4 压缩器、0x2F 报文发送函数，也不执行内存奖励写入**。

选择此处作 Hook 的根本理由：**前一条 `main+0xE32980` 已从 `memcpy@plt` 返回**，整块 `0x3E8` 字节 RaidPoint 此时已复制进 `0xAA0` 字节的 Bootstrap **明文输出副本**；而下一阶段 `main+0xE329B0` 尾调用原版 `0x1592B7C`，进一步进入压缩路径。可在这一同步调用期间获取 `x20`（源序列化对象）和 `x23`（输出缓冲），不再需要 PC 端慢速猜测堆指针。

这种 **post-memcpy / pre-compression** 选择只是经过独立静态证据支持的合理 Hook 候选。**真正开坑发包、接收方使用新奖励、实际保存，仍必须分阶段实机验证。**

## 1. 使用了哪一份真实文件

已从用户 Library 取出原始文件 `Violet4.0.0.elf` 并在本地运行 `sha256sum`、`readelf -n`、`llvm-objdump`，而不是引用别人粘贴的伪代码：

| 属性 | 已核验数值 |
| --- | --- |
| 文件长度 | **74,362,480 字节** |
| ELF | AArch64、ELF64 little-endian |
| SHA-256 | `d17403eaa9d844db82f647fd2f1518806b10c0518d53602fc0ac1766298fdba0` |
| GNU Build ID | `709bfd66115298640155fcc4979dba151c7cc79a` |
| 序列化函数入口 RVA | `main+0xE327FC` |
| Hook 指令 RVA | `main+0xE32984` |
| 同步模式 | `main + RVA` 是主程序映像**相对偏移**，非绝对虚拟地址 |

本地复现：

```bash
sha256sum Violet4.0.0.elf
readelf -n Violet4.0.0.elf
llvm-objdump -d --start-address=0xE327FC --stop-address=0xE329B4 Violet4.0.0.elf
llvm-objdump -d --start-address=0x1592B7C --stop-address=0x1592EB8 Violet4.0.0.elf
```

仓库现有的 `tools/host/audit_violet_elf.py` 也能校验该 ELF 指纹及 17 个已审计的 Native 安装保护指令。**不要把用户 Library 中《紫》的地址、Build ID 套用到《朱》或其他版本。**

## 2. 对 0xE32984 的逐指令解释：谁在读，谁在写，什么时候复制完

以下均为**本轮直接从真实 ELF 输出**的 ARM64 指令：

```asm
; 输出缓冲已由之前的分配器取得，x23 = 指向 0xAA0 明文的运行时指针
E3295C  ldp x8, x9, [x20, #0x68]     ; 读入源 RaidPoint 的一部分
E32960  add x0, x23, #0x6D8          ; memcpy(dst)  = 输出+0x6D8
E32964  ldp x11, x10, [x20, #0x58]  ; 读入源 RaidPoint 最前的 16B
E32968  add x1, x20, #0x78           ; memcpy(src)  = 源+0x78
E3296C  mov w2, #0x3C8              ; memcpy(size) = 968 bytes
E32970  str x11, [x23, #0x6B8]      ; 输出 RaidPoint 的前 8B
E32974  str x9,  [x23, #0x6D0]      ; 前 0x20B 的第 4 个 8B
E32978  str x8,  [x23, #0x6C8]      ; 前 0x20B 的第 3 个 8B
E3297C  str x10, [x23, #0x6C0]      ; 前 0x20B 的第 2 个 8B
E32980  bl  0x343C210               ; memcpy@plt；返回时 0x3C8 复制完成
E32984  mov x8, x21                 ; ★ 被选为 Hook 的原生指令
E32988  mov x0, x20
E3298C  ldr x25, [sp, #0x10]
E32990  mov x1, x23                 ; 完整 0xAA0 明文数据
E32994  mov w2, w22                 ; 0xAA0（2720）
E32998  mov x3, x19
E3299C  ldp x20, x19, [sp, #0x40]
E329A0  mov w4, #1
E329A4  ldp x22, x21, [sp, #0x30]
E329A8  ldp x24, x23, [sp, #0x20]
E329AC  ldp x29, x30, [sp], #0x50
E329B0  b   0x1592B7C               ; 尾调用编码/压缩分支
```

### 2.1 复制边界是怎样严格闭合的

原始源 RaidPoint 的起始地址是 **`x20+0x58`**；输出 RaidPoint 是 **`x23+0x6B8`**。

1. 四条 `str` 先覆盖 `[x23+0x6B8, x23+0x6D8)`，共 `0x20` 字节。
2. `memcpy(x23+0x6D8, x20+0x78, 0x3C8)` 复制剩余字节。
3. `0x20 + 0x3C8 = 0x3E8`；输出目标末尾 `0x6B8+0x3E8=0xAA0`，**正好是完整明文 Bootstrap 的末尾**。
4. 输入源区终点 `0x58+0x3E8=0x440`，紧挨源序列化对象的下一个已知字段。

所以在 **`0xE32984` 第一时间**，无需猜测原版生成奖励的时间：**这个函数已将自己接收到的 RaidPoint 副本完整复制完毕。** 但仅由此不能证明它一定是「当前目标房主房间」或「一定马上实际网络发包」。

### 2.2 该指令 `mov x8,x21` 的真实用途

函数前部有：

```asm
E32814  mov x20, x0
E3281C  mov x21, x8
E32828  mov w22, #0xAA0
E3282C  mov x19, x1
E3283C  mov x23, x0
E32844  mov w24, #0x158
```

`0xE3281C` 将进入该函数时的 `x8` 暂存为 `x21`，到 `0xE32984` 再恢复给 `x8`。由于调用中 `x8` 是易失寄存器、而 ARM64 AAPCS64 使用 `x8` 传递某些**间接返回值的位置**，此处的成对操作符合调用约定。下游 `0x1592B7C` 又把 `x8` 保存到 `x25` 并在 `0x1592DAC` 相关逻辑中继续使用，因此**不可将此处 `x8` 当作游戏奖励指针，也不可让 Hook 破坏原版 `mov` 的执行语义**。

特别注意：如果 InlineHook 回调是在**原指令执行前**运行，则回调看到的 `ctx->X[8]` 仍是当前执行现场的 `x8`，不保证已等于 `x21`。真正需要的是此处的 `ctx->X[20]` 和 `ctx->X[23]`；它们由函数早先固定赋值。

## 3. 整包 0xAA0 不是只包含 RaidPoint

原函数有五次 `0x158`（344B）队伍记录处理：`x20` 的 `+0x440/+0x448/+0x450/+0x458/+0x50` 等成员被拷贝到 `x23` 中，随后才追加 `0x3E8` RaidPoint。这给出如下布局：

| `x23` 相对地址 | 长度 | 含义 |
| --- | ---: | --- |
| `+0x000` | `0x158` | 第 1 个参与者队伍记录 |
| `+0x158` | `0x158` | 第 2 个参与者队伍记录 |
| `+0x2B0` | `0x158` | 第 3 个参与者队伍记录 |
| `+0x408` | `0x158` | 第 4 个参与者队伍记录 |
| `+0x560` | `0x158` | Boss 队伍记录 |
| `+0x6B8` | `0x3E8` | RaidPoint：奖励、星级、Boss 摘要等 |
| **总计** | **`0xAA0`** | **原生编码/压缩前的明文** |

该布局与 [PokéLDN 当前源码](https://github.com/Decryptu/pokeldn/blob/main/pokeldn/sv/raid.py) 的 `BOOTSTRAP_SIZE`、`RAIDPOINT = 5 * gen9.SIZE_PARTY` 以及 [研究文档](https://github.com/Decryptu/pokeldn/blob/main/docs/sv_raid.md) 中 0x2F LZ4 Bootstrap 的静态格式一致。**格式一致不等于本次已捕获到真实网络 0x2F。**

## 4. 奖励具体定位：源对象地址与输出对象地址

PokéLDN `raid.py` 使用：

```python
POINT_REWARDS = 0x0E4         # item, quantity, rare, subject
POINT_SUMMARY = 0x3B8
REWARD_ROWS = (POINT_SUMMARY - POINT_REWARDS) // 16  # 45
struct.pack_into("<IIII", out, POINT_REWARDS + index * 16, *row)
```

因此：

| 区域 | 地址表达式 | 实际含义 |
| --- | --- | --- |
| 游戏代码位置 | `mainBase+0xE32984` | **指令地址**，不是数据位置 |
| 源 RaidPoint | `x20+0x58` | **本次调用**的输入数据 |
| 源奖励第 0 行 | `x20+0x13C` | `x20+0x58+0xE4` |
| 明文副本 RaidPoint | `x23+0x6B8` | 当前输出 `0xAA0` 内的 RaidPoint |
| 明文副本奖励第 0 行 | `x23+0x79C` | `x23+0x6B8+0xE4` |
| 明文副本奖励第 i 行 | `x23+0x79C+0x10*i` | `0 <= i < 45` |
| 奖励区域结束（不含） | `x23+0xA6C` | 45×16B = 720B |
| 非奖励保留区 | `x23+0xA6C..0xA6F` | 不能当第 46 行 |
| RaidPoint 摘要开始 | `x23+0xA70` | `0x6B8+0x3B8` |

每行的四个小端 `u32` 是 **`[ItemId, Quantity, RareFlag, SubjectType]`**。历史旧写法 `RaidPoint+0xE0`、`[subject,item,qty,reserved]` 会造成**四字节错位**，不可用于修改；详见 [Bootstrap 发送副本重构文档](BOOTSTRAP_SEND_COPY_REFACTOR.md)。

**例：** 假设某次实机捕获中 `x23=0x……`，不能由固定 RVA 推出具体 `x23`；必须在 `0xE32984` Hook 的同步 ARM64 寄存器现场读取它。即使已经通过 PC 的 `GetMainNsoBaseAsync` 得到了代码段基址，也不能反推出瞬时的 `x20/x23`。

## 5. 往下追踪：原版确实进入压缩调用链

`0xE329B0` 是 **`b 0x1592B7C` 尾调用**；该函数读取传进来的 `w4=1` 并走对应分支。

从真实 ELF 观察到：

```asm
1592BC0  cmp w4, #1
1592BC8  b.eq 0x1592CAC
1592CAC  add x8, sp, #0x18
1592CB4  mov x0, x23
1592CB8  mov x1, x26
1592CBC  mov w2, w24
1592CC0  bl  0x1592DAC

1592DAC  ...                         ; 分配并组织压缩结果
1592E10  mov x0, x21                 ; 明文输入
1592E14  mov w2, w20                 ; 明文长度
1592E20  bl  0x1592E80

1592E80  ...
1592E90  mov w3, w2
1592E94  mov x2, x1
1592E98  mov x1, x0
1592E9C  mov x0, sp
1592EA0  mov w5, #1
1592EA4  bl  0x71D940
```

ELF 直接证明了 **`E329B0 → 1592B7C → 1592DAC → 1592E80 → 71D940`** 的静态调用关系；`0x1592E80` 准备压缩所需工作区，配合 PokéLDN 逆向资料，该分支属于 LZ4 编码路线。

**严格区分：** `0x1592B7C` 是**明文结果编码/压缩包装函数**，不宜直接称为“网络发包函数”；当前反汇编没有独立完成由这一调用实例到一次**确实发送给参与者**的 `0x2F` 网络报文的动态数据关联。没有实机报文，就不能宣称已经证明联机奖励注入成功。

## 6. exlaunch InlineHook 对 `mov x8,x21` 实际做了什么

当前 `switch/host-probe/build.sh` 固定 `shadowninja108/exlaunch@f9f4b0dd07b68f97958cb9c79228bbca22ca80d5`。

对应的 [inline.hpp](https://github.com/shadowninja108/exlaunch/blob/f9f4b0dd07b68f97958cb9c79228bbca22ca80d5/source/lib/hook/inline.hpp) 将 **`GetTargetStart()+HookRva`** 传入 `HookInline`；[inline_impl.cpp](https://github.com/shadowninja108/exlaunch/blob/f9f4b0dd07b68f97958cb9c79228bbca22ca80d5/source/lib/hook/nx64/inline_impl.cpp) 创建 Entry / trampoline；[inline_asm.s](https://github.com/shadowninja108/exlaunch/blob/f9f4b0dd07b68f97958cb9c79228bbca22ca80d5/source/lib/hook/nx64/inline_asm.s) 将 `x0..x29` 保存到 `InlineCtx`，回调后恢复，LR 单独保存：

```text
main + 0xE32984 被 InlineHook 修改为跳转
       ↓
exlaunch Entry → 保存寄存器 → 回调 RaidBootstrapReadOnly::Callback(ctx)
       ↓                                 ↓
       ↓                      x20 = 本次源序列化对象
       ↓                      x23 = 本次输出 0xAA0 明文
       ↓                      仅读 720B 或在特许实验中改写空行
       ↓
恢复寄存器 → trampoline 重放被覆盖的原版指令（包含 mov x8,x21）
       ↓
原版参数设置和尾调用 → 后续编码/压缩链
```

**「默认只读」指不写业务奖励数据；安装 InlineHook 本身仍然会改写游戏的可执行代码，并有崩溃风险。** 必须保持原指令语义、现场寄存器、栈帧和转移目标完整。

## 7. 当前 host Native/PC 的代码状态（直接检查源码）

目标文件：

- [switch/host-probe/source/program/main.cpp](../../switch/host-probe/source/program/main.cpp)
- [RaidCrawler.Core/Host/HostProbeMailbox.cs](../../RaidCrawler.Core/Host/HostProbeMailbox.cs)
- [RaidCrawler.Core/Host/HostProbeLocator.cs](../../RaidCrawler.Core/Host/HostProbeLocator.cs)
- [RaidCrawler.Core/Host/BootstrapSendCopyPlanner.cs](../../RaidCrawler.Core/Host/BootstrapSendCopyPlanner.cs)

**Native：**

1. `HookRva=0xE32984`，`SourceRows=0x58+0xE4=0x13C`，`WireRows=0x6B8+0xE4=0x79C`。
2. 启动时核对 17 条指定原始机器码，指纹异常时拒绝安装 Hook；**17 条指纹不等于动态验证了完整 Build ID**。
3. 只有状态启用才开始读取，捕获双方奖励各 720B 到 **v3 邮箱 `RCHMB03\0`（0x800B）**；检查 `generation` 防止 PC 读取跨局混合数据。v3 在原有 0x600 前缀后追加批量配置区，但**并未包含完整的两份 0x3E8 RaidPoint**。
4. 追加前做 `memcmp(x20+0x58, x23+0x6B8, 0x3E8)`，并检查源奖励 45 行的合法性和连续空槽。
5. CI 默认 Native **不会编译奖励写入代码**；只有显式 `HOST_N2_EXPERIMENTAL=1` 编译才具有真正写入能力。旧模式 1/2 限制单行先机球（道具 ID 15）×1–10、`subject=5`。**最新 v3 已新增模式 3/4：最多 28 行 subject=5 批量预览/实验写入协议**，请求有 nonce、FNV1a 校验和及容量检查；这仍是未经实机确认的代码能力，不等于实际可发送或 999 道具必然到账。
6. 单行及 v3 批量写入均只针对 `x23+0x79C + used*16` 起的未占用奖励槽；不改 `x20` 源 RaidPoint、存档坑块或已压缩的 LZ4 报文。batch 模式在全部条目校验通过后才一次性写入。

**PC：** 仍通过已安装的 `sys-botbase` 读/写**Native 自己的内存邮箱**，通过 Hook Entry 和编译配对的 `mailbox-locator.json` 定位邮箱。**不是纯 sys-botbase 的“无额外模块方案”**：若未加载 `subsdk9`，PC 没有 Native 回调可观察、也找不到这个邮箱。旧 `SysbotOnly` 扫描/候选源 RaidPoint 写入属于另一条未完成动态关联的诊断路线。

**当前弱点 / 待优化：**

- Native 比对**完整 `0x3E8` RaidPoint**，但当前 **v3 邮箱虽已扩展为 0x800B，仍仅保存两份 720B 奖励数组**；新增的 0x200B 是批量控制区，不是完整 RaidPoint 样本。PC 的奖励数组一致报告不能**独立证实**整个 RaidPoint 一致；可在后续邮箱版本加入全 RaidPoint 摘要或完整样本。
- 本机 `status=1` 只代表安装动作已完成；`count>0` 只说明 Hook 命中一次；它们不自动证明命中“真实房主发出的那一个 0x2F”。
- Native 没有持续保存 `x20/x23` 对象生命周期和消息种类的证据；只在同步回调有效，PC 不应将其作为可靠、跨局长期固定指针。
- 当前代码仅做非零、最低地址和 8 字节对齐的基本指针检查；如果运行时地址与预期类型不符仍存在内存访问崩溃风险。应先执行只读 N1，不能直接启动写入试验。
- 如果游戏存在其他 ExeFS Mod/相同 `subsdk9`，Hook 点和 trampoline 可能冲突；未核实兼容性时禁止覆盖。
- 官网互联网服务、PokéLDN 模拟房主以及零售机局域网房主必须分别证明，不能以其中一种结果替代另两种。

## 8. 可以证实到哪一步：实机验收表

| 门槛 | 要证明什么 | 目前 |
| --- | --- | --- |
| A. ELF 与原始指令 | Build ID、`0xE32984=mov x8,x21`、完整副本长度 | **已完成（原始 ELF）** |
| B. 调用链与字段 | 5×`0x158` + `0x3E8`、`0x79C` 首奖励行、进入压缩链 | **已完成静态研究** |
| C. Native 编译 | 对应 `subsdk9` 和 PC 客户端编译、离线测试 | **之前 CI 有成功记录；此次文档提交不代表新的 CI 成功** |
| D. N1 真实房主 | 游戏正确启动、Hook 命中、同局两份奖励数据一致 | **未验证** |
| E. N2 受控追加 | 写前后审计，原版奖励无损、只新增一行、输出明文正确 | **未验证** |
| F. 对端 0x2F | 真实参与者接收到改动后的 Bootstrap | **未验证** |
| G. 掉落/持久化 | 有/无护符、奖励、背包和保存重进分别正确 | **未验证** |
| H. 最多 28 行批量奖励 | v3 模式 3/4 已有代码；容量、背包行为、多人结果全验证 | **代码已接入，实机未通过** |

对实验返回值要正确解读：

- `1`：源/副本奖励一致；**不代表有网络发送**。
- `10`：当前 Native 预览允许增加单行；**不做游戏奖励写入**。
- `14`：完整副本或奖励与源不一致；**拒绝写入**。
- `15`：当前构建不允许写入。
- `20`：Native 在本机输出明文空行追加了一行；**不是接收端到账证明**。

### 推荐验证顺序

1. Switch 上确认《紫》`4.0.0`、准确 Build ID，安装**对应版本的只读 `subsdk9`**。主程序经 `sys-botbase` 自动定位已安装 Hook 邮箱，**不用进行堆扫描**。
2. 实机真实房主开启一局普通团体战，N1 只读记录 `count`、`generation`、奖励行及是否完整一致；异常即停止。
3. 同一时刻由参与者一侧观察 Bootstrap 及原版奖励，证明所捕获数据与该局真实 `0x2F` 关联。
4. 再用显式单行实验构建，不改源 `x20`、仅改发包明文 `x23` 第一空槽；对端逐项验证其收到的数据、护符过滤、战后道具和存档重进。
5. 当前 `host` 的 **v3 已新增最多 28 行批量配置和可选 Native 写入代码**，而且公开 CI 默认不包含写入分支。只有以上完整链路通过，才能认为 [多行规划器](../../RaidCrawler.Core/Host/BootstrapSendCopyPlanner.cs) 对应的动态批量奖励可用；20×999 目前仍**未经 Switch 实机/接收者/持久化验证**。

## 9. 参考文档

- [PokéLDN battle bootstrap / `0x2F`](https://github.com/Decryptu/pokeldn/blob/main/docs/sv_raid.md)
- [PokéLDN raid.py 当前奖励行序列化](https://github.com/Decryptu/pokeldn/blob/main/pokeldn/sv/raid.py)
- [exlaunch 固定提交的 inline.hpp](https://github.com/shadowninja108/exlaunch/blob/f9f4b0dd07b68f97958cb9c79228bbca22ca80d5/source/lib/hook/inline.hpp)
- [exlaunch InlineHook Entry/trampoline 构造](https://github.com/shadowninja108/exlaunch/blob/f9f4b0dd07b68f97958cb9c79228bbca22ca80d5/source/lib/hook/nx64/inline_impl.cpp)
- [exlaunch ARM64 保存/恢复寄存器](https://github.com/shadowninja108/exlaunch/blob/f9f4b0dd07b68f97958cb9c79228bbca22ca80d5/source/lib/hook/nx64/inline_asm.s)
- [本仓库：ELF N1/N2 历史记录](ELF_REVALIDATION_N1_N2.md)
- [本仓库：Bootstrap 发包副本重构](BOOTSTRAP_SEND_COPY_REFACTOR.md)
