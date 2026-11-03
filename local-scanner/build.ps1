param([string]$OutputDirectory = 'dist-cycle-v40',[switch]$SkipNative)
$ErrorActionPreference = 'Stop'
$env:MSBuildEnableWorkloadResolver = 'false'
$env:DOTNET_SKIP_FIRST_TIME_EXPERIENCE = '1'
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
$env:DOTNET_GENERATE_ASPNET_CERTIFICATE = 'false'
$env:DOTNET_NOLOGO = '1'
$env:DOTNET_CLI_HOME = Join-Path $PSScriptRoot '.build-home'
dotnet publish (Join-Path $PSScriptRoot 'Metin2Search.Local.csproj') -c Release -o (Join-Path $PSScriptRoot $OutputDirectory) --self-contained false -p:NuGetAudit=false
if ($LASTEXITCODE -ne 0) { throw "Derleme başarısız: $LASTEXITCODE" }
if(!$SkipNative){& (Join-Path $PSScriptRoot 'native/build.ps1') -OutputDirectory $OutputDirectory}
if (Test-Path -LiteralPath (Join-Path $PSScriptRoot 'samples')) {
    Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'samples') -Destination (Join-Path $PSScriptRoot $OutputDirectory) -Recurse -Force
}
