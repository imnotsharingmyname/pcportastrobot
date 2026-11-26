import importlib.util
import json
from pathlib import Path
import tempfile
import unittest
from unittest.mock import patch

spec = importlib.util.spec_from_file_location(
    "patch_game", Path(__file__).resolve().parents[1] / "Tools/linux/patch_game.py")
game = importlib.util.module_from_spec(spec)
spec.loader.exec_module(game)


class RuntimeVariantTests(unittest.TestCase):
    def test_rebuild_only_uses_variants_created_in_current_run(self):
        with tempfile.TemporaryDirectory() as tmp:
            root = Path(tmp)
            base = root / "AstroBot"
            # Artificial signatures; the test does not require a game dump.
            with patch.multiple(game, BASE_PATCHES=[], LIGHTING=[("lighting", "4141", "4242")],
                                ULTRAWIDE=("wide", "4343", "4444"),
                                SOFT_ASSERT=("assert", "4545", "4646")):
                base.write_bytes(b"AA-CC-EE")
                game.main(["--run", str(root), "--game", str(root)])
                expected = {
                    "AstroBot": b"AA-CC-EE",
                    "AstroBot-NonRT": b"BB-CC-EE",
                    "AstroBot-Ultrawide": b"AA-DD-EE",
                    "AstroBot-NonRT-Ultrawide": b"BB-DD-EE",
                    "AstroBot-NonRT-SoftAssert": b"BB-CC-FF",
                    "AstroBot-NonRT-Ultrawide-SoftAssert": b"BB-DD-FF",
                }
                for name, data in expected.items():
                    self.assertEqual((root / name).read_bytes(), data)
                self.assertEqual(set(json.loads((root / "features.json").read_text())["variants"]),
                                 set(expected))

                unrelated = root / "AstroBot-Custom"
                unrelated.write_bytes(b"custom")
                base.write_bytes(b"AA-EE")  # no ultrawide signature
                game.main(["--run", str(root), "--game", str(root)])
                self.assertFalse((root / "AstroBot-Ultrawide").exists())
                self.assertFalse((root / "AstroBot-NonRT-Ultrawide").exists())
                self.assertFalse((root / "AstroBot-NonRT-Ultrawide-SoftAssert").exists())
                self.assertEqual((root / "AstroBot-NonRT-SoftAssert").read_bytes(), b"BB-FF")

                base.write_bytes(b"new-version")
                game.main(["--run", str(root), "--game", str(root)])
                self.assertEqual(json.loads((root / "features.json").read_text())["variants"], ["AstroBot"])
                for name in game.GENERATED_VARIANTS:
                    self.assertFalse((root / name).exists())
                self.assertEqual(unrelated.read_bytes(), b"custom")


if __name__ == "__main__":
    unittest.main()
