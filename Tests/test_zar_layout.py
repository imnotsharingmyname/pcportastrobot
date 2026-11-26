"""Portable tests for the shared Run/RunHybrid dump layout; no game or Windows tools needed."""

import errno
import importlib.util
import os
import pathlib
import subprocess
import sys
import tempfile
import unittest
from unittest import mock

ROOT = pathlib.Path(__file__).resolve().parents[1]
SPEC = importlib.util.spec_from_file_location("make_run_hybrid", ROOT / "Tools" / "hybrid" / "make_run_hybrid.py")
layout = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(layout)


class ZarLayoutTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory(prefix="zar-layout-")
        self.addCleanup(self.temp.cleanup)
        self.root = pathlib.Path(self.temp.name)
        self.outputs = [self.root / "Build" / "Run", self.root / "Build" / "RunHybrid"]

    def dump(self, name, archived=True):
        game = self.root / name
        game.mkdir()
        (game / "eboot.bin").write_bytes(b"untouched program")
        if archived:
            (game / "app0.zar").write_bytes(b"archive contents for " + name.encode())
        else:
            (game / "data").mkdir()
            (game / "data" / "asset.bin").write_bytes(b"plain asset")
        return game

    def test_archive_only_has_sibling_in_both_layouts(self):
        game = self.dump("archived")
        before = sorted(p.name for p in game.iterdir())
        for output in self.outputs:
            with self.subTest(output=output.name):
                layout.setup_game_layout(game, output)
                self.assertTrue(os.path.samefile(output / "app0", game))
                self.assertTrue(os.path.samefile(output / "app0.zar", game / "app0.zar"))
                self.assertFalse((output / "app0" / "data").exists())
        self.assertEqual(sorted(p.name for p in game.iterdir()), before)
        self.assertEqual((game / "eboot.bin").read_bytes(), b"untouched program")

    def test_placeholder_data_with_archive_is_allowed(self):
        game = self.dump("placeholder")
        (game / "data").mkdir()
        layout.setup_game_layout(game, self.outputs[0])
        self.assertTrue((self.outputs[0] / "app0.zar").is_file())
        self.assertEqual(list((game / "data").iterdir()), [])

    def test_relative_source_archive_symlink_is_resolved_before_linking(self):
        game = self.dump("linked")
        source = self.root / "real.zar"
        source.write_bytes(b"external archive")
        (game / "app0.zar").unlink()
        (game / "app0.zar").symlink_to("../real.zar")
        output = self.outputs[0]
        layout.setup_game_layout(game, output)
        self.assertTrue(os.path.samefile(source, output / "app0.zar"))
        self.assertEqual((output / "app0.zar").read_bytes(), b"external archive")
        self.assertTrue((game / "app0.zar").is_symlink())
        layout.remove_generated_path(output / "app0.zar")
        self.assertEqual(source.read_bytes(), b"external archive")

    def test_broken_source_archive_symlink_is_rejected(self):
        game = self.dump("broken-source")
        (game / "app0.zar").unlink()
        (game / "app0.zar").symlink_to("../missing.zar")
        with self.assertRaisesRegex(ValueError, "not a usable archive"):
            layout.setup_game_layout(game, self.outputs[0])

    def test_repeated_layout_is_idempotent(self):
        game = self.dump("repeat")
        output = self.outputs[0]
        layout.setup_game_layout(game, output)
        with mock.patch.object(layout.os, "link", side_effect=AssertionError("must reuse same file")):
            layout.setup_game_layout(game, output)
        self.assertTrue(os.path.samefile(game / "app0.zar", output / "app0.zar"))

    def test_switch_to_plain_removes_stale_archive_but_preserves_source(self):
        archived = self.dump("old")
        plain = self.dump("plain", archived=False)
        original = (archived / "app0.zar").read_bytes()
        for output in self.outputs:
            layout.setup_game_layout(archived, output)
            layout.setup_game_layout(plain, output)
            self.assertFalse(os.path.lexists(output / "app0.zar"))
            self.assertTrue(os.path.samefile(output / "app0", plain))
        self.assertEqual((archived / "app0.zar").read_bytes(), original)
        self.assertEqual((archived / "eboot.bin").read_bytes(), b"untouched program")

    def test_switch_archives_does_not_overwrite_old_source(self):
        old, new = self.dump("old"), self.dump("new")
        output = self.outputs[0]
        original = (old / "app0.zar").read_bytes()
        layout.setup_game_layout(old, output)
        layout.setup_game_layout(new, output)
        self.assertTrue(os.path.samefile(new / "app0.zar", output / "app0.zar"))
        self.assertEqual((old / "app0.zar").read_bytes(), original)

    def test_broken_links_are_replaced_or_removed(self):
        game = self.dump("plain", archived=False)
        output = self.outputs[0]
        output.mkdir(parents=True)
        (output / "app0").symlink_to(self.root / "missing-dir", target_is_directory=True)
        (output / "app0.zar").symlink_to(self.root / "missing.zar")
        layout.setup_game_layout(game, output)
        self.assertTrue(os.path.samefile(game, output / "app0"))
        self.assertFalse(os.path.lexists(output / "app0.zar"))

    def test_replace_real_generated_directory_does_not_follow_nested_links(self):
        game = self.dump("new")
        old = self.dump("old")
        output = self.outputs[0]
        (output / "app0").mkdir(parents=True)
        (output / "app0" / "assets").symlink_to(old, target_is_directory=True)
        (output / "app0" / "local.bin").write_bytes(b"generated")
        layout.setup_game_layout(game, output)
        self.assertEqual((old / "eboot.bin").read_bytes(), b"untouched program")
        self.assertTrue((old / "app0.zar").exists())
        self.assertTrue(os.path.samefile(game, output / "app0"))

    def test_cross_volume_archive_falls_back_to_copy(self):
        game = self.dump("network")
        output = self.outputs[0]
        with mock.patch.object(layout.os, "link", side_effect=OSError(errno.EXDEV, "cross-device link")):
            layout.setup_game_layout(game, output)
        target = output / "app0.zar"
        self.assertFalse(os.path.samefile(game / "app0.zar", target))
        self.assertEqual(target.read_bytes(), (game / "app0.zar").read_bytes())
        layout.remove_generated_path(target)
        self.assertTrue((game / "app0.zar").is_file())

    def test_link_replaces_directory_and_keeps_linked_source(self):
        source = self.root / "source.bin"
        source.write_bytes(b"source")
        target = self.root / "target"
        target.mkdir()
        (target / "source-link").symlink_to(source)
        layout.link(source, target)
        self.assertTrue(os.path.samefile(source, target))
        layout.remove_generated_path(target)
        self.assertEqual(source.read_bytes(), b"source")

    def test_copy_detaches_old_hardlink_before_overwriting(self):
        source = self.root / "fresh.prx"
        old = self.root / "run.prx"
        target = self.root / "hybrid.prx"
        source.write_bytes(b"fresh")
        old.write_bytes(b"old run")
        os.link(old, target)
        layout.copy(source, target)
        self.assertEqual(target.read_bytes(), b"fresh")
        self.assertEqual(old.read_bytes(), b"old run")
        self.assertFalse(os.path.samefile(old, target))

    def test_missing_source_does_not_delete_existing_target(self):
        target = self.root / "target.prx"
        target.write_bytes(b"keep")
        for operation in (layout.link, layout.copy):
            with self.subTest(operation=operation.__name__):
                with self.assertRaises(FileNotFoundError):
                    operation(self.root / "missing.prx", target)
                self.assertEqual(target.read_bytes(), b"keep")

    def test_no_backend_is_rejected_before_replacing_layout(self):
        game = self.dump("invalid", archived=False)
        (game / "data" / "asset.bin").unlink()
        (game / "data").rmdir()
        old = self.dump("old")
        output = self.outputs[0]
        layout.setup_game_layout(old, output)
        with self.assertRaises(ValueError):
            layout.setup_game_layout(game, output)
        self.assertTrue(os.path.samefile(old, output / "app0"))
        self.assertTrue(os.path.samefile(old / "app0.zar", output / "app0.zar"))

    def test_empty_archive_is_rejected_without_modifying_dump(self):
        game = self.dump("empty")
        (game / "app0.zar").write_bytes(b"")
        with self.assertRaises(ValueError):
            layout.setup_game_layout(game, self.outputs[0])
        self.assertEqual((game / "app0.zar").read_bytes(), b"")
        self.assertFalse(self.outputs[0].exists())

    def test_unsafe_layout_locations_are_rejected(self):
        game = self.dump("game")
        for output in (game, game / "runtime"):
            with self.subTest(output=output):
                with self.assertRaises(ValueError):
                    layout.setup_game_layout(game, output)
        inside_app0 = self.root / "unsafe" / "app0"
        inside_app0.mkdir(parents=True)
        (inside_app0 / "data").mkdir()
        with self.assertRaises(ValueError):
            layout.setup_game_layout(inside_app0, inside_app0.parent)
        self.assertTrue((game / "app0.zar").is_file())
        self.assertTrue((inside_app0 / "data").is_dir())

    def test_windows_junction_failure_tries_directory_symlink(self):
        game = self.dump("network")
        output = self.outputs[0]
        output.mkdir(parents=True)
        platform = mock.Mock(wraps=os)
        platform.name = "nt"

        def mklink(command, **kwargs):
            if command[3] == "/J":
                return subprocess.CompletedProcess(command, 1, "junction refused", "")
            pathlib.Path(command[4]).symlink_to(command[5], target_is_directory=True)
            return subprocess.CompletedProcess(command, 0, "symbolic link created", "")

        with mock.patch.object(layout, "os", platform), mock.patch.object(layout.subprocess, "run", side_effect=mklink) as run:
            layout.link_directory(game, output / "app0")
        self.assertEqual([call.args[0][3] for call in run.call_args_list], ["/J", "/D"])
        self.assertTrue(os.path.samefile(game, output / "app0"))

    def test_layout_only_cli_respects_game_dump_environment(self):
        game = self.dump("env")
        output = self.outputs[0]
        env = os.environ.copy()
        env["ASTROHYBRID_GAME_DUMP"] = str(game)
        result = subprocess.run([sys.executable, "-I", str(ROOT / "Tools" / "hybrid" / "make_run_hybrid.py"),
                                 "--layout-only", "--output", str(output)],
                                capture_output=True, text=True, env=env)
        self.assertEqual(result.returncode, 0, result.stdout + result.stderr)
        self.assertTrue(os.path.samefile(game, output / "app0"))
        self.assertTrue(os.path.samefile(game / "app0.zar", output / "app0.zar"))

    def test_layout_only_cli_does_not_require_build_dependencies(self):
        game = self.dump("cli")
        output = self.outputs[1]
        result = subprocess.run([sys.executable, "-I", str(ROOT / "Tools" / "hybrid" / "make_run_hybrid.py"),
                                 "--layout-only", "--game", str(game), "--output", str(output)],
                                capture_output=True, text=True)
        self.assertEqual(result.returncode, 0, result.stdout + result.stderr)
        self.assertTrue(os.path.samefile(game, output / "app0"))
        self.assertTrue(os.path.samefile(game / "app0.zar", output / "app0.zar"))


if __name__ == "__main__":
    unittest.main()
