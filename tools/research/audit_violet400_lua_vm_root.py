#!/usr/bin/env python3
"""Exact SHA / read-only AArch64 audit of a Lua VM state pointer *candidate*.

The source ELF is user-owned; nothing is patched or uploaded. Addresses in
this report are main+RVA, NOT actual runtime pointers. In particular the
720-byte Lua *stack allocation* is NOT a 720-byte raid reward block.
"""
from __future__ import annotations

import argparse
import hashlib
import json
import struct
from pathlib import Path

ELF_SHA256 = "d17403eaa9d844db82f647fd2f1518806b10c0518d53602fc0ac1766298fdba0"
TEXT_FILE_OFFSET = 0x8D8
GLOBAL_REFERENCE_RVA = 0x4759EB0
SCRIPT_CONTEXT_BIAS = 0x88
LUA_STATE_FIELD = 0x70
# RVAs and exact AArch64 words from the user's Violet4.0.0.elf.
ANCHORS = {
    0x3B050: 0xA9BE7BFD,   # Lua stack allocator entry candidate
    0x3B064: 0x52805A01,   # MOV W1,#0x2D0 = 720B stack
    0x3B070: 0x97FFC9D0,   # BL 0x2D7B0 (allocator)
    0x3B074: 0xF9001A60,   # STR X0,[X19,#0x30] stack base
    0x3B078: 0xF9002260,   # STR X0,[X19,#0x40]
    0x3B450: 0xD63F02A0,   # BLR X21 (allocator; 0x658 bytes)
    0x3B5DC: 0x97FFFE9D,   # BL 0x3B050
    0x1107A64: 0xAA1303F6,  # MOV X22,X19 (script context)
    0x1107A84: 0xF8088EDF,  # STR XZR,[X22,#0x88]! (X22=context+0x88)
    0x1107AF4: 0x97BCCE4B,  # BL 0x3B420 candidate main state constructor
    0x1107B00: 0xF9003A60,  # STR X0,[X19,#0x70]
    0x1107B34: 0xD001B288,  # ADRP X8,0x4759000
    0x1107B38: 0xF9075916,  # STR X22,[X8,#0xEB0]
    0x26076B8: 0x9768CE66,  # child thread calls 0x3B050
    0x2607874: 0xD0010A88,  # ADRP X8,0x4759000
    0x2607878: 0xF9475909,  # LDR X9,[X8,#0xEB0]
    0x260787C: 0x91022274,  # ADD X20,X19,#0x88
    0x2607880: 0xEB14013F,  # CMP X9,X20
    0x2607888: 0xF907591F,  # STR XZR,[X8,#0xEB0] on equality branch
}


def branch_target(rva: int, word: int) -> int:
    if word >> 26 not in (0b100101, 0b000101):
        raise ValueError("Expected BL or B immediate instruction")
    delta = word & 0x3FFFFFF
    if delta & (1 << 25):
        delta -= 1 << 26
    return rva + 4 * delta


def verify(blob: bytes) -> dict:
    digest = hashlib.sha256(blob).hexdigest()
    if digest != ELF_SHA256:
        raise ValueError("Exact Violet 4.0.0 ELF SHA mismatch")
    if blob[:6] != b"\x7fELF\x02\x01":
        raise ValueError("Not ELF64 little-endian")
    code = {}
    for rva, word in ANCHORS.items():
        actual = struct.unpack_from("<I", blob, rva + TEXT_FILE_OFFSET)[0]
        if actual != word:
            raise ValueError(f"Native opcode differs from expected at main+0x{rva:X}")
        code[f"0x{rva:X}"] = f"0x{word:08X}"
    if branch_target(0x1107AF4, ANCHORS[0x1107AF4]) != 0x3B420:
        raise ValueError("Lua state constructor BL target mismatch")
    if branch_target(0x3B5DC, ANCHORS[0x3B5DC]) != 0x3B050:
        raise ValueError("Main Lua stack initializer BL target mismatch")
    if branch_target(0x26076B8, ANCHORS[0x26076B8]) != 0x3B050:
        raise ValueError("Coroutine stack initializer BL target mismatch")
    # x22=context+0x88, and *(context+0x70) receives newly initialized L.
    return {
        "evidence": "EXACT_ELF_STATIC_POINTER_CHAIN",
        "sha256": digest,
        "anchors_verified": len(ANCHORS),
        "opcode_words": code,
        "global_reference_slot_rva": "0x4759EB0",
        "global_value_semantics": "script_context_plus_0x88",
        "state_field_from_script_context": "0x70",
        "candidate_state_expression": "*( *(mainBase+0x4759EB0) - 0x18 )",
        "lua_stack_initial_allocation_bytes": 0x2D0,
        "lua_stack_allocation_is_raid_reward_buffer": False,
        "root_pointer_runtime_observed": False,
        "lua_vm_dispatch_rva_verified": False,
        "lua_closure_live_id_verified": False,
        "abc_arrays_captured": False,
        "game_memory_changed": False,
    }


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("elf", type=Path)
    parser.add_argument("--output", type=Path)
    args = parser.parse_args()
    try:
        report = verify(args.elf.read_bytes())
    except (ValueError, OSError, struct.error) as error:
        parser.exit(2, "ERROR: " + str(error) + "\n")
    content = json.dumps(report, ensure_ascii=False, indent=2) + "\n"
    if args.output:
        args.output.write_text(content, encoding="utf-8")
    else:
        print(content, end="")


if __name__ == "__main__":
    main()
