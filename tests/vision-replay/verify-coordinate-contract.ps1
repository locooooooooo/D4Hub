[CmdletBinding()]
param()

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$repositoryRoot = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$harness = Join-Path $repositoryRoot "scripts\verify-vision-replay.ps1"
$temporaryRoot = Join-Path ([System.IO.Path]::GetTempPath()) ("d4hub-v10-e02-" + [Guid]::NewGuid().ToString("N"))

function Assert-True {
    param(
        [Parameter(Mandatory = $true)][bool]$Condition,
        [Parameter(Mandatory = $true)][string]$Message
    )

    if (-not $Condition)
    {
        throw $Message
    }
}

function New-ReplayCase {
    param(
        [Parameter(Mandatory = $true)][string]$Id,
        [Parameter(Mandatory = $true)][int]$Width,
        [Parameter(Mandatory = $true)][int]$Height,
        [AllowNull()][string]$CaptureSpace,
        [switch]$OmitCaptureSpace
    )

    $case = [ordered]@{
        id = $Id
        image = "$Id-must-not-be-read.png"
        expectedWidth = $Width
        expectedHeight = $Height
        panelThreshold = 0.55
        expectedDecision = "accepted"
    }

    if (-not $OmitCaptureSpace)
    {
        $case.captureSpace = $CaptureSpace
    }

    return $case
}

function Write-TestManifest {
    param(
        [Parameter(Mandatory = $true)][string]$Name,
        [Parameter(Mandatory = $true)][string]$EvidenceClass,
        [Parameter(Mandatory = $true)][object[]]$Cases
    )

    $path = Join-Path $temporaryRoot "$Name.json"
    $document = [ordered]@{
        schemaVersion = 1
        evidenceClass = $EvidenceClass
        cases = $Cases
    }
    $document | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $path -Encoding UTF8
    return $path
}

function Invoke-HarnessValidation {
    param(
        [Parameter(Mandatory = $true)][string]$Manifest,
        [string]$OutputPath,
        [switch]$UseSchemaOnlyAlias,
        [switch]$RunReplay
    )

    $arguments = @("-NoProfile", "-ExecutionPolicy", "Bypass", "-File", $harness, "-Manifest", $Manifest)
    if (-not $RunReplay)
    {
        if ($UseSchemaOnlyAlias)
        {
            $arguments += "-SchemaOnly"
        }
        else
        {
            $arguments += "-ValidationOnly"
        }
    }

    if (-not [string]::IsNullOrWhiteSpace($OutputPath))
    {
        $arguments += @("-Output", $OutputPath)
    }

    $previousErrorActionPreference = $ErrorActionPreference
    $ErrorActionPreference = "Continue"
    try
    {
        $lines = @(& powershell.exe @arguments 2>&1)
        $exitCode = $LASTEXITCODE
    }
    finally
    {
        $ErrorActionPreference = $previousErrorActionPreference
    }

    return [pscustomobject][ordered]@{
        ExitCode = $exitCode
        Text = ($lines | Out-String).Trim()
    }
}

try
{
    [void](New-Item -ItemType Directory -Path $temporaryRoot)

    $exactTarget = Write-TestManifest "target-exact-client" "target" @(
        (New-ReplayCase "target-1920x1080" 1920 1080 "client-device-pixels"),
        (New-ReplayCase "target-1280x960" 1280 960 "client-device-pixels")
    )
    $forbiddenReport = Join-Path $temporaryRoot "validation-must-not-write-report.json"
    $exactResult = Invoke-HarnessValidation $exactTarget $forbiddenReport
    Assert-True ($exactResult.ExitCode -eq 0) "Exact client target schema validation returned $($exactResult.ExitCode): $($exactResult.Text)"
    $exactDocument = $exactResult.Text | ConvertFrom-Json
    Assert-True ($exactDocument.Mode -ceq "schema-only") "Validation-only result was not labeled schema-only."
    Assert-True ($exactDocument.ReplayEvidenceGenerated -eq $false) "Validation-only result claimed replay evidence."
    Assert-True ($exactDocument.EvidenceStatus -ceq "not-replay-evidence") "Validation-only result did not disclaim replay evidence."
    Assert-True ($exactDocument.Summary.RequiredResolutionRepresentativesPresent -eq $true) "Exact target representatives were not reported as present."
    Assert-True ($null -eq $exactDocument.Summary.PSObject.Properties["TargetResolutionGateSatisfied"]) "Validation-only output exposed the misleading TargetResolutionGateSatisfied field."
    Assert-True (-not (Test-Path -LiteralPath $forbiddenReport)) "Validation-only execution created a replay report."

    foreach ($invalidSpace in @(
        [pscustomobject]@{ Name = "missing"; Value = $null; Omit = $true },
        [pscustomobject]@{ Name = "unknown"; Value = "unknown"; Omit = $false },
        [pscustomobject]@{ Name = "full-window"; Value = "full-window"; Omit = $false }
    ))
    {
        $invalidTarget = Write-TestManifest "target-$($invalidSpace.Name)" "target" @(
            (New-ReplayCase "invalid-$($invalidSpace.Name)-1920" 1920 1080 $invalidSpace.Value -OmitCaptureSpace:$invalidSpace.Omit),
            (New-ReplayCase "valid-$($invalidSpace.Name)-1280" 1280 960 "client-device-pixels")
        )
        $invalidReport = Join-Path $temporaryRoot "target-$($invalidSpace.Name)-must-not-write-report.json"
        $invalidResult = Invoke-HarnessValidation $invalidTarget $invalidReport -RunReplay
        Assert-True ($invalidResult.ExitCode -eq 2) "Target captureSpace '$($invalidSpace.Name)' returned $($invalidResult.ExitCode), expected 2."
        Assert-True ($invalidResult.Text -match "must declare captureSpace") "Target captureSpace '$($invalidSpace.Name)' did not report the contract error."
        Assert-True (-not (Test-Path -LiteralPath $invalidReport)) "Rejected target captureSpace '$($invalidSpace.Name)' created a replay report."
    }

    $nearTarget = Write-TestManifest "target-near-size" "target" @(
        (New-ReplayCase "near-1922x1112" 1922 1112 "client-device-pixels"),
        (New-ReplayCase "near-1282x992" 1282 992 "client-device-pixels")
    )
    $nearResult = Invoke-HarnessValidation $nearTarget
    Assert-True ($nearResult.ExitCode -eq 2) "Near-size target returned $($nearResult.ExitCode), expected 2."
    Assert-True ($nearResult.Text -match "both 1920x1080 and 1280x960") "Near-size target bypassed the exact-resolution gate."

    $smokeManifest = Write-TestManifest "smoke-non-client" "smoke" @(
        (New-ReplayCase "smoke-unknown" 1362 800 "unknown"),
        (New-ReplayCase "smoke-full-window" 1922 1112 "full-window")
    )
    $smokeResult = Invoke-HarnessValidation $smokeManifest -UseSchemaOnlyAlias
    Assert-True ($smokeResult.ExitCode -eq 0) "Explicit non-client smoke schema validation returned $($smokeResult.ExitCode): $($smokeResult.Text)"
    $smokeDocument = $smokeResult.Text | ConvertFrom-Json
    Assert-True ($smokeDocument.EvidenceClass -ceq "smoke") "Smoke validation changed the evidence class."
    Assert-True ($smokeDocument.ReplayEvidenceGenerated -eq $false) "Smoke schema validation claimed replay evidence."

    $runtimeMissingImage = Invoke-HarnessValidation $smokeManifest -RunReplay
    Assert-True ($runtimeMissingImage.ExitCode -eq 2) "Replay mode did not reject the missing image before probe startup."
    Assert-True ($runtimeMissingImage.Text -match "image not found") "Replay mode missing-image rejection was not reported as a manifest error."

    Write-Host "PASS vision replay client-surface coordinate contract"
}
finally
{
    if (Test-Path -LiteralPath $temporaryRoot)
    {
        Remove-Item -LiteralPath $temporaryRoot -Recurse -Force
    }
}
