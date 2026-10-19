#!/usr/bin/env python3
"""Read-only, source-aware SV raid reward lifecycle assertions.

Usage:
    python3 tools/research/verify_sv_reward_three_nodes.py /path/to/SV-Script-RE

Expected upstream checkout: Martmists-GH/SV-Script-RE at
1f843bd2517c707fbb47491dad58856eaa2c2cc7 (Lua circa SV 3.0.1).
This proves only source-level logical paths. It NEVER claims Violet 4.0.0
Lua equivalence, runtime address identity, or Hook hits.

No game binaries or savedata are read or modified.
"""
from __future__ import annotations

import argparse
import hashlib
import json
import re
import subprocess
from pathlib import Path

PINNED_COMMIT = "1f843bd2517c707fbb47491dad58856eaa2c2cc7"
SOURCES = {
    "generator": "decompiled/C81390F82EADEF632.lua",
    "context": "decompiled/C849FF496D0E93636.lua",
    "consumer": "decompiled/C889F6553EDFCD011.lua",
}

# Assert the order of the crucial *source-level* operations. These names
# are decompiled local slots, NOT stable pointers into 4.0.0 runtime memory.
CHECKS = {
    "GENERATION": [
        r"self\.FE77BCB620AB2EDCA",
        r"L16_2\s*=\s*L20_2",
        r"SE9713F112CDA989C\s*=\s*L16_2",
        r"L38_2\s*=\s*C849FF496D0E93636",
        r"L38_2\s*=\s*L38_2\.SFCE091807173F6E9",
    ],
    "TRANSPORT_AND_FILTER": [
        r"L15_2\.raidBattleRewardData\s*=\s*A8_2",
        r"L31_2\s*=\s*L31_2\.raidBattleRewardData",
        r"L29_2\[92\]\s*=\s*L30_2",
        r"L31_2\s*=\s*L31_2\.F316609DEA110601D",
    ],
    "AWARD_AND_BAG": [
        r"L2_2\s*=\s*self\[17\]\.setupData",
        r"self\[18\]\s*=\s*L1_2",
        r"L1_2\s*=\s*self\.FCBBD4D63B93E4C04",
        r"L2_2\s*=\s*L2_2\[92\]",
        r"L2_2\s*=\s*L2_2\[3\]",
        r"L4_2\s*=\s*L3_2\.ItemNum",
        r"L5_2\s*=\s*L5_2\.F2001F41E6B757BB1",
        r"L5_2\s*=\s*L5_2\.F6C18EEC27ED4BA1E",
        r"L5_2\s*=\s*L5_2\.FB01A9B29CD4CC6CB",
    ],
}


def matches_in_order(content: str, rules: list[str]) -> list[dict]:
    cursor = 0
    results = []
    for rule in rules:
        hit = re.search(rule, content[cursor:], re.M)
        if hit is None:
            raise ValueError(f"Missing or out-of-order pattern: {rule}")
        index = cursor + hit.start()
        line = content.count("\n", 0, index) + 1
        results.append({"pattern": rule, "line": line})
        cursor += hit.end()
    return results


def evaluate(root: Path, commit: str | None) -> dict:
    if commit != PINNED_COMMIT:
        raise ValueError(
            f"Repository commit must be pinned to {PINNED_COMMIT}, got {commit!r}"
        )
    src = {}
    for role, relative in SOURCES.items():
        path = root / relative
        if not path.is_file():
            raise FileNotFoundError(relative)
        text = path.read_text(encoding="utf-8")
        src[role] = {"content": text, "sha256": hashlib.sha256(
            text.encode("utf-8")).hexdigest()}
    found = {
        "GENERATION": matches_in_order(src["generator"]["content"], CHECKS["GENERATION"]),
        "TRANSPORT_AND_FILTER": matches_in_order(src["context"]["content"],
                                                 CHECKS["TRANSPORT_AND_FILTER"]),
        "AWARD_AND_BAG": matches_in_order(src["consumer"]["content"],
                                          CHECKS["AWARD_AND_BAG"]),
    }
    return {
        "provenance": {
            "repository": "Martmists-GH/SV-Script-RE",
            "commit": commit,
            "source_sha256": {role: value["sha256"] for role, value in src.items()},
            "source_version_approx": "SV 3.0.1",
            "violet_400_lua_matched": False,
        },
        "checks": {
            key: {"source_chain": "STATIC_MATCH", "assertions": rows}
            for key, rows in found.items()
        },
        "runtime_observations": {
            "violet_400_generation_hit": "NOT_OBSERVED",
            "violet_400_actual_rewards_generated": "NOT_OBSERVED",
            "violet_400_source_object_identity": "NOT_OBSERVED",
            "violet_400_filter_output": "NOT_OBSERVED",
            "violet_400_RaidResultAddItem_hit": "NOT_OBSERVED",
            "violet_400_Bag_AddItem_delta": "NOT_OBSERVED",
            "save_persistence": "NOT_OBSERVED",
        },
        "caveats": [
            "A wrapper/copy exists between fields; static source is not proof of identical object address.",
            "Old Lua source assertions cannot prove matching 4.0.0 runtime Lua code.",
            "No executable Hook or memory modification performed."
        ],
    }


def get_commit(root: Path) -> str | None:
    try:
        proc = subprocess.run(
            ["git", "-C", str(root), "rev-parse", "HEAD"],
            capture_output=True, text=True, check=True, timeout=10
        )
        return proc.stdout.strip()
    except (OSError, subprocess.CalledProcessError, subprocess.TimeoutExpired):
        return None


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("repo", type=Path, help="Pinned SV-Script-RE checkout")
    args = parser.parse_args()
    report = evaluate(args.repo, get_commit(args.repo))
    print(json.dumps(report, ensure_ascii=False, indent=2))


if __name__ == "__main__":
    main()
