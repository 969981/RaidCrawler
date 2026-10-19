# RaidCrawler benefit × PokéLDN × 《紫》4.0.0 ELF：单场太晶坑自定义奖励

> 状态：**RaidCrawler 代码集成和 Windows 构建已验证；原版《紫》4.0.0 ELF 的数据结构与机器码经前期独立分析验证；Switch 实机联机、入包与保存成功仍未端到端验证。**
>
> 整理日期：2026-10-19。源码基线：[RaidCrawler benefit @ 2b36104](https://github.com/969981/RaidCrawler/commit/2b36104d6c44c9f915061b8f4dbb296d4c5160b0)；[PokéLDN research @ a222940](https://github.com/969981/pokeldn/commit/a222940a3a70e78ae4a5fdf8b567afbc9b4c7e14)。
>
> 原始 ELF：用户 Library `Violet4.0.0.elf`，SHA-256 `d17403eaa9d844db82f647fd2f1518806b10c0518d53602fc0ac1766298fdba0`，Build ID `709bfd66115298640155fcc4979dba151c7cc79a`。本篇**交叉引用既有 ELF 复核记录**，不是此次再次独立运行新的反汇编或实机测试。不上传游戏 ELF、RomFS 或密钥。

## 1. 一句话结论与完整工作流程

**PokéLDN 是在本场战斗初始化前构造自定义奖励，放进当前 RaidPoint，再发给参战客户端；没有改写《朱／紫》全部地图坑或全局掉落表。**

```text
RaidCrawler (读 Switch 0x20 字节坑位记录)
  选中：地图 / Area / Den / Seed / Flags
      │
      ├─ 本地按 Seed 计算默认奖励
      ▼
RewardEditorForm
  逐行编辑最多 45 行：Item ID / Quantity / Rare / Subject
  支持 Subject 5 的单行或全表快捷设置
      │
      ▼
PokeLdnHostPlan
  校验 Seed / Version / Map / Content / Progress / Host .pk9
  冻结本场奖励快照；转为 --raid-reward ITEM:QUANTITY:SUBJECT
      │
      ▼
PokeLdnHostForm (Windows -> WSL -> python3 bin/sv_host.py)
  prepare_raid() 重算首领、校验背包可用道具、创建一个 RaidGroup
      │
      ▼
LDN 房间：玩家进入 -> Ready -> Start -> session_ack -> guest_start
      │
      ▼
RaidHost._next() -> RaidGroup.bootstrap()（首次构造并缓存）
  raid_point() 生成 RaidPoint 0x3E8（奖励 +0xE4）
  bootstrap() 生成明文 0xAA0（奖励 +0x79C）
      │
      ▼
KEY_LOBBY=0x3380 / KIND_BOOTSTRAP=0x2F / LZ4 压缩和分片
      │
      ▼
参战客户端读取奖励 -> Subject 条件过滤
  战斗胜利 -> RaidResultAddItem -> Bag.AddItem -> 请求自动保存
```

注意区分两个时间点：`prepare_raid()` 在启动时**验证并持有奖励配置**；真正为本场玩家**生成 bootstrap 明文字节并发送 0x2F**则是在开战交接的条件满足之后。

## 2. RaidCrawler 当前如何选择目标坑：已经传入哪些字段？

[原始 Raid 类](https://github.com/969981/RaidCrawler/blob/2b36104d6c44c9f915061b8f4dbb296d4c5160b0/RaidCrawler.Core/Structures/Raid.cs) 的每条坑位记录大小只有 **0x20**，而非 0x3E8。典型字段如下：

| 坑位记录偏移 | 字段 | 作用 |
|---|---|---|
| `+0x00` | IsActive | 是否激活 |
| `+0x04` | Area | 地图区域 |
| `+0x0C` | Den | 坑索引 |
| `+0x10` | Seed | 当前随机种子 |
| `+0x18` | Flags | 1=黑坑；大于等于 2 为活动相关坑 |

[MainWindow.Rewards_Click](https://github.com/969981/RaidCrawler/blob/2b36104d6c44c9f915061b8f4dbb296d4c5160b0/RaidCrawler.WinForms/MainWindow.cs#L1651-L1696) 把选中目标转换成 [PokeLdnRaidSelection](https://github.com/969981/RaidCrawler/blob/2b36104d6c44c9f915061b8f4dbb296d4c5160b0/RaidCrawler.Core/Structures/PokeLdnHostPlan.cs#L5-L24)，保留种子、版本、地图、普通/黑坑、活动标记、物种、星级、Area 和 Den。编辑器里的本地预览以地图/区域/坑号/种子/团战力作会话键，但它不写入 Switch。

[PokeLdnHostPlan.BuildArguments](https://github.com/969981/RaidCrawler/blob/2b36104d6c44c9f915061b8f4dbb296d4c5160b0/RaidCrawler.Core/Structures/PokeLdnHostPlan.cs#L35-L88) 传给 PokéLDN 的实际参数：

| 来源 | 主持参数 |
|---|---|
| Seed | `--raid-seed`（8 位十六进制） |
| Version | `--raid-version scarlet` 或 `violet`，紫额外使用 `--violet` |
| Map | `--raid-map paldea/kitakami/blueberry` |
| Content | `--raid-content standard/black` |
| 手动选择的故事进度 | `--raid-progress beginning/tera/3star/4star/5star/6star` |
| 主持参战宝可梦（额外配置） | `--raid-pokemon /absolute/linux/host.pk9` |
| 参战主机数/时长 | `--raid-players`、`--seconds` |
| 逐行自选奖励 | 多个 `--raid-reward ITEM:QUANTITY:SUBJECT` |
| 特殊模拟器 LAN 模式 | 可选 `--ip-host`，**非 Nintendo 官方互联网** |

**尚未作为 CLI 参数下传：Area、Den、Species、Stars。** 这几个值当前只用于选择快照和界面说明；[PokéLDN prepare_raid](https://github.com/969981/pokeldn/blob/a222940a3a70e78ae4a5fdf8b567afbc9b4c7e14/bin/sv_host.py#L443-L513) 会根据 Seed、版本、地图、进度与水晶类型**重新生成** Boss。因此目前的“按坑位主持”其实是“以选中坑的 Seed 和上下文再生成一场战斗”，不是已经验证与原坑所有字段完全相同。务必对照主持日志中的 `stars/species/Tera type`，不一致应停止，不应误称精确复现。

**活动坑当前被阻止主持**：PokéLDN 原生支持用事件 delivery 文件生成，但 benefit 尚未实现把事件 delivery/group 对应到选中坑，不能把活动坑伪装成普通坑。

## 3. 45 行奖励从哪里来：编辑器 vs 主持端

[RaidRewardEditor.cs](https://github.com/969981/RaidCrawler/blob/2b36104d6c44c9f915061b8f4dbb296d4c5160b0/RaidCrawler.Core/Structures/RaidRewardEditor.cs) 实现 `RaidRewardEntry(ItemId, Quantity, Rare, Subject)`、数据校验与 720 字节行编码。RaidCrawler 预测奖励有两种显示用虚拟 ID：`10000`（素材）、`20000`（太晶碎块）；导出前分别用 `Rewards.GetMaterial` 和 `Rewards.GetTeraShard` 转成实际道具 ID。

| Subject | 客户端发放条件 | benefit 可编辑/导出 | 当前 PokéLDN 主持 |
|---:|---|---|---|
| 0 | 全体玩家 | 是 | **是** |
| 1 | 房主 | 是 | 否，报明确错误 |
| 2 | 参与者 | 是 | 否 |
| 3 | 一次性领取资格 | 是 | 否 |
| 4 | 匹配的团体战料理团战力等级及行顺序 | 是 | 否 |
| **5** | 接收玩家持有**晶耀护符 ID 2481** | **是**；一键设置单行/所有行 | **是** |

主持要求 `Rare=0`、`Subject=0/5`、有效行 `1..45`、道具 ID `1..65535`、数量 `1..999`。不会把 1–4 擅自改成 0，也不会忽略 Rare 1 后继续主持。**999 是参数允许的请求数量，不代表任何道具都能净增 999。**

[PokéLDN raid_point](https://github.com/969981/pokeldn/blob/a222940a3a70e78ae4a5fdf8b567afbc9b4c7e14/pokeldn/sv/raid.py#L171-L214) 对自定义奖励做：

```python
# 语义提炼：真实源文件将 2/3 元组转成四字段，并以小端格式写入
row = (item_id, quantity, 0, subject)  # subject: 0 or 5
struct.pack_into("<IIII", raid_point_buffer, 0xE4 + 16*i, *row)
```

非空自定义列表**替换**该场由 Seed 自然生成的整张默认奖励表，不是追加。`RaidGroup` 初始化时会调用 `raid_point` 做提前校验，所以非法配置能够在正式开房前暴露。

### 3.1 实际字节位置

```text
明文 bootstrap   0xAA0 = 5 × 0x158 + 0x3E8
  +0x000..+0x6B7 : 四参战位置 + Boss
  +0x6B8         : RaidPoint 起点，长度 0x3E8
  +0x79C         : 奖励第 0 行 = RaidPoint +0xE4
  +0xA5C         : 奖励第 44 行，最后一行
  +0xA6C..+0xA6F: 非奖励行，不能塞第 46 行
  +0xA70         : RaidPoint summary

单行 16 字节（little-endian）：
+0x00 item (u32) / +0x04 quantity (u32)
+0x08 rare (u32) / +0x0C subject (u32)
```

大师球 ×999 且 `subject=5` 的**明文**行：

```text
01 00 00 00 | E7 03 00 00 | 00 00 00 00 | 05 00 00 00
  item=1        quantity=999      rare=0        subject=5
```

45×16=**720 字节**仅对应奖励行；编辑器导出的 BIN **不是**可直接发给 Switch 的 0xAA0 明文，更不是 LZ4 压缩后的网络帧。

## 4. 什么时间点写入奖励、只影响哪一场？

[PokéLDN `RaidHost._next()`](https://github.com/969981/pokeldn/blob/a222940a3a70e78ae4a5fdf8b567afbc9b4c7e14/pokeldn/sv/raid.py#L540-L559) 在 `start` 已进入、`session_ack` 通过、每位加入者收到 `guest_start` 之后执行：

```python
self.whole = bootstrap_message(self.group.bootstrap(), self._count())
```

第一次调用 [RaidGroup.bootstrap](https://github.com/969981/pokeldn/blob/a222940a3a70e78ae4a5fdf8b567afbc9b4c7e14/pokeldn/sv/raid.py#L297-L311) 才真正将 `self.rewards` 打包进 0xAA0；返回的 `self.plain` 会缓存，再次获取直接复用同一内容。随后代码把外层 LZ4 `0x2F` 分成 `boot1` / `boot2` 发给该房间玩家。

影响范围对照：

| 对象 | 是否改变 |
|---|---|
| 当前 PokéLDN 创建的**这一场**战斗初始化奖励 | **是** |
| 同一场各参战者看到的奖励配置 | 同一份数据，各端独立执行资格过滤 |
| 地图其他自然坑与 RaidCrawler 扫描的其他 Seed | **否** |
| Nintendo 官方联网大厅 / 全局掉落表 | **否** |
| 启动后改编辑器预览就实时更新已有房间 | **否**（未实现同步；bootstrap 已缓存时尤其不能改变） |
| 退出后自动轮流开下一个 Seed | **否**（需显式重新启动） |

**正确切入点：在创建新的 RaidGroup 之前冻结并传入已确认的奖励配置。** 不需要也不应该对所有坑批量写存档。将来实现多场循环时必须逐场新建 RaidGroup、逐场分配 session ID 和唯一奖励 hash，并在 `BOOTSTRAP_LOCKED` 状态后禁止改变本场奖励。

## 5. 与真实《紫》4.0.0 ELF 的独立交叉验证

下面是**原版游戏 serializer** 的静态 main-relative RVA；它不是 PokéLDN 模拟器内的函数地址，不是跨版本的固定内存 pointer chain，也不意味着 benefit 需要往这些地址写数据。依据 [既有真实 ELF opcode 审计](https://github.com/969981/pokeldn/blob/a222940a3a70e78ae4a5fdf8b567afbc9b4c7e14/docs/research/sv/violet400-elf-raid-bootstrap-audit.md) 和 [动态指针研究](https://github.com/969981/pokeldn/blob/a222940a3a70e78ae4a5fdf8b567afbc9b4c7e14/docs/research/sv/sv400-custom-reward-pointer-patch.md)。

| 4.0.0 main+RVA | 已验证指令含义 |
|---|---|
| `0xE327FC` | 原版 bootstrap serializer 入口 |
| `0xE32814` | `mov x20,x0`，本次输入对象指针 |
| `0xE32828` | `mov w22,#0xAA0`，明文长度 2720 |
| `0xE3283C` | `mov x23,x0`，本次输出明文缓冲区 |
| `0xE32968` | `add x1,x20,#0x78`，RaidPoint 后段源地址 |
| `0xE3296C` | `mov w2,#0x3C8`，复制长度 |
| `0xE32970` | 写入 `[x23+0x6B8]`，RaidPoint 起始 |
| `0xE32980` | `bl memcpy@plt`，复制结构尾段 |
| `0xE329B0` | 跳往 `0x1592B7C` 序列化/压缩链 |

静态关系：`source RaidPoint = x20+0x58`、`output RaidPoint = x23+0x6B8`。

| 第 i 行 | 输入消息对象 | 输出明文缓冲区 |
|---|---|---|
| 道具 ID | `x20+0x13C+16*i` | `x23+0x79C+16*i` |
| 数量 | 上式 `+4` | 上式 `+4` |
| Rare | 上式 `+8` | 上式 `+8` |
| Subject | 上式 `+12` | 上式 `+12` |

例如第一行 subject 为 `x20+0x148` / `x23+0x7A8`，第 44 行起点为 `x20+0x3FC` / `x23+0xA5C`。当 `main+0xE32984` 尚未恢复 `x23` 时，可在只读断点检查完整明文；函数返回后该寄存器含义可能改变。

先前对指定 Build ID 执行过 **13/13 奖励结构关键 opcode**、**15/15 serializer opcode** 的验证；本篇不把之前的 ELF 实证说成“此次又独立跑过一次”。这些操作只能证明**游戏原生数据布局**与模拟器一致，不能证明官方互联网模式能接受修改后的数据。

## 6. Subject=5 的接收端与道具保存链

[BattleRewardData 反编译源码](https://github.com/Martmists-GH/SV-Script-RE/blob/1f843bd2517c707fbb47491dad58856eaa2c2cc7/decompiled/C07C34FC3B976E5A7.lua#L183-L321) 及 [PokéLDN 4.0.0 Lua 研究](https://github.com/969981/pokeldn/blob/a222940a3a70e78ae4a5fdf8b567afbc9b4c7e14/docs/research/sv/battle-reward-lua-cross-version.md) 给出客户端语义：

```lua
-- 简化逻辑，不是逐字原版代码
local hasCharm = Bag.GetNumItem(2481) > 0
if reward.subjectType == 5 then
    keep = hasCharm
end
```

这里是读取**玩家背包持有晶耀护符 2481**的条件，不是闪耀护符，也不是此函数内的 DLC 购买凭据检查。该机制不能单独证明任何商业联网坑就是用 `subject=5` 实现。

胜利结算：`GameEventBattleResult.RaidResultAddItem` 读取已过滤的道具/数量，按实际 `SlotMaxNum` 算入包空间，调用 `Bag.AddItem`、`BagSaveAccessor`，再经 `RequestAutoSave -> StartSaveProcess -> SaveProcess` 进入异步保存流程。既有《紫》ELF 分析找到了保存入口 `main+0x16D16D0`、完成查询 `main+0x147F530` 和 `nn::fs::Commit` 分支；**轮询返回完成 ≠ Commit 成功 / 重启数据必定保留**。[完整研究](https://github.com/969981/pokeldn/blob/a222940a3a70e78ae4a5fdf8b567afbc9b4c7e14/docs/research/sv/sv400-autosave-and-item-slotmax.md)。

示例：奖励行要求 999、原先已有 200 且该道具真实上限假定为 999，则**可增加数量最多 799**。原版 `itemdata_array.bin` 尚未在 Library 中定位，不能把 PKHeX 背包规则误标为真实逐道具 `SlotMaxNum` 实测。

## 7. Windows WSL 主持程序与网络范围

[\`PokeLdnHostForm.cs\`](https://github.com/969981/RaidCrawler/blob/2b36104d6c44c9f915061b8f4dbb296d4c5160b0/RaidCrawler.WinForms/SubForms/PokeLdnHostForm.cs) 使用 `ProcessStartInfo.ArgumentList` 分别传递参数，不经 Shell 拼接执行，减少特殊路径空格导致的误解析。示例（**Seed 仅为测试数据，不代表已验证的真实坑**）：

```text
wsl.exe --distribution Ubuntu-24.04 --cd /home/develop/github/pokeldn --exec python3 -u bin/sv_host.py
  --raid-seed BD13FB43 --raid-version violet --violet
  --raid-map kitakami --raid-content standard --raid-progress 4star
  --raid-pokemon /home/develop/pokemon/host.pk9
  --raid-players 2 --seconds 300
  --raid-reward 1:999:5 --raid-reward 796:20:0
```

当前窗体提供用户确认、stdout/stderr 日志、Stop 操作；本机路径设置保存为 `%LOCALAPPDATA%\RaidCrawler\pokeldn-host.json`。即使成功启动 WSL2 Python，也**不等于 WSL2 已具备原生无线网卡 AP/LDN 能力**。`--ip-host` 仅针对 ldn_mitm / 模拟器 LAN，不是 Nintendo 官方互联网太晶坑。

关闭 Windows 父进程后如遗留 Linux 子进程，需在 WSL 自行检查 `ps` 并安全停止。此版本不注入正在运行的正版游戏，不修改任何其他坑位存档，也没有自动开启下一场的逻辑。

## 8. 验收证据、剩余风险、建议

| 检查对象 | 状态 / 证据 |
|---|---|
| 45×16 奖励布局、二进制编解码 | **核心 C# + PokéLDN 交叉确认** |
| UI `subject=5` 一键设定及完整 CLI 传参 | **源码与单元测试确认** |
| RaidCrawler/benefit 单场 WSL 主持窗口 | **Windows 编译成功；未在真实 Switch 验证流程** |
| PokéLDN 的 boot1/boot2 时序与 plain 缓存 | **代码确认** |
| 《紫》4.0.0 原版 0xAA0 / 0x3E8 结构及指针 RVA | **前期 ELF 实际反汇编确认** |
| Species、Stars、Area、Den 与实际新主持坑完全一致 | **目前无自动比对，需手工检查日志** |
| 主持后收奖，Subject 5，有护符/无护符差分 | **尚未实际实机验证** |
| 999 入包、存档重启、官方 Nintendo 在线模式 | **未证明** |

已有 [Windows CI #39229101208](https://github.com/969981/RaidCrawler/actions/runs/39229101208) 确认 WinForms 编译和新增功能测试 **21/21**，对应 [benefit-host-v0.1.0 Preview Release](https://github.com/969981/RaidCrawler/releases/tag/benefit-host-v0.1.0)。旧有测试仍 **8 项失败（非阻断）**；不能说全部测试通过。

**下一阶段最值得补的三个验证**：①同一 Seed、故事进度下对比 RaidCrawler 和 PokéLDN 的星级/物种/太晶属性；②subject=0/5 对有无晶耀护符玩家的**小额普通道具**差分；③战斗成功后检查背包与保存状态、退出重启读回。当前代码已经能在创建 RaidGroup 前传入奖励，但还不能以此认定联网 999 奖励在零售设备上永久入包。

## 参考定位

- [RaidCrawler benefit 功能使用文档](benefit-reward-editor.md)
- [RaidCrawler 源码：Host 参数生成](https://github.com/969981/RaidCrawler/blob/2b36104d6c44c9f915061b8f4dbb296d4c5160b0/RaidCrawler.Core/Structures/PokeLdnHostPlan.cs)
- [RaidCrawler 源码：奖励编辑器与格式](https://github.com/969981/RaidCrawler/blob/2b36104d6c44c9f915061b8f4dbb296d4c5160b0/RaidCrawler.Core/Structures/RaidRewardEditor.cs)
- [PokéLDN 主持入口](https://github.com/969981/pokeldn/blob/a222940a3a70e78ae4a5fdf8b567afbc9b4c7e14/bin/sv_host.py)
- [PokéLDN RaidGroup / RaidPoint / bootstrap](https://github.com/969981/pokeldn/blob/a222940a3a70e78ae4a5fdf8b567afbc9b4c7e14/pokeldn/sv/raid.py)
- [ELF serializer 审计](https://github.com/969981/pokeldn/blob/a222940a3a70e78ae4a5fdf8b567afbc9b4c7e14/docs/research/sv/violet400-elf-raid-bootstrap-audit.md)
- [ELF 奖励指针、偏移与离线结构测试](https://github.com/969981/pokeldn/blob/a222940a3a70e78ae4a5fdf8b567afbc9b4c7e14/docs/research/sv/sv400-custom-reward-pointer-patch.md)
- [Lua 奖励入包与存档调用链](https://github.com/969981/pokeldn/blob/a222940a3a70e78ae4a5fdf8b567afbc9b4c7e14/docs/research/sv/raid-reward-to-bag-save-chain.md)
