#!/usr/bin/env python3
"""Read-only equality check for REAL, same-session SV 4.0.0 plaintext captures.

Inputs MUST already be decoded 0x3E8 RaidPoint and 0xAA0 bootstrap bytes.
This tool cannot connect to a Switch or validate a network receiver.
"""
from __future__ import annotations

import argparse
import hashlib
import json
from pathlib import Path

RAIDPOINT_SIZE = 0x3E8
BOOTSTRAP_SIZE = 0xAA0
SOURCE_REWARDS = 0xE4
BOOTSTRAP_REWARDS = 0x6B8 + 0xE4
ROW_BYTES = 16
ROW_COUNT = 45
REGION_SIZE = ROW_BYTES * ROW_COUNT
FIELDS = ("item_id", "quantity", "rare_type", "subject_type")


def compare(source: bytes, plain: bytes) -> dict:
    """Structural compare only; caller must establish matching real session."""
    if len(source) != RAIDPOINT_SIZE:
        raise ValueError(f"expected 0x3E8-byte RaidPoint, got {len(source)}")
    if len(plain) != BOOTSTRAP_SIZE:
        raise ValueError(f"expected 0xAA0-byte decoded bootstrap, got {len(plain)}")
    src = source[SOURCE_REWARDS:SOURCE_REWARDS + REGION_SIZE]
    dst = plain[BOOTSTRAP_REWARDS:BOOTSTRAP_REWARDS + REGION_SIZE]
    first = next((i for i, (a, b) in enumerate(zip(src, dst)) if a != b), None)
    return {
        "schema": "raidcrawler.host.compare.v1",
        "evidence": "OFFLINE_TWO_SNAPSHOTS_ONLY",
        "source_size": len(source),
        "bootstrap_size": len(plain),
        "reward_bytes": REGION_SIZE,
        "source_sha256": hashlib.sha256(src).hexdigest().upper(),
        "bootstrap_sha256": hashlib.sha256(dst).hexdigest().upper(),
        "equal": first is None,
        "first_mismatch_byte": first,
        "first_mismatch_row": None if first is None else first // ROW_BYTES,
        "first_mismatch_field": None if first is None else FIELDS[(first % ROW_BYTES) // 4],
    }


def main(argv: list[str] | None = None) -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--source", type=Path, required=True,
                        help="0x3E8 native RaidPoint snapshot from real host")
    parser.add_argument("--bootstrap", type=Path, required=True,
                        help="0xAA0 decoded/pre-LZ4 plaintext from SAME raid")
    parser.add_argument("--session", required=True,
                        help="locally assigned anonymized same-raid session identifier")
    parser.add_argument("--build-id", required=True,
                        help="Build ID measured from actual game process")
    parser.add_argument("--jsonl", type=Path,
                        help="optional append-only JSONL report destination")
    args = parser.parse_args(argv)
    try:
        result = compare(args.source.read_bytes(), args.bootstrap.read_bytes())
    except (ValueError, OSError) as ex:
        parser.error(str(ex))
    result["session"] = args.session
    result["build_id"] = args.build_id
    result["evidence"] = "USER_SUPPLIED_SNAPSHOTS_UNVERIFIED"
    result["note"] = "Same-session provenance and authenticity require external verification"
    line = json.dumps(result, ensure_ascii=False, separators=(",", ":"))
    print(line)
    if args.jsonl:
        with args.jsonl.open("a", encoding="utf-8") as f:
            f.write(line + "\n")
    return 0 if result["equal"] else 2


if __name__ == "__main__":
    raise SystemExit(main())
