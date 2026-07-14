import json
import unittest
from pathlib import Path


repo_root = Path(__file__).resolve().parents[2]
manifest_path = repo_root / "fixtures" / "telemetry-analysis" / "fuel-v2-strategy-stress" / "manifest.json"


class FuelV2StrategyStressManifestTests(unittest.TestCase):
    def setUp(self):
        self.manifest = load_json_without_duplicate_keys(manifest_path)
        self.cases = self.manifest["cases"]

    def test_catalogue_has_unique_provenance_and_semantic_expectations(self):
        self.assertEqual(1, self.manifest["schemaVersion"])
        self.assertIn("runtimeAuthority", self.manifest["sourcePolicy"])
        self.assertIn("core", self.manifest["consumerContract"])
        self.assertIn("workbench", self.manifest["consumerContract"])
        self.assertIn("finalOverlay", self.manifest["consumerContract"])
        self.assertIn("exportKey", self.manifest["consumerContract"])

        coverage_default = self.manifest["coverageDefault"]
        self.assertEqual(
            {"core", "workbench", "browser", "localhost", "native"},
            set(coverage_default),
        )

        case_ids = []
        for case in self.cases:
            with self.subTest(case=case.get("id")):
                case_ids.append(case["id"])
                self.assertRegex(case["id"], r"^fuel-v2-[a-z0-9-]+$")
                self.assertTrue(case["kind"].strip())
                self.assertTrue(case["stage"].strip())
                self.assertTrue(case["inputMode"].strip())
                self.assertGreater(len(case["sources"]), 0)
                self.assertGreater(len(case["expected"]), 0)
                coverage = {**coverage_default, **case.get("coverage", {})}
                self.assertEqual(set(coverage_default), set(coverage))
                self.assertTrue(set(coverage.values()).issubset({"planned", "implemented-equivalent"}))
                for source in case["sources"]:
                    self.assertTrue(source["kind"].strip())
                    if source["kind"] == "local-raw-capture":
                        self.assertRegex(source["captureId"], r"^capture-\d{8}-\d{6}-\d{3}$")
                        self.assertIn(
                            source["availability"],
                            {
                                "compact-fixture-checked-in",
                                "local-archive-available",
                                "archive-target-not-in-checkout",
                                "unavailable",
                            },
                        )

        self.assertEqual(len(case_ids), len(set(case_ids)))

    def test_constructed_cases_cannot_claim_capture_derivation(self):
        for case in self.cases:
            if not case["kind"].startswith("constructed-"):
                continue

            with self.subTest(case=case["id"]):
                self.assertFalse(
                    any(source["kind"] == "local-raw-capture" for source in case["sources"]),
                    "Constructed policy cases must not be labelled capture-derived.",
                )

    def test_current_test_references_resolve_without_requiring_local_archives(self):
        for case in self.cases:
            for reference in case["expected"].get("testRefs", []):
                with self.subTest(case=case["id"], reference=reference):
                    self.assertTrue(reference.startswith("tests/"), reference)
                    path_text, separator, token = reference.partition(":")
                    path = repo_root / path_text
                    self.assertTrue(path.exists(), reference)
                    self.assertTrue(separator, "test references must name the exact test member")
                    self.assertIn(token, path.read_text(encoding="utf-8"), reference)

                    coverage = {
                        **self.manifest["coverageDefault"],
                        **case.get("coverage", {}),
                    }
                    self.assertEqual("implemented-equivalent", coverage["core"])

            for reference in case["expected"].get("fixtureRefs", []):
                with self.subTest(case=case["id"], reference=reference):
                    path_text, separator, token = reference.partition(":")
                    path = repo_root / path_text
                    self.assertTrue(path.exists(), reference)
                    if separator:
                        self.assertIn(token, path.read_text(encoding="utf-8"), reference)

    def test_catalogue_stays_compact_and_redacted(self):
        text = manifest_path.read_text(encoding="utf-8")
        self.assertLess(manifest_path.stat().st_size, 32_000)
        lowered = text.lower()
        for forbidden in ("telemetry.bin", ".ibt", "drivername", "username", "userid", "teamname"):
            self.assertNotIn(forbidden, lowered)
        self.assertNotIn("/users/", lowered)
        self.assertNotIn("\\\\", text)

        for path in manifest_path.parent.rglob("*"):
            if not path.is_file():
                continue
            self.assertNotIn(path.suffix.lower(), {".bin", ".ibt"}, path)


def load_json_without_duplicate_keys(path: Path):
    def reject_duplicate_keys(pairs):
        result = {}
        for key, value in pairs:
            if key in result:
                raise ValueError(f"Duplicate JSON key {key!r} in {path}")
            result[key] = value
        return result

    return json.loads(path.read_text(encoding="utf-8"), object_pairs_hook=reject_duplicate_keys)


if __name__ == "__main__":
    unittest.main()
