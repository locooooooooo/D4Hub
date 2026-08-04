[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [ValidatePattern('^\d+\.\d+\.\d+$')]
    [string]$Version,

    [string]$UpdateFeedUrl = '',

    [string]$ReleaseDirectory = '',

    [switch]$SkipVerify
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$stagingRoot = Join-Path $root 'publish\release-staging'
$publishDirectory = Join-Path $stagingRoot ([Guid]::NewGuid().ToString('N'))
if ([string]::IsNullOrWhiteSpace($ReleaseDirectory)) {
    $ReleaseDirectory = Join-Path $root 'publish\releases'
}
elseif (-not [IO.Path]::IsPathRooted($ReleaseDirectory)) {
    $ReleaseDirectory = Join-Path $root $ReleaseDirectory
}
$ReleaseDirectory = [IO.Path]::GetFullPath($ReleaseDirectory)
$toolDirectory = Join-Path $root '.tools\velopack'
$vpk = Join-Path $toolDirectory 'vpk.exe'
$releaseNotes = Join-Path $root "docs\release-notes\$Version.md"
$appIcon = Join-Path $root 'src\D4Hub.App\Assets\AppIcon.ico'

if (-not (Test-Path -LiteralPath $releaseNotes -PathType Leaf)) {
    throw "Release notes are required: $releaseNotes"
}

if (-not (Test-Path -LiteralPath $appIcon -PathType Leaf)) {
    throw "Application icon is required: $appIcon"
}

if (-not [string]::IsNullOrWhiteSpace($UpdateFeedUrl)) {
    $updateFeedUri = $null
    if (-not [Uri]::TryCreate($UpdateFeedUrl, [UriKind]::Absolute, [ref]$updateFeedUri) `
        -or $updateFeedUri.Scheme -ne [Uri]::UriSchemeHttps) {
        throw 'UpdateFeedUrl must be an absolute HTTPS URL.'
    }
}

if (-not $SkipVerify) {
    & powershell -NoProfile -ExecutionPolicy Bypass -File (Join-Path $PSScriptRoot 'verify.ps1')
    if ($LASTEXITCODE -ne 0) {
        throw 'Repository verification failed.'
    }
}

if (-not (Test-Path -LiteralPath $vpk -PathType Leaf)) {
    New-Item -ItemType Directory -Force -Path $toolDirectory | Out-Null
    & dotnet tool install vpk --tool-path $toolDirectory --version 1.2.0
    if ($LASTEXITCODE -ne 0) {
        throw 'Unable to install the pinned Velopack CLI.'
    }
}

New-Item -ItemType Directory -Force -Path $stagingRoot, $publishDirectory, $ReleaseDirectory | Out-Null

try {
    $publishArguments = @(
        'publish',
        (Join-Path $root 'src\D4Hub.App\D4Hub.App.csproj'),
        '--configuration', 'Release',
        '--runtime', 'win-x64',
        '--self-contained', 'false',
        '--output', $publishDirectory,
        "-p:Version=$Version",
        '--nologo'
    )
    if (-not [string]::IsNullOrWhiteSpace($UpdateFeedUrl)) {
        $publishArguments += "-p:UpdateFeedUrl=$($updateFeedUri.AbsoluteUri.TrimEnd('/'))"
    }

    & dotnet @publishArguments
    if ($LASTEXITCODE -ne 0) {
        throw 'D4Hub publish failed.'
    }

    $startupSmoke = Start-Process `
        -FilePath (Join-Path $publishDirectory 'D4Hub.exe') `
        -ArgumentList '--verify-xaml-startup' `
        -WorkingDirectory $publishDirectory `
        -WindowStyle Hidden `
        -Wait `
        -PassThru
    if ($startupSmoke.ExitCode -ne 0) {
        throw "D4Hub XAML startup smoke failed with exit code $($startupSmoke.ExitCode)."
    }
    Write-Host 'PASS D4Hub XAML startup smoke'

    & $vpk pack `
        --packId D4Hub `
        --packVersion $Version `
        --packDir $publishDirectory `
        --outputDir $ReleaseDirectory `
        --mainExe D4Hub.exe `
        --packTitle D4Hub `
        --packAuthors loco `
        --icon $appIcon `
        --releaseNotes $releaseNotes `
        --framework net8-x64-desktop `
        --shortcuts StartMenuRoot,Desktop `
        --yes
    if ($LASTEXITCODE -ne 0) {
        throw 'Velopack packaging failed.'
    }

    $feedPath = Join-Path $ReleaseDirectory 'releases.win.json'
    $feed = Get-Content -LiteralPath $feedPath -Encoding UTF8 -Raw | ConvertFrom-Json
    $matchingAssets = @($feed.Assets | Where-Object {
        $_.PackageId -eq 'D4Hub' -and $_.Version -eq $Version -and $_.Type -eq 'Full'
    })
    if ($matchingAssets.Count -ne 1) {
        throw "Generated feed must contain exactly one D4Hub $Version full package."
    }

    $asset = $matchingAssets[0]
    $packagePath = Join-Path $ReleaseDirectory $asset.FileName
    if (-not (Test-Path -LiteralPath $packagePath -PathType Leaf)) {
        throw "Generated feed references a missing package: $($asset.FileName)"
    }

    $package = Get-Item -LiteralPath $packagePath
    if ($package.Length -ne [long]$asset.Size) {
        throw "Generated feed size does not match $($asset.FileName)."
    }

    $packageHash = (Get-FileHash -LiteralPath $packagePath -Algorithm SHA256).Hash
    if (-not [string]::Equals($packageHash, [string]$asset.SHA256, [StringComparison]::OrdinalIgnoreCase)) {
        throw "Generated feed SHA-256 does not match $($asset.FileName)."
    }

    Write-Host "PASS release feed $Version package size and SHA-256"

    Get-ChildItem -LiteralPath $ReleaseDirectory -File |
        Sort-Object Name |
        Select-Object Name, Length, LastWriteTime
}
finally {
    if (Test-Path -LiteralPath $publishDirectory) {
        try {
            Remove-Item -LiteralPath $publishDirectory -Recurse -Force
        }
        catch {
            Write-Warning "Unable to clean release staging directory: $publishDirectory"
        }
    }
}
