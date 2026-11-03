param([Parameter(Mandatory)][string]$Original,[Parameter(Mandatory)][string]$Compact,[Parameter(Mandatory)][string]$Report)
$ErrorActionPreference='Stop'
. (Join-Path $PSScriptRoot 'inspect-local-data.ps1') -Database $Original -QuerySql 'SELECT 1' | Out-Null
$versions=[MarketSqlRead]::Query($Compact,"SELECT key,payload_hash FROM compact_versions WHERE reason='Legacy observation'")
$hashes=[Collections.Generic.Dictionary[string,string]]::new()
foreach($row in $versions){$hashes.Add($row['key'],$row['payload_hash'])}
$cursor=0L;$verified=0L
while($true){
    $rows=[MarketSqlRead]::Query($Original,"SELECT rowid,page_id,slot,decoded_json FROM trade_items WHERE rowid>$cursor ORDER BY rowid LIMIT 200")
    if($rows.Count -eq 0){break}
    foreach($row in $rows){
        $key='legacy:'+$row['page_id']+':'+$row['slot']
        $hash=[Convert]::ToHexString([Security.Cryptography.SHA256]::HashData([Text.Encoding]::UTF8.GetBytes($row['decoded_json'])))
        if(!$hashes.ContainsKey($key) -or $hashes[$key] -ine $hash){throw "Orijinal JSON hash eşleşmedi: $key"}
        $verified++;$cursor=[long]$row['rowid']
    }
}
if($verified -ne $hashes.Count){throw 'Kayıt sayıları eşleşmedi'}
$cursor=0L;$payloads=0L
while($true){
    $rows=[MarketSqlRead]::Query($Compact,"SELECT rowid,hash,hex(bytes) AS packed FROM compact_payloads WHERE rowid>$cursor ORDER BY rowid LIMIT 200")
    if($rows.Count -eq 0){break}
    foreach($row in $rows){
        $inputStream=[IO.MemoryStream]::new([Convert]::FromHexString($row['packed']))
        $stream=[IO.Compression.BrotliStream]::new($inputStream,[IO.Compression.CompressionMode]::Decompress)
        $output=[IO.MemoryStream]::new()
        try{$stream.CopyTo($output);$hash=[Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($output.ToArray()));if($hash -ine $row['hash']){throw 'Payload hash eşleşmedi'}}
        finally{$output.Dispose();$stream.Dispose();$inputStream.Dispose()}
        $payloads++;$cursor=[long]$row['rowid']
    }
}
$result=[ordered]@{originalJsonVerified=$verified;compressedPayloadsVerified=$payloads;integrity=[MarketSqlRead]::Query($Compact,'PRAGMA integrity_check')[0]['integrity_check'];sourceReadOnly=$true}
$json=$result|ConvertTo-Json
[IO.File]::WriteAllText([IO.Path]::GetFullPath($Report),$json,[Text.UTF8Encoding]::new($false))
$json
