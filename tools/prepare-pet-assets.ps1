param(
    [string]$SourceDirectory = "$env:USERPROFILE\Desktop",
    [string]$OutputDirectory = "$PSScriptRoot\..\versions\csharp-wpf\assets\pets\deepseek"
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing

$states = @(
    'idle', 'loading', 'success', 'low', 'inactive',
    'codex-working', 'codex-done', 'error', 'clicked'
)

New-Item -ItemType Directory -Force -Path $OutputDirectory | Out-Null

function Test-CheckerPixel([System.Drawing.Color]$pixel) {
    $minimum = [Math]::Min($pixel.R, [Math]::Min($pixel.G, $pixel.B))
    $maximum = [Math]::Max($pixel.R, [Math]::Max($pixel.G, $pixel.B))
    return $minimum -ge 240 -and ($maximum - $minimum) -le 14
}

function Test-SoftCheckerPixel([System.Drawing.Color]$pixel) {
    $minimum = [Math]::Min($pixel.R, [Math]::Min($pixel.G, $pixel.B))
    $maximum = [Math]::Max($pixel.R, [Math]::Max($pixel.G, $pixel.B))
    return $minimum -ge 226 -and ($maximum - $minimum) -le 22
}

foreach ($state in $states) {
    $sourcePath = Join-Path $SourceDirectory "$state.png"
    if (-not (Test-Path -LiteralPath $sourcePath)) {
        throw "Missing source image: $sourcePath"
    }

    $source = [System.Drawing.Bitmap]::new($sourcePath)
    try {
        if ($source.Width -ne $source.Height) {
            throw "Image must be square: $sourcePath"
        }

        $bitmap = [System.Drawing.Bitmap]::new($source.Width, $source.Height, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
        try {
            $graphics = [System.Drawing.Graphics]::FromImage($bitmap)
            try { $graphics.DrawImageUnscaled($source, 0, 0) } finally { $graphics.Dispose() }

            $width = $bitmap.Width
            $height = $bitmap.Height
            $visited = [bool[]]::new($width * $height)
            $queue = [System.Collections.Generic.Queue[int]]::new()

            function Add-BackgroundSeed([int]$x, [int]$y) {
                $index = $y * $width + $x
                if ($visited[$index]) { return }
                if (-not (Test-CheckerPixel $bitmap.GetPixel($x, $y))) { return }
                $visited[$index] = $true
                $queue.Enqueue($index)
            }

            for ($x = 0; $x -lt $width; $x++) {
                Add-BackgroundSeed $x 0
                Add-BackgroundSeed $x ($height - 1)
            }
            for ($y = 1; $y -lt ($height - 1); $y++) {
                Add-BackgroundSeed 0 $y
                Add-BackgroundSeed ($width - 1) $y
            }

            $neighborOffsets = @(
                @(-1, -1), @(0, -1), @(1, -1),
                @(-1, 0),            @(1, 0),
                @(-1, 1),  @(0, 1),  @(1, 1)
            )
            while ($queue.Count -gt 0) {
                $index = [int]$queue.Dequeue()
                $x = [int]($index % $width)
                $y = [int][Math]::Floor($index / $width)
                if ($x -lt 0 -or $y -lt 0 -or $x -ge $width -or $y -ge $height) { continue }
                $bitmap.SetPixel($x, $y, [System.Drawing.Color]::Transparent)
                foreach ($offset in $neighborOffsets) {
                    $nx = $x + $offset[0]
                    $ny = $y + $offset[1]
                    if ($nx -lt 0 -or $ny -lt 0 -or $nx -ge $width -or $ny -ge $height) { continue }
                    $neighborIndex = $ny * $width + $nx
                    if ($visited[$neighborIndex]) { continue }
                    if (Test-CheckerPixel $bitmap.GetPixel($nx, $ny)) {
                        $visited[$neighborIndex] = $true
                        $queue.Enqueue($neighborIndex)
                    }
                }
            }

            # Remove a narrow neutral fringe left by anti-aliased checkerboard edges.
            for ($y = 1; $y -lt ($height - 1); $y++) {
                for ($x = 1; $x -lt ($width - 1); $x++) {
                    $pixel = $bitmap.GetPixel($x, $y)
                    if (-not (Test-SoftCheckerPixel $pixel) -or $pixel.A -eq 0) { continue }
                    $nearTransparent = $false
                    foreach ($offset in $neighborOffsets) {
                        $neighbor = $bitmap.GetPixel($x + $offset[0], $y + $offset[1])
                        if ($neighbor.A -eq 0) { $nearTransparent = $true; break }
                    }
                    if ($nearTransparent) { $bitmap.SetPixel($x, $y, [System.Drawing.Color]::Transparent) }
                }
            }

            # Fully transparent pixels must not retain hidden checkerboard RGB.
            for ($y = 0; $y -lt $height; $y++) {
                for ($x = 0; $x -lt $width; $x++) {
                    if ($bitmap.GetPixel($x, $y).A -eq 0) { $bitmap.SetPixel($x, $y, [System.Drawing.Color]::Transparent) }
                }
            }

            $outputPath = Join-Path $OutputDirectory "$state.png"
            $bitmap.Save($outputPath, [System.Drawing.Imaging.ImageFormat]::Png)
            Write-Output "Prepared $outputPath"
        } finally { $bitmap.Dispose() }
    } finally { $source.Dispose() }
}
