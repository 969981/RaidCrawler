import importlib.util
import struct
import unittest
from pathlib import Path

spec=importlib.util.spec_from_file_location("plan",Path(__file__).with_name("build_offline_plan.py"))
plan=importlib.util.module_from_spec(spec)
spec.loader.exec_module(plan)

class PatchPlanTests(unittest.TestCase):
    def test_branch_reaches_both_directions(self):
        for pc,target,link in [(plan.HOOK,plan.CAVE,False),
                               (plan.CAVE+0x48,plan.MEMCPY,True),
                               (plan.CAVE+0x54,plan.HOOK+4,False)]:
            value=plan.arm64_b(pc,target,link)
            displacement=value & 0x03FFFFFF
            if displacement & (1<<25):
                displacement-=1<<26
            self.assertEqual(pc+displacement*4,target)
        with self.assertRaises(ValueError):plan.arm64_b(0,1)
        with self.assertRaises(ValueError):plan.arm64_b(0,1<<27)

    def test_strict_slot_validation(self):
        rows=bytearray(720)
        struct.pack_into("<4I",rows,0,1,999,0,5)
        struct.pack_into("<4I",rows,44*16,796,127,0,0)
        self.assertEqual(plan.rows_valid(rows),2)
        for field,value in [(0,0),(1,1000),(2,2),(3,6)]:
            bad=bytearray(rows)
            struct.pack_into("<I",bad,field*4,value)
            with self.assertRaises(ValueError):plan.rows_valid(bad)

if __name__=="__main__":
    unittest.main()
