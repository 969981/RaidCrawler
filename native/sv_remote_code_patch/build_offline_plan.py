#!/usr/bin/env python3
"""Offline-only AArch64 proof-of-layout patch generator. NO Switch connection or writes."""
from __future__ import annotations
import argparse
import hashlib
import json
import re
import struct
import subprocess
from pathlib import Path

HOOK = 0xE32984
CAVE = 0x343FC90
END = 0x3440000
MEMCPY = 0x343C210
SHA = "d17403eaa9d844db82f647fd2f1518806b10c0518d53602fc0ac1766298fdba0"
OLD = 0xAA1503E8

def arm64_b(pc: int, target: int, link: bool = False) -> int:
    delta = target - pc
    if delta % 4 or not -(1 << 27) <= delta < 1 << 27:
        raise ValueError("A64 B/BL target is outside aligned +/-128 MiB range")
    return (0x94000000 if link else 0x14000000) | ((delta // 4) & 0x03FFFFFF)

def rows_valid(data: bytes) -> int:
    if len(data) != 720:
        raise ValueError("must supply exactly 720 reward bytes")
    count = 0
    for idx in range(45):
        row = data[idx*16:idx*16+16]
        if row == bytes(16):
            continue
        item, quantity, rare, subject = struct.unpack("<4I", row)
        if not 1 <= item <= 65535 or not 1 <= quantity <= 999 or rare > 1 or subject > 5:
            raise ValueError(f"invalid reward row {idx+1}")
        count += 1
    return count

def build(elf: Path, asm: Path, out: Path, rewards: Path | None = None) -> dict:
    blob = elf.read_bytes()
    if hashlib.sha256(blob).hexdigest() != SHA:
        raise ValueError("ELF does not match exact verified Violet 4.0.0 SHA-256")
    sections = subprocess.check_output(["readelf", "-W", "-S", str(elf)], text=True)
    def section(name: str) -> tuple[int,int,int]:
        match = re.search(r"\." + name +
                          r"\s+PROGBITS\s+([0-9a-fA-F]+)\s+([0-9a-fA-F]+)\s+([0-9a-fA-F]+)",
                          sections)
        if not match:
            raise ValueError(f"missing section {name}")
        return tuple(int(x,16) for x in match.groups())
    vaddr, offset, size = section("text")
    next_vaddr, _, _ = section("rodata")
    if (vaddr, vaddr+size, next_vaddr) != (0, CAVE, END):
        raise ValueError("code/rodata boundary differs; abandon plan")
    orig = blob[offset+HOOK:offset+HOOK+4]
    if orig != struct.pack("<I", OLD):
        raise ValueError("hook instruction fingerprint mismatch")

    out.mkdir(parents=True, exist_ok=True)
    obj=out/"stub.o"
    raw=out/"stub_raw.bin"
    subprocess.run(["clang","--target=aarch64-none-elf","-c",str(asm),"-o",str(obj)],check=True)
    subprocess.run(["llvm-objcopy","-j",".text","-O","binary",str(obj),str(raw)],check=True)
    stub=bytearray(raw.read_bytes())
    if len(stub) != END-CAVE or stub[0x54:0x58] != bytes(4) or stub[0x80:0x84] != bytes(4):
        raise ValueError("unexpected stub layout or relocation slots")
    if any(stub[0x90:]):
        raise ValueError("stub data should be zero-initialized")
    stub[0x54:0x58] = struct.pack("<I", arm64_b(CAVE+0x54, MEMCPY, True))
    stub[0x80:0x84] = struct.pack("<I", arm64_b(CAVE+0x80, HOOK+4))

    count=0
    if rewards is not None:
        data=rewards.read_bytes()
        count=rows_valid(data)
        stub[0xA0:0xA0+720]=data
    assert stub[0x90:0x94] == bytes(4)  # always OBSERVE at initial installation
    (out/"cave-880.bin").write_bytes(stub)
    (out/"hook-branch-4.bin").write_bytes(struct.pack("<I",arm64_b(HOOK,CAVE)))
    (out/"restore-original-4.bin").write_bytes(orig)
    manifest={
        "build":"Violet 4.0.0", "elf_sha256":SHA,
        "status":"OFFLINE ONLY. NOT INSTALLED OR EXECUTED ON SWITCH",
        "hook_rva":hex(HOOK), "hook_original":orig.hex(),
        "code_cave_candidate_rva":hex(CAVE), "cave_end_rva":hex(END),
        "candidate_bytes":len(stub), "payload_sha256":hashlib.sha256(stub).hexdigest(),
        "mode_rva":hex(CAVE+0x90), "hit_count_rva":hex(CAVE+0x94),
        "copy_count_rva":hex(CAVE+0x98), "rejected_count_rva":hex(CAVE+0x9c),
        "reward_data_rva":hex(CAVE+0xA0), "configured_rows":count,
        "default_mode":"observe",
        "runtime_checks_required":[
            "Violet exact game version and full NSO identity",
            "8 opcode fingerprint anchors",
            "runtime 0x370 gap is mapped and zeroed",
            "code page RX and no active foreign hook",
            "thread-safe one-instruction install and read-back",
            "kernel cache coherence for running target",
            "recovery without overwriting an executing trampoline"],
        "warning":"Never write this candidate to a Switch without runtime mapping validation and recovery.",
    }
    (out/"manifest.json").write_text(json.dumps(manifest,indent=2),encoding="utf-8")
    return manifest

if __name__ == "__main__":
    p=argparse.ArgumentParser(description=__doc__)
    p.add_argument("elf",type=Path)
    p.add_argument("--asm",type=Path,default=Path(__file__).with_name("stub.S"))
    p.add_argument("--out",type=Path,default=Path("offline-violet400-code-hook"))
    p.add_argument("--rewards",type=Path)
    args=p.parse_args()
    manifest=build(args.elf,args.asm,args.out,args.rewards)
    print(json.dumps({k:manifest[k] for k in ("build","status","hook_rva","code_cave_candidate_rva","candidate_bytes","default_mode")},indent=2))
