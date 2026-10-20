#!/usr/bin/env python3
"""No distributed ELF: test exact native encodings and reject wrong binaries."""
import os
import struct
import unittest
from pathlib import Path

from audit_violet400_lua_vm_root import (
    ANCHORS, ELF_SHA256, GLOBAL_REFERENCE_RVA, branch_target, verify,
)


class LuaVmRootNativeAuditTests(unittest.TestCase):
    def test_exact_arm64_bls_and_pointer_chain(self):
        self.assertEqual(branch_target(0x1107AF4, ANCHORS[0x1107AF4]), 0x3B420)
        self.assertEqual(branch_target(0x3B5DC, ANCHORS[0x3B5DC]), 0x3B050)
        self.assertEqual(branch_target(0x26076B8, ANCHORS[0x26076B8]), 0x3B050)
        self.assertEqual(GLOBAL_REFERENCE_RVA, 0x4759EB0)
        self.assertEqual(0x88 - 0x70, 0x18)

    def test_encoded_alloc_and_slots(self):
        self.assertEqual(ANCHORS[0x3B064], 0x52805A01)
        self.assertEqual(ANCHORS[0x3B074], 0xF9001A60)
        self.assertEqual(ANCHORS[0x3B078], 0xF9002260)
        self.assertEqual(ANCHORS[0x1107B00], 0xF9003A60)
        self.assertEqual(ANCHORS[0x1107B38], 0xF9075916)
        self.assertEqual(ANCHORS[0x2607888], 0xF907591F)

    def test_nonmatching_elf_fails_closed(self):
        with self.assertRaisesRegex(ValueError, "SHA mismatch"):
            verify(bytes(256))
        with self.assertRaisesRegex(ValueError, "SHA mismatch"):
            verify(b"\\x7fELF" + bytes(256))

    @unittest.skipUnless(os.getenv("RAIDCRAWLER_LOCAL_ELF"),
                         "Original ELF only on user's workstation, never in GitHub CI")
    def test_full_exact_elf(self):
        blob = Path(os.environ["RAIDCRAWLER_LOCAL_ELF"]).read_bytes()
        result = verify(blob)
        self.assertEqual(result["sha256"], ELF_SHA256)
        self.assertEqual(result["anchors_verified"], len(ANCHORS))
        self.assertFalse(result["root_pointer_runtime_observed"])
        self.assertFalse(result["abc_arrays_captured"])
        self.assertFalse(result["game_memory_changed"])


if __name__ == "__main__":
    unittest.main()
