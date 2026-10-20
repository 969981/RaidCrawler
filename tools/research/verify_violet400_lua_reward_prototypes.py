#!/usr/bin/env python3
"""Exact-sha, read-only audit of user-owned Violet 4.0.0 main.blua.

Returns Lua Prototype bytecode FILE offsets, not Switch addresses or ARM64 RVAs.
No runtime VM, Hook, memory writes, Bag deltas or online rewards are asserted.
"""
from __future__ import annotations

import argparse
import hashlib
import json
import struct
from pathlib import Path

EXPECTED_SHA256 = "c1a4f4e2625912cf0b739a642a79ba3357df0557a544b92da22a6cb139de2815"
ROOT_CODE = 429034
ROOT_CHILDREN = 30829
TARGETS = {
    "LotteryRewardItem": (2899, "C81390F82EADEF632", "FE77BCB620AB2EDCA",
                          0x377501, 477, 59986),
    "FilterSubject5": (4870, "C07C34FC3B976E5A7", "F316609DEA110601D",
                       0x410673, 137, 87463),
    "RaidResultAddItem": (3756, "C889F6553EDFCD011", "FCBBD4D63B93E4C04",
                          0x3B1E39, 69, 74347),
    "RaidRewardSerializer": (27146, "C050DB411547B4BAC", "FCE48462FE20C26A7",
                             0xAA084E, 91, 380468),
}


def opcode(word):
    return word & 127


def bx(word):
    return (word >> 15) & 0x1FFFF


def signed_bx(word):
    return bx(word) - 65535


def reg_a(word):
    return (word >> 7) & 255


def reg_b(word):
    return (word >> 16) & 255


def reg_c(word):
    return (word >> 24) & 255


class Lua54Reader:
    def __init__(self, data):
        self.data = data
        self.at = 0
        self.total = 0
        self.meta = {}

    def read(self, n):
        if n < 0 or self.at + n > len(self.data):
            raise ValueError("Truncated Lua chunk")
        v = self.data[self.at:self.at + n]
        self.at += n
        return v

    def varuint(self):
        v = 0
        for _ in range(10):
            byte = self.read(1)[0]
            v = (v << 7) | (byte & 127)
            if byte & 128:
                return v
        raise ValueError("Invalid Lua varuint")

    def lua_string(self):
        length = self.varuint()
        if length > 0x10000000:
            raise ValueError("Oversized Lua string")
        return self.read(length - 1).decode("utf-8", "backslashreplace") if length else None

    def constant(self):
        kind = self.read(1)[0]
        if kind in (0, 1, 17):
            return None if kind == 0 else kind == 17
        if kind == 3:
            return struct.unpack("<d", self.read(8))[0]
        if kind == 19:
            return struct.unpack("<q", self.read(8))[0]
        if kind in (4, 20):
            return self.lua_string()
        raise ValueError("Unsupported Lua 5.4 constant tag")

    def proto(self, path="root", depth=0):
        if depth > 512 or self.total >= 100000:
            raise ValueError("Lua prototype limit exceeded")
        self.total += 1
        self.lua_string()
        self.varuint()
        self.varuint()
        self.read(3)
        ncode = self.varuint()
        code_offset = self.at
        if ncode > (len(self.data) - self.at) // 4:
            raise ValueError("Truncated code array")
        selected = path == "root" or path in {
            "root/" + str(spec[0]) for spec in TARGETS.values()
        }
        code = struct.unpack_from("<" + str(ncode) + "I", self.data, code_offset) if selected else None
        self.read(ncode * 4)
        nconst = self.varuint()
        if nconst > 2000000:
            raise ValueError("Excessive Lua constants")
        constants = [self.constant() for _ in range(nconst)]
        upvals = self.varuint()
        self.read(upvals * 3)
        children = self.varuint()
        if selected:
            self.meta[path] = {
                "code": code, "offset": code_offset, "children": children,
                "constants": constants,
            }
        for i in range(children):
            self.proto(path + "/" + str(i), depth + 1)
        self.read(self.varuint())
        for _ in range(self.varuint()):
            self.varuint()
            self.varuint()
        for _ in range(self.varuint()):
            self.lua_string()
            self.varuint()
            self.varuint()
        for _ in range(self.varuint()):
            self.lua_string()

    def parse(self):
        if self.read(4) != b"\x1bLua" or self.read(2) != b"\x54\x00":
            raise ValueError("Expected standard Lua 5.4 chunk")
        if self.read(6) != b"\x19\x93\x0d\x0a\x1a\x0a" or self.read(3) != b"\x04\x08\x08":
            raise ValueError("Wrong Lua header layout")
        if struct.unpack("<q", self.read(8))[0] != 0x5678:
            raise ValueError("Wrong Lua integer")
        if struct.unpack("<d", self.read(8))[0] != 370.5:
            raise ValueError("Wrong Lua float")
        self.read(1)
        self.proto()
        root = self.meta["root"]
        if self.at != len(self.data) or len(root["code"]) != ROOT_CODE or root["children"] != ROOT_CHILDREN:
            raise ValueError("Unexpected root Prototype layout")
        return self


def check(ok, why):
    if not ok:
        raise ValueError("Verified-sha bytecode invariant failed: " + why)


def verify(data):
    digest = hashlib.sha256(data).hexdigest()
    check(digest == EXPECTED_SHA256, "Exact Violet 4.0.0 SHA mismatch")
    source = Lua54Reader(data).parse()
    root = source.meta["root"]
    words = root["code"]
    constants = root["constants"]
    result = {}

    for label, (index, cls, method, offset, size, pc) in TARGETS.items():
        entry = source.meta.get("root/" + str(index))
        check(entry is not None and entry["offset"] == offset
              and len(entry["code"]) == size, label + " missing or wrong code span")
        check(opcode(words[pc]) == 79 and bx(words[pc]) == index, label + " OP_CLOSURE")
        check(opcode(words[pc - 1]) == 3 and constants[bx(words[pc - 1])] == method,
              label + " LOADK method")
        check(opcode(words[pc - 3]) == 3 and constants[bx(words[pc - 3])] == "prototype",
              label + " prototype registration")
        check(cls in [constants[bx(words[j])] for j in range(pc - 15, pc + 7)
                      if opcode(words[j]) == 3], label + " class hash")
        result[label] = {
            "class_hash": cls, "method_hash": method,
            "prototype_path": "root/" + str(index),
            "code_file_offset": hex(offset), "instruction_count": size,
            "registration_pc": pc, "runtime_hook_rva": "NOT_ESTABLISHED",
        }

    gen = source.meta["root/2899"]["code"]
    check(opcode(gen[475]) == 70 and reg_a(gen[475]) == 8,
          "LotteryRewardItem final RETURN")
    check(opcode(gen[459]) == 18 and opcode(gen[462]) == 18,
          "LotteryRewardItem last reward row")
    result["LotteryRewardItem"].update({
        "candidate_final_return_pc": 475,
        "candidate_final_return_file_offset": hex(TARGETS["LotteryRewardItem"][3] + 475 * 4),
        "early_return_pcs": [i for i, word in enumerate(gen[:475])
                             if opcode(word) in (70, 71, 72)],
    })
    filt = source.meta["root/4870"]["code"]
    check(opcode(filt[8]) == 1 and signed_bx(filt[8]) == 2481,
          "Subject5 charm inventory check")
    check(opcode(filt[134]) == 17 and reg_b(filt[134]) == 3
          and opcode(filt[135]) == 72, "filter new [3] array store / return")
    result["FilterSubject5"].update({
        "charm_item_id": 2481, "new_array_store_pc": 134, "return_pc": 135,
    })
    award = source.meta["root/3756"]["code"]
    check(all(opcode(award[i]) == 13 and reg_c(award[i]) == n
              for i, n in ((1, 18), (2, 92), (3, 3))),
          "RaidResultAddItem self[18][92][3]")
    check(opcode(award[66]) == 68 and opcode(award[68]) == 71,
          "RaidResultAddItem Bag call and RETURN0")
    result["RaidResultAddItem"].update({
        "reward_deref_pcs": [1, 2, 3], "bag_add_call_pc": 66,
    })
    serialize = source.meta["root/27146"]["code"]
    check(opcode(serialize[8]) == 62 and reg_b(serialize[8]) - 127 == 45,
          "serialized RaidPoint 45-row bound")
    result["RaidRewardSerializer"]["network_row_limit"] = 45

    return {
        "result": "EXACT_VIOLET400_BYTECODE_STATIC_METHOD_BINDINGS_VERIFIED",
        "sha256": digest, "file_size": len(data),
        "prototype_count": source.total, "root_code_count": ROOT_CODE,
        "root_child_count": ROOT_CHILDREN, "methods": result,
        "runtime_lua_state": "NOT_ESTABLISHED",
        "runtime_vm_pc": "NOT_ESTABLISHED",
        "native_hook_installed": False,
        "singleplayer_reward_callback_observed": False,
        "bag_delta_observed": False, "save_reload_verified": False,
        "official_online_guest_verified": False,
    }


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("main_blua", type=Path)
    parser.add_argument("--out", type=Path)
    args = parser.parse_args()
    try:
        report = verify(args.main_blua.read_bytes())
    except (OSError, ValueError, struct.error) as exc:
        parser.exit(2, "ERROR: " + str(exc) + "\n")
    payload = json.dumps(report, ensure_ascii=False, indent=2) + "\n"
    if args.out:
        args.out.write_text(payload, encoding="utf-8")
    else:
        print(payload, end="")


if __name__ == "__main__":
    main()
