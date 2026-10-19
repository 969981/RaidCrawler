import importlib.util
import struct
import unittest
from pathlib import Path

target = Path(__file__).with_name("audit_violet400_reward_source_bridges.py")
spec = importlib.util.spec_from_file_location("reward_source_bridges", target)
audit = importlib.util.module_from_spec(spec)
spec.loader.exec_module(audit)


class RewardSourceBridgeAuditTests(unittest.TestCase):
    def test_rela_fields_are_three_eight_byte_words(self):
        blob = struct.pack("<QQq", 0x4429760, 0x403, 0x3B5CA56)
        self.assertEqual(audit.parse_rela(blob, 0),
                         (0x4429760, 0x403, 0x3B5CA56))

    def test_rela_cannot_read_outside_file(self):
        for offset in (-1, 1, 24):
            with self.subTest(offset=offset):
                with self.assertRaises(ValueError):
                    audit.parse_rela(bytes(24), offset)

    def test_exact_build_is_required(self):
        with self.assertRaisesRegex(ValueError, "SHA256"):
            audit.inspect(bytes(1024))

    def test_only_expected_native_reward_bridges_are_listed(self):
        self.assertEqual({x[0] for x in audit.BRIDGES},
                         {"f21AF1B35", "f66894ABE", "f859EFF3C"})
        for (_, _, name_file, callback_file,
             name_slot, callback_slot, _) in audit.BRIDGES:
            self.assertEqual(callback_file - name_file, 24)
            self.assertEqual(callback_slot - name_slot, 8)

    def test_script_never_rewrites_elf(self):
        self.assertFalse(hasattr(audit, "patch"))
        self.assertFalse(hasattr(audit, "install"))


if __name__ == "__main__":
    unittest.main()
