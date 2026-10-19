import importlib.util
import tempfile
import unittest
from pathlib import Path

module_path = Path(__file__).with_name("verify_sv_reward_three_nodes.py")
spec = importlib.util.spec_from_file_location("verify_sv_reward_three_nodes", module_path)
verify = importlib.util.module_from_spec(spec)
spec.loader.exec_module(verify)


def sample_source(patterns):
    # Synthetic statement-only fixture, not redistributed game Lua.
    literals = {
        "GENERATION": [
            "self.FE77BCB620AB2EDCA",
            "L16_2 = L20_2",
            "SE9713F112CDA989C = L16_2",
            "L38_2 = C849FF496D0E93636",
            "L38_2 = L38_2.SFCE091807173F6E9",
        ],
        "TRANSPORT_AND_FILTER": [
            "L15_2.raidBattleRewardData = A8_2",
            "L31_2 = L31_2.raidBattleRewardData",
            "L29_2[92] = L30_2",
            "L31_2 = L31_2.F316609DEA110601D",
        ],
        "AWARD_AND_BAG": [
            "L2_2 = self[17].setupData",
            "self[18] = L1_2",
            "L1_2 = self.FCBBD4D63B93E4C04",
            "L2_2 = L2_2[92]",
            "L2_2 = L2_2[3]",
            "L4_2 = L3_2.ItemNum",
            "L5_2 = L5_2.F2001F41E6B757BB1",
            "L5_2 = L5_2.F6C18EEC27ED4BA1E",
            "L5_2 = L5_2.FB01A9B29CD4CC6CB",
        ],
    }
    return "\n".join(literals[patterns])


class ThreeNodeVerifierTests(unittest.TestCase):
    def test_chains_are_order_sensitive(self):
        for name, rules in verify.CHECKS.items():
            text = sample_source(name)
            found = verify.matches_in_order(text, rules)
            self.assertEqual(len(found), len(rules))
            self.assertEqual([p["line"] for p in found],
                             list(range(1, len(rules) + 1)))

    def test_broken_critical_bridge_fails_closed(self):
        sample = sample_source("TRANSPORT_AND_FILTER")
        broken = sample.replace("L29_2[92]", "L29_2[97]")
        with self.assertRaisesRegex(ValueError, "Missing or out-of-order"):
            verify.matches_in_order(broken, verify.CHECKS["TRANSPORT_AND_FILTER"])

    def test_no_consumer_bag_add_is_not_reported_as_proven(self):
        sample = sample_source("AWARD_AND_BAG")
        broken = sample.replace("FB01A9B29CD4CC6CB", "FAKE000000000000")
        with self.assertRaisesRegex(ValueError, "Missing or out-of-order"):
            verify.matches_in_order(broken, verify.CHECKS["AWARD_AND_BAG"])

    def test_all_synthetic_sources_pass_but_no_runtime_claim(self):
        with tempfile.TemporaryDirectory() as tmp:
            root = Path(tmp)
            roles = [("generator","GENERATION"),
                     ("context","TRANSPORT_AND_FILTER"),
                     ("consumer","AWARD_AND_BAG")]
            for role, check in roles:
                path = root / verify.SOURCES[role]
                path.parent.mkdir(parents=True,exist_ok=True)
                path.write_text(sample_source(check),encoding="utf-8")
            res = verify.evaluate(root, verify.PINNED_COMMIT)
            self.assertEqual(len(res["checks"]),3)
            self.assertFalse(res["provenance"]["violet_400_lua_matched"])
            self.assertTrue(all(value == "NOT_OBSERVED" for value
                                in res["runtime_observations"].values()))

    def test_other_versions_are_rejected(self):
        with self.assertRaisesRegex(ValueError, "pinned"):
            verify.evaluate(Path("."), "3.0.1")


if __name__ == "__main__":
    unittest.main()
