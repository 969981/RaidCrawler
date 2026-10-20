#!/usr/bin/env python3
"""Extract one-build RC mailbox locator delta from exlaunch's unstripped ELF.

For the pinned exlaunch InlineHook Entry layout:
  main hook => Entry.m_CbEntry => Entry.m_Callback at +0x10
  mailbox_va = Entry.m_Callback + (g_mailbox - Callback), ASLR independent.
Must be packaged together with that SAME compiled subsdk9.
"""
import argparse
import json
import re
import subprocess
from pathlib import Path

def get_one(symbols: str, pattern: str) -> int:
    hits = []
    for line in symbols.splitlines():
        m = re.match(r"^([0-9a-fA-F]+)\s+[a-zA-Z]\s+(.+)$", line.strip())
        if m and re.search(pattern, m.group(2)):
            hits.append((int(m.group(1), 16), m.group(2)))
    if len(hits) != 1:
        raise ValueError(f"Expected exactly one symbol for {pattern}, got {hits!r}")
    return hits[0][0]

def build_manifest(nm_text: str) -> dict:
    callback = get_one(nm_text, r"^RaidBootstrapReadOnly::Callback\(")
    mailbox = get_one(nm_text, r"^rcprobe::g_mailbox$")
    return {
        "schema": "raidcrawler.host.mailbox-locator.v1",
        "probe": "Violet400",
        "title_id": "01008F6008C5E000",
        "build_id_expected": "709bfd66115298640155fcc4979dba151c7cc79a",
        "main_hook_rva": "0xE32984",
        "jit_callback_pointer_offset": 16,
        "callback_rva": f"0x{callback:X}",
        "mailbox_rva": f"0x{mailbox:X}",
        "callback_to_mailbox_delta": mailbox - callback,
        "unsafe_if_not_matching_binary": True
    }

def main():
    p = argparse.ArgumentParser()
    p.add_argument("--elf", type=Path, required=True)
    p.add_argument("--out", type=Path, required=True)
    p.add_argument("--nm", default="aarch64-none-elf-nm")
    args = p.parse_args()
    symbols = subprocess.check_output([args.nm, "-n", "-C", str(args.elf)], text=True)
    manifest = build_manifest(symbols)
    args.out.write_text(json.dumps(manifest, indent=2) + "\n")
    print(f"Callback-to-mailbox delta: {manifest['callback_to_mailbox_delta']}; manifest {args.out}")

if __name__ == "__main__":
    main()
