# 《紫》4.0.0：不替换 sys-botbase 的 ARM64 代码段 Hook（离线可执行验证）

## 确认的关键发现

重新读取原始 `Violet4.0.0.elf`（SHA256 `d17403eaa9d844db82f647fd2f1518806b10c0518d53602fc0ac1766298fdba0`）：

- `.text` 虚拟地址 `0x0..0x343FC90`，具有 RX 加载属性；
- 后续只读 `.rodata` 从 `0x3440000` 开始；
- 二者虚拟地址间存在 **880B** 的页面对齐间隔 `[0x343FC90,0x3440000)`。这是*候选*，不是 ELF 明确分配的可执行空间；
- Hook 原指令 `main+0xE32984 = mov x8,x21`，ARM64 指令字 `AA1503E8`；
- 复制完成的游戏内 `0xAA0` 缓冲在 `x23`；45 槽奖励在 `x23+0x79C`；
- 到候选空隙的单条 ARM64 `B` 指令目标距离约 40 MiB，在 ±128 MiB 范围；到 `memcpy@plt=0x343C210` 亦可直接 `BL`。

这比寻找已使用函数内部的 `NOP` 区域靠谱，但**必须实机用只读 `peekMain` 核验 880 字节是否映射、是否全零，以及目标是不是 RX 页面**。此前仅发现 28B 的函数间零填充不能容纳可靠的跳板。

### 重要纠正：Atmosphère 的写入内核负责 I-cache 失效

已检查 [Mesosphere KPageTableBase::WriteDebugMemory](https://github.com/Atmosphere-NX/Atmosphere/blob/master/libraries/libmesosphere/source/kern_k_page_table_base.cpp)，在通过 debug SVC 拷贝数据、StoreDataCache 后明确调用 `cpu::InvalidateEntireInstructionCache()`，且注释说明“this svc allows modifying executable pages”。因此在**该内核实现及其可用的 debug 权限**下，`pokeMain` 有可能直接作为 ARM64 指令修补的写入通道，**无需替换 sys-botbase 或另增加独立刷新协议**。不同内核／权限仍须实机确认，且缓存同步不等于补丁执行成功。

## 本轮离线交付

`stub.S`：可由 clang/LLVM 汇编的 AArch64 内联跳板原型。它在候选尾部 880B 里以紧凑布局容纳：

| 相对偏移 | 大小 | 用途 |
|---|---:|---|
| `+0x00..+0x8F` | 144B | ARM64 处理代码、原始 `mov x8,x21` 搬迁与返回 |
| `+0x90` | 4B | 模式开关，初始 0（仅观察） |
| `+0x94..+0x9F` | 12B | 保留填充；**不得作为游戏内写入计数器**，因为该页预期为 RX |
| `+0xA0..+0x36F` | 720B | 45 行 `ItemId/Qty/Rare/Subject` 奖励表 |

代码原型使用 `BL memcpy@plt` 将 720B 拷贝到 `x23+0x79C`。在 `observe` 模式不会改写奖励，也**没有 Hook 命中计数**（RX 代码页不能被游戏直接写入）。恢复执行前重新执行被替换的原指令并跳回 `main+0xE32988`。

`build_offline_plan.py`：只能读取用户本地合法拥有的原始 ELF 和可选 `rewards.bin`，严格核对 SHA、段边界、原始操作码，并运行 clang + llvm-objcopy 生成：
- `cave-880.bin`：候选跳板，默认观察模式
- `hook-branch-4.bin`：入口处单条 A64 跳转指令
- `restore-original-4.bin`：被覆盖的 4B 原指令
- `manifest.json`：地址、尺寸、模板哈希、运行时验收条件和恢复原则

**脚本绝不连接或写入 Switch，无法证明任意游戏模式实际执行本回调。** 生成的二进制仅为研究候选，不是可以无条件安装的稳定 Hook。

本地复核（先在支持 clang/LLVM 的 Linux/WSL 上运行）：

```bash
python3 -m unittest discover -s native/sv_remote_code_patch -p 'test_*.py' -v
python3 native/sv_remote_code_patch/build_offline_plan.py /path/to/Violet4.0.0.elf --out ./offline-hook-plan
```

此前真实 71MB ELF 在本地验证时，离线脚本成功生成 **880 字节**候选，跳转往返地址和默认观察模式通过本地测试。

### 关键修复：不得在 RX 代码洞内维护计数

初版原型曾尝试在候选代码页写入 `HOOK_HIT` 等计数。经检查发现这是错误的：代码末尾候选区域属于可能的 **RX** 页，普通游戏 CPU 向这里执行 `str` 可能产生内存权限异常。现已删除所有在 Hook 中向 RX 页写入的指令，保留只读模式开关和奖励表。模式可由外部调试写入修改，但当前离线脚本默认 `observe=0`；没有可写遥测区就不能声称观察模式会回传命中计数。

## 尚未解决的问题（不应写进“完成”）

1. **运行时内存映射**：`.text` 的 `p_memsz` 只到 `0x343FC90`，段间空隙是页对齐空间，不是 ELF 的已定义可执行代码。必须在用户设备上只读查询该内存是否实际存在；还要确认页属性允许执行。若不存在，本方案直接失败，不能向 `.rodata` 或已使用函数盲写。
2. **用户设备上的 debug SVC 权限和其它补丁**：需验证当前 Title、游戏版本、NSO 完整身份、8 条操作码，并确保没有同时安装其他 Hook。旧版 `getBuildID` 只提供前 8 字节，必须搭配代码指纹。
3. **并发安装、回滚**：先完整安装并读回候选，再最后原子写入**单条 4B 跳转指令**，失败优先恢复原指令；卸载时先禁用模式、恢复跳转点、读回核对。游戏运行期间**不要擦除可能正被执行的跳板**。游戏重启自然清除 RAM 内补丁。
4. **功能验证**：当前未在 Switch 上运行，且没有安全的可写遥测缓冲区。原生跳板目前不能生成 `HOOK_HIT` 计数；需要后续寻找经过验证的 RW 状态区或独立执行追踪。即使实际调用 `memcpy`，也不等于奖励入包、存档成功。
5. **风险**：静态验证仍无法证明当前 serializer 出现在每一种普通坑、活动坑、本地/官方互联网构建流程；首次必须仅观察并进行受控单机验证。未经实机观察不应默认 20×999 自动生效。

## 结论

在现有 Atmosphère sys-botbase 情况下，**Windows 端修改代码段以完成游戏进程内 Hook，是一条合理的零额外插件方案**。它的实证阻断条件不再是“必须扩展 sys-botbase”，而是**末尾对齐空间的运行时映射、可执行性、单指令安装时序、原始指令恢复、实际 Hook 命中**。本轮仅提交**离线原型与可审计地址计算**，没有远程写入安装器，也没有实机验证。
