import argparse
import importlib.util
from pathlib import Path
import tempfile
import unittest
from unittest.mock import patch

spec = importlib.util.spec_from_file_location(
    "astrohybrid", Path(__file__).resolve().parents[1] / "Tools/linux/astrohybrid.py")
launcher = importlib.util.module_from_spec(spec)
spec.loader.exec_module(launcher)


class RebuildSourceTests(unittest.TestCase):
    def make_dump(self, path, name):
        (path / "sce_module").mkdir(parents=True)
        (path / "sce_sys").mkdir()
        (path / "sce_module" / "module.prx").write_bytes(name)
        (path / "eboot.bin").write_bytes(name)
        (path / "sce_sys" / "param.json").write_text(
            '{"titleId": "PPSA21567", "contentVersion": "01.019.000"}')
        return path

    def test_rebuild_replaces_links_and_preserves_original_dumps(self):
        with tempfile.TemporaryDirectory() as tmp:
            root = Path(tmp)
            first = self.make_dump(root / "first", b"first")
            second = self.make_dump(root / "second", b"second")
            (first / "obsolete").write_bytes(b"old")
            run = root / "run"
            launcher.make_app0(first, run)
            launcher.make_app0(second, run)
            self.assertEqual((run / "app0" / "eboot.bin").resolve(), second / "eboot.bin")
            self.assertFalse((run / "app0" / "obsolete").is_symlink())
            self.assertEqual((run / "app0" / "sce_module" / "module.prx").read_bytes(), b"second")
            self.assertEqual((first / "obsolete").read_bytes(), b"old")
            self.assertEqual((first / "eboot.bin").read_bytes(), b"first")
            self.assertEqual((second / "eboot.bin").read_bytes(), b"second")

    def test_source_and_generated_app0_must_not_overlap(self):
        with tempfile.TemporaryDirectory() as tmp:
            run = Path(tmp)
            source = self.make_dump(run / "app0", b"original")
            with self.assertRaises(SystemExit):
                launcher.make_app0(source, run)
            self.assertEqual((source / "eboot.bin").read_bytes(), b"original")

    def test_hybrid_setup_rejects_base_from_different_dump(self):
        with tempfile.TemporaryDirectory() as tmp:
            root = Path(tmp)
            first = self.make_dump(root / "first", b"first")
            second = self.make_dump(root / "second", b"second")
            run = root / "run"
            launcher.make_app0(first, run)
            (run / "AstroBot").write_bytes(b"generated")
            args = argparse.Namespace(game=str(second), hybrid=True)
            with patch.object(launcher, "RUN", run), patch.object(launcher.subprocess, "run") as invoke:
                with self.assertRaisesRegex(SystemExit, "different or moved dump"):
                    launcher.cmd_setup(args)
                invoke.assert_not_called()


if __name__ == "__main__":
    unittest.main()
