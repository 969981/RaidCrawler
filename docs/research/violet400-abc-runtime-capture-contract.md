# 只读奖励生命周期 A / B / C 证据协议（Violet 4.0.0）

> 本次实际实现的是 **A/B/C 导入验证、同场次关联、奖励内容对比**，而非 Switch 侧自动捕获。所有被导入的 A/B/C 日志一律标记 `UNATTESTED_IMPORTED_CAPTURES`。当前 sys-botbase 无 Lua VM 回调/寄存器断点接口，不能把每 2 秒阶段轮询或每 5 秒 Hook 模板 SHA 伪装成 RewardData 运行时内容。

## 已核实的代码关系

- A：`FieldRaidJewelry.FE77BCB620AB2EDCA / LotteryRewardItem` 返回 `BattleRewardData`，业务数组为 `[3]`：[C81390F82EADEF632.lua L469–485, L820–1335](https://github.com/Martmists-GH/SV-Script-RE/blob/1f843bd2517c707fbb47491dad58856eaa2c2cc7/decompiled/C81390F82EADEF632.lua)
- B：`GameEventNetworkBattleRaid` 在 L1512–1517 从事件参数 `raidBattleRewardData` 构造 `self[19][92]`，L1533–1548 在该对象上运行 `F316609DEA110601D`；后者查本机背包 `item 2481` 保留 `subject=5`。
- C：同一 Lua 文件 L1987–1995 将 `self[19]` 直接交给 `GameEventBattleResult` 作为 `setupData`；结算事件读取 `self[18][92][3]` 并调用 `Bag.AddItem`：[C889F6553EDFCD011.lua L1625–1696](https://github.com/Martmists-GH/SV-Script-RE/blob/1f843bd2517c707fbb47491dad58856eaa2c2cc7/decompiled/C889F6553EDFCD011.lua)
- 这些源码的业务链有原生 3.0.1/4.0.0 Build ID 兼容证据，但**在用户 4.0.0 单人真机上的对象内容和运行时执行仍没有观测**。
- 当前 `main+0xE32984` Hook 只尝试写 `x23+0x79C` 的出站 Bootstrap。它不是 A、B 或 C 的运行时对象钩子。

## 1. 实际采集需要的三个时点

| 事件 | 必须采集的阶段与位置 | 元数据 |
|---|---|---|
| `ABC_A_GENERATED` | `LotteryRewardItem` 返回后，真正的 `BattleRewardData[3]`；记录全部 `ItemNo,ItemNum,rareType,subjectType` | `run_id`、游戏 Build ID、捕获实现名称、数组行序 |
| `ABC_B_FILTERED` | `F316609DEA110601D` 执行完毕，读取其修改后的 `[3]` | 相同 run、玩家/角色、护符状态；入参/过滤上下文须来自同一团战 |
| `ABC_C_CONSUMED` | `RaidResultAddItem` 开始时，读取 `setupData[92][3]`，**在背包上限修正前** | 相同 run，来源类型/结算结果，原始应发道具清单 |

下一阶段要做的是捕获这三个**Lua/Haxe 逻辑数组**，而不是在进程内搜索任意 720B `RaidPoint` 后把它冒充 `BattleRewardData`。Lua 数组是对象，不保证存储为结构紧凑的 720B。

## 2. JSONL 数据契约

每条有效记录均要求：

- `event`: 三个固定枚举之一（A/B/C）
- `run_id`: 同一场次唯一的字符串（1–128 字符）；不可只根据随机接近的时间戳匹配不同场次
- `build_id`: 与原始 ELF 前缀 `709bfd6611529864` 匹配
- `capture_origin`: 捕获工具实例标识，同场次三阶段必须一致
- `row_entries`: 数组，每行严格 `[index,item_id,quantity,rare,subject]`，index 从 0 连续，按 Lua 数组当前顺序排列。上限 128 行，数量 1–999999，ID 1–65535，rare 0/1，subject 0–5
- `rows_sha256`: 对 `row_entries` 顺序逐行按 **5 × uint32 little-endian** 编码后的字节串求 SHA-256；这里是变长 Lua 行列表哈希，**不是**固定 45×16 的 `RaidPoint` / Hook 模板哈希

外部工具得到真实数组后可按以下 Python 样例**封装记录**（只是格式化，完全不负责读取游戏）：

```python
import hashlib
import json
import struct

def encode_stage(event, run_id, rows, origin="your-capture-adapter"):
    # rows 来自真正的运行时对象；不可用 Hook 配置模板伪装
    encoded = b"".join(struct.pack("<IIIII", idx, item, qty, rare, subject)
        for idx, (item, qty, rare, subject) in enumerate(rows))
    return json.dumps({
        "event": event,
        "run_id": run_id,
        "capture_origin": origin,
        "build_id": "709bfd66115298640155fcc4979dba151c7cc79a",
        "row_entries": [[idx, *v] for idx, v in enumerate(rows)],
        "rows_sha256": hashlib.sha256(encoded).hexdigest(),
    }, ensure_ascii=False)
```

必须明确：`capture_origin` 与 SHA 只是**文件完整性/来源声明**，任何人均能生成合法 SHA，因此**不构成密码学认证或真实 CPU 命中证明**。可信性取决于未来采集器是否在经过验证的 Lua 执行点实际读取了对象。

## 3. 已实现的 Windows 分析

`RaidRewardAbcEvidence.Analyze(jsonl, configuredSlots)`：

1. 文件长度 ≤2 MiB、≤5000 行、三类 stage 白名单；非法字段、错误行序、SHA 不一致均拒绝；RaidCrawler 的 `read-only-hook-snapshot`、`auto-phase-*` 仅作为非 ABC 事件忽略。
2. 严格根据 `run_id` 分组，检查同场次 Build ID 和 `capture_origin` 一致；重复阶段不擅自挑选。
3. **A→B**：按原始行顺序检查 B 是否 A 的子序列（资格过滤应保留行序）。
4. **B→C**：按行精确比较结算前清单（若发生重建/拷贝，内容是否一致）。
5. 同当前 45 槽模板做 **包含重复次数的多重集匹配**，从而识别哪些阶段真正包含用户配置的 20 条 `subject=5`。
6. 分类输出 `CUSTOM_ROWS_ABSENT_AT_GENERATION`、`CUSTOM_ROWS_LOST_DURING_FILTER`、`B_TO_C_NEEDS_INVESTIGATION`、`CUSTOM_ROWS_PRESENT_IN_C_CLAIM_NOT_BAG_PROOF` 等结果。
7. 该分析**不产生 `nativeHookHit=true`、`saved=true` 或真实道具入包断言**，也不会把外部 run 自动绑定到当前打开的 Switch。

界面入口：RaidCrawler → 全局奖励编辑器 → 写入奖励 → 诊断窗口 → **高级工具 → 导入 A/B/C 奖励追踪**。仅检查本地 JSONL，不向 Switch 写入。

## 4. 已有实机日志复核（不是 A/B/C）

`raid-20261019-162502-2f64d2a2.jsonl` 共 129 条事件，其中 64 条 `read-only-hook-snapshot`，均为 `epoch=2`，补丁/奖励模板 SHA 一致；**没有 `ABC_A_GENERATED`、`ABC_B_FILTERED`、`ABC_C_CONSUMED`**。所以不可根据这份日志宣称 A/B/C 已有值或说 20 条奖励已经被过滤。

玩家明确拥有晶耀护符，后续无需再让用户证明 `item 2481`；要验证的是 `subject=5` 行何时进入/丢失于本地奖品列表。

## 5. 采集器缺失项（不能冒充完成）

- 普通 sys-botbase 缺少 Lua 回调、暂停线程与读取实时函数局部变量/寄存器的标准接口。
- 尚未找到可只读轮询的**经过生命周期验证**的 `BattleRewardData` Lua 对象根指针。
- 如要真实 A/B/C 自动捕获，必须额外研究 Lua VM/游戏事件对象存储和可信回调采集，或明确启用受控原生仪表化；后者会写游戏进程内存，即使只是记录数据，也不属于“完全只读 Switch 内存”的方案。
- 尚未实现 C→Bag 自动计数或持久化验证。

**下一件实质性的工作**：解决单人团战如何找到 `FieldRaidJewelry` 当前 `BattleRewardData[3]` 实例，并在调用 `F316609DEA110601D` 的前后通过可靠只读对象解码或明确受控采样生成三个真实 capture 事件；没有可靠指针，不应按全内存形似 720B 的字节序列扫描并认定为奖励对象。
