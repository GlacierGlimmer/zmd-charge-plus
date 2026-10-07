param(
    [ValidateSet('win-x64','win-x86')][string]$Runtime = 'win-x64',
    [string]$Output = (Join-Path $PSScriptRoot "../publish/$Runtime-portable")
)
$ErrorActionPreference = 'Stop'
# A self-contained folder avoids extracting the runtime/native libraries on each
# first single-file launch. The ZIP remains portable and needs no installed .NET.
& dotnet publish (Join-Path $PSScriptRoot '../EndfieldChargePlus.csproj') -c Release -r $Runtime --self-contained true -p:PublishSingleFile=false -p:NuGetAudit=false -o $Output
if ($LASTEXITCODE -ne 0) { throw "Windows publish failed: $LASTEXITCODE" }
