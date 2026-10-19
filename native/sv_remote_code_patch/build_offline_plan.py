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
SHA = "d17403eaa9d844db82f647fd2f1518806b10c0518d53602fc0ac1766298fdba0"
OLD = 0xAA1503E8
RETURN_AT = 0x8C
CODE_SHA = "7c54a5ca250a1683f5d41ad6dd8fc928d83c7e94494fdafd1e1da18cda9158b8"

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
    if len(stub) != END-CAVE or stub[RETURN_AT:RETURN_AT+4] != bytes(4):
        raise ValueError("unexpected v2 trampoline size/return relocation slot")
    if stub[0x90:0x94] != (1).to_bytes(4,"little") or stub[0x94:0xa0] != b"RHS2RaidPoin":
        raise ValueError("unexpected v2 protocol header")
    if any(stub[0xa0:]):
        raise ValueError("reward table should be zero-initialized")
    stub[RETURN_AT:RETURN_AT+4] = struct.pack("<I",arm64_b(CAVE+RETURN_AT,HOOK+4))
    if hashlib.sha256(stub[:0x90]).hexdigest() != CODE_SHA:
        raise ValueError("unrecognized v2 ARM64 reader instruction fingerprint")

    count=0
    if rewards is not None:
        data=rewards.read_bytes()
        count=rows_valid(data)
        stub[0xA0:0xA0+720]=data
    assert stub[0x90:0x94] == (1).to_bytes(4,'little')  # odd epoch: disarmed
    (out/"cave-880.bin").write_bytes(stub)
    (out/"hook-branch-4.bin").write_bytes(struct.pack("<I",arm64_b(HOOK,CAVE)))
    (out/"restore-original-4.bin").write_bytes(orig)
    manifest={
        "build":"Violet 4.0.0", "elf_sha256":SHA,
        "status":"OFFLINE ONLY. NOT INSTALLED OR EXECUTED ON SWITCH",
        "hook_rva":hex(HOOK), "hook_original":orig.hex(),
        "code_cave_candidate_rva":hex(CAVE), "cave_end_rva":hex(END),
        "candidate_bytes":len(stub), "payload_sha256":hashlib.sha256(stub).hexdigest(),
        "epoch_rva":hex(CAVE+0x90), "reader_sha256":CODE_SHA,
        "return_branch_offset":hex(RETURN_AT),
        "game_side_telemetry":"UNAVAILABLE: code RX page may not be written by game's CPU; requires a separate verified writable buffer",
        "reward_data_rva":hex(CAVE+0xA0), "configured_rows":count,
        "default_mode":"disarmed",
        "runtime_checks_required":[
            "Violet exact game version and full NSO identity",
            "8 opcode fingerprint anchors",
            "runtime 0x370 gap is mapped and zeroed",
            "code page RX and no active foreign hook",
            "thread-safe one-instruction install and read-back",
            "kernel cache coherence for running target",
            "recovery without overwriting an executing trampoline"],
        "warning":"Offline plan only; runtime X permission, callbacks and reward persistence have not been validated.",
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
