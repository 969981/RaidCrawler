# P1-B2 · Violet 4.0.0 lua_State / CallInfo live structure audit

Date: 2026-10-20. Branch: benefit. Status: exact-ELF static layout VERIFIED; P1-B1 first live root-pointer observation supplied; P1-B2 main-state header / active CallInfo runtime capture STILL PENDING.

## 1. New live evidence from user's Switch

At 10:41:04.925 the Legacy network-hook status was Empty, epoch=N/A, original instructions and zero-filled 880B code-gap candidate. This means the old outgoing network hook is not installed; it says nothing about Lua reward generation.

At 10:41:21.719 the P1-B1 probe returned CANDIDATE_POINTER_STABLE_NOT_VM_VERIFIED. Thus the proposed global script-context reference and state-pointer field are readable and agree on two reads on the user's actual Switch. This is a real pointer-chain observation, not a lua_State structure, Lua closure, or function-hit attestation.

## 2. Exact ELF instruction evidence

User's exact ELF SHA256: d17403eaa9d844db82f647fd2f1518806b10c0518d53602fc0ac1766298fdba0.
The separate audit script tools/research/audit_violet400_lua_vm_frame_layout.py checks 15 native AArch64 opcodes (all independently checked against the original ELF). These anchors include:

- main+0x3B45C stores and preincrements the returned allocator pointer by 8, establishing L = allocation+8.
- main+0x3B464 initializes the GC thread tag at L+0x08 (byte 0x08). The neighboring GC mark byte may change.
- main+0x3B474 increments X8 to L+0xC8; main+0x3B478 stores X8 into L+0x18, i.e. global_State pointer for the main Lua thread.
- main+0x3B074 initializes L+0x30 (stack base); main+0x3B078 initializes L+0x40 (tbclist).
- main+0x3B1E8 uses STR X8,[X19,#0x60]! and updates X19 to L+0x60. Later pre/post-indexed offsets MUST be calculated relative to the modified register. This yields the original L+0x10 top, L+0x20 current CallInfo, L+0x28 stack_last, and L+0x60 base_ci.func, L+0x68 base_ci.top.

Validated field offsets for this **main** Lua state:

| lua_State offset | Field | Static expected invariant |
| --- | --- | --- |
| +0x08 | GC object type byte | 0x08 (thread) |
| +0x10 | top | 16-byte aligned pointer into Lua stack |
| +0x18 | global_State* | For initial main state, L+0xC8 |
| +0x20 | current CallInfo* | nonzero, aligned |
| +0x28 | stack_last | stack upper safe limit |
| +0x30 | stack | stack base |
| +0x40 | tbclist | points within Lua stack |
| +0x60 | base_ci.func | base-frame stack function slot |
| +0x68 | base_ci.top | base-frame top |

The initial Lua 0x2D0=720-byte stack is a different allocation from a 45×16B=720-byte raid network reward area.

## 3. Implemented bounded, read-only stage

- RaidCrawler.Core/Structures/Violet400LuaVmFrameProbe.cs: first invokes the stable P1-B1 global-root probe. It then reads only a fixed 0x70-byte main state header, validates GC tag and global-state pointer (L+0xC8), verifies stack bounds, top, and base CallInfo.
- If current ci != L+0x60, at most one 0x20-byte CallInfo and one 0x10-byte function TValue are read. Lua 5.4 TValue type tag 0x66 is treated ONLY as a Lua-closure type *candidate*, not as an authenticated Proto or a target-function hit.
- The header and global root are sampled again. Null, misaligned, short, inconsistent, or changed values fail closed. At most 10 bounded read calls; no arbitrary heap scan, closure/proto dereference, writing, system module replacement, or hook installation.
- RaidCrawler.WinForms/SubForms/RaidRewardDiagnosticForm.LuaVm.cs: Advanced tools now provide P1-B2: read-only Lua main state / CallInfo checks, after original TitleID, Version, BuildID and Main NSO fingerprint validation. Diagnostic event: lua-frame-readonly. Heap addresses are not logged.
- RaidCrawler.Tests/Violet400LuaVmFrameProbeTests.cs: base frame, active Lua/C function-tag candidates, invalid pointer layouts, shortened reads, snapshot races, and cancellation.
- tools/research/test_audit_violet400_lua_vm_frame_layout.py and the Charm P1 CI guard ELF opcode inferences without distributing the game binary.

## 4. Expected outputs and safe interpretation

| Status | Meaning |
| --- | --- |
| MAIN_STATE_LAYOUT_MATCH_BASE_CI_IDLE | Main Lua state and base frame are structurally plausible, sampled at apparent base frame |
| ACTIVE_CALLINFO_SAMPLED_NO_EXECUTION_ATTESTATION | Current CallInfo differs from base; bounded func TValue sampled; execution of reward code NOT established |
| MAIN_STATE_LAYOUT_MISMATCH | This address is not an expected main-state header, or state changed during the read |
| STACK_OR_BASE_CI_LAYOUT_MISMATCH | Stack, base CallInfo or offsets do not match at this point |
| STATE_FIELDS_CHANGED_DURING_READ | A non-atomic sample changed; discard result |
| ROOT_NOT_STABLE_OR_NULL | Root pointer no longer stable, or script context destroyed |

No status here means reward LotteryRewardItem or GameEventBattleResult actually ran. Standard sys-botbase 2.5 polling cannot guarantee a sample exactly at an arbitrary Lua method's entry/return.

## 5. Next hardware steps

1. With backed-up save and Violet 4.0.0 running offline, open RaidCrawler > reward editor > diagnostics > Advanced tools > P1-B2. First sample while idle, then optionally around an offline solo raid and retain event JSONL.
2. If main-state layout matches, separately research safe, execution-level VM observation for the exact closures previously mapped in main.blua: LotteryRewardItem=root/2899, F316 filter=root/4870, RaidResultAddItem=root/3756. A sampled TValue type is not sufficient. Requires exact live Proto identity and call/return evidence.
3. Before any reward write, capture same-run A0 original reward list, B new filtered [3], C setupData[92][3], plus actual Bag count and save reload. Until proven, keep P1-C/D/E unverified.

P1-B2 stage has not received a new live lua-frame-readonly log. Current source/CI work is strictly research and no 20×999 real-world reward capability is claimed.
