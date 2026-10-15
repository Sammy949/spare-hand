#!/bin/bash
# Shared paths for the WSL dev scripts. Everything is derived at run time; nothing is hard-coded.
ROOT="$(cd -P "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
ROOT_WIN="$(wslpath -w "$ROOT")"
BIN="$ROOT/src/SpareHand/bin/x64/Debug/net10.0-windows10.0.26100.0/win-x64"
BIN_WIN="$(wslpath -w "$BIN" 2>/dev/null)"
LOCALAPPDATA_WIN="$(cd /mnt/c && cmd.exe /c 'echo %LOCALAPPDATA%' 2>/dev/null | tr -d '\r')"
LOGS="$(wslpath "$LOCALAPPDATA_WIN")/SpareHand/logs"
latest_log() { ls -t "$LOGS"/*.log 2>/dev/null | head -1; }
