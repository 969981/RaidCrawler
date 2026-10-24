#!/usr/bin/env python3
"""Run offline; no Nintendo ELF, Switch, keys, devkitPro, or network needed."""
import importlib.util
from pathlib import Path
import sys
import unittest

p = Path(__file__).resolve().parents[1] / "gen_manifest.py"
spec = importlib.util.spec_from_file_location("v4_manifest", p)
module = importlib.util.module_from_spec(spec)
sys.modules[spec.name] = module
spec.loader.exec_module(module)

SYMBOLS = """0000000000011000 t RaidV4ReadOnly::Callback(exl::hook::InlineCtx*)
000000000001A000 b rcv4::g_mailbox
0000000000000200 t unrelated::Foo()
"""

class ManifestTests(unittest.TestCase):
    def test_matching_pair_is_pinned(self):
        m = module.make_manifest(SYMBOLS, b"readonly-nso")
        self.assertEqual(0x9000, m["callback_to_mailbox_delta"])
        self.assertEqual("0xE32984", m["hook_rva"])
        self.assertTrue(m["read_only"])
        self.assertFalse(m["native_write_capability"])
        self.assertEqual(64, len(m["subsdk9_sha256"]))
        self.assertNotEqual(m["subsdk9_sha256"],
                            module.make_manifest(SYMBOLS, b"different")["subsdk9_sha256"])

    def test_missing_or_duplicate_symbol_fail(self):
        with self.assertRaises(ValueError):
            module.make_manifest("00000020 t unrelated", b"nso")
        with self.assertRaises(ValueError):
            module.make_manifest(SYMBOLS + "\n00000030 b rcv4::g_mailbox", b"nso")

    def test_delta_range(self):
        symbols = SYMBOLS.replace("000000000001A000", "000000000401A000")
        with self.assertRaises(ValueError):
            module.make_manifest(symbols, b"nso")

if __name__ == "__main__":
    unittest.main()
