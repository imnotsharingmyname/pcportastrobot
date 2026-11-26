import importlib.util
from pathlib import Path
import subprocess
import unittest
from unittest.mock import patch

spec = importlib.util.spec_from_file_location(
    "route_audio", Path(__file__).resolve().parents[1] / "Tools/linux/route-audio.py")
audio = importlib.util.module_from_spec(spec)
spec.loader.exec_module(audio)


class RoutingTests(unittest.TestCase):
    def test_link_parser_tracks_all_links_without_attributing_other_ports(self):
        output = """AstroBot:output_FL
  |-> old:playback_FL
  |-> extra:playback_FL
Other:output_FL
  |-> foreign:playback_FL
AstroBot:output_FR
  |-> old:playback_FR
"""
        with patch.object(audio, "run", return_value=subprocess.CompletedProcess([], 0, output)):
            self.assertEqual(audio.links("AstroBot"), [
                ("AstroBot:output_FL", "old:playback_FL"),
                ("AstroBot:output_FL", "extra:playback_FL"),
                ("AstroBot:output_FR", "old:playback_FR")])

    def test_pulse_only_routing(self):
        def run(cmd):
            if cmd == ["pactl", "list", "sink-inputs"]:
                return subprocess.CompletedProcess(cmd, 0, 'Sink Input #12\n application.name = "AstroBot"\n')
            if cmd == ["pactl", "move-sink-input", "12", "sink"]:
                return subprocess.CompletedProcess(cmd, 0, "")
            return None

        with patch.object(audio, "run", side_effect=run):
            self.assertTrue(audio.route("sink", "AstroBot"))

    def exercise_graph(self, can_connect):
        graph = {
            ("AstroBot:output_FL", "old:playback_FL"),
            ("AstroBot:output_FR", "old:playback_FR"),
            ("AstroBot:output_FC", "old:playback_FC"),
        }
        removed = []
        def run(cmd):
            if cmd == ["pw-link", "-o"]:
                return subprocess.CompletedProcess(cmd, 0, "\n".join(sorted({s for s, _ in graph})))
            if cmd == ["pw-link", "-l"]:
                output = "".join(s + "\n" + "".join("  |-> " + t + "\n" for x, t in sorted(graph) if x == s)
                                 for s in sorted({s for s, _ in graph}))
                return subprocess.CompletedProcess(cmd, 0, output)
            if cmd[:2] == ["pw-link", "-d"]:
                pair = tuple(cmd[2:])
                removed.append(pair)
                graph.remove(pair)
                return subprocess.CompletedProcess(cmd, 0, "")
            if cmd[0] == "pw-link":
                if can_connect or cmd[1].endswith("FL"):
                    graph.add(tuple(cmd[1:]))
                    return subprocess.CompletedProcess(cmd, 0, "")
                return subprocess.CompletedProcess(cmd, 1, "")
            return None

        with patch.object(audio, "run", side_effect=run):
            self.assertEqual(audio.route("sink", "AstroBot"), can_connect)
        self.assertIn(("AstroBot:output_FC", "old:playback_FC"), graph)
        if can_connect:
            self.assertEqual(len(removed), 2)
            self.assertIn(("AstroBot:output_FL", "sink:playback_FL"), graph)
            self.assertIn(("AstroBot:output_FR", "sink:playback_FR"), graph)
        else:
            self.assertEqual(removed, [("AstroBot:output_FL", "sink:playback_FL")])
            self.assertNotIn(("AstroBot:output_FL", "sink:playback_FL"), graph)
            self.assertIn(("AstroBot:output_FL", "old:playback_FL"), graph)
            self.assertIn(("AstroBot:output_FR", "old:playback_FR"), graph)

    def test_failed_stereo_connection_preserves_old_audio(self):
        self.exercise_graph(False)

    def test_successful_connection_removes_only_old_stereo_links(self):
        self.exercise_graph(True)


if __name__ == "__main__":
    unittest.main()
