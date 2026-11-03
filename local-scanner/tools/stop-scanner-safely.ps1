param([Parameter(Mandatory=$true)][int]$ScannerPid,[Parameter(Mandatory=$true)][string]$BudgetFile,[Parameter(Mandatory=$true)][string]$Report)
$ErrorActionPreference='Stop'
try{
 $taskScanner=Get-Process -Id $ScannerPid -ErrorAction Stop
 if($taskScanner.ProcessName -ne 'Metin2Search.Local' -or !$taskScanner.Path.StartsWith('C:\Metin2Search\local-scanner\dist-fast-v',[StringComparison]::OrdinalIgnoreCase)){throw 'Scanner identity mismatch'}
 $taskDeadline=[DateTime]::UtcNow.AddSeconds(20)
 do{
  $taskState=Get-Content -LiteralPath $BudgetFile -Raw|ConvertFrom-Json
  if($taskState.phase -ne 'Sending'){break}
  Start-Sleep -Milliseconds 50
 }while([DateTime]::UtcNow -lt $taskDeadline)
 if($taskState.phase -eq 'Sending'){throw 'An unresolved purchase is pending; scanner left running.'}
 if(!$taskScanner.CloseMainWindow()){throw 'Scanner did not accept graceful close request'}
 $taskClosed=$taskScanner.WaitForExit(30000)
 @{at=[DateTimeOffset]::UtcNow;closed=$taskClosed;forced=$false;phaseBeforeClose=$taskState.phase}|ConvertTo-Json|Set-Content -LiteralPath $Report -Encoding utf8
 if(!$taskClosed){exit 1}
}catch{
 @{at=[DateTimeOffset]::UtcNow;closed=$false;forced=$false;error=$_.Exception.Message}|ConvertTo-Json|Set-Content -LiteralPath $Report -Encoding utf8
 exit 1
}
