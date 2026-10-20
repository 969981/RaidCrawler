# N1 RAM mailbox: RaidCrawler C# + sys-botbase

**Scope (2026-10-20):** Research only. Native ARM64 build and Windows tests are separate from real Switch validation. Supports Pokémon Violet 4.0.0 ONLY, title ID 01008F6008C5E000. Static research Build ID 709bfd66115298640155fcc4979dba151c7cc79a still requires independent console verification. Inline Hook installation patches code; gameplay rewards/network/save are read-only.

## Use

1. Install the experimental subsdk9 native probe on an authorized, recoverable test Switch with Atmosphère and sys-botbase. This is still required ONCE for each native-module build; no sys-botbase firmware modification is necessary.
2. Boot Violet 4.0.0; native module defaults to capture DISABLED. Its debug output contains **[RC-N1] mailbox=0x... protocol=1 size=0x600**. Copy the actual runtime address, not an address from another boot. There is currently **no automatic subsdk9 global-address discovery via the standard sys-botbase API**, so access to that initial debug line is necessary.
3. Open RaidCrawler, connect to Switch normally via Wi-Fi or USB, click the new **Host Probe** button next to Settings/Date Tools, paste the mailbox address, click **Read status**. A valid probe shows hook=1.
4. Click **Start capture**; C# writes little-endian 1 to mailbox absolute address +0x10 via sys-botbase. Begin a real host local raid. Clicking **Read / compare** will read two 720-byte reward regions and show source and bootstrap SHA-256 equality. Press **Stop capture** to write zero to the enable flag.
5. This does not modify raid rewards and does not claim receiver receipt or NSO compatibility. Do not concurrently run background RaidCrawler scanning on the same connection while probing.

## Mailbox v1, 0x600 bytes total, all little-endian

| Offset | Bytes | Description |
|---:|---:|---|
| +0x00 | 8 | RCHMB01 followed by NUL |
| +0x08 | 4 | version = 1 |
| +0x0C | 4 | size = 0x600 |
| +0x10 | 4 | enabled (0 or 1) - the ONLY writable control field exposed to C# |
| +0x14 | 4 | status: 0 not ready, 1 hook installed, 2 fingerprint mismatch |
| +0x18 | 4 | capture count |
| +0x1C | 4 | dropped count |
| +0x20 | 4 | generation, odd while being written |
| +0x24 | 4 | reserved |
| +0x28 | 8 | last capture tick |
| +0x30 | 16 | reserved |
| +0x40 | 720 | source reward 45 rows |
| +0x310 | 720 | decoded bootstrap reward 45 rows |
| +0x5E0 | 32 | reserved |

Native callback copies both regions without SD, IO, or network. C# reads header, arrays, header and rejects if count/generation differ. C++ static_assert and C# tests check offsets. This guards against torn captures but NOT against incorrect native pointer assumptions.

## Files

- Native code: [source/program/main.cpp](source/program/main.cpp)
- Native build: [build.sh](build.sh); [ARM64 CI](https://github.com/969981/RaidCrawler/actions/workflows/host-native-probe.yml)
- C# mailbox protocol: [HostProbeMailbox.cs](../../RaidCrawler.Core/Host/HostProbeMailbox.cs)
- C# UI: [HostProbeWindow.cs](../../RaidCrawler.WinForms/SubForms/HostProbeWindow.cs)
- Tests: [HostProbeMailboxTests.cs](../../RaidCrawler.Tests/Host/HostProbeMailboxTests.cs)

## Limitations and safeguards

- Native static 12-word opcode fingerprint must match Violet 4.0.0. It DOES NOT query the actual NSO Build ID; manual verification remains essential.
- Native game pointer offsets x20+0x138 and x23+0x798 are based on audited static instructions and donor data. Failure to prove their live validity can crash the process; build success is not real-console proof.
- The address shown in debug logs changes with ASLR. It is not a persistent fixed main offset. Do not blindly memory-scan all RAM, reuse an old address, or assume main NSO base equals subsdk9 base.
- Captures only the most recent pair, and the capture can be overwritten on another hook hit. A single pair matching establishes only two input buffers match during that callback, not a verified end-to-end network chain.
- The default sys-botbase connector does not support arbitrary custom commands to change the game's outgoing packets. Only +0x10 mailbox enable/disable is implemented; NO reward injection or custom pack sending.
- Other C# routines share the same sys-botbase connection; concurrent reads should be avoided pending a shared IO scheduler.

