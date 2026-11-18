#!/usr/bin/env bash
# Convenience wrapper around Tools/linux/astrohybrid.py.
# Examples:
#   Tools/linux/run.sh check
#   Tools/linux/run.sh setup --game ~/Games/PPSA21567-app
#   Tools/linux/run.sh run --env APS5_TRACE_SUMMARY_EVERY 10
set -euo pipefail
ROOT=$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)
exec python3 "$ROOT/Tools/linux/astrohybrid.py" "$@"
