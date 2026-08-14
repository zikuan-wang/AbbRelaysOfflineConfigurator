param(
    [string]$Configuration = "Release",
    [switch]$SkipTests
)

$ErrorActionPreference = "Stop"

$root = Split-Path -Parent $PSScriptRoot
$solutionPath = Join-Path $root "ABBRelaysOfflineConfigurator.sln"
$appOutput = Join-Path $root "AbbRelaysOfflineConfigurator\bin\$Configuration\net8.0-windows"
$forbiddenClientFiles = @(
    "authorization-private-key.txt",
    "rex615-authorization-private-key.txt",
    "AuthorizationPrivateKeyProvider.local.cs"
)

function Invoke-Step([string]$Name, [scriptblock]$Action) {
    Write-Host ""
    Write-Host "== $Name =="
    & $Action
}

Invoke-Step "Selection data validation" {
    python (Join-Path $root "Tools\ValidateSelectionLogic.py")
    if ($LASTEXITCODE -ne 0) {
        throw "Selection data validation failed with exit code $LASTEXITCODE."
    }
}

Invoke-Step "Release build" {
    dotnet build $solutionPath -c $Configuration /nr:false
    if ($LASTEXITCODE -ne 0) {
        throw "Release build failed with exit code $LASTEXITCODE."
    }
}

if (-not $SkipTests) {
    Invoke-Step "Unit tests" {
        dotnet test $solutionPath -c $Configuration --no-build /nr:false
        if ($LASTEXITCODE -ne 0) {
            throw "Unit tests failed with exit code $LASTEXITCODE."
        }
    }
}

Invoke-Step "Client private-key scan" {
    if (-not (Test-Path -LiteralPath $appOutput)) {
        throw "Application output directory was not found: $appOutput"
    }

    $findings = New-Object System.Collections.Generic.List[string]
    foreach ($fileName in $forbiddenClientFiles) {
        $path = Join-Path $appOutput $fileName
        if (Test-Path -LiteralPath $path) {
            $findings.Add($path)
        }
    }

    $privateKeyProviders = Get-ChildItem -LiteralPath $appOutput -Recurse -File -ErrorAction SilentlyContinue |
        Where-Object { $_.Name -like "*AuthorizationPrivateKeyProvider*" }
    foreach ($provider in $privateKeyProviders) {
        $findings.Add($provider.FullName)
    }

    if ($findings.Count -gt 0) {
        throw "Forbidden authorization signing material found in client output:`n$($findings -join "`n")"
    }

    Write-Host "No authorization signing material found in client output."
}

Write-Host ""
Write-Host "Verification completed successfully."
