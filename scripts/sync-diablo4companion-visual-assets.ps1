param(
    [string]$RepositoryRoot = (Split-Path -Parent $PSScriptRoot),
    [switch]$VerifyOnly
)

$sourceRoot = Join-Path $RepositoryRoot '.tools\Diablo4Companion-audit\Diablo4Companion-master'
$destinationRoot = Join-Path $RepositoryRoot 'third_party\Diablo4Companion\visual-assets'
$manifestPath = Join-Path $destinationRoot 'VISUAL_ASSETS_MANIFEST.json'
$extensions = @('.png', '.jpg', '.jpeg', '.ico', '.xcf', '.bmp', '.gif', '.webp')

if (-not (Test-Path -LiteralPath $sourceRoot -PathType Container)) {
    throw "Audit snapshot was not found: $sourceRoot"
}

$sourceFiles = Get-ChildItem -LiteralPath $sourceRoot -Recurse -File |
    Where-Object { $extensions -contains $_.Extension.ToLowerInvariant() } |
    Sort-Object FullName

if ($sourceFiles.Count -ne 407) {
    throw "Expected 407 upstream visual assets, found $($sourceFiles.Count)."
}

$entries = [System.Collections.Generic.List[object]]::new()
foreach ($sourceFile in $sourceFiles) {
    $relativePath = $sourceFile.FullName.Substring($sourceRoot.Length).TrimStart('\')
    $destinationPath = Join-Path $destinationRoot $relativePath

    if (-not $VerifyOnly) {
        $destinationDirectory = Split-Path -Parent $destinationPath
        New-Item -ItemType Directory -Path $destinationDirectory -Force | Out-Null
        Copy-Item -LiteralPath $sourceFile.FullName -Destination $destinationPath -Force
    }

    if (-not (Test-Path -LiteralPath $destinationPath -PathType Leaf)) {
        throw "Imported asset is missing: $relativePath"
    }

    $sourceHash = (Get-FileHash -Algorithm SHA256 -LiteralPath $sourceFile.FullName).Hash
    $destinationHash = (Get-FileHash -Algorithm SHA256 -LiteralPath $destinationPath).Hash
    if ($sourceHash -ne $destinationHash) {
        throw "SHA-256 mismatch for $relativePath"
    }

    $entries.Add([ordered]@{
            source = ($relativePath -replace '\\', '/')
            destination = ('third_party/Diablo4Companion/visual-assets/' + ($relativePath -replace '\\', '/'))
            sizeBytes = $sourceFile.Length
            sha256 = $sourceHash
        })
}

$totalBytes = [int64]0
foreach ($entry in $entries) {
    $totalBytes += [int64]$entry['sizeBytes']
}

$manifest = [ordered]@{
    '$schema' = 'd4hub.third-party-visual-assets-manifest.v1'
    status = 'source-only-research-assets'
    releaseInclusion = 'excluded-from-build-and-publish'
    upstream = [ordered]@{
        repository = 'https://github.com/josdemmers/Diablo4Companion'
        gitTree = 'b0bfad89a7474676ad9b291c488603fd0a44e52c'
        license = 'MIT'
        retrievedForAudit = '2026-07-27'
    }
    assetCount = $entries.Count
    totalBytes = $totalBytes
    rightsNote = 'The MIT license covers the upstream project; underlying game-derived imagery may have separate rights and is not redistributed by the D4Hub runtime.'
    files = $entries
}

$json = $manifest | ConvertTo-Json -Depth 8
$expectedContent = $json + [Environment]::NewLine
$utf8 = New-Object System.Text.UTF8Encoding($false)
if ($VerifyOnly) {
    if (-not (Test-Path -LiteralPath $manifestPath -PathType Leaf)) {
        throw "Visual asset manifest is missing: $manifestPath"
    }

    $actualContent = [System.IO.File]::ReadAllText($manifestPath, $utf8)
    if ($actualContent -ne $expectedContent) {
        throw 'Visual asset manifest does not match the imported files.'
    }
}
else {
    New-Item -ItemType Directory -Path $destinationRoot -Force | Out-Null
    [System.IO.File]::WriteAllText($manifestPath, $expectedContent, $utf8)
}

Write-Output "Visual assets: $($entries.Count)"
Write-Output "Total bytes: $($manifest.totalBytes)"
Write-Output "Manifest: $manifestPath"
if ($VerifyOnly) {
    Write-Output 'Mode: verify-only'
}
