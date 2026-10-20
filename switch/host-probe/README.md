# RaidCrawler / Switch 房主 N1-N2 探针（《紫》4.0.0）

> **状态：工程编译与离线验证通过，尚未实机加载和参与者领取验证。** 普通公开 CI 只打包只读 Native；实验写入必须手动明确编译，且不自动上传。

## 下载与部署

在 [host Windows Actions](https://github.com/969981/RaidCrawler/actions/workflows/host.yml) 选择最新成功任务，下载 **RaidCrawler-host-experimental**。此构建包含：

- Windows RaidCrawler 客户端和中文“房主探针”窗口
- 与同一 GitHub commit 生成的 `mailbox-locator.json`（位于程序同目录）
- `Native/subsdk9`、`Native/main.npdm`、哈希与安装提示（该 Native 仅用于只读与预览）

实际测试设备必须可恢复、授权使用，并核对《紫》Title ID `01008F6008C5E000`、版本 `4.0.0` 与 Build ID `709bfd66115298640155fcc4979dba151c7cc79a`。需根据 Atmosphère/exlaunch 自身部署说明安装 Native；不要盲目覆盖已有同名插件。无需修改 sys-botbase 固件，也不添加第二条通信端口。**本项目不会通过 CI 自动向 Switch 部署文件。**

## C# 只通过 sys-botbase 操作

1. 打开 RaidCrawler，连接 Switch，点击主窗口“房主探针”。
2. 点击 **自动定位**。C# 通过 `GetMainNsoBaseAsync`、原版 Hook 跳板和同次构建的定位清单查找内存邮箱。若无对应文件或指令不匹配则拒绝。调试时仍可手动输入本次启动日志中的地址。
3. 点击 **读取状态**：期望 Hook 状态=1，普通 Native 的“写入能力”=否；然后点击 **开始捕获**。
4. 真正的房主进行一次太晶团体战后，点击 **读取并比较**；核对捕获次数增长、原版两份 720B 是否完全一致。
5. 可以点 **预览 Subject=5**，设置先机球 ID15 数量1–10。下一次 Bootstrap 的返回码 10 表示当前布局允许安排单行、但没有实际改写。
6. 普通 CI 的 **试验追加** 因原生写入能力被禁用，会安全拒绝；停止捕获同时清空请求模式。

## 手动编译的单行实验分支（不上传）

只有经过 N1 真实房主的完整只读验收后才适合考虑。命令：

```bash
HOST_N2_EXPERIMENTAL=1 bash switch/host-probe/build.sh
```

必须将 **同一次手动编译** 的 `out/subsdk9` 和 `out/mailbox-locator.json` 一起替换，以避免 ASLR 清单与 Native 回调不匹配。Native 只支持在原版 memcpy 后、压缩前，对输出 Bootstrap 第一条空行追加 **先机球 ×1–10、Subject=5**；原版奖励必须字节级保持不变。N2 的请求只尝试一次，即使失败也会自动解除武装。

Native 返回码 **20 只代表发送前的明文缓冲已修改**；并不能证明 Pia 网络传输、参与者筛选、背包入账、保存持久化或 NSO 官方互联网房间有效。

## 当前内存邮箱 ABI

邮箱大小为 `0x600` 字节，版本 v2，Magic `RCHMB02\0`。`+0x10` 捕获开关，`+0x24` Native 写入能力，`+0x40` 原始奖励 720B，`+0x310` 发送前明文奖励 720B，`+0x5E0` N2 模式，`+0x5E4` 起 16B 奖励请求，`+0x5F4/+0x5F8/+0x5FC` 返回码/追加次数/原行数。C# 读两份奖励及 N2 结果时检查捕获的 generation 与 count 不变，拒绝跨局拼接的数据。

完整结构、ELF 地址、donor 复核和实机验收矩阵参见 [ELF_REVALIDATION_N1_N2.md](../../docs/host/ELF_REVALIDATION_N1_N2.md)。**尚未取得《朱》的 4.0.0 ELF，现有探针严禁用于《朱》。**
