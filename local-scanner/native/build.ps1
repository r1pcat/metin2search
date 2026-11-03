param([string]$OutputDirectory='dist-cycle-v40')
$ErrorActionPreference='Stop'
$root=Split-Path $PSScriptRoot
$destination=Join-Path $root $OutputDirectory
New-Item -ItemType Directory -Path $destination -Force | Out-Null
$vs=& 'C:\Program Files (x86)\Microsoft Visual Studio\Installer\vswhere.exe' -latest -products * -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -property installationPath
if(!$vs){throw 'Visual C++ x86 derleyicisi bulunamadi'}
$script=[IO.File]::ReadAllText((Join-Path $PSScriptRoot 'trade_bridge.py'))
$script+="`n"+[IO.File]::ReadAllText((Join-Path $PSScriptRoot 'rotation_bridge.py'))
$segments=for($offset=0;$offset -lt $script.Length;$offset+=8000){$chunk=$script.Substring($offset,[Math]::Min(8000,$script.Length-$offset));"R`"PYBRIDGE($chunk)PYBRIDGE`""}
[IO.File]::WriteAllText((Join-Path $PSScriptRoot 'script.h'),("static const char* bridgeScript=`n"+($segments -join "`n")+';'),[Text.UTF8Encoding]::new($false))
$setup=[IO.File]::ReadAllText((Join-Path $PSScriptRoot 'python-test-setup.py'))
$assertions=[IO.File]::ReadAllText((Join-Path $PSScriptRoot 'python-test-assertions.py'))
$assertions+="`n"+[IO.File]::ReadAllText((Join-Path $PSScriptRoot 'rotation-test-assertions.py'))
function TestLiteral([string]$value){
 $chunks=for($at=0;$at -lt $value.Length;$at+=8000){$part=$value.Substring($at,[Math]::Min(8000,$value.Length-$at));"R`"PYTEST($part)PYTEST`""}
 return $chunks -join "`n"
}
[IO.File]::WriteAllText((Join-Path $PSScriptRoot 'test-script.h'),("static const char* testSetup=`n"+(TestLiteral $setup)+";`nstatic const char* testAssertions=`n"+(TestLiteral $assertions)+";"),[Text.UTF8Encoding]::new($false))
$identityText=(@('bridge.cpp','bridge.h','navigation.h','host.cpp','trade_bridge.py','rotation_bridge.py','build.ps1')|ForEach-Object {$_+"`n"+[IO.File]::ReadAllText((Join-Path $PSScriptRoot $_))}) -join "`n--source--`n"
$identityHash=[Security.Cryptography.SHA256]::Create()
try{$buildId=[BitConverter]::ToString($identityHash.ComputeHash([Text.Encoding]::UTF8.GetBytes($identityText))).Replace('-','').ToLowerInvariant().Substring(0,16)}finally{$identityHash.Dispose()}
$identityHeader="#pragma once`n#define M2_BRIDGE_BUILD_ID `"$buildId`"`n#define M2_BRIDGE_ABI 2`n#define M2_BRIDGE_MAGIC 0x4D325343U`n"
[IO.File]::WriteAllText((Join-Path $PSScriptRoot 'build-identity.h'),$identityHeader,[Text.UTF8Encoding]::new($false))
$dllName="MarketBridge.$buildId.dll"
$batch="@echo off`r`ncall `"$vs\VC\Auxiliary\Build\vcvarsall.bat`" x86 >nul`r`ncd /d `"$PSScriptRoot`"`r`ncl /nologo /std:c++17 /EHsc /W4 /O2 /MT /LD bridge.cpp user32.lib /link /OUT:`"$destination\$dllName`" /IMPLIB:`"$destination\MarketBridge.$buildId.lib`"`r`nif errorlevel 1 exit /b 1`r`ncl /nologo /std:c++17 /EHsc /W4 /O2 /MT host.cpp user32.lib /link /OUT:`"$destination\MarketBridge.Host.exe`"`r`n"
$build=Join-Path $PSScriptRoot 'build-native.cmd'
[IO.File]::WriteAllText($build,$batch,[Text.Encoding]::Default)
& cmd.exe /d /c $build
if($LASTEXITCODE -ne 0){throw 'Native kopru derlenemedi'}

@{schemaVersion=1;abi=2;buildId=$buildId;dll=$dllName;dllSha256=(Get-FileHash -LiteralPath (Join-Path $destination $dllName) -Algorithm SHA256).Hash.ToLowerInvariant();hostSha256=(Get-FileHash -LiteralPath (Join-Path $destination 'MarketBridge.Host.exe') -Algorithm SHA256).Hash.ToLowerInvariant()} | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $destination 'bridge-build.json') -Encoding utf8
