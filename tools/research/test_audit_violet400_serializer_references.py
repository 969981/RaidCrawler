import importlib.util
import struct
import unittest
from pathlib import Path

module_file = Path(__file__).with_name("audit_violet400_serializer_references.py")
spec = importlib.util.spec_from_file_location("serializer_refs", module_file)
audit = importlib.util.module_from_spec(spec)
spec.loader.exec_module(audit)


class SerializerReferenceAuditTests(unittest.TestCase):
    def test_direct_bl_to_target_is_detected(self):
        pc, dest = 0x1000, 0x1100
        word = 0x94000000 | ((dest - pc) // 4)
        code = struct.pack("<I", word) + bytes(8)
        self.assertEqual(audit.direct_references(code, pc, dest), [
            {"source_rva": "0x1000", "kind": "BL"}
        ])

    def test_indirect_function_pointer_is_not_code_branch(self):
        self.assertEqual(audit.direct_references(bytes(32), 0, audit.TARGET), [])

    def test_negative_branch_displacement(self):
        pc, dest = 0x2000, 0x1000
        word = 0x14000000 | (((dest - pc) // 4) & 0x03ffffff)
        code = struct.pack("<I", word)
        self.assertEqual(audit.direct_references(code, pc, dest), [
            {"source_rva": "0x2000", "kind": "B"}
        ])


if __name__ == "__main__":
    unittest.main()
