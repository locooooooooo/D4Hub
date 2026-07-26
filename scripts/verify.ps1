[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot

Push-Location $root
try {
    dotnet build .\D4Hub.sln --configuration Release --nologo
    if ($LASTEXITCODE -ne 0) {
        throw 'Release build failed.'
    }

    dotnet run --project .\tests\D4Hub.AcceptanceTests\D4Hub.AcceptanceTests.csproj --configuration Release --no-build
    if ($LASTEXITCODE -ne 0) {
        throw 'Acceptance checks failed.'
    }

    powershell -NoProfile -ExecutionPolicy Bypass -File .\tests\vision-replay\verify-coordinate-contract.ps1
    if ($LASTEXITCODE -ne 0) {
        throw 'Vision replay coordinate contract checks failed.'
    }

    powershell -NoProfile -ExecutionPolicy Bypass -File .\tests\vision-replay\verify-client-capture-receipt.ps1
    if ($LASTEXITCODE -ne 0) {
        throw 'VisionProbe client capture receipt checks failed.'
    }

    $forbiddenPatterns = @(
        'ReadProcessMemory',
        'WriteProcessMemory',
        'CreateRemoteThread',
        'VirtualAllocEx',
        'OpenProcess\s*\(',
        'SendInput\s*\(',
        'mouse_event\s*\(',
        'keybd_event\s*\(',
        'SetWindowsHookEx'
    )
    $sourceFiles = Get-ChildItem .\src -Recurse -File -Include *.cs,*.xaml
    foreach ($pattern in $forbiddenPatterns) {
        $match = $sourceFiles | Select-String -Pattern $pattern
        if ($match) {
            throw "Forbidden game integration or input automation API found: $pattern"
        }
    }

    git diff --check
    if ($LASTEXITCODE -ne 0) {
        throw 'Git whitespace verification failed.'
    }

    Write-Host 'PASS repository verification'
}
finally {
    Pop-Location
}
