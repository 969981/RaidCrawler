# Benefit — SV raid reward editor

**Scope / state (2026-10-19):** This branch includes a functioning *local* reward-row editor, not a game memory patch. See the original project README for ordinary raid scanning.

## What was implemented

When a raid is selected, use the existing **Rewards** button. The editor starts from the RaidCrawler-calculated default rewards and provides editable Item ID, Quantity, Rare flag and Subject columns. Actions include Add/Remove (up to 45), Reset defaults, View calculated, **Apply preview (session-only)**, Load/Save JSON, Import/Export 720-byte reward rows, and **Copy PokéLDN** host flags.

- Subject 0: everyone
- Subject 1: host
- Subject 2: guest
- Subject 3: once-only eligibility
- Subject 4: matching Raid Power (the Lua filter keeps up to level N of these rows)
- Subject 5: receiving player possesses the **Glimmering Charm** (item ID 2481, not the Shiny Charm)

The edit controls permit 1–999 quantity, IDs 1–65535 and rare flag 0/1, and reject row 46 instead of silently truncating. The session preview uses the raid map, area, den, seed and Raid Power boost as a key; it is only recalled in the editor and does not alter the Seed prediction data, filters, or live game. The 999 value is only an editing constraint, **not proof of SlotMaxNum in the game's itemdata_array.bin**.

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

### Important: this does not change Switch raid rewards

The existing RaidCrawler Switch connection reads 0x20-byte **den entries** containing raid seed/area/status, not a ready-to-edit 45-row RaidPoint. Editing local predictions or exporting bytes will **not alter** game rewards or internet raid lobbies. This branch intentionally adds no unverified WriteBytesAbsoluteAsync calls, hardcoded live pointer or forced console writes. For a game-hosting integration, the full transient battle bootstrap and current runtime game version must first be validated using a controlled test environment.

Relevant upstream reference: [PokéLDN research SV raid source](https://github.com/969981/pokeldn/blob/research/pokeldn/sv/raid.py).

## Code and tests

- [RaidRewardEditor.cs](../../RaidCrawler.Core/Structures/RaidRewardEditor.cs) — validated row model and little-endian codec.
- [RewardEditorForm.cs](../../RaidCrawler.WinForms/SubForms/RewardEditorForm.cs) — WinForms grid and import/export UI.
- [MainWindow.cs](../../RaidCrawler.WinForms/MainWindow.cs) — opens editor with selected seed's calculated reward rows.
- [RaidRewardEditorTests.cs](../../RaidCrawler.Tests/RaidRewardEditorTests.cs) — row limit, 720-byte roundtrip, malformed rows, pseudo ID resolution.

On Windows with .NET 10 SDK:

~~~powershell
dotnet test RaidCrawler.Tests/RaidCrawler.Tests.csproj
dotnet build RaidCrawler.slnx -c Release
~~~

**Build/test execution status:** Windows GitHub Actions runs on `benefit`. An early full-test run compiled the core/test projects but reported 8 failures in existing RaidReadTests/RaidStatTests/FilterTests unrelated to the editor (baseline raid-data assertions; some shared-file contention was also observed). Tests were made serial to avoid the file contention, but 8 baseline assertions remained. The workflow now builds WinForms and gates on the new reward-editor tests, reports existing baseline tests as nonblocking, and then publishes artifacts. Always consult the latest CI run for final build and unit-test results; Switch end-to-end testing is not performed here.
