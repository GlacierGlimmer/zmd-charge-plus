param(
    [ValidateSet('win-x64','win-x86')][string]$Runtime = 'win-x64',
    [string]$Output = (Join-Path $PSScriptRoot "../publish/$Runtime-portable")
)
$ErrorActionPreference = 'Stop'
& dotnet publish (Join-Path $PSScriptRoot '../EndfieldChargePlus.csproj') -c Release -r $Runtime --self-contained true `
    -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true `
    -p:PublishTrimmed=false -p:PublishReadyToRun=false -p:DebugType=none -p:DebugSymbols=false `
    -p:NuGetAudit=false -o $Output
if ($LASTEXITCODE -ne 0) { throw "Windows publish failed: $LASTEXITCODE" }
$payload = @(Get-ChildItem -LiteralPath $Output -File)
if ($payload.Count -ne 1 -or $payload[0].Name -ne 'EndfieldChargePlus.exe') {
    throw "Expected one executable in $Output; inspect the publish output before distributing it."
}
