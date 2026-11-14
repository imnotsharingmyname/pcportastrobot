#!/usr/bin/env python3
"""Route the game's audio to a PipeWire/Pulse sink, bypassing filter chains.

The desktop may restore an application stream into a filter sink such as
JamesDSP (virtual surround), which adds latency.  Once the game's PipeWire
client appears, disconnect its outputs from everything except the requested
sink and link them directly.  This is a no-op if the tools or ports are absent.
"""

from __future__ import annotations

import argparse
import re
import subprocess
import sys
import time
from pathlib import Path


def run(command: list[str]) -> subprocess.CompletedProcess[str] | None:
    try:
        return subprocess.run(command, capture_output=True, text=True, timeout=5)
    except (OSError, subprocess.TimeoutExpired):
        return None


def default_sink() -> str | None:
    result = run(["pactl", "get-default-sink"])
    if result and result.returncode == 0:
        name = result.stdout.strip()
        if name:
            return name
    result = run(["pactl", "info"])
    if result and result.returncode == 0:
        for line in result.stdout.splitlines():
            if line.startswith("Default Sink:"):
                return line.split(":", 1)[1].strip()
    return None


def output_ports(game: str) -> list[str]:
    result = run(["pw-link", "-o"])
    if result is None or result.returncode != 0:
        return []
    prefix = game + ":output_"
    return [line.strip() for line in result.stdout.splitlines() if line.strip().startswith(prefix)]


def links(game: str) -> list[tuple[str, str]]:
    result = run(["pw-link", "-l"])
    if result is None or result.returncode != 0:
        return []
    found: list[tuple[str, str]] = []
    source: str | None = None
    for line in result.stdout.splitlines():
        stripped = line.strip()
        if stripped.startswith(game + ":output_"):
            source = stripped
        elif source is not None and stripped.startswith("|->"):
            found.append((source, stripped[3:].strip()))
            source = None
    return found


def route(sink: str, game: str) -> bool:
    ports = output_ports(game)
    if not ports:
        return False

    # One Pulse sink-input move covers PulseAudio hosts; PipeWire filter chains may ignore it,
    # so fix the graph below as well.
    result = run(["pactl", "list", "sink-inputs"])
    if result is not None and result.returncode == 0:
        for block in result.stdout.split("\n\n"):
            if f'application.name = "{game}"' not in block:
                continue
            match = re.search(r"Sink Input #(\d+)", block)
            if match:
                run(["pactl", "move-sink-input", match.group(1), sink])

    wanted = {
        f"{sink}:playback_FL": f"{game}:output_FL",
        f"{sink}:playback_FR": f"{game}:output_FR",
    }
    for source, target in links(game):
        if target not in wanted:
            run(["pw-link", "-d", source, target])

    existing = set(links(game))
    connected = 0
    for target, source in wanted.items():
        if source not in ports:
            continue
        if (source, target) in existing:
            connected += 1
            continue
        result = run(["pw-link", source, target])
        if result is not None and result.returncode == 0:
            connected += 1
    return connected > 0


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--sink", default="", help="PipeWire sink name; default: the current default sink")
    parser.add_argument("--game", default="AstroBot", help="PipeWire client name of the game")
    parser.add_argument("--wait", type=float, default=60.0, help="seconds to wait for the game's ports")
    parser.add_argument("--quiet", action="store_true")
    args = parser.parse_args()

    sink = args.sink or default_sink()
    if not sink:
        if not args.quiet:
            print("route-audio: no default sink", file=sys.stderr)
        return 1

    deadline = time.monotonic() + max(1.0, args.wait)
    while time.monotonic() < deadline:
        if route(sink, args.game):
            if not args.quiet:
                print(f"route-audio: {args.game} -> {sink}")
            return 0
        time.sleep(0.5)

    if not args.quiet:
        print(f"route-audio: {args.game} could not be routed to {sink}", file=sys.stderr)
    return 1


if __name__ == "__main__":
    raise SystemExit(main())
