import importlib.util
from pathlib import Path
import unittest

spec = importlib.util.spec_from_file_location(
    "astrohybrid", Path(__file__).resolve().parents[1] / "Tools/linux/astrohybrid.py")
launcher = importlib.util.module_from_spec(spec)
spec.loader.exec_module(launcher)


class HybridCacheTests(unittest.TestCase):
    def test_default_title_fallback(self):
        env = {}
        launcher.configure_hybrid_cache(env)
        self.assertEqual(env["ASTROHYBRID_CACHE_TITLE_ID"], launcher.TITLE_ID)

    def test_explicit_title_override(self):
        for value in ("PPSA12345", ""):
            env = {"ASTROHYBRID_CACHE_TITLE_ID": value}
            launcher.configure_hybrid_cache(env)
            self.assertEqual(env["ASTROHYBRID_CACHE_TITLE_ID"], value)

    def test_disabled_cache_overrides_inherited_persistence(self):
        env = {"ASTROHYBRID_SHADER_CACHE": "0", "ASTROHYBRID_CACHE_TITLE_ID": launcher.TITLE_ID,
               "KYTY_PROGRAM_CACHE": "1", "KYTY_SHADER_PRECOMPILE": "1",
               "KYTY_PIPELINE_JOURNAL": "1", "KYTY_PIPELINE_LIST": "1",
               "KYTY_PIPELINE_LIST_PREPARE": "1"}
        launcher.configure_hybrid_cache(env)
        self.assertEqual(env["ASTROHYBRID_CACHE_TITLE_ID"], "")
        for key in ("KYTY_PROGRAM_CACHE", "KYTY_SHADER_PRECOMPILE", "KYTY_PIPELINE_JOURNAL",
                    "KYTY_PIPELINE_LIST", "KYTY_PIPELINE_LIST_PREPARE"):
            self.assertEqual(env[key], "0")


if __name__ == "__main__":
    unittest.main()
