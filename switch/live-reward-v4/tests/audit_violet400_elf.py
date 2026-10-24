#!/usr/bin/env python3
"""Independently audit a locally supplied Violet 4.0.0 unstripped/decompressed ELF.

Never bundles copyrighted game binaries or uses remote connectivity.
Read-only, fail-closed fingerprint check for exact game build.
"""
from __future__ import annotations
import argparse
import hashlib
from pathlib import Path
import struct
import sys

EXPECTED_SHA256 = "d17403eaa9d844db82f647fd2f1518806b10c0518d53602fc0ac1766298fdba0"
GUARDS = {
    0xE327FC: 0xA9BB7BFD, 0xE32814: 0xAA0003F4,
    0xE32828: 0x52815416, 0xE3283C: 0xAA0003F7,
    0xE32844: 0x52802B18, 0xE32960: 0x911B62E0,
    0xE32964: 0xA945AA8B, 0xE32968: 0x9101E281,
    0xE3296C: 0x52807902, 0xE32970: 0xF9035EEB,
    0xE32980: 0x94982624, 0xE32984: 0xAA1503E8,
    0xE32988: 0xAA1403E0, 0xE32990: 0xAA1703E1,
    0xE32994: 0x2A1603E2, 0xE329A0: 0x52800024,
    0xE329B0: 0x141D8073,
}

def read_text(binary: bytes) -> tuple[int, bytes]:
    if binary[:4] != b"\x7fELF" or binary[4] != 2 or binary[5] != 1 or binary[18:20] != b"\xb7\x00":
        raise ValueError("Expected little-endian AArch64 ELF64")
    shoff = struct.unpack_from("<Q", binary, 0x28)[0]
    stride = struct.unpack_from("<H", binary, 0x3A)[0]
    nsh = struct.unpack_from("<H", binary, 0x3C)[0]
    shstrndx = struct.unpack_from("<H", binary, 0x3E)[0]
    if stride < 64 or nsh == 0 or shstrndx >= nsh:
        raise ValueError("Invalid section header table")
    if shoff + stride * nsh > len(binary):
        raise ValueError("Section headers outside input")
    headers = [binary[shoff+i*stride:shoff+i*stride+64] for i in range(nsh)]
    _, _, _, _, name_at, name_size, _, _, _, _ = struct.unpack("<IIQQQQIIQQ", headers[shstrndx])
    strings = binary[name_at:name_at+name_size]
    if len(strings) != name_size:
        raise ValueError("Truncated section name table")
    for header in headers:
        stridx, _, _, vaddr, offset, size, _, _, _, _ = struct.unpack("<IIQQQQIIQQ", header)
        if stridx >= len(strings):
            continue
        name = strings[stridx:].split(b"\x00", 1)[0]
        if name == b".text":
            if offset+size > len(binary):
                raise ValueError("Truncated .text section")
            return vaddr, binary[offset:offset+size]
    raise ValueError("Missing .text section")

def audit(binary: bytes) -> int:
    digest = hashlib.sha256(binary).hexdigest()
    if digest != EXPECTED_SHA256:
        raise ValueError(f"ELF SHA256 does not match audited Violet4.0.0: {digest}")
    base, code = read_text(binary)
    for rva, word in GUARDS.items():
        at = rva - base
        if at < 0 or at + 4 > len(code):
            raise ValueError(f"Opcode outside .text: 0x{rva:X}")
        actual = struct.unpack_from("<I", code, at)[0]
        if actual != word:
            raise ValueError(f"Opcode mismatch @ main+0x{rva:X}: got {actual:08X}; expected {word:08X}")
    return len(GUARDS)

if __name__ == "__main__":
    p = argparse.ArgumentParser()
    p.add_argument("--elf", type=Path, required=True, help="Local decompressed Violet4.0.0.elf")
    args = p.parse_args()
    try:
        n = audit(args.elf.read_bytes())
    except (OSError, ValueError, struct.error) as error:
        print("FAIL:", error, file=sys.stderr)
        raise SystemExit(2)
    print(f"PASS: original Violet4.0.0 ELF SHA256 + {n} ARM64 opcode guards")
    print("N1 remains read-only; no Switch runtime/capture claims.")
