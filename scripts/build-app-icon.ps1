[CmdletBinding()]
param(
    [string]$OutputPath = '',

    [string]$PreviewPath = ''
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
if ([string]::IsNullOrWhiteSpace($OutputPath)) {
    $OutputPath = Join-Path $root 'src\D4Hub.App\Assets\AppIcon.ico'
}
elseif (-not [IO.Path]::IsPathRooted($OutputPath)) {
    $OutputPath = Join-Path $root $OutputPath
}

if (-not [string]::IsNullOrWhiteSpace($PreviewPath) -and -not [IO.Path]::IsPathRooted($PreviewPath)) {
    $PreviewPath = Join-Path $root $PreviewPath
}

Add-Type -AssemblyName System.Drawing

function New-AppIconBitmap {
    param([int]$Size)

    $bitmap = New-Object System.Drawing.Bitmap(
        $Size,
        $Size,
        [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $graphics = [System.Drawing.Graphics]::FromImage($bitmap)
    try {
        $graphics.Clear([System.Drawing.Color]::Transparent)
        $graphics.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
        $graphics.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
        $graphics.CompositingQuality = [System.Drawing.Drawing2D.CompositingQuality]::HighQuality

        $scale = $Size / 256.0
        $circleBounds = New-Object System.Drawing.RectangleF(
            [single](12 * $scale),
            [single](12 * $scale),
            [single](232 * $scale),
            [single](232 * $scale))
        $surface = New-Object System.Drawing.Drawing2D.LinearGradientBrush(
            $circleBounds,
            [System.Drawing.ColorTranslator]::FromHtml('#2a2722'),
            [System.Drawing.ColorTranslator]::FromHtml('#161512'),
            90.0)
        $accent = New-Object System.Drawing.Pen(
            [System.Drawing.ColorTranslator]::FromHtml('#d45945'),
            [single]([Math]::Max(1.0, 11 * $scale)))
        try {
            $graphics.FillEllipse($surface, $circleBounds)
            $graphics.DrawEllipse($accent, $circleBounds)
        }
        finally {
            $surface.Dispose()
            $accent.Dispose()
        }

        $outerPoints = [System.Drawing.PointF[]]@(
            (New-Object System.Drawing.PointF([single](128 * $scale), [single](32 * $scale))),
            (New-Object System.Drawing.PointF([single](224 * $scale), [single](128 * $scale))),
            (New-Object System.Drawing.PointF([single](128 * $scale), [single](224 * $scale))),
            (New-Object System.Drawing.PointF([single](32 * $scale), [single](128 * $scale))),
            (New-Object System.Drawing.PointF([single](128 * $scale), [single](32 * $scale)))
        )
        $outerPen = New-Object System.Drawing.Pen(
            [System.Drawing.ColorTranslator]::FromHtml('#d45945'),
            [single]([Math]::Max(1.0, 14 * $scale)))
        $outerPen.LineJoin = [System.Drawing.Drawing2D.LineJoin]::Round
        try {
            $graphics.DrawLines($outerPen, $outerPoints)
        }
        finally {
            $outerPen.Dispose()
        }

        if ($Size -ge 24) {
            $innerPoints = [System.Drawing.PointF[]]@(
                (New-Object System.Drawing.PointF([single](128 * $scale), [single](79 * $scale))),
                (New-Object System.Drawing.PointF([single](177 * $scale), [single](128 * $scale))),
                (New-Object System.Drawing.PointF([single](128 * $scale), [single](177 * $scale))),
                (New-Object System.Drawing.PointF([single](79 * $scale), [single](128 * $scale))),
                (New-Object System.Drawing.PointF([single](128 * $scale), [single](79 * $scale)))
            )
            $innerPen = New-Object System.Drawing.Pen(
                [System.Drawing.ColorTranslator]::FromHtml('#c6baa5'),
                [single]([Math]::Max(1.0, 8 * $scale)))
            $innerPen.LineJoin = [System.Drawing.Drawing2D.LineJoin]::Round
            try {
                $graphics.DrawLines($innerPen, $innerPoints)
            }
            finally {
                $innerPen.Dispose()
            }
        }

        if ($Size -ge 32) {
            $spokePen = New-Object System.Drawing.Pen(
                [System.Drawing.ColorTranslator]::FromHtml('#867c6c'),
                [single]([Math]::Max(1.0, 8 * $scale)))
            $spokePen.StartCap = [System.Drawing.Drawing2D.LineCap]::Round
            $spokePen.EndCap = [System.Drawing.Drawing2D.LineCap]::Round
            try {
                $graphics.DrawLine($spokePen, [single](128 * $scale), [single](32 * $scale), [single](128 * $scale), [single](79 * $scale))
                $graphics.DrawLine($spokePen, [single](224 * $scale), [single](128 * $scale), [single](177 * $scale), [single](128 * $scale))
                $graphics.DrawLine($spokePen, [single](128 * $scale), [single](224 * $scale), [single](128 * $scale), [single](177 * $scale))
                $graphics.DrawLine($spokePen, [single](32 * $scale), [single](128 * $scale), [single](79 * $scale), [single](128 * $scale))
            }
            finally {
                $spokePen.Dispose()
            }
        }

        $centerBrush = New-Object System.Drawing.SolidBrush(
            [System.Drawing.ColorTranslator]::FromHtml('#77bba7'))
        try {
            $centerRadius = if ($Size -lt 24) { 26 } else { 22 }
            $centerBounds = New-Object System.Drawing.RectangleF(
                [single]((128 - $centerRadius) * $scale),
                [single]((128 - $centerRadius) * $scale),
                [single](2 * $centerRadius * $scale),
                [single](2 * $centerRadius * $scale))
            $graphics.FillEllipse($centerBrush, $centerBounds)
        }
        finally {
            $centerBrush.Dispose()
        }

        return $bitmap
    }
    finally {
        $graphics.Dispose()
    }
}

$frames = @()
foreach ($size in @(16, 20, 24, 32, 40, 48, 64, 128, 256)) {
    $bitmap = New-AppIconBitmap -Size $size
    $stream = New-Object IO.MemoryStream
    try {
        $bitmap.Save($stream, [System.Drawing.Imaging.ImageFormat]::Png)
        $frames += [pscustomobject]@{
            Size = $size
            Bytes = $stream.ToArray()
        }
    }
    finally {
        $stream.Dispose()
        $bitmap.Dispose()
    }
}

$outputDirectory = Split-Path -Parent ([IO.Path]::GetFullPath($OutputPath))
New-Item -ItemType Directory -Force -Path $outputDirectory | Out-Null
$file = [IO.File]::Open($OutputPath, [IO.FileMode]::Create, [IO.FileAccess]::Write)
$writer = New-Object IO.BinaryWriter($file)
try {
    $writer.Write([uint16]0)
    $writer.Write([uint16]1)
    $writer.Write([uint16]$frames.Count)

    $offset = 6 + (16 * $frames.Count)
    foreach ($frame in $frames) {
        $dimension = if ($frame.Size -eq 256) { 0 } else { $frame.Size }
        $writer.Write([byte]$dimension)
        $writer.Write([byte]$dimension)
        $writer.Write([byte]0)
        $writer.Write([byte]0)
        $writer.Write([uint16]1)
        $writer.Write([uint16]32)
        $writer.Write([uint32]$frame.Bytes.Length)
        $writer.Write([uint32]$offset)
        $offset += $frame.Bytes.Length
    }

    foreach ($frame in $frames) {
        $writer.Write([byte[]]$frame.Bytes)
    }
}
finally {
    $writer.Dispose()
    $file.Dispose()
}

if (-not [string]::IsNullOrWhiteSpace($PreviewPath)) {
    $previewDirectory = Split-Path -Parent ([IO.Path]::GetFullPath($PreviewPath))
    New-Item -ItemType Directory -Force -Path $previewDirectory | Out-Null
    $preview = New-AppIconBitmap -Size 512
    try {
        $preview.Save($PreviewPath, [System.Drawing.Imaging.ImageFormat]::Png)
    }
    finally {
        $preview.Dispose()
    }
}

Write-Host "PASS app icon: $OutputPath ($($frames.Count) sizes)"
