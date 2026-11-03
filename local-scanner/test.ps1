param([string]$OutputDirectory='dist-cycle-v40')
$ErrorActionPreference = 'Stop'
$executable = Join-Path $PSScriptRoot "$OutputDirectory\Metin2Search.Local.exe"
if (-not (Test-Path -LiteralPath $executable)) { & (Join-Path $PSScriptRoot 'build.ps1') -OutputDirectory $OutputDirectory }
Push-Location $PSScriptRoot
try {
    $testRun = Start-Process -FilePath $executable -ArgumentList @('--self-test','--data-dir','test-output') -Wait -PassThru -WindowStyle Hidden
    $reportPath = Join-Path $PSScriptRoot 'test-output\last-run.json'
    $report = Get-Content -Raw -LiteralPath $reportPath | ConvertFrom-Json
    $report.checks
    if ($testRun.ExitCode -ne 0 -or -not $report.success) { throw "Yerel kontroller başarısız. Rapor: $reportPath" }
    $pythonDll = 'C:\metin2\tr-TR\python27.dll'
    if (Test-Path -LiteralPath $pythonDll) {
        & (Join-Path $PSScriptRoot "$OutputDirectory\MarketBridge.Host.exe") --check-python $pythonDll
        if ($LASTEXITCODE -ne 0) { throw 'Python 2.7 köprü kontrolleri başarısız.' }
    }
} finally { Pop-Location }
