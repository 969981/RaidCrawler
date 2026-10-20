import importlib.util
import struct
import unittest
from pathlib import Path

path = Path(__file__).with_name("audit_violet400_abc_native_boundaries.py")
spec = importlib.util.spec_from_file_location("abc_native_boundaries", path)
audit = importlib.util.module_from_spec(spec)
spec.loader.exec_module(audit)


class AbcNativeBoundaryAuditTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        # Synthetic ELF RELOCATION fixture, no copyrighted game data.
        # Full exact-ELF integrity test is executed locally with user's file.
        size = audit.RELA_START + audit.RELA_SIZE + 0x400
        data = bytearray(size)
        data[0:6] = b"\x7fELF\x02\x01"
        cls.native = []
        for i, (name, purpose, slot, cb, opcode) in enumerate(audit.BRIDGES):
            name_off = audit.RELA_START + audit.RELA_SIZE + 0x10 + 20 * i
            data[name_off:name_off + len(name) + 1] = name.encode() + b"\x00"
            rela_pos = audit.RELA_START + 48 * i
            struct.pack_into("<QQq", data, rela_pos, slot,
                             audit.R_AARCH64_RELATIVE,
                             name_off - audit.RODATA_FILE_DELTA)
            struct.pack_into("<QQq", data, rela_pos + 24, slot + 8,
                             audit.R_AARCH64_RELATIVE, cb)
            struct.pack_into("<I", data, audit.TEXT_FILE_OFF + cb, opcode)
            cls.native.append(name)
        cls.data = data

    def test_relocation_and_opcode_registration(self):
        found = audit.verify_native_registrations(self.data)
        self.assertEqual([v["method"] for v in found], self.native)
        self.assertEqual(found[-1]["native_callback_rva"], "0x1A3DB80")
        self.assertEqual(found[2]["runtime_hit"], "NOT_OBSERVED")

    def test_native_opcodes_are_guarded(self):
        data = bytearray(self.data)
        cb = audit.BRIDGES[-1][3]
        struct.pack_into("<I", data, audit.TEXT_FILE_OFF + cb, 0)
        with self.assertRaisesRegex(ValueError, "opcode"):
            audit.verify_native_registrations(data)

    def test_ambiguous_native_relocation_is_rejected(self):
        data = bytearray(self.data)
        name, purpose, slot, cb, op = audit.BRIDGES[0]
        # Duplicate registration with same slot+callback must fail.
        at = audit.RELA_START + 48 * len(audit.BRIDGES)
        base = audit.RELA_START + audit.RELA_SIZE + 0x10
        struct.pack_into("<QQq", data, at, slot, audit.R_AARCH64_RELATIVE,
                         base - audit.RODATA_FILE_DELTA)
        struct.pack_into("<QQq", data, at+24, slot+8, audit.R_AARCH64_RELATIVE, cb)
        with self.assertRaisesRegex(ValueError, "ambiguous"):
            audit.verify_native_registrations(data)

    def test_exact_binary_hash_gate_and_no_lua_native_claim(self):
        with self.assertRaisesRegex(ValueError, "SHA256 mismatch"):
            audit.inspect(bytes(8192))
        self.assertEqual(
            {name for name, stage, purpose in audit.LUA_METHODS},
            {"F316609DEA110601D", "FCBBD4D63B93E4C04",
             "FB01A9B29CD4CC6CB"}
        )
        self.assertEqual(audit.SOURCE_LINES["B"].split(":")[0],
                         "decompiled/C849FF496D0E93636.lua")


if __name__ == "__main__":
    unittest.main()
