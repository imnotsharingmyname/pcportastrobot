import tempfile
import unittest
from pathlib import Path
from skygarden_resolution_ab import ORIGINAL, CAPPED, CONTROL, CANDIDATE, prepare, gate, summarize


class SkyGardenTests(unittest.TestCase):
    def test_isolated_variants(self):
        with tempfile.TemporaryDirectory() as tmp:
            root = Path(tmp)
            source = root / 'AstroBot.exe'
            for signature in (ORIGINAL, CAPPED):
                payload = b'a' * 65533 + signature + b'b'
                source.write_bytes(payload)
                result = prepare(source)
                self.assertEqual(result['source_already_capped'], signature == CAPPED)
                self.assertEqual(source.read_bytes(), payload)
                self.assertEqual((root / CONTROL).read_bytes(), payload.replace(signature, ORIGINAL))
                self.assertEqual((root / CANDIDATE).read_bytes(), payload.replace(signature, CAPPED))
                with self.assertRaises(FileExistsError):
                    prepare(source)
                (root / CONTROL).unlink()
                (root / CANDIDATE).unlink()

    def test_refuse_ambiguous(self):
        with tempfile.TemporaryDirectory() as tmp:
            source = Path(tmp) / 'AstroBot.exe'
            for payload in (b'none', ORIGINAL + CAPPED, ORIGINAL * 2):
                source.write_bytes(payload)
                with self.assertRaises(ValueError):
                    prepare(source)
                self.assertFalse(source.with_name(CANDIDATE).exists())

    def test_gate(self):
        with tempfile.TemporaryDirectory() as tmp:
            paths = [Path(tmp) / f'{i}.csv' for i in range(4)]
            for path, dt in zip(paths, (33333, 25000, 24000, 33333)):
                path.write_text(''.join(f'{i * dt},{dt}\n' for i in range(1, 4001)))
            self.assertTrue(gate(paths)['numeric_target_met'])
            paths[2].write_text(''.join(f'{i * 26000},26000\n' for i in range(1, 4001)))
            self.assertFalse(gate(paths)['numeric_target_met'])
            paths[2].write_text('1,25000\n2,25000\n')
            with self.assertRaises(ValueError):
                summarize(paths[2])
            with self.assertRaises(ValueError):
                gate([paths[0]] * 4)


if __name__ == '__main__':
    unittest.main()
