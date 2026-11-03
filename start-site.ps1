param([switch]$Telefon)
$ErrorActionPreference = 'Stop'
$taskRoot = $PSScriptRoot
$taskWeb = Join-Path $taskRoot 'market-draft'
$taskPidFile = Join-Path $taskWeb 'data/live-server-process.json'
if (Test-Path -LiteralPath $taskPidFile) {
    $taskPrevious = Get-Content -LiteralPath $taskPidFile -Raw | ConvertFrom-Json
    $taskProcess = Get-CimInstance Win32_Process -Filter "ProcessId=$($taskPrevious.processId)" -ErrorAction SilentlyContinue
    if ($taskProcess -and $taskProcess.CommandLine.Contains((Join-Path $taskWeb 'server.cjs'))) {
        Write-Output 'Site zaten çalışıyor: http://127.0.0.1:4180/'
        return
    }
}
$taskNode = (Get-Command node -ErrorAction Stop).Source
$taskDefs = Join-Path $taskWeb 'data/definitions'
$taskMissing = @('item_proto.json','item_names.json','item_icon.json','stat_map.json','client-stat-map.json','proto_stat_map.json') | Where-Object { !(Test-Path -LiteralPath (Join-Path $taskDefs $_)) }
if ($taskMissing.Count -gt 0) { throw ('Oyun veri tablolari eksik (market-draft/data/definitions): ' + ($taskMissing -join ', ') + '. Ayrintilar icin ayni klasordeki BENI-OKU.md dosyasini okuyun.') }
if (!(Test-Path -LiteralPath (Join-Path $taskWeb 'assets/site'))) { Push-Location $taskWeb; try { & $taskNode (Join-Path $taskWeb 'build-public-assets.cjs') | Out-Null } finally { Pop-Location } }
$taskEnvironment = @{SOURCE_ROOT=(Join-Path $taskRoot 'scanner-data/Sunucular');SERVE_BUILD='1';READ_WORKERS='2';SYNC_INTERVAL_MS='60000';DATA_ROOT=$taskWeb;PORT='4180'}
if ($Telefon) { $taskEnvironment['HOST']='0.0.0.0' }
$taskSaved = @{}
try {
    foreach ($taskKey in $taskEnvironment.Keys) { $taskSaved[$taskKey]=[Environment]::GetEnvironmentVariable($taskKey,'Process'); [Environment]::SetEnvironmentVariable($taskKey,$taskEnvironment[$taskKey],'Process') }
    $taskStarted = Start-Process -FilePath $taskNode -ArgumentList @(('"'+(Join-Path $taskWeb 'server.cjs')+'"')) -WorkingDirectory $taskWeb -WindowStyle Hidden -RedirectStandardOutput (Join-Path $taskWeb 'data/live-server.out.log') -RedirectStandardError (Join-Path $taskWeb 'data/live-server.err.log') -PassThru
    @{started=[DateTimeOffset]::UtcNow.ToString('O');processId=$taskStarted.Id;script=(Join-Path $taskWeb 'server.cjs')} | ConvertTo-Json | Set-Content -LiteralPath $taskPidFile -Encoding utf8
    Write-Output "Site başlatıldı: http://127.0.0.1:4180/ (PID $($taskStarted.Id))"
} finally { foreach ($taskKey in $taskSaved.Keys) { [Environment]::SetEnvironmentVariable($taskKey,$taskSaved[$taskKey],'Process') } }
