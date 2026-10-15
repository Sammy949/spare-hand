#!/bin/bash
# WSL: launch Spare Hand in self-test mode (tray opens at a fixed point, no drag) and screenshot it.
# Usage: selftest.sh <dark|light> <out.png>
source "$(dirname "${BASH_SOURCE[0]}")/wsl-env.sh"
taskkill.exe /IM SpareHand.exe /F >/dev/null 2>&1; sleep 1
MARK=$(date +%H:%M:%S)
cd /mnt/c && powershell.exe -NoProfile -Command "Start-Process '$BIN_WIN\\SpareHand.exe' -ArgumentList '--selftest','--theme','$1'"
sleep 7
L=$(latest_log)
read X Y W H < <(grep "tray shown" "$L" | tail -1 | grep -oP 'at \K-?\d+,-?\d+ \d+x\d+' | tr ',x' '  ')
powershell.exe -NoProfile -Command "Add-Type -AssemblyName System.Drawing; Add-Type 'using System.Runtime.InteropServices; public static class D{[DllImport(\"user32.dll\")] public static extern bool SetProcessDPIAware();}'; [D]::SetProcessDPIAware()|Out-Null; \$b=New-Object System.Drawing.Bitmap $((W+40)),$((H+40)); [System.Drawing.Graphics]::FromImage(\$b).CopyFromScreen($((X-20)),$((Y-20)),0,0,\$b.Size); \$b.Save('$(wslpath -w "$2")')"
awk -v m="$MARK" '$1 >= m' "$L" | tail -25
