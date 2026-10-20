"""Synthetic-only tests. They do not count as retail Switch observations."""
import struct
import unittest

from compare_capture import (
    BOOTSTRAP_REWARDS, BOOTSTRAP_SIZE, RAIDPOINT_SIZE, SOURCE_REWARDS,
    REGION_SIZE, compare, compare_regions,
)


class CompareCaptureTests(unittest.TestCase):
    def make_pair(self):
        source = bytearray(RAIDPOINT_SIZE)
        plain = bytearray(BOOTSTRAP_SIZE)
        return source, plain

    def test_equal_reward_region(self):
        source, plain = self.make_pair()
        struct.pack_into("<IIII", source, SOURCE_REWARDS, 1128, 3, 0, 5)
        plain[BOOTSTRAP_REWARDS:BOOTSTRAP_REWARDS+720] = source[SOURCE_REWARDS:SOURCE_REWARDS+720]
        plain[0] = 123
        report = compare(bytes(source), bytes(plain))
        self.assertTrue(report["equal"])
        self.assertIsNone(report["first_mismatch_row"])
        self.assertEqual(64, len(report["source_sha256"]))

    def test_report_exact_first_field_mismatch(self):
        source, plain = self.make_pair()
        struct.pack_into("<IIII", source, SOURCE_REWARDS + 7*16, 15, 1, 0, 5)
        struct.pack_into("<IIII", plain, BOOTSTRAP_REWARDS + 7*16, 15, 1, 0, 0)
        report = compare(bytes(source), bytes(plain))
        self.assertFalse(report["equal"])
        self.assertEqual(7, report["first_mismatch_row"])
        self.assertEqual("subject_type", report["first_mismatch_field"])
        self.assertEqual(7*16+12, report["first_mismatch_byte"])

    def test_native_reward_regions_equal(self):
        source = bytearray(REGION_SIZE)
        target = bytearray(REGION_SIZE)
        struct.pack_into("<IIII", source, 4 * 16, 15, 1, 0, 5)
        target[:] = source
        result = compare_regions(bytes(source), bytes(target))
        self.assertTrue(result["equal"])
        self.assertEqual(REGION_SIZE, result["reward_bytes"])

    def test_native_reward_regions_first_mismatch(self):
        source = bytearray(REGION_SIZE)
        target = bytearray(REGION_SIZE)
        struct.pack_into("<IIII", source, 44 * 16, 15, 10, 0, 5)
        struct.pack_into("<IIII", target, 44 * 16, 15, 11, 0, 5)
        result = compare_regions(bytes(source), bytes(target))
        self.assertFalse(result["equal"])
        self.assertEqual(44, result["first_mismatch_row"])
        self.assertEqual("quantity", result["first_mismatch_field"])

    def test_native_reward_regions_wrong_size(self):
        with self.assertRaises(ValueError):
            compare_regions(bytes(REGION_SIZE - 1), bytes(REGION_SIZE))
        with self.assertRaises(ValueError):
            compare_regions(bytes(REGION_SIZE), bytes(REGION_SIZE + 1))

    def test_wrong_size_rejected(self):
        source, plain = self.make_pair()
        with self.assertRaises(ValueError):
            compare(bytes(source[:-1]), bytes(plain))
        with self.assertRaises(ValueError):
            compare(bytes(source), bytes(plain[:-1]))

    def test_unmodified_copy_not_full_bootstrap_equality(self):
        source, plain = self.make_pair()
        plain[0x6B8 + 0x3B8] = 255  # Summary differs; reward area still equal
        self.assertTrue(compare(bytes(source), bytes(plain))["equal"])


if __name__ == "__main__":
    unittest.main()
