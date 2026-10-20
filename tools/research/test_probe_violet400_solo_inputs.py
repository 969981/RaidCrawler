import hashlib
import importlib.util
import tempfile
import unittest
from pathlib import Path

path = Path(__file__).with_name("probe_violet400_solo_inputs.py")
spec = importlib.util.spec_from_file_location("violet400_solo_probe", path)
audit = importlib.util.module_from_spec(spec)
spec.loader.exec_module(audit)

class SoloInputProbeTests(unittest.TestCase):
    def test_missing_inputs_never_claim_runtime(self):
        r = audit.probe(None, None)
        self.assertEqual(r["elf"], "MISSING")
        self.assertEqual(r["blua"], "MISSING")
        self.assertFalse(r["ready_to_install_runtime_patch"])
        self.assertEqual(r["actual_singleplayer_reward"], "NOT_OBSERVED")

    def test_elf_checksum_and_blua_signature_are_separate(self):
        with tempfile.TemporaryDirectory() as d:
            elf = Path(d) / "game.elf"
            data = bytearray(32)
            data[:6] = b"\x7fELF\x02\x01"
            data[18:20] = b"\xb7\x00"
            elf.write_bytes(data)
            blua = Path(d) / "main.blua"
            b = b"\x1bLua\x54\x00dummy-code-and-data"
            blua.write_bytes(b)
            original_elf = audit.VIOLET_ELF_SHA256
            original_blua = audit.KM_VANILLA_BLUA_SHA256
            try:
                audit.VIOLET_ELF_SHA256 = hashlib.sha256(data).hexdigest()
                audit.KM_VANILLA_BLUA_SHA256 = hashlib.sha256(b).hexdigest()
                r = audit.probe(elf, blua)
                self.assertEqual(r["elf"], "EXACT_VIOLET_400")
                self.assertEqual(r["blua"], "KM_EDITOR_VANILLA_MATCH")
                self.assertFalse(r["lua_prototype_identified"])
                self.assertFalse(r["verified_lua_object_pointer"])
                self.assertFalse(r["ready_to_install_runtime_patch"])
            finally:
                audit.VIOLET_ELF_SHA256 = original_elf
                audit.KM_VANILLA_BLUA_SHA256 = original_blua

    def test_unknown_blua_and_wrong_arch_fail_closed(self):
        with tempfile.TemporaryDirectory() as d:
            elf = Path(d) / "game.elf"
            elf.write_bytes(b"not-an-ELF-file"*2)
            blua = Path(d) / "main.blua"
            blua.write_bytes(b"\x1bLua\x54\x00not-verified-file")
            r = audit.probe(elf, blua)
            self.assertEqual(r["elf"], "WRONG_ELF_CLASS_OR_ARCH")
            self.assertEqual(r["blua"], "LUA_54_UNKNOWN_FINGERPRINT_NO_PATCH")
            self.assertFalse(r["native_hook_installed"])
            blua.write_bytes(b"not-Lua-anymore")
            self.assertEqual(audit.probe(None, blua)["blua"],
                             "NOT_STANDARD_LUA_54_CHUNK")

if __name__ == "__main__":
    unittest.main()
