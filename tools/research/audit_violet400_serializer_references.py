#!/usr/bin/env python3
"""Read-only exact-SHA Violet 4.0.0 ELF audit of LDN serializer references.

Reports direct ARM64 B/BL references and data pointers to main+0xE327FC.
A zero direct-call count does not prove the function is never executed.
No runtime memory operations, patch generation, or game modification.
"""
from __future__ import annotations

import argparse
import hashlib
import json
import struct
from pathlib import Path

SHA = "d17403eaa9d844db82f647fd2f1518806b10c0518d53602fc0ac1766298fdba0"
TEXT_OFFSET = 0x8D8
TEXT_LENGTH = 0x343FC90
RODATA_OFFSET = 0x3440568
RODATA_RVA = 0x3440000
TARGET = 0xE327FC
POINTER_FILE_OFFSET = 0x359C720
TABLE_TYPE_WORD = 0x403


def direct_references(code: bytes, text_rva: int, target: int):
    branches = []
    for offset in range(0, len(code) - 3, 4):
        op = struct.unpack_from("<I", code, offset)[0]
        mask = op & 0xFC000000
        if mask not in (0x14000000, 0x94000000):
            continue
        imm = op & 0x03FFFFFF
        if imm & 0x02000000:
            imm -= 1 << 26
        pc = text_rva + offset
        if pc + imm * 4 == target:
            branches.append({
                "source_rva": f"0x{pc:X}",
                "kind": "BL" if mask == 0x94000000 else "B",
            })
    return branches


def audit(data: bytes):
    if hashlib.sha256(data).hexdigest() != SHA:
        raise ValueError("Not the audited Violet 4.0.0 ELF")
    if len(data) < TEXT_OFFSET + TEXT_LENGTH or data[:4] != b"\x7fELF":
        raise ValueError("Unexpected binary size or ELF type")
    text = data[TEXT_OFFSET:TEXT_OFFSET + TEXT_LENGTH]
    direct = direct_references(text, 0, TARGET)
    needle = struct.pack("<Q", TARGET)
    occurrences = []
    pos = 0
    while True:
        pos = data.find(needle, pos)
        if pos < 0:
            break
        if pos % 8 == 0:
            occurrences.append(pos)
        pos += 1

    table = []
    for file_offset in occurrences:
        va = file_offset - RODATA_OFFSET + RODATA_RVA
        preceding = struct.unpack_from("<Q", data, file_offset - 8)[0]
        following = struct.unpack_from("<Q", data, file_offset + 8)[0]
        table.append({
            "file_offset": f"0x{file_offset:X}",
            "rva": f"0x{va:X}",
            "preceding_word": f"0x{preceding:X}",
            "following_word": f"0x{following:X}",
        })
    return {
        "source": "EXACT_ELF_STATIC_ONLY",
        "serializer_entry": f"0x{TARGET:X}",
        "direct_calls": direct,
        "aligned_u64_references": table,
        "expected_pointer_present": POINTER_FILE_OFFSET in occurrences,
        "function_ptr_table_type_word": f"0x{TABLE_TYPE_WORD:X}",
        "supports_indirect_dispatch_hypothesis": not direct and
            POINTER_FILE_OFFSET in occurrences and
            struct.unpack_from("<Q", data, POINTER_FILE_OFFSET - 8)[0] == TABLE_TYPE_WORD,
        "solo_execution_proven": False,
        "native_runtime_hook_hit_proven": False,
    }


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("elf", type=Path)
    args = parser.parse_args()
    print(json.dumps(audit(args.elf.read_bytes()), indent=2))
