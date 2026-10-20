#!/usr/bin/env python3
import os
import unittest
from pathlib import Path
from audit_violet400_lua_vm_frame_layout import OPCODES, SHA, verify


class LuaVmFrameLayoutAuditTests(unittest.TestCase):
    def test_verified_instruction_anchor_contract(self):
        self.assertEqual(len(OPCODES), 15)
        self.assertEqual(OPCODES[0x3B478], 0xA901FE68)
        self.assertEqual(OPCODES[0x3B1E8], 0xF8060E68)
        self.assertEqual(OPCODES[0x3B21C], 0xF81C0273)

    def test_unknown_build_fails_closed(self):
        with self.assertRaisesRegex(ValueError, "SHA256 mismatch"):
            verify(b"Not a Violet 4.0.0 game binary")

    @unittest.skipUnless(os.environ.get("RAIDCRAWLER_LOCAL_ELF"),
                         "User-owned ELF not distributed through CI")
    def test_user_owned_exact_elf(self):
        report = verify(Path(os.environ["RAIDCRAWLER_LOCAL_ELF"]).read_bytes())
        self.assertEqual(report["sha256"], SHA)
        self.assertEqual(report["instructions_verified"], 15)
        self.assertEqual(report["main_state_offsets"]["global_state"], "0x18")
        self.assertFalse(report["live_closure_proto_verified"])
        self.assertFalse(report["switch_memory_modified"])


if __name__ == "__main__":
    unittest.main()
