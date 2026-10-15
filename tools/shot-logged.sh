#!/bin/bash
# WSL: launch with test args, then screenshot the rect the app logged on the line matching <pattern>.
# Usage: shot-logged.sh "<powershell argument list>" <pattern> <out.png>
source "$(dirname "${BASH_SOURCE[0]}")/wsl-env.sh"
taskkill.exe /IM SpareHand.exe /F >/dev/null 2>&1; sleep 1
MARK=$(date +%H:%M:%S)
cd /mnt/c && powershell.exe -NoProfile -Command "Start-Process '$BIN_WIN\\SpareHand.exe' -ArgumentList $1"
sleep 7
LINE=$(awk -v m="$MARK" '$1 >= m' "$(latest_log)" | grep "$2" | tail -1)
echo "$LINE"
read X Y W H < <(echo "$LINE" | grep -oP 'at \K-?\d+,-?\d+ \d+x\d+' | tr ',x' '  ')
powershell.exe -NoProfile -Command "Add-Type -AssemblyName System.Drawing; Add-Type 'using System.Runtime.InteropServices; public static class D{[DllImport(\"user32.dll\")] public static extern bool SetProcessDPIAware();}'; [D]::SetProcessDPIAware()|Out-Null; \$b=New-Object System.Drawing.Bitmap $W,$H; [System.Drawing.Graphics]::FromImage(\$b).CopyFromScreen($X,$Y,0,0,\$b.Size); \$b.Save('$(wslpath -w "$3")')"
