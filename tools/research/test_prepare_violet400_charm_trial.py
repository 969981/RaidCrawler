#!/usr/bin/env python3
"""Offline contract tests; does NOT test Switch reward delivery."""
import json
import tempfile
import unittest
from pathlib import Path

from prepare_violet400_charm_trial import (
    SCREENSHOT_ITEM_IDS, load_profile, profile, screenshot_profile, stage, trial
)


class CharmTrialTests(unittest.TestCase):
    def test_screenshot_preset_exact_twenty_requested_999(self):
        source = screenshot_profile()
        self.assertFalse(source["Enabled"])
        self.assertEqual(len(SCREENSHOT_ITEM_IDS), 20)
        self.assertEqual(len(set(SCREENSHOT_ITEM_IDS)), 20)
        self.assertEqual([x["ItemId"] for x in source["Items"]], list(SCREENSHOT_ITEM_IDS))
        self.assertTrue(all(x["Quantity"] == 999 for x in source["Items"]))

    def test_single_row_preserves_source_and_is_not_a_live_hook(self):
        source = screenshot_profile()
        previous = json.dumps(source, sort_keys=True)
        one, plan = trial(source, 492)
        self.assertEqual(previous, json.dumps(source, sort_keys=True))
        self.assertEqual(one["Items"], [{"ItemId": 492, "Quantity": 1}])
        self.assertTrue(one["Enabled"])
        self.assertEqual(plan["TrialRewardRow"], {
            "ItemNo": 492, "ItemNum": 1, "rareType": 0, "subjectType": 5
        })
        self.assertTrue(plan["PreserveOriginalFixedLotteryMealAndCharm"])
        self.assertFalse(plan["HookInstalled"])
        self.assertFalse(plan["RuntimeVerified"])
        self.assertFalse(plan["OnlineModeVerified"])

    def test_csharp_digest_round_trip_and_tamper_rejection(self):
        src = screenshot_profile()
        with tempfile.TemporaryDirectory() as directory:
            p = Path(directory) / "profile.json"
            p.write_text(json.dumps(src), encoding="utf-8")
            self.assertEqual(load_profile(p), src)
            src["Items"][0]["Quantity"] = 2
            p.write_text(json.dumps(src), encoding="utf-8")
            with self.assertRaisesRegex(ValueError, "SHA mismatch"):
                load_profile(p)

    def test_invalid_quantities_ids_and_more_than_twenty_fail(self):
        with self.assertRaises(ValueError):
            profile([{"ItemId": 10000, "Quantity": 1}], False)
        with self.assertRaises(ValueError):
            profile([{"ItemId": 492, "Quantity": 1000}], False)
        with self.assertRaises(ValueError):
            profile([{"ItemId": True, "Quantity": 1}], False)
        with self.assertRaises(ValueError):
            profile([{"ItemId": 492, "Quantity": 1}] * 21, False)
        with self.assertRaises(ValueError):
            trial(screenshot_profile(), 12345)

    def test_stage_never_overwrites_active_source_or_previous_output(self):
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            src_file = root / "active-source.json"
            src_file.write_text(json.dumps(screenshot_profile()), encoding="utf-8")
            old = src_file.read_bytes()
            trial_dir = root / "trial"
            first, second = stage(load_profile(src_file), trial_dir, 492)
            self.assertEqual(load_profile(first)["Items"], [{"ItemId": 492, "Quantity": 1}])
            self.assertEqual(json.loads(second.read_text())["Scope"],
                             "OFFLINE_DRAFT_NOT_APPLIED")
            self.assertEqual(src_file.read_bytes(), old)
            with self.assertRaises(FileExistsError):
                stage(load_profile(src_file), trial_dir, 492)
            self.assertEqual(src_file.read_bytes(), old)


if __name__ == "__main__":
    unittest.main()
