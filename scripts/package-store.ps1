param(
    [string]$X64Executable = (Join-Path $PSScriptRoot '../publish/win-x64-portable/EndfieldChargePlus.exe'),
    [string]$X86Executable = (Join-Path $PSScriptRoot '../publish/win-x86-portable/EndfieldChargePlus.exe'),
    [string]$Output = (Join-Path $PSScriptRoot '../publish/MicrosoftStore'),
    [ValidatePattern('^\d+\.\d+\.\d+\.\d+$')][string]$PackageVersion = '1.0.2.0'
)
$ErrorActionPreference = 'Stop'
$delivery = [IO.Path]::GetFullPath($Output)
$taskRoot = Join-Path $PSScriptRoot '../artifacts/store-work'
# A fresh staging directory prevents old ARM64 MSIX files from entering a bundle.
$storeWork = Join-Path $taskRoot ([guid]::NewGuid().ToString('N'))
$taskRoot = $storeWork
$kit = Join-Path $PSScriptRoot '../packaging/windows-store'
$makeAppx = @(Get-ChildItem -LiteralPath 'C:\Program Files (x86)\Windows Kits\10\bin' -Directory |
    Sort-Object Name -Descending | ForEach-Object { Join-Path $_.FullName 'x64/makeappx.exe' } |
    Where-Object { Test-Path -LiteralPath $_ -PathType Leaf } | Select-Object -First 1)
if ($makeAppx.Count -ne 1) { throw 'Windows SDK MakeAppx is required.' }
$makeAppx = $makeAppx[0]
[xml]$project = Get-Content -LiteralPath (Join-Path $PSScriptRoot '../EndfieldChargePlus.csproj')
$productVersion = @($project.Project.PropertyGroup.Version | Where-Object { $_ })[0]
$packageVersion = $PackageVersion
$executables = @{ x64 = [IO.Path]::GetFullPath($X64Executable); x86 = [IO.Path]::GetFullPath($X86Executable) }
foreach ($arch in @('x64','x86')) {
    $bytes = [IO.File]::ReadAllBytes($executables[$arch])
    $pe = [BitConverter]::ToInt32($bytes,0x3c)
    $machine = [BitConverter]::ToUInt16($bytes,$pe+4)
    if ($machine -ne @{ x64 = 0x8664; x86 = 0x014c }[$arch]) { throw "Incorrect $arch executable architecture." }
}
$packageDir = Join-Path $storeWork 'packages'
New-Item -ItemType Directory -Force -Path $delivery,$packageDir | Out-Null
foreach ($arch in @('x64','x86')) {
    $stage = Join-Path $storeWork "staging-$arch"
    $app = Join-Path $stage 'App'
    $assets = Join-Path $stage 'Assets'
    New-Item -ItemType Directory -Force -Path $app,$assets | Out-Null
    Copy-Item -LiteralPath $executables[$arch] -Destination $app
    Get-ChildItem -LiteralPath (Join-Path $kit 'Assets') -File | Copy-Item -Destination $assets
    $manifest = @"
<?xml version="1.0" encoding="utf-8"?>
<Package xmlns="http://schemas.microsoft.com/appx/manifest/foundation/windows10"
 xmlns:uap="http://schemas.microsoft.com/appx/manifest/uap/windows10"
 xmlns:uap10="http://schemas.microsoft.com/appx/manifest/uap/windows10/10"
 xmlns:uap5="http://schemas.microsoft.com/appx/manifest/uap/windows10/5"
 xmlns:uap7="http://schemas.microsoft.com/appx/manifest/uap/windows10/7"
 xmlns:rescap="http://schemas.microsoft.com/appx/manifest/foundation/windows10/restrictedcapabilities"
 IgnorableNamespaces="uap uap5 uap7 uap10 rescap">
 <Identity Name="GlacierGlimmer.EndfieldChargePlus" Publisher="CN=2630DF61-646F-4490-BDEC-93D9A63975E7" Version="$packageVersion" ProcessorArchitecture="$arch" />
 <Properties><DisplayName>Endfield Charge Plus</DisplayName><PublisherDisplayName>GlacierGlimmer冰川雪貓</PublisherDisplayName><Description>Endfield-style configurable Windows status HUD.</Description><Logo>Assets\StoreLogo.png</Logo></Properties>
 <Resources><Resource Language="zh-CN" /><Resource Language="en-US" /></Resources>
 <Dependencies><TargetDeviceFamily Name="Windows.Desktop" MinVersion="10.0.19041.0" MaxVersionTested="10.0.26100.0" /></Dependencies>
 <Applications><Application Id="App" Executable="App\EndfieldChargePlus.exe" uap10:RuntimeBehavior="packagedClassicApp" uap10:TrustLevel="mediumIL">
  <uap:VisualElements DisplayName="Endfield Charge Plus" Description="Endfield-style configurable Windows status HUD" Square150x150Logo="Assets\Square150x150Logo.png" Square44x44Logo="Assets\Square44x44Logo.png" BackgroundColor="transparent" />
  <Extensions><uap5:Extension Category="windows.startupTask" Executable="App\EndfieldChargePlus.exe" EntryPoint="Windows.FullTrustApplication"><uap5:StartupTask TaskId="EndfieldChargePlusStartup" Enabled="false" DisplayName="Endfield Charge Plus" /></uap5:Extension></Extensions>
 </Application></Applications>
 <Capabilities><Capability Name="internetClient" /><uap7:Capability Name="globalMediaControl" /><rescap:Capability Name="runFullTrust" /></Capabilities>
</Package>
"@
    [xml]$manifest | Out-Null
    [IO.File]::WriteAllText((Join-Path $stage 'AppxManifest.xml'),$manifest,[Text.UTF8Encoding]::new($false))
    $msix = Join-Path $packageDir "EndfieldChargePlus-v$productVersion-store-$packageVersion-win-$arch.msix"
    & $makeAppx pack /d $stage /p $msix /o /h SHA256 *> (Join-Path $taskRoot "store-$arch-pack.log")
    if ($LASTEXITCODE -ne 0) { Get-Content (Join-Path $taskRoot "store-$arch-pack.log") -Tail 25; throw 'MSIX validation or packing failed' }
}
$base = "EndfieldChargePlus-v$productVersion-store-$packageVersion-windows-x64-x86"
$bundle = Join-Path $delivery "$base.msixbundle"
& $makeAppx bundle /d $packageDir /p $bundle /bv $packageVersion /o *> (Join-Path $taskRoot 'store-bundle.log')
if ($LASTEXITCODE -ne 0) { Get-Content (Join-Path $taskRoot 'store-bundle.log') -Tail 25; throw 'Bundle packing failed' }
# MakeAppx validates schemas while unpacking each package and the bundle.
$validation = Join-Path $storeWork 'validated-bundle'
& $makeAppx unbundle /p $bundle /d $validation /o *> (Join-Path $taskRoot 'store-unbundle.log')
if ($LASTEXITCODE -ne 0) { throw 'Bundle validation failed' }
foreach ($arch in @('x64','x86')) {
    $msix = Join-Path $validation "EndfieldChargePlus-v$productVersion-store-$packageVersion-win-$arch.msix"
    $unpacked = Join-Path $storeWork "validated-$arch"
    & $makeAppx unpack /p $msix /d $unpacked /o *> (Join-Path $taskRoot "store-$arch-unpack.log")
    if ($LASTEXITCODE -ne 0) { throw 'Package validation failed' }
    if ((Get-FileHash (Join-Path $unpacked 'App\EndfieldChargePlus.exe')).Hash -ne (Get-FileHash $executables[$arch]).Hash) { throw 'Store payload differs from verified Windows release' }
}
$tempZip = Join-Path $storeWork 'upload.zip'
Compress-Archive -LiteralPath $bundle -DestinationPath $tempZip -CompressionLevel Optimal -Force
Move-Item -LiteralPath $tempZip -Destination (Join-Path $delivery "$base.msixupload") -Force
Write-Output "Microsoft Store: product v$productVersion, package $packageVersion, x64/x86 validated; unsigned submission bundle and upload exported."
