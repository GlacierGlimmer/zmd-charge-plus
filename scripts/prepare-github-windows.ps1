param(
 [Parameter(Mandatory)][string]$InputFolder,
 [string]$Output=(Join-Path $PSScriptRoot '../publish/github-windows-assets'),
 [switch]$ValidateOnly
)
$ErrorActionPreference='Stop'
$repo=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$version=([xml](Get-Content -LiteralPath (Join-Path $repo 'EndfieldChargePlus.csproj') -Raw)).Project.PropertyGroup.Version | Where-Object {$_} | Select-Object -First 1
$assets=@()
foreach($arch in @('x64','x86','arm64')){
 $name="EndfieldChargePlus-v$version-win-$arch-portable.zip"
 $source=Join-Path ([IO.Path]::GetFullPath($InputFolder)) $name
 if(!(Test-Path -LiteralPath $source -PathType Leaf)){throw "Missing required Windows ZIP asset: $name"}
 $assets+=@{file=$name;source=$source;sha256=(Get-FileHash -LiteralPath $source -Algorithm SHA256).Hash.ToLowerInvariant()}
}
if(!$ValidateOnly){
 $destination=[IO.Path]::GetFullPath($Output)
 if((Test-Path -LiteralPath $destination) -and @(Get-ChildItem -LiteralPath $destination -Force).Count){throw 'Use an empty output folder so no 7z, Setup, Store or test files can enter the release asset set.'}
 New-Item -ItemType Directory -Force -Path $destination | Out-Null
 foreach($asset in $assets){Copy-Item -LiteralPath $asset.source -Destination (Join-Path $destination $asset.file)}
 # The asset directory contains only the three ZIPs. The manifest stays beside
 # that directory and is not itself a GitHub release attachment.
 [IO.File]::WriteAllText(($destination+'.manifest.json'),(@{productVersion=$version;releaseAssets=$assets;publishesRelease=$false}|ConvertTo-Json -Depth 5),[Text.UTF8Encoding]::new($false))
}
$assets | ForEach-Object {Write-Output ("ZIP asset: "+$_.file+"  "+$_.sha256)}
Write-Output 'No GitHub tag, upload or release action was performed.'
