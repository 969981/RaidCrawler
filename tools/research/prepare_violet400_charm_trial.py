#!/usr/bin/env python3
"""Stage an OFFLINE, one-row Subject-5 trial without touching the Switch.

The generated file uses the exact C# CharmBonusProfile hash format. This is a
trial plan, not a Lua Hook, a save patch, or evidence of reward delivery.
"""
from __future__ import annotations

import argparse
import hashlib
import json
from pathlib import Path

VIOLET_400_BUILD_ID = "709bfd66115298640155fcc4979dba151c7cc79a"
VIOLET_400_ELF_SHA256 = "d17403eaa9d844db82f647fd2f1518806b10c0518d53602fc0ac1766298fdba0"
# Same order as RaidCrawler.Core/Structures/RaidRewardPresets.cs.
SCREENSHOT_ITEM_IDS = (492, 493, 494, 495, 496, 497, 498, 499, 795, 796, 1,
                       50, 1128, 581, 1907, 1908, 1906, 1904, 1905, 1606)


def bounded(value: object, lo: int, hi: int, name: str) -> int:
    if type(value) is not int or not lo <= value <= hi:
        raise ValueError(f"{name} must be integer from {lo} to {hi}")
    return value


def profile(items: list[dict], enabled: bool, revision: int = 0) -> dict:
    if type(enabled) is not bool:
        raise ValueError("Enabled must be boolean")
    bounded(revision, 0, (1 << 63) - 1, "Revision")
    if len(items) > 20:
        raise ValueError("Cannot configure more than 20 bonus rows")
    normalized = []
    for row in items:
        if type(row) is not dict or set(row) != {"ItemId", "Quantity"}:
            raise ValueError("Each row requires only ItemId and Quantity")
        item = bounded(row["ItemId"], 1, 65535, "ItemId")
        count = bounded(row["Quantity"], 1, 999, "Quantity")
        if item in (10000, 20000):
            raise ValueError("Pseudo item ID is not a bag item")
        normalized.append({"ItemId": item, "Quantity": count})
    raw = f"charm-bonus-v1\n1|{revision}|{int(enabled)}|{len(normalized)}\n"
    raw += "".join(f"{row['ItemId']}:{row['Quantity']}\n" for row in normalized)
    return {"SchemaVersion": 1, "Revision": revision, "Enabled": enabled,
            "Items": normalized, "Sha256": hashlib.sha256(raw.encode()).hexdigest().upper()}


def load_profile(path: Path) -> dict:
    obj = json.loads(path.read_text(encoding="utf-8-sig"))
    if type(obj) is not dict or set(obj) != {"SchemaVersion", "Revision",
                                           "Enabled", "Items", "Sha256"}:
        raise ValueError("Malformed CharmBonusProfile")
    if type(obj["SchemaVersion"]) is not int or obj["SchemaVersion"] != 1:
        raise ValueError("Unknown profile version")
    if type(obj["Items"]) is not list:
        raise ValueError("Items must be a list")
    result = profile(obj["Items"], obj["Enabled"], obj["Revision"])
    if type(obj["Sha256"]) is not str or obj["Sha256"].upper() != result["Sha256"]:
        raise ValueError("CharmBonusProfile SHA mismatch")
    return result


def screenshot_profile() -> dict:
    return profile([{"ItemId": item, "Quantity": 999} for item in SCREENSHOT_ITEM_IDS], False)


def trial(source: dict, item_id: int) -> tuple[dict, dict]:
    bounded(item_id, 1, 65535, "Trial ItemId")
    if not any(row["ItemId"] == item_id for row in source["Items"]):
        raise ValueError("Trial ID must be in the selected source profile")
    one = profile([{"ItemId": item_id, "Quantity": 1}], True)
    plan = {
        "Format": "raidcrawler.violet400.charm-p1-trial.v1",
        "Scope": "OFFLINE_DRAFT_NOT_APPLIED",
        "RequiredBuildId": VIOLET_400_BUILD_ID,
        "RequiredElfSha256": VIOLET_400_ELF_SHA256,
        "SourceProfileSha256": source["Sha256"],
        "TrialProfileSha256": one["Sha256"],
        "TrialRewardRow": {"ItemNo": item_id, "ItemNum": 1, "rareType": 0, "subjectType": 5},
        "Eligibility": "Bag.GetNumItem(2481) > 0",
        "PreserveOriginalFixedLotteryMealAndCharm": True,
        "RequiredRuntimeStages": ["A0_ORIGINAL", "A1_APPEND", "B_FILTERED",
                                  "C_SETTLEMENT", "BAG_BEFORE_AFTER", "SAVE_RELOAD"],
        "HookInstalled": False,
        "RuntimeVerified": False,
        "OnlineModeVerified": False,
        "Warning": "No Lua runtime entry or live reward delivery verified; the "
                   "existing E32984 hook changes only a network outgoing copy."
    }
    return one, plan


def stage(source: dict, out_dir: Path, item_id: int) -> tuple[Path, Path]:
    one, plan = trial(source, item_id)
    out_dir.mkdir(parents=True, exist_ok=True)
    a, b = out_dir / "p1-single-charm-bonus.json", out_dir / "p1-trial-manifest.json"
    if a.exists() or b.exists():
        raise FileExistsError("Refusing to overwrite previously staged trial files")
    try:
        with a.open("x", encoding="utf-8") as f:
            json.dump(one, f, indent=2, ensure_ascii=False)
            f.write("\n")
        with b.open("x", encoding="utf-8") as f:
            json.dump(plan, f, indent=2, ensure_ascii=False)
            f.write("\n")
    except BaseException:
        a.unlink(missing_ok=True)
        b.unlink(missing_ok=True)
        raise
    return a, b


def main() -> None:
    p = argparse.ArgumentParser(description=__doc__)
    p.add_argument("--source", type=Path, help="read existing charm-bonus-append.json, do not modify")
    p.add_argument("--item-id", type=int, default=492, help="selected preset item; defaults to 速度球")
    p.add_argument("--out-dir", type=Path, required=True, help="new private trial folder")
    args = p.parse_args()
    source = load_profile(args.source) if args.source else screenshot_profile()
    files = stage(source, args.out_dir, args.item_id)
    print(json.dumps({"result": "OFFLINE_TRIAL_ONLY", "files": [str(x) for x in files],
                      "nativeHook": "NOT_INSTALLED", "gameRewards": "NOT_VERIFIED"},
                     ensure_ascii=False, indent=2))


if __name__ == "__main__":
    main()
