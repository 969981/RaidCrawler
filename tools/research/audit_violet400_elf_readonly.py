#!/usr/bin/env python3
"""Read-only audit of the original Violet 4.0.0 AArch64 ELF.

Verifies the segment boundary, trailing PLT instruction and serializer's
immediate post-memcpy instruction sequence. Does NOT generate/install hooks,
modify an ELF, contact a Switch, or attest runtime execute permissions.
"""
from __future__ import annotations

import argparse
import hashlib
import json
import struct
from pathlib import Path

EXPECTED_SHA = "d17403eaa9d844db82f647fd2f1518806b10c0518d53602fc0ac1766298fdba0"
PT_LOAD, PF_X, PF_R = 1, 1, 4
TEXT_END = 0x343FC90
RODATA_START = 0x3440000
PAGE = 0x1000
POST_MEMCPY_WORDS = {
    0xE32980: 0x94982624,  # BL memcpy@plt
    0xE32984: 0xAA1503E8,  # mov x8,x21
    0xE32988: 0xAA1403E0,  # mov x0,x20
    0xE3298C: 0xF9400BF9,  # ldr x25,[sp,#0x10]
    0xE32990: 0xAA1703E1,  # mov x1,x23
    0xE32994: 0x2A1603E2,  # mov w2,w22
    0xE32998: 0xAA1303E3,  # mov x3,x19
    0xE329A0: 0x52800024,  # mov w4,#1
    0xE329AC: 0xA8C57BFD,  # ldp x29,x30,[sp],#0x50
    0xE329B0: 0x141D8073,  # b next serializer stage
    0x343FC8C: 0xD61F0220, # last PLT br x17
}


def inspect(blob: bytes) -> dict:
    if hashlib.sha256(blob).hexdigest() != EXPECTED_SHA:
        raise ValueError("unexpected SHA256: this is not the audited original ELF")
    if blob[:4] != b"\x7fELF" or blob[4:6] != bytes([2, 1]):
        raise ValueError("ELF64 little-endian required")
    e_type, e_machine = struct.unpack_from("<HH", blob, 16)
    if (e_type, e_machine) != (3, 183):
        raise ValueError("expected ET_DYN AArch64")
    phoff = struct.unpack_from("<Q", blob, 32)[0]
    phentsize, phnum = struct.unpack_from("<HH", blob, 54)
    if phentsize < 56:
        raise ValueError("ELF64 program header size invalid")
    segments = []
    for i in range(phnum):
        start = phoff + i * phentsize
        if start + 56 > len(blob):
            raise ValueError("invalid program header")
        kind, flags, offset, vaddr, _, filesz, memsz, align = struct.unpack_from(
            "<IIQQQQQQ", blob, start)
        if kind == PT_LOAD:
            segments.append((flags, offset, vaddr, filesz, memsz, align))
    executable = [s for s in segments if s[0] & PF_X]
    rodata = [s for s in segments if (s[0] & PF_R) and not(s[0] & PF_X)
              and not (s[0] & 2)]
    if len(executable) != 1 or not rodata:
        raise ValueError("unknown LOAD layout")
    flags, offset, vaddr, filesz, memsz, align = executable[0]
    if (vaddr, memsz, filesz) != (0, TEXT_END, TEXT_END):
        raise ValueError("text boundary differs from verified ELF")
    if RODATA_START not in [s[2] for s in rodata]:
        raise ValueError("rodata start differs from verified ELF")
    checks = {}
    for rva, expected in POST_MEMCPY_WORDS.items():
        actual = struct.unpack_from("<I", blob, offset + rva)[0]
        if actual != expected:
            raise ValueError(f"opcode mismatch {rva:#x}: {actual:#x} != {expected:#x}")
        checks[f"0x{rva:X}"] = f"0x{actual:08X}"
    start_page = TEXT_END & ~(PAGE - 1)
    end_page = (TEXT_END + PAGE - 1) & ~(PAGE - 1)
    assert end_page == RODATA_START
    return {
        "sha256": EXPECTED_SHA,
        "result": "STATIC_ELF_ONLY",
        "text_rx": True,
        "last_plt_instruction": "0x343FC8C",
        "tail_start": f"0x{TEXT_END:X}",
        "tail_end": f"0x{end_page:X}",
        "tail_bytes": end_page - TEXT_END,
        "same_page_as_executable_plt": True,
        "runtime_execute_permission_verified": False,
        "serializer_post_memcpy_word_checks": checks,
        "session_quiescence_verified": False,
    }


if __name__ == "__main__":
    p = argparse.ArgumentParser(description=__doc__)
    p.add_argument("elf", type=Path)
    args = p.parse_args()
    print(json.dumps(inspect(args.elf.read_bytes()), ensure_ascii=False, indent=2))
