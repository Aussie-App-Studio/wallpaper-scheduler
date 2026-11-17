param([string]$OutDir = "$PSScriptRoot\Assets")
Add-Type -AssemblyName System.Drawing
New-Item -ItemType Directory -Force -Path $OutDir | Out-Null

function Draw-AppIconBitmap {
    param([int]$Width, [int]$Height)
    $bmp = New-Object System.Drawing.Bitmap $Width, $Height
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $g.Clear([System.Drawing.Color]::Transparent)

    $s = [Math]::Min($Width, $Height)
    $pad = [int]($s * 0.08)
    $cx = [int]($Width / 2)
    $cy = [int]($Height / 2)
    $d = $s - 2 * $pad
    $x = $cx - [int]($d / 2)
    $y = $cy - [int]($d / 2)

    $bgBrush = New-Object System.Drawing.Drawing2D.LinearGradientBrush(
        (New-Object System.Drawing.Point $x, $y),
        (New-Object System.Drawing.Point ($x + $d), ($y + $d)),
        [System.Drawing.Color]::FromArgb(255, 0, 120, 212),
        [System.Drawing.Color]::FromArgb(255, 32, 32, 64))
    $g.FillEllipse($bgBrush, $x, $y, $d, $d)

    $sunR = [int]($d * 0.44)
    $sx = $cx - [int]($sunR / 2)
    $sy = $cy - [int]($sunR / 2)
    $sunBrush = New-Object System.Drawing.SolidBrush([System.Drawing.Color]::FromArgb(255, 255, 200, 50))
    $g.FillPie($sunBrush, $sx, $sy, $sunR, $sunR, 90, 180)

    $moonBrush = New-Object System.Drawing.SolidBrush([System.Drawing.Color]::FromArgb(245, 235, 242, 255))
    $g.FillPie($moonBrush, $sx, $sy, $sunR, $sunR, 270, 180)

    $g.Dispose(); $bgBrush.Dispose(); $sunBrush.Dispose(); $moonBrush.Dispose()
    return $bmp
}

function New-AppIconPng {
    param([int]$Width, [int]$Height, [string]$Path)
    $bmp = Draw-AppIconBitmap $Width $Height
    $bmp.Save($Path, [System.Drawing.Imaging.ImageFormat]::Png)
    $bmp.Dispose()
}

function New-AppIconIco {
    param([string]$Path)
    $sizes = @(16, 32, 48, 256)
    $pngs = @()
    foreach ($sz in $sizes) {
        $bmp = Draw-AppIconBitmap $sz $sz
        $ms = New-Object System.IO.MemoryStream
        $bmp.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
        $bmp.Dispose()
        $pngs += ,($ms.ToArray())
        $ms.Dispose()
    }
    $fs = [System.IO.File]::Create($Path)
    $bw = New-Object System.IO.BinaryWriter($fs)
    $bw.Write([uint16]0); $bw.Write([uint16]1); $bw.Write([uint16]$sizes.Count)
    $offset = 6 + 16 * $sizes.Count
    for ($i = 0; $i -lt $sizes.Count; $i++) {
        $sz = $sizes[$i]
        $b = if ($sz -ge 256) { [byte]0 } else { [byte]$sz }
        $bw.Write($b); $bw.Write($b); $bw.Write([byte]0); $bw.Write([byte]0)
        $bw.Write([uint16]1); $bw.Write([uint16]32)
        $bw.Write([uint32]$pngs[$i].Length)
        $bw.Write([uint32]$offset)
        $offset += $pngs[$i].Length
    }
    foreach ($p in $pngs) { $bw.Write($p) }
    $bw.Dispose(); $fs.Dispose()
}

New-AppIconPng 44 44   "$OutDir\Square44x44Logo.png"
New-AppIconPng 150 150 "$OutDir\Square150x150Logo.png"
New-AppIconPng 310 150 "$OutDir\Wide310x150Logo.png"
New-AppIconPng 50 50   "$OutDir\StoreLogo.png"
New-AppIconIco "$OutDir\AppIcon.ico"
Write-Host "Generated MSIX icon assets and AppIcon.ico in $OutDir"
