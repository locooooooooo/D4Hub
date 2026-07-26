[CmdletBinding()]
param()

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$repositoryRoot = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$solution = Join-Path $repositoryRoot "D4Hub.sln"
$fixtureExecutable = Join-Path $repositoryRoot "tests\D4Hub.GameWindowFixture\bin\Release\net8.0-windows\D4Hub.GameWindowFixture.exe"
$probeExecutable = Join-Path $repositoryRoot "tests\D4Hub.VisionProbe\bin\Release\net8.0-windows\D4Hub.VisionProbe.exe"
$fixtureImage = Join-Path $repositoryRoot "src\D4Hub.App\Assets\Hud\Source\marker-masterworked-source.png"
$temporaryRoot = Join-Path ([System.IO.Path]::GetTempPath()) ("d4hub-v10-e03-" + [Guid]::NewGuid().ToString("N"))
$fixtureTitle = [System.Text.Encoding]::UTF8.GetString(
    [System.Convert]::FromBase64String("5pqX6buR56C05Z2P56WeSVY="))
$fixtureProcess = $null
$fixtureStarted = $false

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
    try
    {
        return [pscustomobject][ordered]@{
            ExitCode = $process.ExitCode
            StandardOutput = $standardOutput.GetAwaiter().GetResult()
            StandardError = $standardError.GetAwaiter().GetResult()
        }
    }
    finally
    {
        $process.Dispose()
    }
}

function Read-PngHeader {
    param([Parameter(Mandatory = $true)][string]$Path)

    $stream = [System.IO.File]::OpenRead($Path)
    try
    {
        $header = New-Object byte[] 24
        $count = $stream.Read($header, 0, $header.Length)
    }
    finally
    {
        $stream.Dispose()
    }

    Assert-True ($count -eq 24) "Captured PNG is too short to contain an IHDR header."
    Assert-True ([System.BitConverter]::ToString($header[0..7]) -ceq "89-50-4E-47-0D-0A-1A-0A") "Captured file does not have the PNG signature."
    Assert-True ([System.Text.Encoding]::ASCII.GetString($header, 12, 4) -ceq "IHDR") "Captured PNG does not begin with an IHDR chunk."

    $width = ([int64]$header[16] -shl 24) -bor
        ([int64]$header[17] -shl 16) -bor
        ([int64]$header[18] -shl 8) -bor
        [int64]$header[19]
    $height = ([int64]$header[20] -shl 24) -bor
        ([int64]$header[21] -shl 16) -bor
        ([int64]$header[22] -shl 8) -bor
        [int64]$header[23]

    return [pscustomobject][ordered]@{
        Width = $width
        Height = $height
    }
}

function Assert-CaptureReceipt {
    param(
        [Parameter(Mandatory = $true)]$Result,
        [Parameter(Mandatory = $true)][string]$ExpectedMethod,
        [Parameter(Mandatory = $true)][string]$ExpectedOutputPath
    )

    Assert-True ($Result.ExitCode -eq 0) "Capture method '$ExpectedMethod' returned $($Result.ExitCode): $($Result.StandardError.Trim())"
    Assert-True ([string]::IsNullOrWhiteSpace($Result.StandardError)) "Successful capture method '$ExpectedMethod' wrote standard error."
    Assert-True (-not [string]::IsNullOrWhiteSpace($Result.StandardOutput)) "Capture method '$ExpectedMethod' did not emit a receipt."

    try
    {
        $receipt = $Result.StandardOutput | ConvertFrom-Json
    }
    catch
    {
        throw "Capture method '$ExpectedMethod' did not emit valid JSON: $($_.Exception.Message)"
    }

    $expectedProperties = @(
        "byteLength",
        "captureMethod",
        "captureSpace",
        "height",
        "mode",
        "outputPath",
        "schemaVersion",
        "sha256",
        "width"
    )
    $actualProperties = @($receipt.PSObject.Properties.Name | Sort-Object)
    Assert-True (($actualProperties -join '|') -ceq ($expectedProperties -join '|')) "Capture receipt properties do not match the privacy-minimized schema."
    Assert-True ($receipt.schemaVersion -eq 1) "Capture receipt schemaVersion is not 1."
    Assert-True ($receipt.mode -ceq "client-surface-capture") "Capture receipt mode is incorrect."
    Assert-True ($receipt.captureSpace -ceq "client-device-pixels") "Capture receipt captureSpace is incorrect."
    Assert-True ($receipt.captureMethod -ceq $ExpectedMethod) "Capture receipt method is incorrect."
    Assert-True ([int64]$receipt.width -gt 0 -and [int64]$receipt.height -gt 0) "Capture receipt dimensions are not positive."
    Assert-True ([int64]$receipt.byteLength -gt 0) "Capture receipt byteLength is not positive."
    Assert-True ($receipt.sha256 -cmatch '^[0-9A-F]{64}$') "Capture receipt SHA-256 is not uppercase hexadecimal."
    Assert-True ([System.IO.Path]::IsPathRooted([string]$receipt.outputPath)) "Capture receipt outputPath is not absolute."
    Assert-True ([string]::Equals(
        [System.IO.Path]::GetFullPath([string]$receipt.outputPath),
        [System.IO.Path]::GetFullPath($ExpectedOutputPath),
        [StringComparison]::OrdinalIgnoreCase)) "Capture receipt outputPath does not match the requested path."
    Assert-True ($Result.StandardOutput.IndexOf($fixtureTitle, [StringComparison]::Ordinal) -lt 0) "Capture receipt leaked the fixture window title."
    Assert-True ($Result.StandardOutput -notmatch '(?i)"(windowTitle|title|handle|left|top|screenX|screenY|pixels|content)"\s*:') "Capture receipt leaked a forbidden privacy or position field."

    Assert-True (Test-Path -LiteralPath $ExpectedOutputPath -PathType Leaf) "Capture receipt output file does not exist."
    $file = Get-Item -LiteralPath $ExpectedOutputPath
    $hash = (Get-FileHash -LiteralPath $ExpectedOutputPath -Algorithm SHA256).Hash
    $png = Read-PngHeader $ExpectedOutputPath
    Assert-True ($file.Length -eq [int64]$receipt.byteLength) "Capture receipt byteLength does not match the saved PNG."
    Assert-True ($hash -ceq [string]$receipt.sha256) "Capture receipt SHA-256 does not match the saved PNG."
    Assert-True ($png.Width -eq [int64]$receipt.width) "Capture receipt width does not match the PNG header."
    Assert-True ($png.Height -eq [int64]$receipt.height) "Capture receipt height does not match the PNG header."

    return $receipt
}

function Assert-FailureWithoutReceipt {
    param(
        [Parameter(Mandatory = $true)]$Result,
        [Parameter(Mandatory = $true)][string]$OutputPath,
        [Parameter(Mandatory = $true)][string]$Label,
        [Parameter(Mandatory = $true)][int]$ExpectedExitCode
    )

    Assert-True ($Result.ExitCode -eq $ExpectedExitCode) "$Label returned $($Result.ExitCode), expected $ExpectedExitCode."
    Assert-True ([string]::IsNullOrWhiteSpace($Result.StandardOutput)) "$Label emitted a success receipt on standard output."
    Assert-True (-not (Test-Path -LiteralPath $OutputPath)) "$Label created an output PNG."
}

try
{
    [void](New-Item -ItemType Directory -Path $temporaryRoot)

    $build = Invoke-CapturedProcess "dotnet" @("build", $solution, "--configuration", "Release", "--nologo") $repositoryRoot
    Assert-True ($build.ExitCode -eq 0) "Release build failed: $($build.StandardError.Trim())"
    Assert-True (Test-Path -LiteralPath $fixtureExecutable -PathType Leaf) "Fixture executable was not built."
    Assert-True (Test-Path -LiteralPath $probeExecutable -PathType Leaf) "VisionProbe executable was not built."
    Assert-True (Test-Path -LiteralPath $fixtureImage -PathType Leaf) "Repository fixture image is missing."

    $fixtureStartInfo = New-Object System.Diagnostics.ProcessStartInfo
    $fixtureStartInfo.FileName = $fixtureExecutable
    $fixtureStartInfo.Arguments = ConvertTo-ProcessArgument $fixtureImage
    $fixtureStartInfo.WorkingDirectory = $repositoryRoot
    $fixtureStartInfo.UseShellExecute = $false
    $fixtureStartInfo.CreateNoWindow = $false
    if ([string]::IsNullOrWhiteSpace($fixtureStartInfo.EnvironmentVariables["WINDIR"]))
    {
        $fixtureStartInfo.EnvironmentVariables["WINDIR"] = $env:SystemRoot
    }
    $fixtureProcess = New-Object System.Diagnostics.Process
    $fixtureProcess.StartInfo = $fixtureStartInfo
    $fixtureStarted = $fixtureProcess.Start()
    Assert-True $fixtureStarted "Fixture process did not start."
    Assert-True ($fixtureProcess.WaitForInputIdle(10000)) "Fixture process did not reach an input-idle state."

    $windowDeadline = [DateTime]::UtcNow.AddSeconds(10)
    while (-not $fixtureProcess.HasExited -and $fixtureProcess.MainWindowHandle -eq [IntPtr]::Zero -and [DateTime]::UtcNow -lt $windowDeadline)
    {
        Start-Sleep -Milliseconds 100
        $fixtureProcess.Refresh()
    }

    Assert-True (-not $fixtureProcess.HasExited) "Fixture process exited before capture."
    Assert-True ($fixtureProcess.MainWindowHandle -ne [IntPtr]::Zero) "Fixture window did not become visible."
    Start-Sleep -Milliseconds 500

    $printWindowOutput = Join-Path $temporaryRoot "print-window.png"
    $screenCopyOutput = Join-Path $temporaryRoot "screen-copy.png"
    [System.IO.File]::WriteAllText($printWindowOutput, "overwrite-me", [System.Text.Encoding]::ASCII)
    [System.IO.File]::WriteAllText($screenCopyOutput, "overwrite-me", [System.Text.Encoding]::ASCII)

    $printWindowResult = Invoke-CapturedProcess $probeExecutable @(
        "--capture-title", $fixtureTitle, $printWindowOutput) $repositoryRoot
    $printWindowReceipt = Assert-CaptureReceipt $printWindowResult "print-window-client-only" $printWindowOutput

    $screenCopyResult = Invoke-CapturedProcess $probeExecutable @(
        "--capture-screen-title", $fixtureTitle, $screenCopyOutput) $repositoryRoot
    $screenCopyReceipt = Assert-CaptureReceipt $screenCopyResult "client-rect-screen-copy" $screenCopyOutput

    Assert-True ($printWindowReceipt.width -eq $screenCopyReceipt.width) "Capture methods reported different client widths."
    Assert-True ($printWindowReceipt.height -eq $screenCopyReceipt.height) "Capture methods reported different client heights."

    $missingTitle = "D4Hub missing capture " + [Guid]::NewGuid().ToString("N")
    foreach ($missingCommand in @("--capture-title", "--capture-screen-title"))
    {
        $missingOutput = Join-Path $temporaryRoot (($missingCommand -replace '^--', '') + "-missing.png")
        $missingResult = Invoke-CapturedProcess $probeExecutable @(
            $missingCommand, $missingTitle, $missingOutput) $repositoryRoot
        Assert-FailureWithoutReceipt $missingResult $missingOutput "Missing-window command '$missingCommand'" 3
    }

    $writeFailureOutput = Join-Path $temporaryRoot "absent-parent\capture.png"
    $writeFailureResult = Invoke-CapturedProcess $probeExecutable @(
        "--capture-screen-title", $fixtureTitle, $writeFailureOutput) $repositoryRoot
    Assert-FailureWithoutReceipt $writeFailureResult $writeFailureOutput "Capture write failure" 5
}
finally
{
    if ($null -ne $fixtureProcess)
    {
        try
        {
            if ($fixtureStarted -and -not $fixtureProcess.HasExited)
            {
                [void]$fixtureProcess.CloseMainWindow()
                if (-not $fixtureProcess.WaitForExit(3000))
                {
                    $fixtureProcess.Kill()
                    $fixtureProcess.WaitForExit()
                }
            }
        }
        finally
        {
            $fixtureProcess.Dispose()
        }
    }

    if (Test-Path -LiteralPath $temporaryRoot)
    {
        Remove-Item -LiteralPath $temporaryRoot -Recurse -Force
    }
}

Write-Host "PASS VisionProbe client capture receipt contract"
