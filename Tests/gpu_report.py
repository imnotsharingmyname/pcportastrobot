import argparse
import csv
import json
import re
import statistics
from collections import defaultdict
from datetime import datetime, timedelta
from pathlib import Path


def median(values):
    return round(statistics.median(values), 3) if values else None


def report(run, telemetry):
    text = '\n'.join((run / name).read_text(encoding='utf-8', errors='replace')
                     for name in ('stderr.log', 'stdout.log') if (run / name).exists())
    presents = re.findall(r'\[present\] (\d+) presents[^\n]*?GPU busy ([\d.]+) ms, idle gaps ([\d.]+) ms', text)
    steady = presents[-10:][2:]
    classes = defaultdict(list)
    programs = defaultdict(list)
    batches = []
    timed = [line for line in text.splitlines() if line.startswith('[gputime]') and '; 0 presents;' not in line]
    for line in timed[-8:]:
        count = re.search(r'; (\d+) presents;', line)
        batch = re.search(r'\(batch (\d+) ms', line)
        if count is None or int(count[1]) == 0:
            continue
        frames = int(count[1])
        if batch:
            batches.append(int(batch[1]) / frames)
        for name, ms, mib in re.findall(r'([\w-]+) x\d+ [\d.]+ms [\d.]+MiB \(per present x[\d.]+ ([\d.]+)ms ([\d.]+)MiB\)', line):
            classes[name].append(float(ms))
        for address, count, ms in re.findall(r'(0x[\da-f]+) x(\d+) ([\d.]+)ms', line.partition('; by class:')[0]):
            programs[address].append(float(ms) / frames)
    start = datetime.strptime(run.name, 'run-%Y%m%d-%H%M%S')
    result_file = run / 'result.txt'
    result_text = result_file.read_text(encoding='utf-8', errors='replace') if result_file.exists() else ''
    duration = re.search(r'after (\d+)s', result_text)
    end = start + timedelta(seconds=int(duration[1]) if duration else 150)
    samples = []
    for path in telemetry:
        with path.open(encoding='utf-8-sig', newline='') as stream:
            for raw in csv.DictReader(stream):
                row = {key.strip(): value.strip() for key, value in raw.items() if key and value}
                stamp = datetime.strptime(row['timestamp'], '%Y/%m/%d %H:%M:%S.%f')
                if start + timedelta(seconds=70) <= stamp <= end:
                    samples.append(row)
    metrics = {}
    for key in ('memory.used [MiB]', 'clocks.current.sm [MHz]', 'clocks.current.memory [MHz]', 'power.draw [W]', 'temperature.gpu', 'utilization.gpu [%]'):
        values = [float(row[key].split()[0]) for row in samples if key in row and not row[key].startswith('[N/A]')]
        if values:
            metrics[key] = {'min': min(values), 'median': median(values), 'max': max(values)}
    return {
        'run': run.name,
        'result': result_text.strip(),
        'steady_presents_per_10s': [int(row[0]) for row in steady],
        'steady_median_presents': median([int(row[0]) for row in steady]),
        'steady_gpu_busy_ms': median([float(row[1]) for row in steady]),
        'steady_gpu_idle_ms': median([float(row[2]) for row in steady]),
        'timing_windows': len(batches),
        'batch_ms_per_present': median(batches),
        'class_ms_per_present': {key: median(value) for key, value in classes.items()},
        'top_program_ms_per_present': dict(sorted(((key, median(value)) for key, value in programs.items()), key=lambda item: item[1], reverse=True)[:8]),
        'telemetry_samples_after_70s': len(samples),
        'telemetry': metrics,
        'pstates': sorted({row['pstate'] for row in samples}),
    }


if __name__ == '__main__':
    parser = argparse.ArgumentParser(description='Compare steady GPU classes and clock/memory telemetry.')
    parser.add_argument('runs', type=Path, nargs='+')
    parser.add_argument('--smi', type=Path, nargs='*', default=[])
    args = parser.parse_args()
    print(json.dumps([report(run, args.smi) for run in args.runs], indent=2))
