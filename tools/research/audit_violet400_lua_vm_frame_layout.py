#!/usr/bin/env python3
"""Read-only exact ELF check: Lua 5.4 main state and CallInfo field offsets.

These static offset inferences do not certify currently executing Lua closure
or prove that an arbitrary memory region is a live lua_State.
"""
import argparse
import hashlib
import json
import struct
from pathlib import Path

SHA = "d17403eaa9d844db82f647fd2f1518806b10c0518d53602fc0ac1766298fdba0"
TEXT_FILE_DELTA = 0x8D8

# Machine words independently extracted from exact user-owned Violet4.0.0.elf.
OPCODES = {
    0x3B45C: 0xF8008E7F, # str xzr,[x19,#8]! => state=L=alloc+8
    0x3B464: 0x79001268, # strh w8,[L,#8] => thread tag+mark
    0x3B474: 0xF80C8D15, # str x21,[x8,#0xC8]! => x8=L+0xC8
    0x3B478: 0xA901FE68, # stp x8,xzr,[L,#0x18] => global_State
    0x3B484: 0xA9057E73, # stp L,xzr,[L,#0x50]
    0x3B074: 0xF9001A60, # str stackBase,[L,#0x30]
    0x3B078: 0xF9002260, # str stackBase,[L,#0x40] tbclist
    0x3B1E4: 0x910A0109, # add x9,x8,#0x280 => stack_last
    0x3B1E8: 0xF8060E68, # str stackBase,[x19,#0x60]! => x19=L+0x60
    0x3B1EC: 0xF81C8269, # stur x9,[x19,#-0x38] = L+0x28
    0x3B1F4: 0xF81B0268, # stur x8,[x19,#-0x50] = L+0x10
    0x3B214: 0xF81B0269, # stur x9,[x19,#-0x50] = L+0x10 top
    0x3B218: 0xF9000668, # str x8,[x19,#8] = base_ci.top
    0x3B21C: 0xF81C0273, # stur x19,[x19,#-0x40] = L+0x20 ci
    0x3B200: 0xB9003E69, # stur? frame-size field of Lua CI
}


def verify(blob: bytes):
    digest = hashlib.sha256(blob).hexdigest()
    if digest != SHA:
        raise ValueError("Violet 4.0.0 exact ELF SHA256 mismatch")
    if blob[:6] != b"\x7fELF\x02\x01":
        raise ValueError("Expected ELF64 little-endian")
    checks = {}
    for rva, word in OPCODES.items():
        actual = struct.unpack_from("<I", blob, TEXT_FILE_DELTA + rva)[0]
        if actual != word:
            raise ValueError(f"ARM64 opcode mismatch at main+0x{rva:X}")
        checks[f"0x{rva:X}"] = f"0x{actual:08X}"
    return {
        "sha256": digest,
        "status": "STATIC_EXACT_ELF_VERIFIED_NOT_RUNTIME",
        "instructions_verified": len(OPCODES),
        "state_pointer_is_allocator_plus_8": True,
        "main_state_offsets": {
            "top": "0x10", "global_state": "0x18",
            "current_callinfo": "0x20", "stack_last": "0x28",
            "stack_base": "0x30", "tbclist": "0x40",
            "base_callinfo": "0x60",
            "main_global_relative": "0xC8"
        },
        "callinfo_offsets": {"func": "0x0", "top": "0x8"},
        "lua_stack_initial_allocation": "0x2D0",
        "lua_vm_active_execution_verified": False,
        "live_closure_proto_verified": False,
        "abc_rewards_captured": False,
        "switch_memory_modified": False,
        "machine_code_anchors": checks
    }


def main():
    p = argparse.ArgumentParser(description=__doc__)
    p.add_argument("elf", type=Path)
    p.add_argument("--output", type=Path)
    args = p.parse_args()
    report = verify(args.elf.read_bytes())
    output = json.dumps(report, ensure_ascii=False, indent=2) + "\n"
    if args.output:
        args.output.write_text(output, encoding="utf-8")
    else:
        print(output, end="")


if __name__ == "__main__":
    main()
