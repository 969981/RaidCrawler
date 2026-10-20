#!/usr/bin/env python3
"""P1 offline prerequisites: identify exact Violet ELF and user-owned main.blua.

Does not modify or redistribute binary game content. No memory writes, no
live Switch connection, no discovery of Lua closure pointer or function hits.
"""
from __future__ import annotations
import argparse
import hashlib
import json
from pathlib import Path

VIOLET_ELF_SHA256 = "d17403eaa9d844db82f647fd2f1518806b10c0518d53602fc0ac1766298fdba0"
MAIN_BLUA_ROMFS_PATH = "script/lua/bin/release/main/main.blua"
# Public factual version fingerprints from KM-Editor/SvGameplayOptionsLuaTransformer.
# Matching means the referenced script bytes match; it does NOT identify raid closures.
KM_VANILLA_BLUA_SHA256 = "c1a4f4e2625912cf0b739a642a79ba3357df0557a544b92da22a6cb139de2815"
KM_PATCHED_BLUA_SHA256 = "59566a4f2e9e686d89a8655a5604cae99c28fa34bbf10852f358807a7c9006f2"
MAX_ELF_BYTES = 128 * 1024 * 1024
MAX_BLUA_BYTES = 256 * 1024 * 1024


def file_hash(path: Path, limit: int) -> str:
    if not path.is_file() or path.stat().st_size > limit or path.stat().st_size < 6:
        raise ValueError(f"Missing, empty or unreasonably large local input: {path}")
    digest = hashlib.sha256()
    with path.open("rb") as stream:
        for chunk in iter(lambda: stream.read(1024 * 1024), b""):
            digest.update(chunk)
    return digest.hexdigest()


def probe(elf: Path | None, blua: Path | None) -> dict:
    result = {
        "purpose": "P1_OFFLINE_READONLY_INPUT_AUDIT",
        "source_binary_uploaded": False,
        "user_owned_romfs_relative_path": MAIN_BLUA_ROMFS_PATH,
        "elf": "MISSING",
        "blua": "MISSING",
        "lua_prototype_identified": False,
        "verified_lua_object_pointer": False,
        "native_hook_installed": False,
        "actual_singleplayer_reward": "NOT_OBSERVED",
        "ready_to_install_runtime_patch": False,
    }
    if elf:
        elf_digest = file_hash(elf, MAX_ELF_BYTES)
        result["elf_sha256"] = elf_digest
        with elf.open("rb") as f:
            elf_head = f.read(24)
        if elf_head[:6] != b"\x7fELF\x02\x01" or elf_head[18:20] != b"\xb7\x00":
            result["elf"] = "WRONG_ELF_CLASS_OR_ARCH"
        else:
            result["elf"] = ("EXACT_VIOLET_400" if elf_digest == VIOLET_ELF_SHA256
                             else "OTHER_AARCH64_ELF")
    if blua:
        blua_digest = file_hash(blua, MAX_BLUA_BYTES)
        result["main_blua_sha256"] = blua_digest
        with blua.open("rb") as f:
            header = f.read(6)
        if header != b"\x1bLua\x54\x00":
            result["blua"] = "NOT_STANDARD_LUA_54_CHUNK"
        elif blua_digest == KM_VANILLA_BLUA_SHA256:
            result["blua"] = "KM_EDITOR_VANILLA_MATCH"
        elif blua_digest == KM_PATCHED_BLUA_SHA256:
            result["blua"] = "KM_EDITOR_PATCHED_MATCH_DO_NOT_ASSUME_VANILLA"
        else:
            result["blua"] = "LUA_54_UNKNOWN_FINGERPRINT_NO_PATCH"
    result["next_required_evidence"] = (
        "Exact game Lua prototype/callframe identity, GC-safe reward-object "
        "capture A0/A1/B/C, live item deltas and save reload. "
        "Matching file hashes alone never prove Hook feasibility."
    )
    return result


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--elf", type=Path)
    parser.add_argument("--main-blua", type=Path)
    args = parser.parse_args()
    print(json.dumps(probe(args.elf, args.main_blua), indent=2, ensure_ascii=False))


if __name__ == "__main__":
    main()
