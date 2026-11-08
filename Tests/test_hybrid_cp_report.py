import importlib.util
import pathlib
import tempfile
import unittest

spec = importlib.util.spec_from_file_location(
    "report", pathlib.Path(__file__).with_name("hybrid_cp_report.py"))
report = importlib.util.module_from_spec(spec)
spec.loader.exec_module(report)


class ReportTests(unittest.TestCase):
    def test_bom_encodings_and_last_six(self):
        with tempfile.TemporaryDirectory() as tmp:
            root = pathlib.Path(tmp)
            (root / "stdout.log").write_text(
                "".join(f"CpGaps 10s: frames 300 wall {n}.00 ms/frame | commit "
                        "20.00 (5000/f) | gap 10.00 = headwait.deep 3.00 (0.0/f)\n"
                        for n in range(30, 38)), encoding="utf-8")
            (root / "result.txt").write_text("timeout", encoding="utf-16")
            frames = root / "frames.csv"
            frames.write_text("".join(f"{n * 40000},40000\n" for n in range(1, 2001)))
            (root / "env.txt").write_text(
                f"KYTY_FRAME_TIMES_LOG={frames}\nKYTY_DRAW_PREP_HOT=2\n",
                encoding="utf-16")
            result = report.summarize(root)
            self.assertEqual(result["result"], "timeout")
            self.assertEqual(result["windows"], 8)
            self.assertEqual(result["median"]["wall"], 34.5)
            self.assertEqual(result["median"]["us_draw"], 4)
            self.assertEqual(result["frame_last60s"]["p99_ms"], 40)
            self.assertEqual(result["frame_last60s"]["low1_fps"], 25)
            self.assertEqual(result["switches"], ["KYTY_DRAW_PREP_HOT=2"])

    def test_no_cp_windows(self):
        with tempfile.TemporaryDirectory() as tmp:
            root = pathlib.Path(tmp)
            for name in ("stdout.log", "env.txt"):
                (root / name).write_text("", encoding="utf-8-sig")
            (root / "result.txt").write_text("exit after 5s", encoding="utf-8-sig")
            result = report.summarize(root)
            self.assertEqual(result["windows"], 0)
            self.assertNotIn("median", result)


if __name__ == "__main__":
    unittest.main()
