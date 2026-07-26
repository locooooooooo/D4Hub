param(
    [string]$SourceDirectory = (Join-Path $PSScriptRoot '..\src\D4Hub.App\Assets\Hud\Source'),
    [string]$OutputDirectory = (Join-Path $PSScriptRoot '..\src\D4Hub.App\Assets\Hud')
)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing

function Convert-HudMarker {
    param(
        [Parameter(Mandatory = $true)]
        [string]$SourceName,
        [Parameter(Mandatory = $true)]
        [string]$OutputName,
        [Parameter(Mandatory = $true)]
        [System.Drawing.Rectangle]$Crop,
        [Parameter(Mandatory = $true)]
        [ValidateSet('Masterworked', 'Tempered', 'Transfigured')]
        [string]$Kind
    )

    $sourcePath = Join-Path $SourceDirectory $SourceName
    $outputPath = Join-Path $OutputDirectory $OutputName
    $source = [System.Drawing.Bitmap]::FromFile($sourcePath)
    try {
        $output = New-Object System.Drawing.Bitmap $Crop.Width, $Crop.Height, ([System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
        try {
            for ($y = 0; $y -lt $Crop.Height; $y++) {
                for ($x = 0; $x -lt $Crop.Width; $x++) {
                    $color = $source.GetPixel($Crop.X + $x, $Crop.Y + $y)
                    $luminance = 0.299 * $color.R + 0.587 * $color.G + 0.114 * $color.B

                    switch ($Kind) {
                        'Masterworked' {
                            $brightness = [Math]::Max(0, ($luminance - 78) * 3.4)
                            $purple = [Math]::Max(0, ($color.B - $color.G - 6) * 5.5)
                            $pink = [Math]::Max(0, ($color.R - $color.G - 14) * 4.2)
                            $alpha = [Math]::Max($brightness, [Math]::Max($purple, $pink))
                        }
                        'Tempered' {
                            $alpha = [Math]::Max(0, ($luminance - 82) * 3.2)
                        }
                        'Transfigured' {
                            $alpha = [Math]::Max(0, ($luminance - 94) * 4.2)
                        }
                    }

                    $alpha = [Math]::Min(255, [Math]::Round($alpha))
                    if ($alpha -lt 18) {
                        $alpha = 0
                    }

                    $output.SetPixel($x, $y, [System.Drawing.Color]::FromArgb($alpha, $color.R, $color.G, $color.B))
                }
            }

            $output.Save($outputPath, [System.Drawing.Imaging.ImageFormat]::Png)
        }
        finally {
            $output.Dispose()
        }
    }
    finally {
        $source.Dispose()
    }
}

New-Item -ItemType Directory -Force -Path $OutputDirectory | Out-Null

Convert-HudMarker -SourceName 'marker-masterworked-source.png' -OutputName 'marker-masterworked.png' `
    -Crop ([System.Drawing.Rectangle]::new(8, 6, 21, 18)) -Kind Masterworked
Convert-HudMarker -SourceName 'marker-tempered-source.png' -OutputName 'marker-tempered.png' `
    -Crop ([System.Drawing.Rectangle]::new(3, 7, 17, 15)) -Kind Tempered
Convert-HudMarker -SourceName 'marker-transfigured-source.png' -OutputName 'marker-transfigured.png' `
    -Crop ([System.Drawing.Rectangle]::new(7, 7, 23, 23)) -Kind Transfigured
