#!/usr/bin/env python3
"""Independently verify Violet 4.0.0 serializer C++ vtable from ELF RELATIVE relocations.

Only original ELF needed. Does not redistribute game files, modify Switch RAM,
scan a live process, or claim a stable current-raid owner root.
"""
from __future__ import annotations

import argparse
import hashlib
import json
import struct
from pathlib import Path

SHA256 = "d17403eaa9d844db82f647fd2f1518806b10c0518d53602fc0ac1766298fdba0"
VTABLE_MAIN = 0x4459278
VTABLE_SECOND = 0x4459308
VTABLE_THIRD = 0x4459358
SERIALIZER_SLOT = 0x44592E0
SERIALIZER_RVA = 0xE327FC
EXPECTED_RELOCS = {
    0x4459278: 0x1E631F8,
    0x44592E0: 0xE327FC,
    0x4459308: 0xA6855C,
    0x4459358: 0xA68570,
}
# Raw little-endian ARM64 instructions, independently inspected by llvm-objdump.
EXPECTED_OPCODES = {
    0x1E63204: 0xD0012FA8, # adrp x8, 0x4459000
    0x1E63208: 0x9109E108, # add x8, x8, #0x278
    0x1E6320C: 0xF9000008, # str x8, [x0] - first vptr
    0x1E63210: 0x91024109, # add x9, x8, #0x90
    0x1E63214: 0x91038108, # add x8, x8, #0xe0
    0x1E6321C: 0xA9022009, # stp x9, x8, [x0, #0x20]
    0xE32814: 0xAA0003F4, # mov x20, x0
    0xE32964: 0xA945AA8B, # ldp x11,x10,[x20,#0x58]
    0xE32968: 0x9101E281, # add x1, x20, #0x78
    0xE32980: 0x94982624, # bl memcpy@plt
}
def read_sections(data: bytes) -> dict[str, tuple[int,int,int]]:
    if data[:6] != b"\x7fELF\x02\x01" or struct.unpack_from("<H",data,18)[0] != 183:
        raise ValueError("Expected AArch64 ELF64 little-endian")
    hdr=struct.unpack_from("<QHHH",data,0x28)
    shoff=hdr[0]
    shentsize, shnum, shstrndx=struct.unpack_from("<HHH",data,0x3A)
    if shentsize<64 or shstrndx>=shnum or shoff+shentsize*shnum>len(data):
        raise ValueError("ELF section headers invalid")
    entries=[struct.unpack_from("<IIQQQQIIQQ",data,shoff+i*shentsize) for i in range(shnum)]
    st=entries[shstrndx]
    names=data[st[4]:st[4]+st[5]]
    results={}
    for e in entries:
        if e[0]>=len(names): continue
        stop=names.find(b"\x00",e[0])
        if stop<0:continue
        name=names[e[0]:stop].decode("ascii","replace")
        if e[4]+e[5]>len(data): raise ValueError(f"Section {name} out of range")
        results[name]=(e[3],e[4],e[5])
    return results

def audit(path: Path) -> dict:
    data=path.read_bytes()
    digest=hashlib.sha256(data).hexdigest()
    if digest != SHA256: raise ValueError(f"Unknown ELF sha256={digest}: refusing version-specific conclusions")
    sections=read_sections(data)
    text_va, text_off, text_size=sections[".text"]
    if text_va != 0: raise ValueError("Unexpected .text RVA")
    opcodes={}
    for address,expected in EXPECTED_OPCODES.items():
        if address+4>text_size:raise ValueError("Opcode outside .text")
        actual=struct.unpack_from("<I",data,text_off+address)[0]
        if actual!=expected:raise ValueError(f"Opcode mismatch at main+0x{address:X}")
        opcodes[f"0x{address:X}"]=f"0x{actual:08X}"
    rv,ro,rl=sections[".rela.dyn"]
    if rl%24:raise ValueError("Bad RELA record size")
    targets=set(EXPECTED_RELOCS)
    found={}
    for i in range(ro,ro+rl,24):
        offset,info,addend=struct.unpack_from("<QQq",data,i)
        if offset in targets:
            if (info & 0xffffffff)!=1027:raise ValueError(f"Not RELATIVE at 0x{offset:X}")
            found[offset]=addend
    if found!=EXPECTED_RELOCS:
        raise ValueError(f"Relocation signature mismatch: {found!r}")
    return {"elf_sha256":digest,"game":"Violet 4.0.0",
            "reloc_count_verified":len(found),
            "vtable_main_rva":hex(VTABLE_MAIN),
            "vtable_secondary_rva":hex(VTABLE_SECOND),
            "vtable_tertiary_rva":hex(VTABLE_THIRD),
            "serializer_slot_rva":hex(SERIALIZER_SLOT),
            "serializer_slot_offset":hex(SERIALIZER_SLOT-VTABLE_MAIN),
            "serializer_target_rva":hex(SERIALIZER_RVA),
            "object_raidpoint_offset":"0x58",
            "object_reward_offset":"0x13c",
            "opcodes_verified":len(opcodes),
            "type_identity":"ELF_RELOCATION_VERIFIED",
            "active_owner_pointer":"NOT_DISCOVERED",
            "write_window":"NOT_VERIFIED",
            "switch_hook_or_write_performed":False}

def main():
    p=argparse.ArgumentParser(description=__doc__)
    p.add_argument("elf",type=Path)
    p.add_argument("--out",type=Path)
    args=p.parse_args()
    result=audit(args.elf)
    report=json.dumps(result,ensure_ascii=False,indent=2)+"\n"
    if args.out:args.out.write_text(report,encoding="utf-8")
    print(report,end="")
if __name__=="__main__":
    main()
