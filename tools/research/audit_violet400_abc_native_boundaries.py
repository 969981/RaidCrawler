#!/usr/bin/env python3
"""Exact-build, READ-ONLY native-boundary audit for Violet 4.0.0 raid rewards.

Do not mistake Lua/Haxe script methods for independently-addressable main NSO
ARM64 functions. Native registry wrappers shown here are NOT reward-object
pointers and not proof of any live A/B/C capture. No Switch writes or patches.

Usage:
  python3 tools/research/audit_violet400_abc_native_boundaries.py Violet4.0.0.elf
"""
from __future__ import annotations

import argparse
import hashlib
import json
import struct
from pathlib import Path

SHA256 = "d17403eaa9d844db82f647fd2f1518806b10c0518d53602fc0ac1766298fdba0"
TEXT_FILE_OFF = 0x8D8
TEXT_SIZE = 0x343FC90
RODATA_FILE_DELTA = 0x568
RELA_START = 0x34405D8
RELA_SIZE = 0x678CD8
RELA_ENTRY = 24
R_AARCH64_RELATIVE = 0x403
SOURCE_COMMIT = "1f843bd2517c707fbb47491dad58856eaa2c2cc7"

# First pair of entries in the ELF64 Rela registry: 8-byte name slot and the
# adjacent 8-byte native callback slot. Derived from this SHA-locked ELF.
BRIDGES = (
    ("f21AF1B35", "raid_lottery_table_initializer",
     0x4429760, 0x1AF24F0, 0xA9BE7BFD),
    ("f66894ABE", "raid_fixed_table_initializer",
     0x4429770, 0x1AF2670, 0xA9BE7BFD),
    ("fFC03AF97", "bag_save_accessor_native_field_bridge",
     0x4420288, 0x1A3D940, 0xA9BD7BFD),
    ("fFE5328FE", "bag_save_accessor_native_field_bridge",
     0x44202A8, 0x1A3DB80, 0xA9BD7BFD),
)

# These are SCRIPT methods. Absence of text alone is not proof that some
# arbitrary generated assembly cannot implement the behavior; the pinned
# Lua definitions are the controlling positive evidence.
LUA_METHODS = (
    ("F316609DEA110601D", "B", "BattleRewardData filter"),
    ("FCBBD4D63B93E4C04", "C", "GameEventBattleResult.RaidResultAddItem"),
    ("FB01A9B29CD4CC6CB", "after_C", "Bag.AddItem"),
)

SOURCE_LINES = {
    "A": "decompiled/C81390F82EADEF632.lua:469-485,820-1335",
    "B": "decompiled/C849FF496D0E93636.lua:1512-1549",
    "handoff": "decompiled/C849FF496D0E93636.lua:1987-1995",
    "C": "decompiled/C889F6553EDFCD011.lua:1625-1696",
    "bag_accessor": "decompiled/C985427E5D92698CE.lua:31-120",
}

def relas(data: bytes):
    """Yield exact ELF64 Rela triples with file record offsets."""
    if len(data) < RELA_START + RELA_SIZE:
        raise ValueError("ELF missing expected relocation section")
    for i in range(RELA_START, RELA_START + RELA_SIZE, RELA_ENTRY):
        dest, info, addend = struct.unpack_from("<QQq", data, i)
        yield i, dest, info, addend

def verify_native_registrations(data: bytes) -> list[dict]:
    lookup = {}
    for name, purpose, slot, callback, opcode in BRIDGES:
        string_file_off = data.find(name.encode("ascii") + b"\0")
        if string_file_off < 0:
            raise ValueError(f"missing native registry method string: {name}")
        lookup[string_file_off - RODATA_FILE_DELTA] = (
            name, purpose, slot, callback, opcode, string_file_off
        )
    entries = list(relas(data))
    matches = {}
    for i in range(len(entries)-1):
        file_off, dest, info, addend = entries[i]
        if addend not in lookup or info != R_AARCH64_RELATIVE:
            continue
        name, purpose, slot, callback, opcode, string_off = lookup[addend]
        next_file, next_dest, next_info, next_addend = entries[i + 1]
        if (dest != slot or next_dest != dest + 8 or
            next_file != file_off + 24 or next_info != R_AARCH64_RELATIVE or
            next_addend != callback):
            continue
        if callback >= TEXT_SIZE or struct.unpack_from(
            "<I", data, TEXT_FILE_OFF + callback
        )[0] != opcode:
            raise ValueError(f"ARM64 wrapper opcode changed: {name}")
        matches.setdefault(name, []).append({
            "method": name,
            "purpose": purpose,
            "name_rela_file_offset": f"0x{file_off:X}",
            "name_runtime_slot_rva": f"0x{slot:X}",
            "callback_runtime_slot_rva": f"0x{slot+8:X}",
            "native_callback_rva": f"0x{callback:X}",
            "native_first_opcode": f"0x{opcode:08X}",
            "proven": "STATIC_RELOCATION_AND_OPCODE_MATCH",
            "runtime_hit": "NOT_OBSERVED",
        })
    for name, *_ in BRIDGES:
        if len(matches.get(name, [])) != 1:
            raise ValueError(f"missing or ambiguous RELA registration: {name}")
    return [matches[name][0] for name, *_ in BRIDGES]

def inspect(data: bytes) -> dict:
    digest = hashlib.sha256(data).hexdigest()
    if digest != SHA256:
        raise ValueError("ELF SHA256 mismatch: only the audited Violet 4.0.0 ELF")
    if data[:6] != b"\x7fELF\x02\x01":
        raise ValueError("Expected little-endian ELF64")
    lua_version = b"Lua 5.4\0"
    marker = data.find(lua_version)
    if marker < 0 or marker - RODATA_FILE_DELTA < 0x3440000:
        raise ValueError("Lua 5.4 string not found inside expected rodata")
    native = verify_native_registrations(data)
    script = []
    for name, stage, label in LUA_METHODS:
        positions = []
        next_offset = 0
        while True:
            idx = data.find(name.encode("ascii"), next_offset)
            if idx < 0:
                break
            positions.append(f"0x{idx:X}")
            next_offset = idx + 1
        script.append({
            "lua_hash": name, "stage": stage, "script_method": label,
            "ascii_hash_locations_in_elf": positions,
            "dedicated_main_native_entry": "NOT_ESTABLISHED",
            "reason": "Method implemented as a pinned Lua script closure; "
                      "method name absence is ancillary evidence only.",
        })
    return {
        "elf_sha256": digest,
        "source_lua_commit": SOURCE_COMMIT,
        "elf_embeds_lua": "Lua 5.4 version string observed",
        "lua_version_string_file_offset": f"0x{marker:X}",
        "source_method_anchors": SOURCE_LINES,
        "lua_script_entry_audit": script,
        "confirmed_native_registry": native,
        "interpretation": {
            "B_filter_arm64_rva": "NOT_ESTABLISHED",
            "C_result_add_item_arm64_rva": "NOT_ESTABLISHED",
            "Lua_BattleRewardData_layout": "Logical self[3] list of "
                                           "ItemNo/ItemNum/rareType/subjectType, "
                                           "NOT the RaidPoint 720B binary layout.",
            "bag_accessor_native_callbacks": "Verified lower-layer wrappers, "
                                              "not unique raid calls and NOT "
                                              "C-stage list pointers.",
            "live_ABC_capture": "NOT_PERFORMED",
            "safe_to_install_hook": False,
        },
    }

def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("elf", type=Path)
    args = parser.parse_args()
    print(json.dumps(inspect(args.elf.read_bytes()), indent=2, ensure_ascii=False))

if __name__ == "__main__":
    main()
