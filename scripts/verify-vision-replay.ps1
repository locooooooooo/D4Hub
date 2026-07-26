[CmdletBinding()]
param(
    [string]$Manifest,
    [string]$Output,
    [switch]$NoBuild,
    [Alias("SchemaOnly")]
    [switch]$ValidationOnly
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$manifestErrorExitCode = 2
$assertionFailureExitCode = 5
$operationalFailureExitCode = 6

function Test-HasProperty {
    param(
        [Parameter(Mandatory = $true)]$Value,
        [Parameter(Mandatory = $true)][string]$Name
    )

    return $null -ne $Value.PSObject.Properties[$Name]
}

function Test-IsJsonNumber {
    param($Value)

    return $Value -is [byte] -or
        $Value -is [sbyte] -or
        $Value -is [int16] -or
        $Value -is [uint16] -or
        $Value -is [int32] -or
        $Value -is [uint32] -or
        $Value -is [int64] -or
        $Value -is [uint64] -or
        $Value -is [single] -or
        $Value -is [double] -or
        $Value -is [decimal]
}

function Test-IsInteger {
    param($Value)

    if (-not (Test-IsJsonNumber $Value))
    {
        return $false
    }

    $number = [double]$Value
    return -not [double]::IsNaN($number) -and
        -not [double]::IsInfinity($number) -and
        [Math]::Floor($number) -eq $number
}

function Read-NormalizedRange {
    param(
        [Parameter(Mandatory = $true)]$Range,
        [Parameter(Mandatory = $true)][string]$Label
    )

    if (-not (Test-HasProperty $Range "min") -or -not (Test-HasProperty $Range "max"))
    {
        throw "$Label must contain numeric min and max properties."
    }

    if (-not (Test-IsJsonNumber $Range.min) -or -not (Test-IsJsonNumber $Range.max))
    {
        throw "$Label min and max must be numbers."
    }

    $minimum = [double]$Range.min
    $maximum = [double]$Range.max
    if ([double]::IsNaN($minimum) -or [double]::IsInfinity($minimum) -or
        [double]::IsNaN($maximum) -or [double]::IsInfinity($maximum) -or
        $minimum -lt 0 -or $maximum -gt 1 -or $minimum -gt $maximum)
    {
        throw "$Label must satisfy 0 <= min <= max <= 1."
    }

    return [pscustomobject][ordered]@{
        Min = $minimum
        Max = $maximum
    }
}

function ConvertTo-ProcessArgument {
    param([AllowEmptyString()][string]$Value)

    if ($Value.Length -gt 0 -and $Value -notmatch '[\s"]')
    {
        return $Value
    }

    $builder = New-Object System.Text.StringBuilder
    [void]$builder.Append('"')
    $backslashes = 0
    foreach ($character in $Value.ToCharArray())
    {
        if ($character -eq '\')
        {
            $backslashes++
            continue
        }

        if ($character -eq '"')
        {
            [void]$builder.Append(('\' * ($backslashes * 2 + 1)))
            [void]$builder.Append('"')
        }
        else
        {
            [void]$builder.Append(('\' * $backslashes))
            [void]$builder.Append($character)
        }

        $backslashes = 0
    }

    [void]$builder.Append(('\' * ($backslashes * 2)))
    [void]$builder.Append('"')
    return $builder.ToString()
}

function Invoke-CapturedProcess {
    param(
        [Parameter(Mandatory = $true)][string]$FileName,
        [Parameter(Mandatory = $true)][string[]]$Arguments,
        [Parameter(Mandatory = $true)][string]$WorkingDirectory
    )

    $startInfo = New-Object System.Diagnostics.ProcessStartInfo
    $startInfo.FileName = $FileName
    $startInfo.Arguments = (($Arguments | ForEach-Object { ConvertTo-ProcessArgument $_ }) -join ' ')
    $startInfo.WorkingDirectory = $WorkingDirectory
    $startInfo.UseShellExecute = $false
    $startInfo.CreateNoWindow = $true
    $startInfo.RedirectStandardOutput = $true
    $startInfo.RedirectStandardError = $true

    $process = New-Object System.Diagnostics.Process
    $process.StartInfo = $startInfo
    if (-not $process.Start())
    {
        throw "Failed to start process: $FileName"
    }

    $standardOutput = $process.StandardOutput.ReadToEndAsync()
    $standardError = $process.StandardError.ReadToEndAsync()
    $process.WaitForExit()

    return [pscustomobject][ordered]@{
        ExitCode = $process.ExitCode
        StandardOutput = $standardOutput.GetAwaiter().GetResult()
        StandardError = $standardError.GetAwaiter().GetResult()
    }
}

function Add-AssertionFailure {
    param(
        [Parameter(Mandatory = $true)][AllowEmptyCollection()][System.Collections.Generic.List[string]]$Failures,
        [Parameter(Mandatory = $true)][bool]$Condition,
        [Parameter(Mandatory = $true)][string]$Message
    )

    if (-not $Condition)
    {
        [void]$Failures.Add($Message)
    }
}

function Write-Report {
    param(
        [Parameter(Mandatory = $true)]$Report,
        [string]$OutputPath
    )

    $json = $Report | ConvertTo-Json -Depth 12
    if (-not [string]::IsNullOrWhiteSpace($OutputPath))
    {
        $fullOutputPath = [System.IO.Path]::GetFullPath($OutputPath)
        $outputDirectory = Split-Path -Parent $fullOutputPath
        if (-not (Test-Path -LiteralPath $outputDirectory -PathType Container))
        {
            [void](New-Item -ItemType Directory -Path $outputDirectory -Force)
        }

        Set-Content -LiteralPath $fullOutputPath -Value $json -Encoding UTF8
    }

    Write-Output $json
}

if ([string]::IsNullOrWhiteSpace($Manifest))
{
    [Console]::Error.WriteLine("Usage: verify-vision-replay.ps1 -Manifest <json> [-Output <json>] [-NoBuild] [-ValidationOnly]")
    exit $manifestErrorExitCode
}

try
{
    $manifestPath = [System.IO.Path]::GetFullPath($Manifest)
    if (-not (Test-Path -LiteralPath $manifestPath -PathType Leaf))
    {
        throw "Manifest not found: $manifestPath"
    }

    try
    {
        $manifestDocument = Get-Content -LiteralPath $manifestPath -Raw -Encoding UTF8 | ConvertFrom-Json
    }
    catch
    {
        throw "Manifest is not valid JSON: $($_.Exception.Message)"
    }

    if (-not (Test-HasProperty $manifestDocument "schemaVersion") -or
        -not (Test-IsInteger $manifestDocument.schemaVersion) -or
        [int]$manifestDocument.schemaVersion -ne 1)
    {
        throw "schemaVersion must be the integer 1."
    }

    if (-not (Test-HasProperty $manifestDocument "evidenceClass") -or
        -not ($manifestDocument.evidenceClass -is [string]) -or
        $manifestDocument.evidenceClass -cnotin @("smoke", "target"))
    {
        throw "evidenceClass must be exactly 'smoke' or 'target'."
    }

    if (-not (Test-HasProperty $manifestDocument "cases") -or
        -not ($manifestDocument.cases -is [System.Array]))
    {
        throw "cases must be a non-empty array."
    }

    $rawCases = @($manifestDocument.cases)
    if ($rawCases.Count -eq 0)
    {
        throw "cases must be a non-empty array."
    }

    $manifestDirectory = Split-Path -Parent $manifestPath
    $seenIds = @{}
    $cases = New-Object System.Collections.Generic.List[object]
    foreach ($case in $rawCases)
    {
        foreach ($requiredProperty in @("id", "image", "expectedWidth", "expectedHeight", "panelThreshold", "expectedDecision"))
        {
            if (-not (Test-HasProperty $case $requiredProperty))
            {
                throw "Every case must contain $requiredProperty."
            }
        }

        if (-not ($case.id -is [string]) -or [string]::IsNullOrWhiteSpace($case.id))
        {
            throw "Every case id must be a non-empty string."
        }

        $id = $case.id.Trim()
        $idKey = $id.ToLowerInvariant()
        if ($seenIds.ContainsKey($idKey))
        {
            throw "Duplicate case id: $id"
        }

        $seenIds[$idKey] = $true
        if (-not ($case.image -is [string]) -or [string]::IsNullOrWhiteSpace($case.image))
        {
            throw "Case '$id' image must be a non-empty path string."
        }

        if (-not (Test-IsInteger $case.expectedWidth) -or [int64]$case.expectedWidth -le 0 -or [int64]$case.expectedWidth -gt [int]::MaxValue -or
            -not (Test-IsInteger $case.expectedHeight) -or [int64]$case.expectedHeight -le 0 -or [int64]$case.expectedHeight -gt [int]::MaxValue)
        {
            throw "Case '$id' expectedWidth and expectedHeight must be positive integers."
        }

        if (-not (Test-IsJsonNumber $case.panelThreshold))
        {
            throw "Case '$id' panelThreshold must be a number."
        }

        $panelThreshold = [double]$case.panelThreshold
        if ([double]::IsNaN($panelThreshold) -or [double]::IsInfinity($panelThreshold) -or
            $panelThreshold -lt 0.35 -or $panelThreshold -gt 0.95)
        {
            throw "Case '$id' panelThreshold must be from 0.35 through 0.95."
        }

        if (-not ($case.expectedDecision -is [string]) -or
            $case.expectedDecision -cnotin @("accepted", "rejected"))
        {
            throw "Case '$id' expectedDecision must be exactly 'accepted' or 'rejected'."
        }

        $captureSpace = $null
        if (Test-HasProperty $case "captureSpace")
        {
            if (-not ($case.captureSpace -is [string]) -or
                $case.captureSpace -cnotin @("client-device-pixels", "full-window", "unknown"))
            {
                throw "Case '$id' captureSpace must be exactly 'client-device-pixels', 'full-window', or 'unknown'."
            }

            $captureSpace = $case.captureSpace
        }

        if ($manifestDocument.evidenceClass -eq "target" -and $captureSpace -cne "client-device-pixels")
        {
            throw "Target case '$id' must declare captureSpace as 'client-device-pixels'."
        }

        $imagePath = $case.image
        if (-not [System.IO.Path]::IsPathRooted($imagePath))
        {
            $imagePath = Join-Path $manifestDirectory $imagePath
        }

        $imagePath = [System.IO.Path]::GetFullPath($imagePath)

        $expectedPanel = $null
        if (Test-HasProperty $case "expectedPanel")
        {
            if ($null -eq $case.expectedPanel)
            {
                throw "Case '$id' expectedPanel cannot be null."
            }

            foreach ($dimension in @("x", "y", "width", "height"))
            {
                if (-not (Test-HasProperty $case.expectedPanel $dimension))
                {
                    throw "Case '$id' expectedPanel must contain x, y, width, and height ranges."
                }
            }

            $expectedPanel = [pscustomobject][ordered]@{
                X = Read-NormalizedRange $case.expectedPanel.x "Case '$id' expectedPanel.x"
                Y = Read-NormalizedRange $case.expectedPanel.y "Case '$id' expectedPanel.y"
                Width = Read-NormalizedRange $case.expectedPanel.width "Case '$id' expectedPanel.width"
                Height = Read-NormalizedRange $case.expectedPanel.height "Case '$id' expectedPanel.height"
            }
        }

        [void]$cases.Add([pscustomobject][ordered]@{
            Id = $id
            Image = $imagePath
            ExpectedWidth = [int]$case.expectedWidth
            ExpectedHeight = [int]$case.expectedHeight
            CaptureSpace = $captureSpace
            PanelThreshold = $panelThreshold
            ExpectedDecision = $case.expectedDecision
            ExpectedPanel = $expectedPanel
        })
    }

    if ($manifestDocument.evidenceClass -eq "target")
    {
        $has1920x1080 = @($cases | Where-Object { $_.ExpectedWidth -eq 1920 -and $_.ExpectedHeight -eq 1080 }).Count -gt 0
        $has1280x960 = @($cases | Where-Object { $_.ExpectedWidth -eq 1280 -and $_.ExpectedHeight -eq 960 }).Count -gt 0
        if (-not $has1920x1080 -or -not $has1280x960)
        {
            throw "target manifests must contain both 1920x1080 and 1280x960 cases."
        }
    }
}
catch
{
    [Console]::Error.WriteLine("Manifest error: $($_.Exception.Message)")
    exit $manifestErrorExitCode
}

if ($ValidationOnly)
{
    $validationResult = [pscustomobject][ordered]@{
        SchemaVersion = 1
        Mode = "schema-only"
        ReplayEvidenceGenerated = $false
        EvidenceStatus = "not-replay-evidence"
        EvidenceClass = $manifestDocument.evidenceClass
        Manifest = $manifestPath
        Summary = [pscustomobject][ordered]@{
            Total = $cases.Count
            RequiredResolutionRepresentativesPresent = if ($manifestDocument.evidenceClass -eq "target") { $true } else { $null }
        }
    }

    Write-Output ($validationResult | ConvertTo-Json -Depth 6)
    exit 0
}

try
{
    foreach ($case in $cases)
    {
        if (-not (Test-Path -LiteralPath $case.Image -PathType Leaf))
        {
            throw "Case '$($case.Id)' image not found: $($case.Image)"
        }
    }
}
catch
{
    [Console]::Error.WriteLine("Manifest error: $($_.Exception.Message)")
    exit $manifestErrorExitCode
}

$repositoryRoot = Split-Path -Parent $PSScriptRoot
$probeProject = Join-Path $repositoryRoot "tests\D4Hub.VisionProbe\D4Hub.VisionProbe.csproj"
$probeExecutable = Join-Path $repositoryRoot "tests\D4Hub.VisionProbe\bin\Release\net8.0-windows\D4Hub.VisionProbe.exe"

try
{
    if (-not $NoBuild)
    {
        $build = Invoke-CapturedProcess "dotnet" @("build", $probeProject, "--configuration", "Release") $repositoryRoot
        if ($build.ExitCode -ne 0)
        {
            throw "VisionProbe Release build failed with exit code $($build.ExitCode): $($build.StandardError.Trim())"
        }
    }

    if (-not (Test-Path -LiteralPath $probeExecutable -PathType Leaf))
    {
        throw "VisionProbe Release executable not found: $probeExecutable"
    }
}
catch
{
    [Console]::Error.WriteLine("Operational error: $($_.Exception.Message)")
    exit $operationalFailureExitCode
}

$caseReports = New-Object System.Collections.Generic.List[object]
foreach ($case in $cases)
{
    $failures = New-Object System.Collections.Generic.List[string]
    $probeResult = $null
    $probeDocument = $null
    try
    {
        $probeResult = Invoke-CapturedProcess $probeExecutable @(
            $case.Image,
            "--panel-threshold",
            $case.PanelThreshold.ToString("0.################", [System.Globalization.CultureInfo]::InvariantCulture)
        ) $repositoryRoot

        $expectedExitCode = 4
        if ($case.ExpectedDecision -eq "accepted")
        {
            $expectedExitCode = 0
        }

        Add-AssertionFailure $failures ($probeResult.ExitCode -eq $expectedExitCode) "Expected probe exit $expectedExitCode but received $($probeResult.ExitCode)."
        if ([string]::IsNullOrWhiteSpace($probeResult.StandardOutput))
        {
            [void]$failures.Add("Probe did not emit JSON on standard output.")
        }
        else
        {
            try
            {
                $probeDocument = $probeResult.StandardOutput | ConvertFrom-Json
            }
            catch
            {
                [void]$failures.Add("Probe standard output is not valid JSON: $($_.Exception.Message)")
            }
        }
    }
    catch
    {
        [void]$failures.Add("Probe process failed: $($_.Exception.Message)")
    }

    $actual = $null
    if ($null -ne $probeDocument)
    {
        try
        {
            $actual = [pscustomobject][ordered]@{
                Width = [int]$probeDocument.image.width
                Height = [int]$probeDocument.image.height
                Confidence = [double]$probeDocument.panel.confidence
                Bounds = [pscustomobject][ordered]@{
                    X = [double]$probeDocument.panel.x
                    Y = [double]$probeDocument.panel.y
                    Width = [double]$probeDocument.panel.width
                    Height = [double]$probeDocument.panel.height
                }
                Decision = [string]$probeDocument.decision.status
                Fingerprint = $probeDocument.fingerprint
            }

            Add-AssertionFailure $failures ($actual.Width -eq $case.ExpectedWidth) "Expected width $($case.ExpectedWidth) but received $($actual.Width)."
            Add-AssertionFailure $failures ($actual.Height -eq $case.ExpectedHeight) "Expected height $($case.ExpectedHeight) but received $($actual.Height)."
            Add-AssertionFailure $failures ($actual.Decision -eq $case.ExpectedDecision) "Expected decision '$($case.ExpectedDecision)' but received '$($actual.Decision)'."
            Add-AssertionFailure $failures ([Math]::Abs([double]$probeDocument.panel.threshold - $case.PanelThreshold) -lt 0.0000001) "Probe reported a different panel threshold."

            if ($case.ExpectedDecision -eq "accepted")
            {
                $hasCompleteFingerprint = $null -ne $probeDocument.fingerprint -and
                    (Test-HasProperty $probeDocument.fingerprint "isComplete") -and
                    $probeDocument.fingerprint.isComplete -eq $true
                Add-AssertionFailure $failures $hasCompleteFingerprint "Accepted decisions must contain a complete fingerprint."
            }
            else
            {
                Add-AssertionFailure $failures ($null -eq $probeDocument.fingerprint) "Rejected decisions must not contain a fingerprint."
            }

            if ($null -ne $case.ExpectedPanel)
            {
                foreach ($bound in @("X", "Y", "Width", "Height"))
                {
                    $actualBound = [double]$actual.Bounds.$bound
                    $expectedRange = $case.ExpectedPanel.$bound
                    Add-AssertionFailure $failures ($actualBound -ge $expectedRange.Min -and $actualBound -le $expectedRange.Max) "Panel $($bound.ToLowerInvariant()) $actualBound is outside [$($expectedRange.Min), $($expectedRange.Max)]."
                }
            }
        }
        catch
        {
            [void]$failures.Add("Probe JSON does not satisfy the expected result shape: $($_.Exception.Message)")
        }
    }

    [void]$caseReports.Add([pscustomobject][ordered]@{
        Id = $case.Id
        Image = $case.Image
        CaptureSpace = $case.CaptureSpace
        Passed = $failures.Count -eq 0
        ProbeExitCode = if ($null -eq $probeResult) { $null } else { $probeResult.ExitCode }
        Actual = $actual
        Failures = $failures.ToArray()
        ProbeStandardError = if ($null -eq $probeResult -or [string]::IsNullOrWhiteSpace($probeResult.StandardError)) { $null } else { $probeResult.StandardError.Trim() }
    })
}

$failedCases = @($caseReports | Where-Object { -not $_.Passed }).Count
$report = [pscustomobject][ordered]@{
    SchemaVersion = 1
    EvidenceClass = $manifestDocument.evidenceClass
    Manifest = $manifestPath
    GeneratedAt = [DateTimeOffset]::UtcNow.ToString("o")
    Passed = $failedCases -eq 0
    Summary = [pscustomobject][ordered]@{
        Total = $caseReports.Count
        Passed = $caseReports.Count - $failedCases
        Failed = $failedCases
    }
    Cases = $caseReports.ToArray()
}

try
{
    Write-Report $report $Output
}
catch
{
    [Console]::Error.WriteLine("Operational error: failed to write replay report: $($_.Exception.Message)")
    exit $operationalFailureExitCode
}

if ($failedCases -gt 0)
{
    exit $assertionFailureExitCode
}

exit 0
