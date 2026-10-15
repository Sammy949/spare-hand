# Captures a square region to a PNG. Usage: shot.ps1 <outPath> [halfSize] [centerX centerY]
# Center defaults to the cursor.
param([string]$Out, [int]$Half = 450, [Nullable[int]]$CX = $null, [Nullable[int]]$CY = $null)
Add-Type -AssemblyName System.Windows.Forms, System.Drawing
Add-Type @"
using System.Runtime.InteropServices;
public static class Dpi { [DllImport("user32.dll")] public static extern bool SetProcessDPIAware(); }
"@
[Dpi]::SetProcessDPIAware() | Out-Null
$p = [System.Windows.Forms.Cursor]::Position
if ($null -eq $CX) { $CX = $p.X; $CY = $p.Y }
$x = $CX - $Half; $y = $CY - $Half
$bmp = New-Object System.Drawing.Bitmap ($Half * 2), ($Half * 2)
$g = [System.Drawing.Graphics]::FromImage($bmp)
$g.CopyFromScreen($x, $y, 0, 0, $bmp.Size)
$bmp.Save($Out, [System.Drawing.Imaging.ImageFormat]::Png)
"cursor=$($p.X),$($p.Y) region=$x,$y"
