# Benefit — SV raid reward editor

**Scope / state (2026-10-19):** This branch includes a functioning *local* reward-row editor plus an optional **explicitly launched** one-raid PokéLDN WSL host, not a game memory patch. See the original project README for ordinary raid scanning.

**跨项目原生技术研究：** [PokéLDN × 《紫》4.0.0 ELF 单场奖励注入、动态指针、Subject=5 及保存调用链](pokeldn-elf-single-raid-hosting.md)。

## 2026-10-19 简体中文界面（benefit-host-v0.1.1）

- 默认语言为 `zh-Hans`，通过 **PKHeX.Core `GameInfo.GetStrings("zh-Hans")`** 直接加载道具、招式、性格、特性和宝可梦名称，不维护静态手工对照表。
- 主界面常用按钮、数据标签、奖励编辑器、查看默认奖励、单场 PokéLDN 主持窗口，以及设置窗口的常用控件已经加入简体中文。
- Subject 下拉框使用“0－所有玩家／1－房主／2－参与者／3－一次性奖励／4－团体战料理／5－晶耀护符”。`Subject` 背后仍然保存整数，二进制 `u32` 与 CLI `:5` 不变。
- PokéLDN 的故事进度下拉框显示中文，但继续向 Python 发送 `4star`、`5star`、`6star` 等英文协议枚举。
- 需要英文界面时，在程序目录 `config.json` 中增加或修改 `"Language": "en"`，**重新启动**程序。缺省字段视为 `zh-Hans`；不会改变语言以外的存档与奖励数据。
- 汉化不会自动实现活动坑主持、任天堂官方联网，也不会修改任何道具 ID。
- Windows CI 新增 `ChineseGameStringsTests`，验证真实 PKHeX 简体中文资源是否可读取，以及与英文字串共享 ID。

## What was implemented

When a raid is selected, use the existing **Rewards** button. The editor starts from the RaidCrawler-calculated default rewards and provides editable Item ID, Quantity, Rare flag and Subject columns. Actions include Add/Remove (up to 45), Reset defaults, View calculated, **Apply preview (session-only)**, Load/Save JSON, Import/Export 720-byte reward rows, and **Copy PokéLDN** host flags.

- Subject 0: everyone
- Subject 1: host
- Subject 2: guest
- Subject 3: once-only eligibility
- Subject 4: matching Raid Power (the Lua filter keeps up to level N of these rows)
- Subject 5: receiving player possesses the **Glimmering Charm** (item ID 2481, not the Shiny Charm). When host launches, the argument is explicitly `--raid-reward ITEM:QUANTITY:5`; the receiving console executes `Bag.GetNumItem(2481) > 0` eligibility filtering.

The edit controls permit 1–999 quantity, IDs 1–65535 and rare flag 0/1, and reject row 46 instead of silently truncating. The session preview uses the raid map, area, den, seed and Raid Power boost as a key; it is only recalled in the editor and does not alter the Seed prediction data, filters, or live game. The 999 value is only an editing constraint, **not proof of SlotMaxNum in the game's itemdata_array.bin**.

The editor provides **Set selected Charm (5)**, **All Charm (5)** and **All players (0)** shortcuts. Editing still accepts all six subject values; the PokéLDN hosting action refuses subjects 1–4 with a clear validation error until upstream implements them. This ensures a host cannot accidentally downgrade a charm-only reward into an all-player reward.

### 45 rows, precisely

In Pokémon Scarlet/Violet's battle initialization, the **RaidPoint** contains 45 reward slots at RaidPoint offset **0xE4** (45 × 0x10 bytes), with the row format in little endian:

| Row field | Byte offset | Type |
|---|---:|---|
| Item ID | +0x00 | u32 |
| Quantity | +0x04 | u32 |
| Rare | +0x08 | u32 |
| Subject | +0x0C | u32 |

The row area occupies RaidPoint +0xE4..0x3B3. The last four bytes before the summary (0x3B4..0x3B7) are not a 46th row. In the uncompressed 0xAA0-byte battle bootstrap, the row area begins at +0x79C. The exported **720-byte file is only these 45 rows**, not the full 0x3E8-byte RaidPoint and not a ready-to-send compressed packet.

### Default rewards from RaidCrawler

RaidCrawler's own predicted result is a list of (item ID, amount, subject), based on seed and reward tables. It uses pseudo IDs 10000 (species material) and 20000 (Tera shard) for display. The editor converts these with Rewards.GetMaterial(species) and Rewards.GetTeraShard(teraType) before export. Unknown pseudo IDs are rejected by validation. Existing prediction data does not preserve every Rare flag, so it defaults to 0 and can be adjusted.

### Optional PokéLDN host-argument export

The **Copy PokéLDN** action builds actual command flags using the supported host grammar (examples: `--raid-reward 1:999:5 --raid-reward 796:7`). Because PokéLDN presently supports custom `subject=0` and `subject=5` only, this action refuses other subject values or `rare=1` rather than silently altering meaning. Use the full raw binary export for offline research of subject 1–4. Copying host arguments does not itself create a lobby or send packets.

### Hosted raids via PokéLDN (opt-in)

After selecting a supported ordinary/black den and configuring rows, press **Host selected raid**. The new modal starts a separate `wsl.exe` process using the **selected seed / version / map / content**, with chosen **story progress**, a legal **host Pokémon record (.pk9)**, one to three guest consoles, and `--raid-reward ITEM:QUANTITY:SUBJECT` flags. This is a single hosted LDN raid; it does **not** rewrite any of the game's on-map den save entries, automatically host a queue, or join Nintendo's online matchmaking.

The host form requires you to configure an installed WSL distribution, **absolute Linux path to `pokeldn`**, and absolute Linux path to a host `.pk9` record. It shows the seed and location snapshot, asks for confirmation, starts a logged external process, and provides a Stop button. Subject 0 and 5 and rare=0 are currently the only allowed custom host flags. **Event dens are refused** because PokéLDN needs a matching event delivery file, not merely an event seed. The program does not silently coerce unsupported rows.

The recommended default is local wireless hosting, which requires an LDN-capable AP Wi-Fi interface accessible to PokéLDN (WSL2 by itself may lack that); `--ip-host` is for the ldn_mitm/emulator LAN backend **only**, not Nintendo online. Wi-Fi/radio permission, keys, network support and path checks are intentionally left to the actual PokéLDN preflight and its log. Stopping `wsl.exe` might not stop a detached Linux child in every environment; verify with `ps` if uncertain.

The editor passes a **snapshot of the selection**, not a future mutable UI index. PokéLDN generates raid details from seed and selected progress: confirm boss/species/stars printed by the host before admitting any guest, since mismatches are possible. The host session ends according to PokéLDN's `--seconds`; rehosting another raid requires another explicit launch.

### Important: no direct Switch save/memory patch

The existing RaidCrawler Switch connection reads 0x20-byte **den entries** containing raid seed/area/status, not a ready-to-edit 45-row RaidPoint. Editing local predictions or exporting bytes alone will **not alter** game rewards or internet raid lobbies. The **Host selected raid** action separately passes selected rewards to PokéLDN's local LDN hosting process; it does not modify existing game dens, save blocks or Nintendo's online lobbies. This branch intentionally adds no unverified WriteBytesAbsoluteAsync calls, hardcoded live pointer or forced console writes. For a game-hosting integration, the full transient battle bootstrap and current runtime game version must first be validated using a controlled test environment.

Relevant upstream reference: [PokéLDN research SV raid source](https://github.com/969981/pokeldn/blob/research/pokeldn/sv/raid.py).

## Code and tests

- [RaidRewardEditor.cs](../../RaidCrawler.Core/Structures/RaidRewardEditor.cs) — validated row model and little-endian codec.
- [RewardEditorForm.cs](../../RaidCrawler.WinForms/SubForms/RewardEditorForm.cs) — WinForms grid and import/export UI.
- [MainWindow.cs](../../RaidCrawler.WinForms/MainWindow.cs) — opens editor with selected seed's calculated reward rows.
- [RaidRewardEditorTests.cs](../../RaidCrawler.Tests/RaidRewardEditorTests.cs) — row limit, 720-byte roundtrip, malformed rows, pseudo ID resolution.
- [PokeLdnHostPlan.cs](../../RaidCrawler.Core/Structures/PokeLdnHostPlan.cs) — immutable selected-den snapshot; validates/builds `sv_host.py` argv before any session starts.
- [PokeLdnHostForm.cs](../../RaidCrawler.WinForms/SubForms/PokeLdnHostForm.cs) — WSL host process, explicit confirmation, stdout/stderr log, stop control; settings stored under `%LOCALAPPDATA%/RaidCrawler/pokeldn-host.json`.
- [PokeLdnHostPlanTests.cs](../../RaidCrawler.Tests/PokeLdnHostPlanTests.cs) — exact seed/version/map, event refusal, no unsupported subject/rare, 45-row boundary, explicit ip-host.

On Windows with .NET 10 SDK:

~~~powershell
dotnet test RaidCrawler.Tests/RaidCrawler.Tests.csproj
dotnet build RaidCrawler.slnx -c Release
~~~

**Build/test execution status:** Windows GitHub Actions runs on `benefit`. An early full-test run compiled the core/test projects but reported 8 failures in existing RaidReadTests/RaidStatTests/FilterTests unrelated to the editor (baseline raid-data assertions; some shared-file contention was also observed). Tests were made serial to avoid the file contention, but 8 baseline assertions remained. The workflow now builds WinForms and gates on the new reward-editor tests, reports existing baseline tests as nonblocking, and then publishes artifacts. Always consult the latest CI run for final build and unit-test results; Switch end-to-end testing is not performed here.

## Preview release

**First host preview v0.1.0** (2026-10-19): The source includes selected-den WSL hosting, reward subject 0–5 editing and subject-5 quick actions. Publishing this preview is gated by a clean WinForms build and new-feature tests; it is an experimental LOCAL LDN host workflow requiring PokéLDN installed separately. It is not a Nintendo online reward modifier.


A special `[release-benefit]` commit on `benefit` triggers the Windows CI to upload an independent **pre-release** `benefit-host-v0.1.0` with a `RaidCrawler-benefit-host-win-x64.zip` package. The normal workflow still uploads the GitHub Actions `RaidCrawler` artifact. It does not modify `main`, upstream official releases, production consoles, or any attached game file. The Windows CI must pass compiler and new host tests before the release step.
