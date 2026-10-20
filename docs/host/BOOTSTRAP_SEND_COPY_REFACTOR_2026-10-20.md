# 《紫》4.0.0：从扫描候选 RaidPoint 转向真实房主 Bootstrap 发送副本

> 状态：方案及现有代码审计；**未完成实机发送/到账/保存闭环**。研究主文已写入 [pokeldn/research](https://github.com/969981/pokeldn/blob/research/docs/research/sv/violet400-raidcrawler-host-bootstrap-copy-refactor.md)。

## 核心结论

1. **数量表修改已由既有实验覆盖，不再重复 `data.bin` 12→13。**
2. 本工具 `host` 分支已有 `switch/host-probe/source/program/main.cpp` 的 exlaunch inline hook、v2 mailbox、`HostProbeLocator`、`HostProbeMailboxClient` 与 `Subject5AppendInspector`；应以此作为主路线，不把 `HostRaidPointAutoLocator` 的全内存扫描当作发送链验证。
3. 真实 Switch 在 `main+0xE32984`（**紫 4.0.0 的静态 RVA**）复制 `RaidPoint` 后、LZ4 压缩前可以同步采样运行时 `x20` / `x23`；这两个寄存器不是可离线复用的固定指针。
4. 发送前明文长度 `0xAA0`；RaidPoint 自 `+0x6B8` 开始，长度 `0x3E8`；45×16 字节奖励行从 **RaidPoint+0xE4 / plaintext+0x79C** 开始，到 `+0x3B4`（exclusive）结束。字段为 little-endian `ItemId | Quantity | RareFlag | SubjectType`。早期 `+0xE0` 解释已证伪。
5. 只允许在**真正的明文发送副本**追加；原始固定/随机/料理/晶耀奖励和源对象不能变。原版 `RareFlag=1` 是允许的。45 槽容量不足、出现空洞、异常行、结构或代码指纹不符均拒绝写入。

## 推荐执行顺序

```text
连接真实 Switch（仅紫 4.0.0）
   → 验证 TitleID、游戏版本、main+RVA 原始指令指纹
   → 通过 exlaunch InlineHook + 同构建 mailbox manifest 自动定位 Native 邮箱
   → 只读 N1：真实房主开战，记录原版源/输出各 720B 与 generation
   → 校验同局源/目标数据一致（不靠扫到的静态 RaidPoint）
   → N2 preview：确认首个全零槽和所需 16B，mode=1 不写
   → 手动特殊 Native 构建 + 人工确认：mode=2，只改 x23+0x79C 的空槽一行
   → 原版 serializer 与 LZ4/Pia 负责网络发送
   → C# 根据 N2 状态与两端字节差异判断是否仅追加一行
   → 两台实机验证护符资格、原奖励、额外到账与重启保留
```

## 现有代码与尚未满足的保证

| 组件 | 已存在的能力 | 仍需独立证明 |
|---|---|---|
| `RaidPointLayout.cs`、`RewardAppendPlanner.cs` | 45 槽结构、离线拒绝、原版奖励保护 | 当前 CI 测试结果 |
| `HostCaptureVerifier.cs`、`Subject5AppendInspector.cs` | 同场快照比较、单行差异校验 | 来源确为真实发送 |
| `HostProbeLocator.cs`、`HostProbeMailbox.cs` | sys-botbase 控制、受控邮箱定位 | Native 模块已在该机正确加载 |
| `main.cpp` | post-memcpy N1，N2 编译门控、一次性尝试写发送缓冲 | 调用时序、Build ID、原生 Hook ABI、参与者接收 |
| `HostProbeWindow.cs` | C# 原生探针 UI、预览和实验授权 | 返回码20并不代表发奖或保存成功 |

## 安全规则和验收

**默认 CI 构建只能只读**，不能自动加载写入功能。只能在匹配的游戏、恢复手段齐备的测试设备上通过特殊编译显式开启 N2。一次请求最多 1 行：先机球 ID15，数量1–10，RareFlag0，Subject5；更大数量/多种物品/20×999 暂不属于已经证明的范围。

需要同时满足：原始固定/随机/条件奖励逐字节不变、修改后的单行进入实机 `0x2F` 明文发送对象、有护符参与者真正收到额外道具、战斗结束保存并重启后仍在。**本地 LDN** 与 **官方 NSO** 是独立验收对象；没有动态原生 Hook 现场记录就不能宣称已解决“实机房主发送”。

### 附：必须避免的误操作

- 不向 `RaidBlockPointerBase` 写发送期奖励。
- 不把搜索到的稳定内存拷贝当成真实房主发送对象。
- 不修改已经压缩的 LZ4 报文。
- 不按 `+0xE0` 解析或写 45 槽，也不把 `RaidPoint+0xE4` 当成第二个字段。
- 不在没有源/目标一致性验证时使用 sys-botbase PC 轮询写替代原生同步 Hook。
