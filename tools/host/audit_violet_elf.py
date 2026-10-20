#!/usr/bin/env python3
"""Offline validator for the exact Violet 4.0.0 ELF used by host N1/N2.

No Switch, network, decryption, game write, or ELF redistribution needed.
New titles/builds must add independently audited signatures, not reuse Violet.
"""
from __future__ import annotations

import argparse
import hashlib
import json
import struct
import sys
from pathlib import Path

EXPECTED_SHA256 = "d17403eaa9d844db82f647fd2f1518806b10c0518d53602fc0ac1766298fdba0"
EXPECTED_BUILD_ID = "709bfd66115298640155fcc4979dba151c7cc79a"
EXPECTED_SIZE = 74_362_480
HOCK_RVA = 0xE32984
FINGERPRINT = {
    0xE327FC: 0xA9BB7BFD,
    0xE32814: 0xAA0003F4,
    0xE32828: 0x52815416,
    0xE3283C: 0xAA0003F7,
    0xE32844: 0x52802B18,
    0xE32960: 0x911B62E0,
    0xE32964: 0xA945AA8B,
    0xE32968: 0x9101E281,
    0xE3296C: 0x52807902,
    0xE32970: 0xF9035EEB,
    0xE32980: 0x94982624,
    0xE329B0: 0x141D8073,
}

def sections(data: bytes) -> dict[str, dict]:
    if len(data) < 64 or data[:4] != b"\x7fELF" or data[4] != 2 or data[5] != 1:
        raise ValueError("Expected AArch64 ELF64 little-endian")
    if struct.unpack_from("<H", data, 18)[0] != 183:
        raise ValueError("Not an AArch64 ELF")
    shoff = struct.unpack_from("<Q", data, 0x28)[0]
    entsize = struct.unpack_from("<H", data, 0x3A)[0]
    count = struct.unpack_from("<H", data, 0x3C)[0]
    stridx = struct.unpack_from("<H", data, 0x3E)[0]
    if not (entsize >= 64 and count and stridx < count and shoff + entsize*count <= len(data)):
        raise ValueError("Invalid ELF section-header bounds")
    raw = [struct.unpack_from("<IIQQQQIIQQ", data, shoff + i * entsize)
           for i in range(count)]
    st = raw[stridx]
    start, end = st[4], st[4] + st[5]
    if end > len(data):
        raise ValueError("Invalid section string table")
    strings = data[start:end]
    result = {}
    for sh in raw:
        if sh[0] >= len(strings):
            continue
        z = strings.find(b"\0", sh[0])
        if z < 0:
            continue
        name = strings[sh[0]:z].decode("ascii", "replace")
        if sh[4] + sh[5] > len(data):
            raise ValueError("ELF section exceeds file")
        result[name] = {"addr": sh[3], "file_offset": sh[4],
                        "size": sh[5], "type": sh[1]}
    return result

def note_build_id(data: bytes, sh: dict) -> str:
    start, end = sh["file_offset"], sh["file_offset"] + sh["size"]
    pos = start
    while pos + 12 <= end:
        namesz, descsz, typ = struct.unpack_from("<III", data, pos)
        pos += 12
        name = data[pos:pos+namesz]
        pos += (namesz + 3) & ~3
        value = data[pos:pos+descsz]
        pos += (descsz + 3) & ~3
        if name.rstrip(b"\0") == b"GNU" and typ == 3:
            return value.hex()
    return ""

def audit(data: bytes) -> dict:
    sh = sections(data)
    if ".text" not in sh or ".note" not in sh:
        raise ValueError("Missing required ELF .text or .note")
    txt = sh[".text"]
    problems = []
    results = []
    for rva, expected in FINGERPRINT.items():
        relative = rva - txt["addr"]
        if relative < 0 or relative + 4 > txt["size"]:
            raise ValueError(f"RVA 0x{rva:X} outside .text")
        actual = struct.unpack_from("<I", data, txt["file_offset"] + relative)[0]
        matches = expected == actual
        results.append({"rva": f"0x{rva:X}", "expected": f"0x{expected:08X}",
                        "actual": f"0x{actual:08X}", "matches": matches})
        if not matches:
            problems.append(f"opcode mismatch at main+0x{rva:X}")
    digest = hashlib.sha256(data).hexdigest()
    build = note_build_id(data, sh[".note"])
    if digest != EXPECTED_SHA256:
        problems.append("SHA256 differs from audited Violet ELF")
    if build != EXPECTED_BUILD_ID:
        problems.append("Build ID differs from audited Violet ELF")
    if len(data) != EXPECTED_SIZE:
        problems.append("file size differs from audited Violet ELF")
    return {"title": "Violet", "game_version": "4.0.0",
            "size_bytes": len(data), "sha256": digest, "build_id": build,
            "hook_rva": f"0x{HOCK_RVA:X}", "reward_rows_rva": "RaidPoint+0xE4",
            "bootstrap_rows": "0x79C..0xA6C (45 x 16 bytes)",
            "fingerprint_matches": sum(v["matches"] for v in results),
            "fingerprint_total": len(results), "opcodes": results,
            "accepted": not problems, "problems": problems}

def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("elf", type=Path)
    parser.add_argument("--output", type=Path, help="Save non-sensitive JSON report")
    args = parser.parse_args()
    report = audit(args.elf.read_bytes())
    encoded = json.dumps(report, ensure_ascii=False, indent=2)
    if args.output:
        args.output.write_text(encoded + "\n", encoding="utf-8")
    print(encoded)
    return 0 if report["accepted"] else 2

if __name__ == "__main__":
    try:
        sys.exit(main())
    except (OSError, ValueError, struct.error) as exc:
        sys.exit(f"ELF audit failed: {exc}")
