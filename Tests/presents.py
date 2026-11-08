import re
import sys
from pathlib import Path

# Usage: python Tests\presents.py Logs\run-YYYYMMDD-HHMMSS [more run folders...]
# Prints, for each run: the switches that differ from nothing, the presents per 10 s (frames shown; /10 = FPS),
# and GPU busy / idle per frame from the last [present] line. Needs APS5_PROFILE_DRAW=1 in the run.
for folder in sys.argv[1:]:
    run = Path(folder)
    text = ''
    for name in ('stderr.log', 'stdout.log'):
        path = run / name
        if path.exists():
            text += path.read_text(encoding='utf-8', errors='replace')
    counts = re.findall(r'\[present\] (\d+) presents', text)
    busy = re.findall(r'GPU busy ([\d.]+) ms, idle gaps ([\d.]+) ms', text)
    env = run / 'env.txt'
    switches = []
    if env.exists():
        for line in env.read_text(encoding='utf-8', errors='replace').splitlines():
            if line.startswith(('APS5_COMPUTE', 'APS5_RECLAIM', 'APS5_PROFILE_GPU', 'APS5_PIPELINE_DISPATCH', 'APS5_LINEAR_BLOCK_TRACKING', 'APS5_PROFILE_STORAGE_RESOURCES', 'APS5_DIRECT_SIZED_FREE', 'APS5_SYNC_COMPLETION_LABELS_ONLY')):
                switches.append(line)
    tail = counts[-10:]
    steady = sorted(int(c) for c in tail[2:]) if len(tail) > 2 else []
    median = steady[len(steady) // 2] if steady else 0
    print(f'{run.name}: presents/10s {" ".join(tail)} | steady median {median} (~{median / 10:.1f} FPS)'
          + (f' | GPU busy {busy[-1][0]} ms idle {busy[-1][1]} ms' if busy else '')
          + (f' | {" ".join(switches)}' if switches else ''))
