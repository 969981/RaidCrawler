#!/usr/bin/env python3
"""Offline read-only exact-build audit of Violet 4.0.0 reward-table native bridges.

The listed ELF64 RELA entries pair Lua native METHOD NAME addends with
adjacent FUNCTION-POINTER addends in the runtime registry. The callbacks are
not the final Lua BattleRewardData object, and static bindings do NOT prove
the solo-raid code path ran.

No source ELF is included in this repository and this script never writes it.
"""
from __future__ import annotations

import argparse
import hashlib
import json
import struct
from pathlib import Path

EXPECTED_SHA256 = "d17403eaa9d844db82f647fd2f1518806b10c0518d53602fc0ac1766298fdba0"
ELF_TEXT_FILE_OFFSET = 0x8D8
ELF_RODATA_RVA_FROM_FILE_DELTA = 0x568
R_AARCH64_RELATIVE = 0x403

# name, original ASCII-string file offset, name RELA file offset,
# callback RELA file offset, destination slots, function RVA.
BRIDGES = (
    ("f21AF1B35", 0x3B5CFBE, 0x3528538, 0x3528550,
     0x4429760, 0x4429768, 0x1AF24F0),
    ("f66894ABE", 0x3B18CCE, 0x3528568, 0x3528580,
     0x4429770, 0x4429778, 0x1AF2670),
    ("f859EFF3C", 0x3B8AFE1, 0x350E3A8, 0x350E3C0,
     0x441F100, 0x441F108, 0x1A27FC0),
)


def parse_rela(blob: bytes, file_offset: int) -> tuple[int, int, int]:
    """Return (r_offset, r_info, r_addend) from a 24B ELF64 RELA record."""
    if file_offset < 0 or file_offset + 24 > len(blob):
        raise ValueError("ELF64 RELA record out of bounds")
    return struct.unpack_from("<QQq", blob, file_offset)


def inspect(blob: bytes) -> dict:
    digest = hashlib.sha256(blob).hexdigest()
    if digest != EXPECTED_SHA256:
        raise ValueError("Not the exact audited Violet 4.0.0 ELF SHA256")
    if blob[:6] != b"\x7fELF\x02\x01":
        raise ValueError("ELF64 little endian is required")

    bindings = []
    for (name, string_offset, name_off, function_off,
         name_slot, function_slot, native_rva) in BRIDGES:
        name_bytes = name.encode("ascii") + b"\0"
        if blob[string_offset:string_offset + len(name_bytes)] != name_bytes:
            raise ValueError(f"Method string changed: {name}")
        n_dest, n_type, n_addend = parse_rela(blob, name_off)
        f_dest, f_type, f_addend = parse_rela(blob, function_off)
        expected_name_rva = string_offset - ELF_RODATA_RVA_FROM_FILE_DELTA
        if (n_dest, n_type, n_addend) != (
            name_slot, R_AARCH64_RELATIVE, expected_name_rva
        ):
            raise ValueError(f"Lua method-name RELA mismatch: {name}")
        if (f_dest, f_type, f_addend) != (
            function_slot, R_AARCH64_RELATIVE, native_rva
        ):
            raise ValueError(f"Native callback RELA mismatch: {name}")
        if f_dest - n_dest != 8 or function_off - name_off != 24:
            raise ValueError(f"Native registry pair not adjacent: {name}")
        prologue = struct.unpack_from(
            "<I", blob, native_rva + ELF_TEXT_FILE_OFFSET
        )[0]
        if name in ("f21AF1B35", "f66894ABE") and prologue != 0xA9BE7BFD:
            raise ValueError(f"Unexpected callback prologue: {name}")
        if name == "f859EFF3C" and prologue != 0xA9BB7BFD:
            raise ValueError("Unexpected getter callback prologue")
        bindings.append({
            "name": name,
            "name_rela_file_offset": f"0x{name_off:X}",
            "function_rela_file_offset": f"0x{function_off:X}",
            "name_runtime_slot_rva": f"0x{name_slot:X}",
            "function_runtime_slot_rva": f"0x{function_slot:X}",
            "native_wrapper_rva": f"0x{native_rva:X}",
            "type": "R_AARCH64_RELATIVE",
            "verified": True,
        })
    return {
        "elf_sha256": digest,
        "evidence": "EXACT_ELF_STATIC_NATIVE_REGISTRY",
        "bridges": bindings,
        "runtime_called_in_solo_raid": "NOT_OBSERVED",
        "lua_400_bytecode_verified": False,
        "rewards_modified": False,
    }


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("elf", type=Path)
    args = parser.parse_args()
    result = inspect(args.elf.read_bytes())
    print(json.dumps(result, ensure_ascii=False, indent=2))


if __name__ == "__main__":
    main()
