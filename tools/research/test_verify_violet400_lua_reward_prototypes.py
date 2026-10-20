#!/usr/bin/env python3
"""Standard-library tests; original game binaries are never checked into Git."""
import os
import unittest
from pathlib import Path

from verify_violet400_lua_reward_prototypes import (
    TARGETS, bx, opcode, reg_a, reg_b, reg_c, signed_bx, verify,
)


class Violet400BytecodeContractTests(unittest.TestCase):
    def test_bindings_are_distinct_and_build_scoped(self):
        self.assertEqual(len(TARGETS), 4)
        self.assertEqual(len({r[0] for r in TARGETS.values()}), 4)
        self.assertEqual(len({r[2] for r in TARGETS.values()}), 4)
        self.assertEqual(TARGETS["LotteryRewardItem"][3], 0x377501)
        self.assertEqual(TARGETS["FilterSubject5"][3], 0x410673)
        self.assertEqual(TARGETS["RaidResultAddItem"][3], 0x3B1E39)

    def test_lua54_opcode_fields_from_verified_chunk(self):
        self.assertEqual(opcode(0x84D80481), 1)
        self.assertEqual(signed_bx(0x84D80481), 2481)
        self.assertEqual(opcode(0x0983234F), 79)
        self.assertEqual(bx(0x0983234F), 4870)
        self.assertEqual(opcode(0x0E030011), 17)
        self.assertEqual(reg_b(0x0E030011), 3)
        self.assertEqual(opcode(0x0302010D), 13)
        self.assertEqual(reg_c(0x0302010D), 3)
        self.assertEqual(reg_a(0x00028446), 8)
        self.assertEqual(opcode(0x00AC023E), 62)
        self.assertEqual(reg_b(0x00AC023E) - 127, 45)

    def test_unknown_binary_fails_closed(self):
        with self.assertRaisesRegex(ValueError, "SHA mismatch"):
            verify(b"not a Violet 4.0.0 Lua chunk")
        with self.assertRaisesRegex(ValueError, "SHA mismatch"):
            verify(bytes(12912264))

    @unittest.skipUnless(os.environ.get("RAIDCRAWLER_LOCAL_BLUA"),
                         "Optional user-owned main.blua, not present on GitHub CI")
    def test_full_exact_4_0_0_bytecode(self):
        data = Path(os.environ["RAIDCRAWLER_LOCAL_BLUA"]).read_bytes()
        result = verify(data)
        self.assertEqual(result["prototype_count"], 37743)
        self.assertEqual(result["methods"]["FilterSubject5"]["charm_item_id"], 2481)
        self.assertFalse(result["singleplayer_reward_callback_observed"])
        self.assertFalse(result["native_hook_installed"])


if __name__ == "__main__":
    unittest.main()
