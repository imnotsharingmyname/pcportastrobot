"""Summarize the last six CP windows without treating scene changes as speedups."""
import argparse
import csv
import json
import pathlib
import re
import statistics


def read_text(path):
    data = pathlib.Path(path).read_bytes()
    return data.decode("utf-16" if data.startswith((b"\xff\xfe", b"\xfe\xff"))
                       else "utf-8-sig", errors="replace")


def summarize(folder):
    folder = pathlib.Path(folder)
    text = read_text(folder / "stdout.log")
    pattern = (r"CpGaps 10s: frames (\d+) wall ([\d.]+) ms/frame \| commit "
               r"([\d.]+) \((\d+)/f\) \| gap ([\d.]+) = (.*)")
    windows = []
    for match in re.finditer(pattern, text):
        frames, wall, commit, draws, gap, rest = match.groups()
        deep = re.search(r"headwait.deep ([\d.]+)", rest)
        windows.append(dict(frames=int(frames), wall=float(wall), commit=float(commit),
                            draws=int(draws), gap=float(gap),
                            deep=float(deep[1]) if deep else 0,
                            us_draw=1000 * float(commit) / max(1, int(draws))))
    last = windows[-6:]
    result = dict(run=folder.name, result=read_text(folder / "result.txt").strip(),
                  windows=len(windows), last=last)
    if last:
        result["median"] = {k: statistics.median(w[k] for w in last)
                            for k in ("wall", "commit", "draws", "gap", "deep", "us_draw")}
    result["fps_last6"] = [float(x) for x in re.findall(
        r"Kyty fault cost: last 10 s, ([\d.]+) fps", text)][-6:]
    env = read_text(folder / "env.txt")
    result["switches"] = [x for x in env.splitlines() if re.match(
        r"(KYTY_DRAW_PREP_(HOT|WORKERS|WAKE_BACKLOG|PRIORITY|STEAL|WINDOW|SPIN_US|COLD_SPIN_US)|"
        r"KYTY_CP_BINDING_MEMO_BITS|KYTY_CPU_RESERVE|KYTY_SKIP_INACTIVE_PS|"
        r"KYTY_SUBMIT_MIN_INTERVAL_US|KYTY_IDLE_FLUSH_DRAWS|KYTY_GFX_SLICE_DRAWS|"
        r"KYTY_EOP_FLUSH_PACKETS|"
        r"APS5_NO_POWER_THROTTLING)=", x)]
    ft = re.search(r"^KYTY_FRAME_TIMES_LOG=(.+)$", env, re.M)
    if ft and pathlib.Path(ft[1].strip()).exists():
        with open(ft[1].strip(), newline="") as f:
            rows = [(int(t), int(dt)) for t, dt in csv.reader(f)]
        result["frame_csv_rows"] = len(rows)
        if rows:
            samples = sorted(dt / 1000 for t, dt in rows if t >= rows[-1][0] - 60_000_000)
            worst = samples[-max(1, int(len(samples) * .01)):]
            result["frame_last60s"] = dict(
                samples=len(samples), median_ms=statistics.median(samples),
                p99_ms=samples[min(len(samples)-1, int(.99 * len(samples)))],
                low1_fps=1000 / statistics.mean(worst),
                avg_fps=1000 / statistics.mean(samples))
    return result


if __name__ == "__main__":
    parser = argparse.ArgumentParser()
    parser.add_argument("folders", nargs="+")
    args = parser.parse_args()
    print(json.dumps([summarize(p) for p in args.folders], indent=2))
