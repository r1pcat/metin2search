param([int]$ClientPid=0)
$ErrorActionPreference = 'Stop'
$taskExecutable = Join-Path $PSScriptRoot 'local-scanner/dist-cycle-v40/Metin2Search.Local.exe'
$taskData = Join-Path $PSScriptRoot 'scanner-data'
if (!(Test-Path -LiteralPath $taskExecutable)) {
    Write-Host 'Tarayici henuz derlenmemis; derleniyor (ilk seferde birkac dakika surebilir)...'
    & powershell.exe -NoProfile -ExecutionPolicy Bypass -File (Join-Path $PSScriptRoot 'local-scanner/build.ps1')
    if ($LASTEXITCODE -ne 0 -or !(Test-Path -LiteralPath $taskExecutable)) { throw 'Tarayici derlenemedi. .NET 9 SDK ve Visual Studio C++ (x86) araclari kurulu olmali; ayrinti icin README.md dosyasina bakin.' }
}
New-Item -ItemType Directory -Force -Path $taskData | Out-Null
Write-Host 'Tarayici aciliyor. Windows bir yonetici izni penceresi gosterecek: Evet secenegine basin. Pencere gorunmezse gorev cubugunu kontrol edin.'
$env:METIN2_PURCHASE_CHECK_BUDGET_YANG='0'
$env:METIN2_NATIVE_BRIDGE_DIRECTORY=Join-Path $PSScriptRoot 'local-scanner/dist-cycle-v40'
$taskArguments=@('--data-dir',('"'+$taskData+'"'),'--error-file',('"'+(Join-Path $taskData 'launcher-error.txt')+'"'))
if($ClientPid -gt 0){$taskArguments+=@('--start-client',[string]$ClientPid)}
Start-Process -FilePath $taskExecutable -ArgumentList $taskArguments -WorkingDirectory (Split-Path $taskExecutable) -WindowStyle Normal
