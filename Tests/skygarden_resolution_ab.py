"""Prepare isolated resolution variants and score native frame-time CSVs.

Only the existing, reversible fixed_1080p game patch is used. The source
executable is never modified. See Docs/SKYGARDEN_40FPS.md for the A/B procedure.
"""
import argparse
import csv
import json
import mmap
import shutil
import statistics
from pathlib import Path

ORIGINAL = bytes.fromhex('440373084489730c')
CAPPED = bytes.fromhex('4531f6904489730c')
CONTROL = 'AstroBot-SkyGarden-Control.exe'
CANDIDATE = 'AstroBot-SkyGarden-1080p.exe'


def patch_offset(path):
    if path.stat().st_size == 0:
        raise ValueError('Empty executable')
    with path.open('rb') as f, mmap.mmap(f.fileno(), 0, access=mmap.ACCESS_READ) as data:
        matches = []
        for signature in (ORIGINAL, CAPPED):
            offset = data.find(signature)
            if offset >= 0:
                if data.find(signature, offset + 1) >= 0:
                    raise ValueError('Ambiguous resolution signature')
                matches.append((offset, signature))
    if len(matches) != 1:
        raise ValueError('Expected exactly one original or capped resolution signature')
    return matches[0]


def prepare(source):
    source = Path(source).resolve()
    if source.name in (CONTROL, CANDIDATE):
        raise ValueError('Choose the original runtime executable, not a test variant')
    offset, signature = patch_offset(source)
    outputs = [source.with_name(CONTROL), source.with_name(CANDIDATE)]
    if any(p.exists() or p.is_symlink() for p in outputs):
        raise FileExistsError('Test variant already exists; remove it explicitly before preparing again')
    created = []
    try:
        for target, desired in zip(outputs, (ORIGINAL, CAPPED)):
            with source.open('rb') as src, target.open('xb') as dst:
                created.append(target)
                shutil.copyfileobj(src, dst, 1024 * 1024)
            if patch_offset(target) != (offset, signature):
                raise ValueError('Source changed during copy')
            with target.open('r+b') as f:
                f.seek(offset)
                f.write(desired[:4])
            if patch_offset(target) != (offset, desired):
                raise ValueError('Variant verification failed')
    except BaseException:
        for target in created:
            target.unlink()
        raise
    return {'source_already_capped': signature == CAPPED,
            'control': str(outputs[0]), 'candidate': str(outputs[1])}


def summarize(path):
    with Path(path).open(newline='', encoding='utf-8-sig') as f:
        rows = [(int(t), int(dt)) for t, dt in csv.reader(f)]
    if not rows or any(dt <= 0 for _, dt in rows) or any(
            b[0] <= a[0] for a, b in zip(rows, rows[1:])):
        raise ValueError(f'{path}: missing or invalid frame times')
    if rows[-1][0] - rows[0][0] < 60_000_000:
        raise ValueError(f'{path}: less than 60 seconds of frame data')
    tail = [dt for t, dt in rows if t > rows[-1][0] - 60_000_000]
    if not 55_000_000 <= sum(tail) <= 65_000_000:
        raise ValueError(f'{path}: incomplete or inconsistent final 60 seconds')
    worst = sorted(tail, reverse=True)[:max(1, (len(tail) + 99) // 100)]
    return {'avg_fps': 1_000_000 / statistics.mean(tail),
            'low1_fps': 1_000_000 / statistics.mean(worst), 'frames': len(tail)}


def gate(paths):
    if len(paths) != 4 or len({Path(p).resolve() for p in paths}) != 4:
        raise ValueError('Supply four distinct CSVs in control/candidate/candidate/control order')
    runs = [summarize(p) for p in paths]
    baseline = statistics.mean(runs[i]['avg_fps'] for i in (0, 3))
    candidate = statistics.mean(runs[i]['avg_fps'] for i in (1, 2))
    return {'runs_abba': runs, 'control_fps': baseline, 'candidate_fps': candidate,
            'gain_percent': (candidate / baseline - 1) * 100,
            'numeric_target_met': all(runs[i]['avg_fps'] >= 40 for i in (1, 2)),
            'review_required': 'Same scene, uncapped native rendering, no frame generation, screenshots and stability must be checked separately.'}


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    sub = parser.add_subparsers(dest='command', required=True)
    prep = sub.add_parser('prepare')
    prep.add_argument('source', type=Path)
    score = sub.add_parser('gate')
    score.add_argument('csv', nargs=4, type=Path)
    args = parser.parse_args()
    result = prepare(args.source) if args.command == 'prepare' else gate(args.csv)
    print(json.dumps(result, indent=2))
    return 0 if args.command == 'prepare' or result['numeric_target_met'] else 2


if __name__ == '__main__':
    raise SystemExit(main())
