# RaidCrawler benefit · 技术研究索引

- [无需替换 sys-botbase：ARM64 代码段跳板候选和离线验证](../../native/sv_remote_code_patch/README.md)——已核验《紫》4.0.0 的 880B 虚拟地址间隔、直接 B/BL 跳转及 ARM64 汇编原型；尚未实机安装。

- [方案 A：Windows 远程安装 Hook 的能力边界与增强 botbase 设计](windows-remote-hook-feasibility.md)——源码核对 sys-botbase 调试读写、Build ID 前缀、缺少的寄存器/缓存/回滚能力、A0/A1/A2 对比和协议 P0–P5。

- [全局奖励模板与任意场次 Hook 边界](global-any-raid-reward-hook.md)——模板不再绑定某个 Seed／坑位，增加实机静态指纹检测与 Hook 待办。

- [实机奖励诊断与 Hook 日志格式](live-raid-hook-diagnostics.md)——在编辑器点“写入奖励”启动只读实机诊断，保存 JSONL、Main NSO 基址、版本及手动开房时间线；实际原生写入尚未安装。

- [官方互联网手动主持：奖励写入研究及 20+25 槽位验收](official-online-host-reward-write-plan.md)——明确 Nintendo 互联网与 PokéLDN 本地 LDN 的差异、当前实现、ELF 发送/接收证据、尚缺的运行时 Hook 证据。

- [PokéLDN × 《紫》4.0.0 ELF：单场太晶坑自定义奖励](pokeldn-elf-single-raid-hosting.md)：选中坑位与真实 Host CLI 参数、奖励注入时机、45 行布局、ELF 动态指针、Subject 5 晶耀护符过滤、背包保存、已验证/未验证范围。
- [Benefit 奖励编辑器和主持功能说明](benefit-reward-editor.md)：操作入口、功能和 CI 状态。

不包含原始游戏二进制、密钥或 Nintendo 官方在线房间修改工具。当前集成的主持模式是 PokéLDN 本地 LDN/特殊模拟器 LAN 路线。

## 《紫》4.0.0 Lua VM / 奖励 P1 逆向研究（2026-10-20）

- [Lua VM 主状态创建与全局引用链（P1-B 技术文档）](violet400-p1b-lua-vm-global-root-audit-20261020.md)：精确解释 `main+0x3B420`、`0x3B050`、`0x26075E0`、`0x1107AF4`、`0x1107B00` 与 `0x4759EB0`；列出已有只读探针和实机尚未完成的验证门槛。
- [Violet4.0.0.elf ARM64 反汇编与 Lua 状态指针技术审计](violet400-elf-lua-vm-state-pointer-reverse-engineering-20261020.md)：原始 ELF 样本哈希、PT_LOAD/BSS 映射、19 条机器码、3 个 BL 调用目标、字段偏移推导、命令行复核和避免将 Lua 栈 720B 误判为 RaidPoint 奖励表。
- [main.blua 精确 Lua 5.4 Proto/PC 绑定](violet400-exact-main-blua-reward-p1-20261020.md)：LotteryRewardItem、护符过滤、RaidResultAddItem 的目标函数标识和字节码文件偏移。
- [P1-B ELF 校验脚本](../../tools/research/audit_violet400_lua_vm_root.py)：离线按原始 SHA/指令/BL 目标检验，不写 Switch 内存。

**证据边界：** 上述均为文件级、静态或离线证据；运行中 `lua_State`、Lua A/B/C 调用、Hook 命中、原版奖励保留与额外物品入包仍须实机验证。

- [P1-B2 Lua 主状态与 CallInfo 只读结构验证](violet400-p1b2-lua-state-callinfo-live-structure-20261020.md)：结合第一份实机全局指针稳定日志、ELF 的 15 处新指令复核、Lua 主状态字段布局、CallInfo 有界读取及下一阶段闭包/奖励追踪 Gate；尚无真实 A/B/C 数据。

- [P1-B2 实机竞态日志与基础奖励表浏览](violet400-p1b2-reward-table-diagnostics-20261020.md)：详细记录用户 10:53 实机证据，按字段诊断 CallInfo/栈变化、有限只读重试，以及固定/随机 Base 奖励表与 20×999 本机模板的来源区分；不等同已抓取 Lua A/B/C 奖励。

- [真实 Switch 房主联机晶耀护符奖励追加实施方案](violet400-retail-host-charm-append-implementation-plan-20261020.md)：将 PokéLDN 正版参与方 x500 实验、0x2F 时序、RaidPoint+0xE4 格式与当前 native 整表覆盖代码对应，优先实机房主 outbound 0x2F 只读验证、保留原版后 Subject=5 追加、两台零售 Switch 验收，强调 45 总行限制与官方互联网模式未验证。
