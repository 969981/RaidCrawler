#!/usr/bin/env python3
"""Read-only exact-SHA Violet 4.0.0 ELF audit of LDN serializer references.

Reports direct ARM64 B/BL references and RELA dynamic pointer initializers to main+0xE327FC.
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
RELA_DYN_FILE_OFFSET = 0x34405D8
RELA_DYN_SIZE = 0x678CD8
R_AARCH64_RELATIVE = 0x403
TARGET = 0xE327FC
RELA_ADDEND_FILE_OFFSET = 0x359C720
EXPECTED_RUNTIME_POINTER_RVA = 0x44592E0

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
    # These are ELF64 Rela triples, NOT native dispatch-table triples.
    # r_offset identifies the runtime .data address to receive the pointer.
    records = []
    for off in range(RELA_DYN_FILE_OFFSET,
                     RELA_DYN_FILE_OFFSET + RELA_DYN_SIZE, 24):
        r_offset, r_info, r_addend = struct.unpack_from("<QQq", data, off)
        if r_addend == TARGET:
            records.append({
                "rela_record_file_offset": f"0x{off:X}",
                "r_offset_rva": f"0x{r_offset:X}",
                "r_info": f"0x{r_info:X}",
                "r_addend": f"0x{r_addend:X}",
                "relative_relocation": r_info == R_AARCH64_RELATIVE,
            })
    expected = any(
        record["rela_record_file_offset"] ==
            f"0x{RELA_ADDEND_FILE_OFFSET - 16:X}" and
        record["r_offset_rva"] == f"0x{EXPECTED_RUNTIME_POINTER_RVA:X}" and
        record["relative_relocation"]
        for record in records
    )
    return {
        "source": "EXACT_ELF_STATIC_ONLY",
        "serializer_entry": f"0x{TARGET:X}",
        "direct_calls": direct,
        "rela_dyn_pointer_initializers": records,
        "expected_relative_relocation": expected,
        "supports_indirect_dispatch_hypothesis": not direct and expected,
        "solo_execution_proven": False,
        "native_runtime_hook_hit_proven": False,
    }


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("elf", type=Path)
    args = parser.parse_args()
    print(json.dumps(audit(args.elf.read_bytes()), indent=2))
