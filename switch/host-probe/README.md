# RaidCrawler：真实 Switch 房主 Bootstrap Hook（《紫》4.0.0）

> 2026-10-20。工程与 CI 离线测试，不等于实机 Hook 命中或团体战奖励到账。**正常公开构建只读**；额外的 Native 实验模块须在自己可恢复的测试机上显式编译。当前主路径不依赖全内存扫描，也不修改 `data.bin`。

## 这是哪两个程序？

- **PC**：RaidCrawler WinForms 管理预设、通过现有 sys-botbase 连接游戏、寻找已安装 Hook 的邮箱、读取捕获结果。
- **Switch**：`subsdk9` 是 exlaunch 编译的 NSO 原生模块。Atmosphère 启动《紫》时将它加载进游戏进程，在 `main+0xE32984` 复制 RaidPoint 后、压缩前触发捕获。它不是应用、不是可由 PC 单独启动的进程。
- **sys-botbase**：保留原版，不替换、不修改，也不另开 TCP 端口。原版 sys-botbase 没有已验证的“捕获这一局 x23”的即时入口，不能单独保证改写实机发送副本。

## 下载同一版本的文件

需要下载 **两个不同的 Actions 附件**：从 [Windows host CI](https://github.com/969981/RaidCrawler/actions/workflows/host.yml) 的最新成功任务下载 `RaidCrawler-host-sysbot-only`（主要是 RaidCrawler.exe 及运行依赖）；从 [Native 探针 CI](https://github.com/969981/RaidCrawler/actions/workflows/host-native-probe.yml) 的成功任务下载 `RaidCrawler-host-N1-Violet400-UNVERIFIED`（包含 `subsdk9`、`main.npdm`、`mailbox-locator.json`、校验文件）。**Windows 附件不会自动包含 Native 定位清单。** 将 Native 附件中的 `mailbox-locator.json` 手动放到 Windows EXE 同一目录，并让它始终与该附件的 `subsdk9` 成对使用。Windows 与 Native 的协议都必须是 v3；旧的 v2 附件不可混用。

```text
Windows 电脑：
RaidCrawler/
  RaidCrawler.exe
  mailbox-locator.json    # 与本次 subsdk9 同一编译产物

Switch SD 卡：
sd:/atmosphere/contents/01008F6008C5E000/exefs/subsdk9
```

Switch 目标：仅《宝可梦 紫》Title ID `01008F6008C5E000`、4.0.0，ELF Build ID `709bfd66115298640155fcc4979dba151c7cc79a`。必须退出游戏再安装或替换 NSO，然后重新启动游戏使其加载。**切勿覆盖现有 Skyline 或其他模组的 subsdk9**；存在冲突时先处理兼容。构建目录还包含 `main.npdm`；NPDM 涉及进程权限，绝不可未经比对就覆盖原有/其他模组的 `main.npdm`，应依照 [exlaunch 的说明](https://github.com/shadowninja108/exlaunch) 检查游戏加载兼容性。安装文件不是对系统 sys-botbase 目录进行替换。

### 第一次：只读确认 Hook

1. 备份存档与现有模组；核对 Title ID、版本和 Build ID。将匹配的 **只读** `subsdk9` 放入上述 exefs 路径，确认没有同名冲突；完全退出《紫》后重新启动。
2. PC 运行 RaidCrawler，使用原有 sys-botbase Wi-Fi/USB 连接；进入 **发送副本 Hook** → **自动定位**。定位器从 main Hook 指令与同构建 manifest 计算邮箱地址，**不会用固定 RAM 指针，也不扫描 8/64 MiB**。
3. 点击 **读取状态**；期望 Hook 状态为 1、邮箱为 v3。点击 **开始捕获**。
4. 自己的 Switch 作为团体战房主，进入真正开战过程，再点 **读取并比较**。只读验收要求捕获次数增加，源/明文 720 字节相同；无法定位、版本不符或捕获次数为 0 都不算通过。
5. **默认奖励25种（自定义最多28）** 可以管理本机道具列表；**空间预检（不写入）** 只检查已捕获快照的槽位；**Native只读预览** 将当前 1–28 行配置传入 v3 邮箱，在下一次真实调用返回 30（足够槽位）或 31/32/33 等拒绝代码。只读预览不执行发奖。
6. 点击 **停止捕获** 可以解除武装。出现游戏异常直接关闭功能、移除或禁用模组并重启游戏。

## Native v3 协议与安全范围

| 字段 | 相对邮箱偏移 |
|---|---|
| Magic / 版本 / 长度 | `RCHMB03\\0`、3、`0x800` |
| 捕获使能、能力位 | `+0x10`、`+0x24`（bit1 批量预览、bit0 编译允许写入） |
| 原版 / 发送副本奖励区域 | `+0x40` / `+0x310`，各 720B |
| 请求模式、旧单行数据、返回码 | `+0x5E0`、`+0x5E4`、`+0x5F4` |
| v3 批量 nonce / 数量 / 校验值 | `+0x600` / `+0x608` / `+0x60C` |
| 本次新增行数 / 结果 nonce | `+0x610` / `+0x618` |
| 最多 28×16B 奖励配置 | `+0x620..+0x7DF` |

奖励位置 `RaidPoint+0xE4 = Bootstrap+0x79C`；单行按 Little Endian `ItemId,Quantity,RareFlag,Subject`。只能在 45 行原奖励后面的**连续全零空槽**中追加；默认 25 种要求原版奖励 `<=20` 行，28 种要求原版 `<=17` 行。原奖励不减少，空间不足整单拒绝。配置 nonce 与结果 nonce 可关联请求，但 **不是经过实机认证的房间 ID**。

模式：`0` 停用，`1` 单行只读预览，`2` 单行实验写入，`3` 批量只读预览，`4` 批量实验写入。只有明确在本地以 `HOST_N2_EXPERIMENTAL=1` 编译的特殊 NSO 才包含模式 2/4 的写入实现；**普通 CI 只读版本不支持这两种写入**。结果 20/40 仅代表本地发送前缓冲变化，不等于网络传输、收到奖励、背包数量或存档持久化。

> **重要限制**：本仓库没有真实 Switch 可供自动测试。请先完成只读 N1 与 v3 mode 3；若未证实运行时 Hook 和发送顺序，不要将“CI 构建通过”“配置已装入邮箱”或“Native 返回40”写成“已经成功发奖”。

## 从源码生成 Native（WSL2 + devkitPro 环境）

```bash
git clone --branch host --recurse-submodules https://github.com/969981/RaidCrawler.git
cd RaidCrawler
bash switch/host-probe/build.sh
```

要求已安装 devkitPro 的 devkitA64/libnx、Git、Make、Python；脚本明确禁止自动向 Switch 上传，输出在 `switch/host-probe/out/`：`subsdk9`、`main.npdm`、`mailbox-locator.json`、`SHA256SUMS.txt`、安装说明。**手动编译和安装时将同一次构建的 subsdk9 与定位清单配对。**

参考：[Bootstrap 发送副本技术研究](../../docs/host/BOOTSTRAP_SEND_COPY_REFACTOR_2026-10-20.md)。
