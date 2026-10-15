#!/bin/bash
# WSL: stop any running Spare Hand (it locks the exe), then build on Windows. Prints errors and the result only.
source "$(dirname "${BASH_SOURCE[0]}")/wsl-env.sh"
taskkill.exe /IM SpareHand.exe /F >/dev/null 2>&1; sleep 1
cd /mnt/c && cmd.exe /c "cd /d $ROOT_WIN\\src\\SpareHand && dotnet build -c Debug -p:Platform=x64 2>&1" | tr -d '\r' \
  | grep -E " error |Build succeeded|FAILED" | sed -E 's/ \[[A-Z]:.*\]$//' | sort -u | head -30
