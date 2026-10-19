import unittest
from pathlib import Path
import sys

sys.path.insert(0, str(Path(__file__).resolve().parent))
import audit_violet400_elf_readonly as audit


class ElfAuditTests(unittest.TestCase):
    def test_known_boundary_shares_page_with_last_plt(self):
        self.assertEqual(audit.TEXT_END - (audit.TEXT_END & ~(audit.PAGE - 1)), 0xC90)
        self.assertEqual(audit.RODATA_START - audit.TEXT_END, 880)
        self.assertEqual(audit.TEXT_END & ~(audit.PAGE - 1), 0x343F000)

    def test_wrong_binary_rejected_before_any_elf_parsing(self):
        with self.assertRaisesRegex(ValueError, "SHA256"):
            audit.inspect(bytes(64))

    def test_post_memcpy_has_original_instruction_and_followup(self):
        self.assertEqual(audit.POST_MEMCPY_WORDS[0xE32984], 0xAA1503E8)
        self.assertEqual(audit.POST_MEMCPY_WORDS[0xE32988], 0xAA1403E0)
        self.assertEqual(audit.POST_MEMCPY_WORDS[0x343FC8C], 0xD61F0220)


if __name__ == "__main__":
    unittest.main()
