import argparse
import csv
import io
import subprocess
import time
from pathlib import Path


QUERY = 'timestamp,memory.used,clocks.sm,clocks.mem,pstate,utilization.gpu,power.draw,temperature.gpu'


def monitor(output, interval, seconds):
    deadline = time.monotonic() + seconds if seconds else float('inf')
    with output.open('x', encoding='utf-8', newline='') as stream:
        writer = csv.writer(stream)
        header = None
        while time.monotonic() < deadline:
            started = time.monotonic()
            result = subprocess.run(
                ['nvidia-smi', f'--query-gpu={QUERY}', '--format=csv'],
                capture_output=True, text=True, check=True, timeout=10,
                creationflags=subprocess.CREATE_NO_WINDOW,
            )
            rows = list(csv.reader(io.StringIO(result.stdout)))
            if len(rows) < 2:
                raise RuntimeError('nvidia-smi returned no GPU samples')
            if header is None:
                header = rows[0]
                writer.writerow(header)
            elif rows[0] != header:
                raise RuntimeError('nvidia-smi columns changed during capture')
            writer.writerows(rows[1:])
            stream.flush()
            time.sleep(max(0, min(interval - (time.monotonic() - started), deadline - time.monotonic())))


if __name__ == '__main__':
    parser = argparse.ArgumentParser(description='Flush each GPU sample so harness termination preserves it.')
    parser.add_argument('output', type=Path)
    parser.add_argument('--interval', type=float, default=5)
    parser.add_argument('--seconds', type=float, default=0)
    args = parser.parse_args()
    if args.interval <= 0 or args.seconds < 0:
        parser.error('interval must be positive and seconds must be nonnegative')
    monitor(args.output, args.interval, args.seconds)
