#!/usr/bin/env bash
# AstroHybrid on Linux through Wine (11+ or Proton's wine; tested with Wine 11.22 on CachyOS).
#
#   ./Run-AstroHybrid-Linux.sh                  start the game (same settings as Run-AstroHybrid.bat)
#   ./Run-AstroHybrid-Linux.sh --setup <dump>   build Build/RunHybrid from your PS5 dump, headless
#   ./Run-AstroHybrid-Linux.sh --launcher       open the AstroHybrid launcher (needs dotnet48 in the prefix)
#
# The game itself needs no winetricks; the WPF launcher needs `winetricks dotnet48 corefonts`.
# Environment variables set before running this script reach the game (Wine passes them through),
# so e.g. `KYTY_GPU_WIDTH=2560 KYTY_GPU_HEIGHT=1440 ./Run-AstroHybrid-Linux.sh` or wrapping the script in
# gamescope/MangoHud works as usual.
#   WINEPREFIX              Wine prefix (default: ~/.wine)
#   WINE                    wine binary (default: wine)
#   ASTROHYBRID_DS_BT_FIX=0 skip the Bluetooth DualSense registry fix (#59)
#   ASTROHYBRID_LSFG=2|3|4  frame generation with lsfg-vk (2x/3x/4x, default off; needs lsfg-vk 2.x)
#   ASTROHYBRID_LSFG_FLOW   lsfg-vk flow scale 0.25..1.0 (default 0.5)
#   ASTROHYBRID_LSFG_PERF=0 lsfg-vk quality mode (default: performance mode)
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
WINE="${WINE:-wine}"
# --help works without Wine installed; everything else needs it.
case "${1:-}" in
    -h|--help) ;;
    *) command -v "$WINE" >/dev/null || { echo "wine not found (set WINE=/path/to/wine)" >&2; exit 1; } ;;
esac

# Wine marks a Bluetooth DualSense's parent devnode only as WINEBUS\WINE_COMP_HID, so SDL (inside
# kyty_gpu.dll) treats it as USB and sends USB-format output reports: no rumble, haptics or light bar
# over Bluetooth (#59). Adding the BTHENUM compatible IDs that Windows reports makes SDL see it as
# wireless. Idempotent; only touches DualSense (054C:0CE6) and DualSense Edge (054C:0DF2) entries.
# ASTROHYBRID_DS_BT_FIX=0 skips it.
fix_dualsense_bt() {
    local base='HKLM\System\CurrentControlSet\Enum\BTHENUM'
    local uuid='{00001124-0000-1000-8000-00805f9b34fb}'
    local key pid
    WINEDEBUG=-all "$WINE" reg query "$base" /s 2>/dev/null | tr -d '\r' | awk '
        /^HKEY/ { key = $0 }
        /CompatibleIDs/ && key ~ /VID_054C&PID_(0CE6|0DF2)\\/ && $0 !~ /BTHENUM/ { print key }' |
    while read -r key; do
        pid=$(sed -nE 's/.*PID_([0-9A-F]{4}).*/\1/p' <<<"$key" | tr 'A-F' 'a-f')
        WINEDEBUG=-all "$WINE" reg add "$key" /v CompatibleIDs /t REG_MULTI_SZ \
            /d "BTHENUM\\${uuid}_VID&0002054c_PID&${pid}\\0BTHENUM\\${uuid}\\0WINEBUS\\WINE_COMP_HID" /f >/dev/null &&
            echo "DualSense Bluetooth fix applied to ${key##*\\}"
    done || true
}

# Frame generation through lsfg-vk (https://github.com/PancakeTAS/lsfg-vk), a Vulkan layer that uses
# Lossless Scaling's frame generation; Kyty renders with Vulkan directly, so it hooks the game's swapchain
# as is. Needs lsfg-vk 2.x installed and Lossless Scaling from Steam on the "lsfg-vk" beta branch (which
# ships lsfg-vk.dll; the normal Lossless.dll fails with "Unable to find base shader"). Set only for this
# run, so nothing else on the system is affected. Put this script inside gamescope, not the variables:
# with LSFGVK_* exported before gamescope starts, gamescope (a Vulkan app) loads the layer as well.
# Defaults from tests on an RX 9070 at 5K: flow 0.5 costs about half the GPU time of 1.0, and
# performance mode lowered GPU load further (84% -> 70% busy at 2x).
setup_lsfg() {
    local mult="$1" dll="${LSFGVK_DLL_PATH:-}" root lib d
    case "$mult" in 2|3|4) ;; *) echo "ASTROHYBRID_LSFG must be 2, 3 or 4" >&2; return 0 ;; esac
    local layer found=""
    for layer in /usr/share/vulkan/implicit_layer.d/*.json /etc/vulkan/implicit_layer.d/*.json \
            "${XDG_DATA_HOME:-$HOME/.local/share}"/vulkan/implicit_layer.d/*.json; do
        case "${layer##*/}" in *[Ll][Ss][Ff][Gg][Vv][Kk]*) [ -f "$layer" ] && found=1 ;; esac
    done
    if [ -z "$found" ]; then
        echo "lsfg-vk 2.x is not installed; starting without frame generation" >&2
        return 0
    fi
    if [ -z "$dll" ]; then
        for root in "${XDG_DATA_HOME:-$HOME/.local/share}/Steam" "$HOME/.steam/steam" \
                "$HOME/.var/app/com.valvesoftware.Steam/.local/share/Steam"; do
            [ -f "$root/steamapps/libraryfolders.vdf" ] || continue
            while read -r lib; do
                d="$lib/steamapps/common/Lossless Scaling/lsfg-vk.dll"
                if [ -f "$d" ]; then dll="$d"; break 2; fi
            done < <(sed -nE 's/^[[:space:]]*"path"[[:space:]]*"(.*)"[[:space:]]*$/\1/p' "$root/steamapps/libraryfolders.vdf")
        done
    fi
    if [ -z "$dll" ] || [ ! -f "$dll" ]; then
        echo "lsfg-vk.dll not found: switch Lossless Scaling to the \"lsfg-vk\" beta branch in Steam" \
             "(or set LSFGVK_DLL_PATH); starting without frame generation" >&2
        return 0
    fi
    export LSFGVK_ENV=1 LSFGVK_DLL_PATH="$dll" LSFGVK_MULTIPLIER="$mult"
    export LSFGVK_FLOW_SCALE="${ASTROHYBRID_LSFG_FLOW:-${LSFGVK_FLOW_SCALE:-0.5}}"
    [ "${ASTROHYBRID_LSFG_PERF:-1}" = "0" ] || export LSFGVK_PERFORMANCE_MODE=1
    echo "Frame generation: ${mult}x (lsfg-vk, flow ${LSFGVK_FLOW_SCALE})"
}

case "${1:-}" in
    --setup)
        [ -d "${2:-}" ] || { echo "usage: $0 --setup <folder with eboot.bin, sce_module, sce_sys>" >&2; exit 1; }
        dump="$(cd "$2" && pwd)"
        cd "$ROOT"
        exec "$WINE" AstroHybrid.exe --setup "Z:${dump//\//\\}"
        ;;
    --launcher)
        [ "${ASTROHYBRID_DS_BT_FIX:-1}" = "0" ] || fix_dualsense_bt
        cd "$ROOT"
        exec "$WINE" AstroHybrid.exe
        ;;
    -h|--help)
        sed -n '2,/^set -euo/p' "${BASH_SOURCE[0]}" | sed -n 's/^# \{0,1\}//p'
        exit 0
        ;;
esac

if [ ! -f "$ROOT/Build/RunHybrid/AstroBot.exe" ]; then
    echo "Build/RunHybrid not found: run $0 --setup <dump folder> first" >&2
    exit 1
fi
[ "${ASTROHYBRID_DS_BT_FIX:-1}" = "0" ] || fix_dualsense_bt
[ -z "${ASTROHYBRID_LSFG:-}" ] || [ "$ASTROHYBRID_LSFG" = "0" ] || setup_lsfg "$ASTROHYBRID_LSFG"

# Real-time game speed, as LauncherService does: on when setup found the frame-rate block, with its address.
features="$ROOT/Build/RunHybrid/astrohybrid-game.json"
if [ -z "${ASTROHYBRID_GAME_SPEED:-}" ] && [ -f "$features" ]; then
    if grep -Eq '"RealTimeSpeed"[[:space:]]*:[[:space:]]*false' "$features"; then
        export ASTROHYBRID_GAME_SPEED=0
    else
        export ASTROHYBRID_GAME_SPEED=1
        step=$(sed -nE 's/.*"GameStepAddress"[[:space:]]*:[[:space:]]*([0-9]+).*/\1/p' "$features" | head -n1)
        if [ -n "$step" ] && [ "$step" != "0" ] && [ -z "${ASTROHYBRID_GAME_STEP_ADDR:-}" ]; then
            export ASTROHYBRID_GAME_STEP_ADDR="$(printf '0x%x' "$step")"
        fi
    fi
fi

# Start through Run-AstroHybrid.bat, as the launcher's Play button does, so Linux gets the same defaults
# as Windows (u59-preset.cmd engine preset, trigger state, motion, priorities, executable choice, logs).
# Variables set before running this script still take precedence.
# The DLL overrides keep Wine from replacing the shipped DLLs with builtin stubs.
export WINEDLLOVERRIDES="${WINEDLLOVERRIDES:-astrohybrid_host=n,b;kyty_gpu=n,b}"
cd "$ROOT"
exec "$WINE" cmd /c Run-AstroHybrid.bat "$@"
