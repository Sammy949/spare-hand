#!/bin/bash
# WSL: smoke-test a build of Spare Hand without touching the mouse. Usage: smoke.sh [folder containing SpareHand.exe]
# Defaults to the Debug build. Prints PASS/FAIL per check, based on the app's own log and UI Automation.
source "$(dirname "${BASH_SOURCE[0]}")/wsl-env.sh"
DIR="${1:-$BIN}"; DIR_WIN="$(wslpath -w "$DIR")"; T="$ROOT/tools"
run() { taskkill.exe /IM SpareHand.exe /F >/dev/null 2>&1; sleep 1; MARK=$(date +%H:%M:%S); (cd /mnt/c && powershell.exe -NoProfile -Command "Start-Process '$DIR_WIN\\SpareHand.exe' -ArgumentList $1"); sleep "${2:-7}"; }
since() { awk -v m="$MARK" '$1 >= m' "$(latest_log)"; }
check() { if since | grep -qE "$2"; then echo "PASS  $1"; else echo "FAIL  $1   (no log line matching: $2)"; fi; }
nocrash() { if since | grep -qE "UNHANDLED|summon failed|unreadable"; then echo "FAIL  $1: $(since | grep -E 'UNHANDLED|summon failed|unreadable' | head -1 | cut -c1-160)"; else echo "PASS  $1"; fi; }

echo "== $DIR ($(du -sh "$DIR" | cut -f1))"
run "'--folders-test'" 9
check "starts, settings + hold load"   "tray prepared"
check "notification icon"              "notify icon added=True"
check "Quick Share detected (COM)"     "quick share available=True"
check "open Explorer folders (COM)"    "folders-test: [0-9]+ open folder"
nocrash "no errors at startup"
run "'--selftest','--theme','dark'" 8
check "tray opens"                     "tray shown"
nocrash "tray without errors"
run "'--library-test'" 8
check "library opens"                  "hold library open"
run "'--settings'" 8
toggles=$(cd /mnt/c && powershell.exe -NoProfile -ExecutionPolicy Bypass -File "$(wslpath -w "$T/uia-toggles.ps1")" -Title 'Spare Hand settings' | tr -d '\r' | grep -c -E '^(On|Off) ')
[ "$toggles" -ge 8 ] && echo "PASS  settings page renders ($toggles toggles)" || echo "FAIL  settings page ($toggles toggles found)"
sleep 12
check "LocalSend scan runs (TLS, JSON)" "localsend scan done"
nocrash "no errors overall"
taskkill.exe /IM SpareHand.exe /F >/dev/null 2>&1
