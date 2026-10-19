# Windows-only ARM64 code write probe (no additional Switch plugin)

## User constraint
Keep the existing, original sys-botbase. No Skyline, no separate NRO, no sys-botbase replacement, no SD-card file copying. RaidCrawler communicates by existing `ReadBytesMainAsync` and `WriteBytesMainAsync` commands.

## Evidence: October 19, 2026 user diagnostic
The user-supplied `raid-20261019-130229-65400606.jsonl` returned:

- TitleID `01008F6008C5E000`, Violet version `4.0.0`, botbase `2.5`;
- Build ID 8-byte prefix `709bfd6611529864` matches (not a complete NSO identity);
- 8/8 expected serializer code opcodes match, including `main+0xE32984 = 0xAA1503E8`;
- 880 bytes at `main+0x343FC90..0x3440000` are mapped/readable and zero-filled;
- `executableVerified=false`, `safeToWrite=false`. The diagnostic did not write anything.

The above does **not** demonstrate that the zero-filled region is executable. It is a potential continuation of the RX text mapping at the page boundary. Page mapping and actual execute permission require separate evidence.

## New implementation: controlled write capability test

Entry: Reward Editor > Write Rewards > Hook diagnostics > **验证写入并恢复**.

1. Explicit warning and user confirmation; intended for offline/single-player testing.
2. Confirm Violet title, version 4.0.0, 8-byte Build ID prefix, all eight *original* opcode anchors, and stable main NSO base. An installed inline hook at the candidate instruction automatically blocks this test.
3. Read all 880 bytes and refuse a nonzero, short or inaccessible region.
4. In the otherwise unused candidate interval, at `main+0x343FCC0`, send exactly four bytes `1F 20 03 D5` (ARM64 NOP) using the installed sys-botbase `pokeMain` transport.
5. Read back the same four bytes and report `WriteVerified`.
6. **Always attempt to restore four zero bytes**, even on failed write/read or cancellation, with a separate bounded rollback token. Read back the restored zeros and report `RestoreVerified`.
7. Emit `write-probe-start` and `write-probe-result` JSONL stages. If restoring fails, instruct a game restart and new zero-gap inspection.

**Boundaries:** This probe never rewrites the real Hook entry `main+0xE32984`, never installs `B` to the cave, never executes the code cave, does not patch `RaidPoint`, does not alter rewards, and does not establish RX execute permission. Even if the test succeeds, the full Hook cannot be claimed as completed.

## Subsequent implementation conditions

- Determine that the candidate *same page* is executable and that debug writes remain visible to the instruction-fetch path (Atmosphère Mesosphere has an I-cache invalidation path, but that is not proof of this live target).
- Validate the 880-byte offline stub layout, register survival, copied 720-byte reward template, and once-only mode switch.
- Provide a separate, explicit, opt-in staged installation: write 880B with mode=0; verify complete read-back; re-check process/entry identity; finally write one four-byte ARM64 B at the entry and verify. Rollback entry first, and do not erase an in-use trampoline.
- Test execution in controlled offline battles and the actual win/reward/save path. A 4-byte probe or valid opcode does not prove a hook hit or successful saved reward.
- Do not send a user into Skyline installation instructions or copy a separate NRO for the Windows-only route.

**Risks:** Even code-gap test changes game process memory momentarily and could fail due to kernel permissions, process restart, race conditions or a nonstandard code map. It is explicitly user-triggered, never automatic. A failed restoration is a reason to restart the game rather than guessing the running process state.
