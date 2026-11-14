"""Windows smoke tests for the built app's external-launcher commands; no game dump needed."""
import json
import os
from pathlib import Path
import shutil
import subprocess
import sys
import tempfile
import time
import unittest


ROOT = Path(__file__).resolve().parents[1]


@unittest.skipUnless(sys.platform == "win32", "requires Windows and a Release app build")
class RecompLauncherTests(unittest.TestCase):
    def setUp(self):
        app = ROOT / "AstroHybrid.App" / "bin" / "Release"
        self.assertTrue((app / "AstroHybrid.exe").is_file(), "Build AstroHybrid.App in Release first")
        self.temp = tempfile.TemporaryDirectory(prefix="astro external launcher ")
        self.addCleanup(self.temp.cleanup)
        self.root = Path(self.temp.name)
        for source in app.iterdir():
            if source.is_file() and source.suffix in (".exe", ".dll", ".config"):
                shutil.copy2(source, self.root)
        self.run = self.root / "Build" / "RunHybrid"
        self.run.mkdir(parents=True)
        # LauncherService checks this exists; the fake batch script never executes it.
        (self.run / "AstroBot.exe").touch()
        (self.root / "Run-AstroHybrid.bat").write_text(
            '@echo off\n'
            '(set KYTY_GPU_ & set ASTROHYBRID_) > "%~dp0environment.txt"\n'
            'ping -n 2 127.0.0.1 > nul\n'
            'exit /b 23\n', encoding="ascii")
        self.settings = self.root / "Build" / "astrohybrid-settings.json"

    def invoke(self, *args):
        # Keep inherited tuning from affecting the test's defaults.
        env = {k: v for k, v in os.environ.items()
               if not k.startswith(("ASTROHYBRID_", "KYTY_GPU_"))}
        return subprocess.run([str(self.root / "AstroHybrid.exe"), *args],
                              cwd=self.root, env=env, timeout=15).returncode

    def environment(self):
        return dict(line.split("=", 1) for line in
                    (self.root / "environment.txt").read_text().splitlines() if "=" in line)

    def test_play_uses_saved_settings_and_waits_for_child(self):
        self.settings.write_text(json.dumps({
            "size": 3, "fps": 5, "fullscreen": False, "vsync": True,
            "upscale": 1, "lowlatency": True, "applyBestOnStart": True,
            "gameDumpPath": "keep this path", "gameVersion": "01.019.000",
        }), encoding="utf-8")
        started = time.monotonic()
        self.assertEqual(self.invoke("--play"), 23)
        self.assertGreaterEqual(time.monotonic() - started, 0.8)
        env = self.environment()
        for key, expected in {
            "KYTY_GPU_WIDTH": "2560", "KYTY_GPU_HEIGHT": "1440",
            "KYTY_GPU_FULLSCREEN": "0", "KYTY_GPU_PRESENT_MODE": "Fifo",
            "ASTROHYBRID_FPS_LIMIT": "60", "ASTROHYBRID_DLSS": "1",
            "ASTROHYBRID_LOW_LATENCY": "1",
        }.items():
            self.assertEqual(env[key], expected, key)
        saved = json.loads(self.settings.read_text(encoding="utf-8"))
        self.assertFalse(saved["applyBestOnStart"])
        self.assertIsNotNone(saved["lastLaunched"])
        self.assertEqual(saved["gameDumpPath"], "keep this path")
        self.assertEqual(saved["gameVersion"], "01.019.000")

    def test_play_applies_frame_generation_target(self):
        self.settings.write_text('{"fgmode": 3, "fgtarget": 4}', encoding="utf-8")
        self.assertEqual(self.invoke("--play"), 23)
        env = self.environment()
        self.assertEqual(env["ASTROHYBRID_DLSSG"], "1")
        self.assertEqual(env["ASTROHYBRID_DLSSG_FRAMES"], "2")
        self.assertEqual(env["ASTROHYBRID_FPS_LIMIT"], "40")

    def test_missing_installation_fails_without_ui(self):
        (self.run / "AstroBot.exe").unlink()
        self.assertEqual(self.invoke("--play"), 2)
        self.assertIn("AstroBot.exe not found", (self.root / "Logs" / "astrohybrid-error.log").read_text())

    def test_setup_rejects_missing_dump_without_ui(self):
        self.assertEqual(self.invoke("--setup", str(self.root / "missing dump")), 2)
        self.assertIn("Selected folder does not exist", (self.root / "Logs" / "setup.log").read_text())


if __name__ == "__main__":
    unittest.main()
