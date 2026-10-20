"""CI assertions for audited Violet 4.0.0 signature, no copyrighted ELF needed."""
import re
import unittest
from pathlib import Path
from audit_violet_elf import (
    FINGERPRINT, EXPECTED_SHA256, EXPECTED_BUILD_ID,
    HOOK_RVA, sections, audit
)

NATIVE = Path(__file__).resolve().parents[2] / "switch/host-probe/source/program/main.cpp"

class AuditScriptTests(unittest.TestCase):
    def test_signature_matches_native_guard(self):
        source = NATIVE.read_text(encoding="utf-8")
        block = source.split("Fingerprint[] = {", 1)[1].split("};", 1)[0]
        actual = {int(a, 16): int(b, 16) for a, b in
                  re.findall(r"\{(0x[0-9A-Fa-f]+),(0x[0-9A-Fa-f]+)\}", block)}
        self.assertEqual(FINGERPRINT, actual)
        self.assertEqual(17, len(actual))
        self.assertEqual(HOOK_RVA, 0xE32984)
        self.assertEqual(0xAA1503E8, actual[HOOK_RVA],
                         "The original MOV at the exact hook site must be fingerprinted")
        self.assertEqual(0xAA1703E1, actual[0xE32990],
                         "Outgoing Bootstrap x1 must come from x23")
        self.assertEqual(0x2A1603E2, actual[0xE32994],
                         "Outgoing Bootstrap length w2 must come from w22")

    def test_exact_version_identity(self):
        self.assertEqual(40, len(EXPECTED_BUILD_ID))
        self.assertEqual(64, len(EXPECTED_SHA256))

    def test_rejects_non_elf_and_wrong_arch(self):
        with self.assertRaises(ValueError):
            sections(b"not an ELF")
        wrong_arch = bytearray(64)
        wrong_arch[:6] = b"\x7fELF\x02\x01"
        wrong_arch[18:20] = (62).to_bytes(2, "little")
        with self.assertRaisesRegex(ValueError, "AArch64"):
            sections(bytes(wrong_arch))

if __name__ == "__main__":
    unittest.main()
