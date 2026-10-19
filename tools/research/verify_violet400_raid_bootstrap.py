#!/usr/bin/env python3
"""Read-only Violet 4.0.0 ELF fingerprint + AArch64 raid bootstrap anchors.
No runtime memory access, game patches, or networking.
Usage: python tools/research/verify_violet400_raid_bootstrap.py /path/to/Violet4.0.0.elf
"""
from __future__ import annotations
import hashlib
import json
import struct
import sys
from pathlib import Path

EXPECTED_SHA256 = "d17403eaa9d844db82f647fd2f1518806b10c0518d53602fc0ac1766298fdba0"
TEXT_FILE_OFFSET = 0x8D8
OPCODES = {
    0xE32814: 0xAA0003F4,  # x20=input message object
    0xE32828: 0x52815416,  # 0xAA0 plaintext
    0xE3283C: 0xAA0003F7,  # x23=output plaintext
    0xE32844: 0x52802B18,  # party record = 0x158
    0xE3295C: 0xA946A688,
    0xE32960: 0x911B62E0,
    0xE32964: 0xA945AA8B,
    0xE32968: 0x9101E281,  # input RaidPoint remainder
    0xE3296C: 0x52807902,  # 0x3C8 bytes
    0xE32970: 0xF9035EEB,
    0xE32980: 0x94982624,  # memcpy
    0xE32984: 0xAA1503E8,  # copied output candidate check site
    0xE329B0: 0x141D8073,
    0x1E63030: 0xF9435E68, # receiver source +0x6B8
    0x1E63034: 0x9101E280,
    0x1E63038: 0x911B6261,
    0x1E63040: 0x52807902,
    0x1E63050: 0xA905A688,
    0x1E6305C: 0x1457646D, # memcpy
}
EXPECTED_RELOCS = {
    0x44592E0: 0xE327FC,
    0x44592E8: 0x1E62FE4,
}

def relocation_entries(data: bytes):
    if data[:4] != b"\x7fELF" or data[4] != 2 or data[5] != 1 or data[18:20] != b"\xb7\x00":
        raise ValueError("Expected ELF64 little-endian AArch64")
    shoff = struct.unpack_from("<Q", data, 0x28)[0]
    shentsize = struct.unpack_from("<H", data, 0x3A)[0]
    shnum = struct.unpack_from("<H", data, 0x3C)[0]
    shstrndx = struct.unpack_from("<H", data, 0x3E)[0]
    if shnum == 0 or shentsize < 64 or shstrndx >= shnum:
        raise ValueError("Unsupported ELF section header layout")
    sections = [data[shoff+i*shentsize:shoff+(i+1)*shentsize] for i in range(shnum)]
    strhdr = sections[shstrndx]
    st, ss = struct.unpack_from("<QQ", strhdr, 24)
    strings = data[st:st+ss]
    result = {}
    for hdr in sections:
        nameidx = struct.unpack_from("<I", hdr, 0)[0]
        end = strings.find(b"\0", nameidx)
        if end == -1 or strings[nameidx:end] != b".rela.dyn":
            continue
        off,size = struct.unpack_from("<QQ", hdr, 24)
        entsize = struct.unpack_from("<Q", hdr, 56)[0]
        if entsize != 24:
            raise ValueError("Unexpected relocation entry width")
        for j in range(0, size, 24):
            addr, info, addend = struct.unpack_from("<QQq", data, off+j)
            if addr in EXPECTED_RELOCS:
                result[addr] = (info & 0xffffffff, addend)
    return result

def verify(filename: Path):
    data = filename.read_bytes()
    sha = hashlib.sha256(data).hexdigest()
    issues = []
    if sha != EXPECTED_SHA256:
        issues.append(f"SHA256 mismatch: {sha}; refusing to identify unknown game build")
        return {"ok": False, "sha256": sha, "problems": issues}
    checked = {}
    for rva, expected in OPCODES.items():
        at = TEXT_FILE_OFFSET + rva
        actual = struct.unpack_from("<I", data, at)[0]
        checked[f"0x{rva:X}"] = actual == expected
        if actual != expected:
            issues.append(f"Opcode mismatch main+0x{rva:X}: 0x{actual:08X} != 0x{expected:08X}")
    reloc = relocation_entries(data)
    for addr, expected in EXPECTED_RELOCS.items():
        t, value = reloc.get(addr, (None, None))
        if t != 1027 or value != expected:  # R_AARCH64_RELATIVE
            issues.append(f"Unexpected vtable relocation at 0x{addr:X}: {reloc.get(addr)}")
    return {
        "ok": not issues,
        "sha256": sha,
        "opcodes_verified": len(OPCODES)-len([x for x in checked.values() if not x]),
        "opcodes_total": len(OPCODES),
        "vtable_relocations": len(EXPECTED_RELOCS),
        "sender_rva": "main+0xE327FC",
        "receiver_rva": "main+0x1E62FE4",
        "reward_input": "x20+0x13C+16*i",
        "reward_plaintext": "x23+0x79C+16*i",
        "scope": "OFFLINE STATIC VERIFICATION ONLY; official Nintendo online dispatch NOT established",
        "problems": issues,
    }

if __name__ == "__main__":
    if len(sys.argv) != 2:
        print(__doc__, file=sys.stderr)
        sys.exit(2)
    try:
        result = verify(Path(sys.argv[1]))
    except (OSError, ValueError, struct.error) as exc:
        print(json.dumps({"ok":False,"error":str(exc)},ensure_ascii=False))
        sys.exit(1)
    print(json.dumps(result,ensure_ascii=False,indent=2))
    sys.exit(0 if result["ok"] else 1)
