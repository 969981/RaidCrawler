#!/usr/bin/env python3
"""Generate one-binary read-only locator, never use a guessed absolute RAM address."""
import argparse
import hashlib
import json
import re
import subprocess
from pathlib import Path

EXPECTED_BUILD_ID = "709bfd66115298640155fcc4979dba151c7cc79a"
CALLBACK = r"^RaidV4ReadOnly::Callback\("
MAILBOX = r"^rcv4::g_mailbox$"


def one_symbol(symbol_dump: str, pattern: str) -> int:
    matches = []
    for line in symbol_dump.splitlines():
        m = re.match(r"^\s*([0-9A-Fa-f]+)\s+[A-Za-z]\s+(.+?)\s*$", line)
        if m and re.search(pattern, m.group(2)):
            matches.append((int(m.group(1), 16), m.group(2)))
    if len(matches) != 1:
        raise ValueError(f"Expected one symbol {pattern!r}; found {matches!r}")
    return matches[0][0]


def make_manifest(nm_text: str, nso: bytes) -> dict:
    callback = one_symbol(nm_text, CALLBACK)
    mailbox = one_symbol(nm_text, MAILBOX)
    delta = mailbox - callback
    if not -16_777_216 <= delta <= 16_777_216:
        raise ValueError("Unexpected callback/mailbox delta")
    return {
        "schema": "raidcrawler.live-reward.mailbox-locator.v4",
        "title_id": "01008F6008C5E000",
        "version_expected": "4.0.0",
        "build_id_expected": EXPECTED_BUILD_ID,
        "hook_rva": "0xE32984",
        "inline_entry_callback_pointer_offset": 16,
        "callback_to_mailbox_delta": delta,
        "subsdk9_sha256": hashlib.sha256(nso).hexdigest(),
        "read_only": True,
        "native_write_capability": False,
        "note": "Build ID is expectation, not runtime proof. Pair with exact subsdk9.",
    }


def main():
    p = argparse.ArgumentParser()
    p.add_argument("--elf", type=Path, required=True)
    p.add_argument("--nso", type=Path, required=True)
    p.add_argument("--out", type=Path, required=True)
    p.add_argument("--nm", default="aarch64-none-elf-nm")
    a = p.parse_args()
    symbols = subprocess.check_output([a.nm, "-n", "-C", str(a.elf)], text=True)
    manifest = make_manifest(symbols, a.nso.read_bytes())
    a.out.write_text(json.dumps(manifest, indent=2, ensure_ascii=False) + "\n")
    print("v4 locator generated:", a.out)


if __name__ == "__main__":
    main()
