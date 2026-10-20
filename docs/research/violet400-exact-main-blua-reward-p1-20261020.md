# 《紫》4.0.0：真实 main.blua 的团体战奖励链精确 Proto 绑定（P1）

2026-10-20。状态：**精确版本 Lua 5.4 字节码静态绑定成功**；真实 Switch Lua VM 调用及奖励追加**尚未动态观察**。本报告由用户提供的 main.blua 计算，不包含原始游戏脚本或其他游戏二进制。

## 1. 本次输入与完整性

- 用户自有 \`main.blua\` 大小：**12,912,264 bytes**，Lua 5.4 标准 chunk。
- 完整 SHA-256：\`c1a4f4e2625912cf0b739a642a79ba3357df0557a544b92da22a6cb139de2815\`。
- 已有 \`Violet4.0.0.elf\` SHA-256：\`d17403eaa9d844db82f647fd2f1518806b10c0518d53602fc0ac1766298fdba0\`。
- 根 Lua 代码 429034 指令、30829 根子 Proto；全文件实际解析 **37743** Proto，EOF 无剩余字节。
- 独立对比 PokéLDN [Lua 5.4 locator](https://github.com/969981/pokeldn/blob/research/scripts/research/sv400_main_blua_locator.py) 的 SHA、根形状和常量指纹。本次进一步利用根 Proto 的 \`LOADK(method)\` → \`OP_CLOSURE\` → \`CALL\` 注册记录，**锁定具体函数名与子 Proto 绑定**。不再只是常量命中就猜函数身份。

## 2. 4 个精确函数，含原始字节码 PC（从 0 开始）

| 方法／注册哈希 | Proto | 指令数组**文件偏移** | 指令数 | 根代码 OP_CLOSURE PC |
|---|---|---:|---:|---:|
| \`FieldRaidJewelry.LotteryRewardItem\` / \`FE77BCB620AB2EDCA\` | \`root/2899\` | \`0x377501\` | 477 | 59986 |
| \`BattleRewardData.F316609DEA110601D\`（subject 过滤） | \`root/4870\` | \`0x410673\` | 137 | 87463 |
| \`GameEventBattleResult.RaidResultAddItem\` / \`FCBBD4D63B93E4C04\` | \`root/3756\` | \`0x3B1E39\` | 69 | 74347 |
| \`FCE48462FE20C26A7\`（RaidPoint 奖励序列化） | \`root/27146\` | \`0xAA084E\` | 91 | 380468 |

相关类哈希分别为 \`C81390F82EADEF632\`、\`C07C34FC3B976E5A7\`、\`C889F6553EDFCD011\`、\`C050DB411547B4BAC\`，在上述注册指令邻近的 LOADK 中逐一交叉验证。

**关键边界：** 所有 \`0x...\` 均为 **.blua 文件偏移**，**不是内存 Lua Proto 地址、机器码 RVA、可写补丁地址或未来稳定 PC 指针**。虽然由真实 4.0.0 字节码证明了「哪一个函数」，仍没有证明单人场次何时运行，更未捕获 \`lua_State\` 或实际奖励对象。

## 3. 字节码中已证明的目标时序

### A0：原版奖励生成完成／候选观察时点

\`root/2899\` 完整包含原版 reward_item_00、difficulty_1、slot_num、subject_type、ItemNo/ItemNum/rareType/subjectType 字段。最后一段 PC 452–474 构造并 push 原版额外奖励；PC **475** 为 \`RETURN\`（寄存器 A8，文件偏移 \`0x377C6D\`），是「原版生成完成后」的**逻辑候选观察点**。该函数另有 PC **27、33、39、53** 的提前返回；不能把 PC475 当成所有路径必经点，无法在未理解 VM 生命周期的情况下直接覆写。

### B：持晶耀护符判断与过滤后列表

\`root/4870\` PC **8** 是 **\`LOADI 2481\`**（字节码指令位于 \`0x410693\`），直接证明使用护符道具 ID 2481；PC 87–92 包含 subjectType 5 筛选判断。PC **134** 是向对象索引 **3** 的 \`SETI\`，PC **135** 返回。与旧公开 Lua 的 \`self[3] = newArray\` 一致；**过滤后应读取新的 [3] 对象**，不得继续对过滤前的旧列表指针做假设。

### C：胜利后发奖

\`root/3756\` PC 1–3 是连续 \`GETI\` 索引 **18 → 92 → 3**，读取结算上下文内的奖励列表。函数包含与物品数量／容量检查有关的哈希字段；PC **66** 是 \`CALL\`，指向该路径加载的入包方法，PC **68** 为 \`RETURN0\`。应记录每个 ItemNo、ItemNum、实际 AddItem 增量和 SlotMaxNum；不是看到奖励界面就证明最终库存 +999。

### 独立网络结构（不得代替单人路径）

\`root/27146\` PC **8** 的 \`LTI ... 45\` 与 PokéLDN 45 行结构互相印证。这是**网络 RaidPoint 的格式容量**；不能自动推广为单人 Lua Haxe 数组容量。旧 ARM64 \`main+0xE32984\` 是网络出站副本，不能当成 \`root/2899\` 的运行时 Hook。

## 4. 新增可复现验证工具

- \`tools/research/verify_violet400_lua_reward_prototypes.py\`：严格 SHA gate、Lua5.4 结构解析、37k+ Proto 遍历、4 个函数的 method/class/closure 验证、特定 PC opcodes 验证。只输出只读 JSON 元信息。
- \`tools/research/test_verify_violet400_lua_reward_prototypes.py\`：无游戏二进制依赖的 opcode 测试与 SHA fail-closed；当环境变量 \`RAIDCRAWLER_LOCAL_BLUA\` 指向用户拥有的文件时，另运行完整实际 blob 验证。
- \`.github/workflows/charm-p1-trial-checks.yml\`：增加上述无版权二进制的 CI 测试，不构建运行时 Hook。

在用户自己的本机/受控研究目录运行：

\`\`\`bash
python tools/research/verify_violet400_lua_reward_prototypes.py /path/to/main.blua --out reward-prototypes.json
RAIDCRAWLER_LOCAL_BLUA=/path/to/main.blua python -m unittest discover -s tools/research -p "test_verify_violet400_lua_reward_prototypes.py" -v
\`\`\`

上述验证已在提供的 4.0.0 Lua 输入上完成；不提交/分发原始 \`main.blua\` 或 \`Violet4.0.0.elf\`。

## 5. 剩余 P1 真实运行时追踪（现在更精准，但仍受阻）

1. **VM 执行存在性（先只读）**：需在有授权的测试 Switch 上，用经过验证的执行时点遥测证明 \`root/2899\`、\`root/4870\` 和 \`root/3756\` 属于同一场团战的真实活动 Lua closure/Proto。记录真实调用次数、同场标识、进入/返回与单人模式。**文件字节码 PC 不是运行时 Native PC；普通 sys-botbase 定时 peek 不能直接获取完整 Lua 调用帧。**
2. **VM 安全访问**：辨认 \`lua_State\`、脚本 closure、Table/TValue 的实际表示，处理 Lua/Haxe 数组和 GC，同步读取 A0 原版、过滤后 B（新 [3]）、C（[18][92][3]），用现有 ABC JSONL 证据格式关联。读取和写入不得靠未验证的堆扫描或改写任意 \`obj+3\` 地址。
3. **追加初试 A1**：只有完整无修改遥测和安全执行边界成立，才在生成后、过滤前追加 **一条 \`ItemNo=492, ItemNum=1, rareType=0, subjectType=5\`**。逐条保留原版固定、随机、料理及护符行；容量不足绝不删除原行，异常原样回退。
4. **真实领取＋持久化**：观察 B、C 对新行资格筛选后的结果；检查背包实际差值和 SlotMaxNum，自动保存完成后完整重启读回。缺任一来源则判为未完成。
5. **多人**：另验证 LDN 房主的网络 RaidPoint 复制/发送/接收与每个参与者自己的护符条件；**官方互联网不可从单人成功自动外推**。

**截至本记录：代码定位/静态闭包绑定 = VERIFIED；4.0.0 单人 Native 回调、Lua 运行时对象、追加、Bag、重载、其他玩家实收 = NOT OBSERVED。** 不修改已备份的原版 drop 行，不装用未验证 Hook，保持当前 sys-botbase 2.5 不变。
