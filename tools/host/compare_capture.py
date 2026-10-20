#!/usr/bin/env python3
"""Read-only raid reward comparison for host 4.0.0 snapshots.

Legacy input: full 0x3E8 RaidPoint + decoded 0xAA0 bootstrap plaintext.
N1 native probe: only 720 reward bytes per input (privacy-preserving).
Neither mode verifies the real console, session, Build ID or receiver.
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
FIELDS = ("item_id", "quantity", "rare_flag", "subject_type")


def compare_regions(source_rows: bytes, bootstrap_rows: bytes) -> dict:
    """Only compare 720-byte reward regions; no gameplay data modification."""
    if len(source_rows) != REGION_SIZE:
        raise ValueError(f"expected 720 source reward bytes, got {len(source_rows)}")
    if len(bootstrap_rows) != REGION_SIZE:
        raise ValueError(f"expected 720 bootstrap reward bytes, got {len(bootstrap_rows)}")
    first = next((i for i, (a, b) in enumerate(zip(source_rows, bootstrap_rows))
                  if a != b), None)
    return {
        "schema": "raidcrawler.host.compare.v2",
        "evidence": "OFFLINE_TWO_SNAPSHOTS_ONLY",
        "source_size": len(source_rows),
        "bootstrap_size": len(bootstrap_rows),
        "reward_bytes": REGION_SIZE,
        "source_sha256": hashlib.sha256(source_rows).hexdigest().upper(),
        "bootstrap_sha256": hashlib.sha256(bootstrap_rows).hexdigest().upper(),
        "equal": first is None,
        "first_mismatch_byte": first,
        "first_mismatch_row": None if first is None else first // ROW_BYTES,
        "first_mismatch_field": None if first is None else FIELDS[(first % ROW_BYTES) // 4],
    }


def compare(source: bytes, plain: bytes) -> dict:
    """Backward-compatible full-decrypted-object comparison."""
    if len(source) != RAIDPOINT_SIZE:
        raise ValueError(f"expected 0x3E8-byte RaidPoint, got {len(source)}")
    if len(plain) != BOOTSTRAP_SIZE:
        raise ValueError(f"expected 0xAA0-byte decoded bootstrap, got {len(plain)}")
    result = compare_regions(
        source[SOURCE_REWARDS:SOURCE_REWARDS + REGION_SIZE],
        plain[BOOTSTRAP_REWARDS:BOOTSTRAP_REWARDS + REGION_SIZE])
    result["source_size"] = len(source)
    result["bootstrap_size"] = len(plain)
    result["input_layout"] = "FULL_DECRYPTED_OBJECTS"
    return result


def main(argv: list[str] | None = None) -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--source", type=Path, required=True)
    parser.add_argument("--bootstrap", type=Path, required=True)
    parser.add_argument("--reward-regions", action="store_true",
                        help="N1 probe files: exactly 720 bytes each, no full packets")
    parser.add_argument("--session", required=True,
                        help="manually assigned anonymous session label")
    parser.add_argument("--build-id", required=True,
                        help="actual, independently observed Build ID")
    parser.add_argument("--jsonl", type=Path,
                        help="optional append-only diagnostic output")
    args = parser.parse_args(argv)
    try:
        source = args.source.read_bytes()
        plain = args.bootstrap.read_bytes()
        if args.reward_regions:
            result = compare_regions(source, plain)
            result["input_layout"] = "REWARD_REGIONS_ONLY"
        else:
            result = compare(source, plain)
    except (ValueError, OSError) as ex:
        parser.error(str(ex))
    result["session"] = args.session
    result["build_id"] = args.build_id
    result["evidence"] = "USER_SUPPLIED_SNAPSHOTS_UNVERIFIED"
    result["note"] = "Source, Build ID, session membership and receiver not independently verified."
    line = json.dumps(result, ensure_ascii=False, separators=(",", ":"))
    print(line)
    if args.jsonl:
        with args.jsonl.open("a", encoding="utf-8") as file:
            file.write(line + "\n")
    return 0 if result["equal"] else 2


if __name__ == "__main__":
    raise SystemExit(main())
