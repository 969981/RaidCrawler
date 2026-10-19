# RaidCrawler benefit · 技术研究索引

- [无需替换 sys-botbase：ARM64 代码段跳板候选和离线验证](../../native/sv_remote_code_patch/README.md)——已核验《紫》4.0.0 的 880B 虚拟地址间隔、直接 B/BL 跳转及 ARM64 汇编原型；尚未实机安装。

- [方案 A：Windows 远程安装 Hook 的能力边界与增强 botbase 设计](windows-remote-hook-feasibility.md)——源码核对 sys-botbase 调试读写、Build ID 前缀、缺少的寄存器/缓存/回滚能力、A0/A1/A2 对比和协议 P0–P5。

- [全局奖励模板与任意场次 Hook 边界](global-any-raid-reward-hook.md)——模板不再绑定某个 Seed／坑位，增加实机静态指纹检测与 Hook 待办。

- [实机奖励诊断与 Hook 日志格式](live-raid-hook-diagnostics.md)——在编辑器点“写入奖励”启动只读实机诊断，保存 JSONL、Main NSO 基址、版本及手动开房时间线；实际原生写入尚未安装。

- [官方互联网手动主持：奖励写入研究及 20+25 槽位验收](official-online-host-reward-write-plan.md)——明确 Nintendo 互联网与 PokéLDN 本地 LDN 的差异、当前实现、ELF 发送/接收证据、尚缺的运行时 Hook 证据。

- [PokéLDN × 《紫》4.0.0 ELF：单场太晶坑自定义奖励](pokeldn-elf-single-raid-hosting.md)：选中坑位与真实 Host CLI 参数、奖励注入时机、45 行布局、ELF 动态指针、Subject 5 晶耀护符过滤、背包保存、已验证/未验证范围。
- [Benefit 奖励编辑器和主持功能说明](benefit-reward-editor.md)：操作入口、功能和 CI 状态。

不包含原始游戏二进制、密钥或 Nintendo 官方在线房间修改工具。当前集成的主持模式是 PokéLDN 本地 LDN/特殊模拟器 LAN 路线。
