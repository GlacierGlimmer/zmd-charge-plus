param(
 [string]$X64Payload,
 [string]$X86Payload,
 [string]$Arm64Payload,
 [string]$Output=(Join-Path $PSScriptRoot '../publish/MicrosoftStore'),
 [ValidatePattern('^\d+\.\d+\.\d+\.\d+$')][string]$PackageVersion,
 [ValidatePattern('^\d+\.\d+\.\d+\.\d+$')][string]$PreviousPackageVersion
)
$ErrorActionPreference='Stop'
$repo=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'));$delivery=[IO.Path]::GetFullPath($Output)
& python (Join-Path $PSScriptRoot 'localization/langtool.py') verify
if($LASTEXITCODE -ne 0){throw 'Invalid language resources'}
$work=Join-Path $repo ('artifacts/store-work/'+[guid]::NewGuid().ToString('N'))
$packages=Join-Path $work 'packages';New-Item -ItemType Directory -Force -Path $delivery,$packages | Out-Null
$makeAppx=@(Get-ChildItem -LiteralPath 'C:\Program Files (x86)\Windows Kits\10\bin' -Directory | Sort-Object Name -Descending | ForEach-Object {Join-Path $_.FullName 'x64/makeappx.exe'} | Where-Object {Test-Path -LiteralPath $_} | Select-Object -First 1)[0]
$payloads=@{x64=$X64Payload;x86=$X86Payload;arm64=$Arm64Payload}
foreach($arch in @('x64','x86','arm64')){
 if(!$payloads[$arch]){
  $payloads[$arch]=Join-Path $work "payload-$arch"
  & dotnet publish (Join-Path $repo 'EndfieldChargePlus.csproj') -c Release -r "win-$arch" --self-contained true -p:PublishSingleFile=false -p:PublishTrimmed=false -p:PublishReadyToRun=false -p:DebugType=none -p:DebugSymbols=false -p:NuGetAudit=false -o $payloads[$arch]
  if($LASTEXITCODE -ne 0){throw 'Full Store application payload publish failed'}
 }
 $payloads[$arch]=[IO.Path]::GetFullPath($payloads[$arch])
}
$catalog=Get-Content -LiteralPath (Join-Path $repo 'Localization/reviewed-language-catalog.json') -Raw | ConvertFrom-Json
$resources=($catalog.languages | ForEach-Object {'<Resource Language="'+$_.code+'" />'}) -join ''
$productVersion=[Diagnostics.FileVersionInfo]::GetVersionInfo((Join-Path $payloads.x64 'EndfieldChargePlus.exe')).ProductVersion.Split('+')[0]
$projectVersion=([xml](Get-Content -LiteralPath (Join-Path $repo 'EndfieldChargePlus.csproj') -Raw)).Project.PropertyGroup.Version | Where-Object {$_} | Select-Object -First 1
if($productVersion -ne $projectVersion){throw "Store payload version $productVersion differs from the source version $projectVersion."}
foreach($arch in @('x64','x86','arm64')){
 $inputVersion=[Diagnostics.FileVersionInfo]::GetVersionInfo((Join-Path $payloads[$arch] 'EndfieldChargePlus.exe')).ProductVersion.Split('+')[0]
 if($inputVersion -ne $productVersion){throw "Mixed software versions in Store payloads: $arch is $inputVersion; expected $productVersion."}
}
if($productVersion -notmatch '^\d+\.\d+\.\d+$'){throw 'Expected a three-part software version for the Store version mapping.'}
$softwareVersion=[version]$productVersion
$packageMajor=[long]$softwareVersion.Major+1
if($packageMajor -gt 65535 -or $softwareVersion.Minor -gt 65535 -or $softwareVersion.Build -gt 65535){throw 'A mapped Store version component exceeds the MSIX component limit.'}
$expectedPackageVersion="$packageMajor.$($softwareVersion.Minor).$($softwareVersion.Build).0"
if(!$PackageVersion){$PackageVersion=$expectedPackageVersion}
elseif($PackageVersion -ne $expectedPackageVersion){throw "Store version policy is (a+1).b.c.0 for software a.b.c; expected $expectedPackageVersion."}
if($PreviousPackageVersion -and [version]$PackageVersion -le [version]$PreviousPackageVersion){throw "Store update must increase its package version: $PreviousPackageVersion -> $PackageVersion. Check the highest applicable version in Partner Center before submission."}
foreach($arch in @('x64','x86','arm64')){
 $payload=$payloads[$arch];if(!(Test-Path (Join-Path $payload 'coreclr.dll'))){throw 'Store requires a complete self-contained runtime, not a single portable EXE.'}
 $stage=Join-Path $work "staging-$arch";New-Item -ItemType Directory -Force -Path $stage | Out-Null
 $runtime=Join-Path $stage 'Runtime'
 Copy-Item -LiteralPath $payload -Destination $runtime -Recurse
 & (Join-Path $repo 'packaging/windows-portable/create-apphost.ps1') -Runtime "win-$arch" -Payload $payload -ManagedAssemblyPath '..\Runtime\EndfieldChargePlus.dll' -Output (Join-Path $stage 'App/EndfieldChargePlus.exe')
 Remove-Item -LiteralPath (Join-Path $runtime 'EndfieldChargePlus.exe')
 $documents=Join-Path $stage 'Documents';$licenses=Join-Path $stage 'Licenses'
 New-Item -ItemType Directory -Force -Path $documents,$licenses | Out-Null
 Copy-Item -LiteralPath (Join-Path $repo 'packaging/windows-store/README.txt') -Destination $documents
 foreach($name in @('PRIVACY.md','NOTICE.md')){Copy-Item -LiteralPath (Join-Path $repo $name) -Destination $documents}
 Copy-Item -LiteralPath (Join-Path $repo 'LICENSE') -Destination (Join-Path $licenses 'LICENSE.txt')
 Copy-Item -LiteralPath (Join-Path $repo 'packaging/windows-portable/LICENSES.txt') -Destination (Join-Path $licenses 'README.txt')
 Copy-Item -LiteralPath (Join-Path $repo 'Assets/PresentMon/LICENSE.txt') -Destination (Join-Path $licenses 'PresentMon-LICENSE.txt')
 $fontSource=[IO.Path]::GetFullPath((Join-Path $runtime 'licenses'));$fontTarget=[IO.Path]::GetFullPath((Join-Path $licenses 'Fonts'))
 if(!$fontSource.StartsWith($work+'\',[StringComparison]::OrdinalIgnoreCase) -or !$fontTarget.StartsWith($work+'\',[StringComparison]::OrdinalIgnoreCase)){throw 'License move escaped Store staging.'}
 Move-Item -LiteralPath $fontSource -Destination $fontTarget
 $fileeLicense=Join-Path $fontTarget 'Filee-MIT.txt'
 if(Test-Path -LiteralPath $fileeLicense){Move-Item -LiteralPath $fileeLicense -Destination (Join-Path $licenses 'Filee-MIT.txt')}
 else{Copy-Item -LiteralPath (Join-Path $repo 'Licenses/Filee-MIT.txt') -Destination (Join-Path $licenses 'Filee-MIT.txt')}
 foreach($name in @('Lucide-LICENSE.txt','SimpleIcons-LICENSE.txt')) {
  $iconLicense=Join-Path $fontTarget $name
  if(Test-Path -LiteralPath $iconLicense){Move-Item -LiteralPath $iconLicense -Destination (Join-Path $licenses $name)}
 }
 Copy-Item -LiteralPath (Join-Path $repo 'Assets/Fonts/README.md') -Destination (Join-Path $licenses 'Fonts')
 $nugetRoot=if($env:NUGET_PACKAGES){$env:NUGET_PACKAGES}else{Join-Path $env:USERPROFILE '.nuget/packages'}
 $runtimeVersion=((Get-Content -LiteralPath (Join-Path $runtime 'EndfieldChargePlus.runtimeconfig.json') -Raw | ConvertFrom-Json).runtimeOptions.includedFrameworks | Where-Object {$_.name -eq 'Microsoft.NETCore.App'}).version
 $runtimePack=Join-Path $nugetRoot "microsoft.netcore.app.runtime.win-$arch/$runtimeVersion"
 $runtimeLicenses=Join-Path $licenses 'DotNet';New-Item -ItemType Directory -Force -Path $runtimeLicenses | Out-Null
 foreach($name in @('LICENSE.TXT','THIRD-PARTY-NOTICES.TXT')){Copy-Item -LiteralPath (Join-Path $runtimePack $name) -Destination $runtimeLicenses}
 foreach($name in @('LICENSE','NOTICE.md','PRIVACY.md')){if(Test-Path -LiteralPath (Join-Path $runtime $name)){Remove-Item -LiteralPath (Join-Path $runtime $name)}}
 if(@(Get-ChildItem -LiteralPath $stage -File -Filter '*.dll').Count){throw 'Store package must not have loose top-level DLLs'}
 Copy-Item -LiteralPath (Join-Path $repo 'packaging/windows-store/Assets') -Destination (Join-Path $stage 'Assets') -Recurse
 $manifest=@"
<?xml version="1.0" encoding="utf-8"?>
<Package xmlns="http://schemas.microsoft.com/appx/manifest/foundation/windows10"
 xmlns:uap="http://schemas.microsoft.com/appx/manifest/uap/windows10"
 xmlns:uap3="http://schemas.microsoft.com/appx/manifest/uap/windows10/3"
 xmlns:uap5="http://schemas.microsoft.com/appx/manifest/uap/windows10/5"
 xmlns:uap7="http://schemas.microsoft.com/appx/manifest/uap/windows10/7"
 xmlns:uap10="http://schemas.microsoft.com/appx/manifest/uap/windows10/10"
 xmlns:rescap="http://schemas.microsoft.com/appx/manifest/foundation/windows10/restrictedcapabilities"
 IgnorableNamespaces="uap uap3 uap5 uap7 uap10 rescap">
 <Identity Name="GlacierGlimmer.EndfieldChargePlus" Publisher="CN=2630DF61-646F-4490-BDEC-93D9A63975E7" Version="$PackageVersion" ProcessorArchitecture="$arch"/>
 <Properties><DisplayName>Endfield Charge Plus</DisplayName><PublisherDisplayName>GlacierGlimmer冰川雪貓</PublisherDisplayName><Description>Endfield Charge Plus</Description><Logo>Assets\StoreLogo.png</Logo></Properties>
 <Resources>$resources</Resources>
 <Dependencies><TargetDeviceFamily Name="Windows.Desktop" MinVersion="10.0.19041.0" MaxVersionTested="10.0.26100.0"/></Dependencies>
 <Applications>
  <Application Id="App" Executable="App\EndfieldChargePlus.exe" uap10:RuntimeBehavior="packagedClassicApp" uap10:TrustLevel="mediumIL">
   <uap:VisualElements DisplayName="Endfield Charge Plus" Description="Endfield Charge Plus" Square150x150Logo="Assets\Square150x150Logo.png" Square44x44Logo="Assets\Square44x44Logo.png" BackgroundColor="transparent"/>
   <Extensions><uap5:Extension Category="windows.startupTask" Executable="App\EndfieldChargePlus.exe" EntryPoint="Windows.FullTrustApplication"><uap5:StartupTask TaskId="EndfieldChargePlusStartup" Enabled="false" DisplayName="Endfield Charge Plus"/></uap5:Extension></Extensions>
  </Application>
 </Applications>
 <Capabilities><Capability Name="internetClient"/><uap7:Capability Name="globalMediaControl"/><rescap:Capability Name="runFullTrust"/><rescap:Capability Name="allowElevation"/></Capabilities>
</Package>
"@
 [xml]$manifest | Out-Null;[IO.File]::WriteAllText((Join-Path $stage 'AppxManifest.xml'),$manifest,[Text.UTF8Encoding]::new($false))
 $msix=Join-Path $packages "EndfieldChargePlus-v$productVersion-store-$PackageVersion-win-$arch.msix"
 & $makeAppx pack /d $stage /p $msix /o /h SHA256 *> (Join-Path $work "pack-$arch.log")
 if($LASTEXITCODE -ne 0){Get-Content (Join-Path $work "pack-$arch.log") -Tail 22;throw 'MSIX schema/package validation failed'}
}
$base="EndfieldChargePlus-v$productVersion-store-$PackageVersion-windows-x64-x86-arm64";$bundle=Join-Path $delivery "$base.msixbundle"
& $makeAppx bundle /d $packages /p $bundle /bv $PackageVersion /o *> (Join-Path $work 'bundle.log')
if($LASTEXITCODE -ne 0){throw 'Bundle failed'}
& $makeAppx unbundle /p $bundle /d (Join-Path $work 'validated') /o *> (Join-Path $work 'unbundle.log')
if($LASTEXITCODE -ne 0){throw 'Bundle validation failed'}
foreach($arch in @('x64','x86','arm64')){
 & $makeAppx unpack /p (Join-Path $work "validated/EndfieldChargePlus-v$productVersion-store-$PackageVersion-win-$arch.msix") /d (Join-Path $work "unpacked-$arch") /o *> (Join-Path $work "unpack-$arch.log")
 if($LASTEXITCODE -ne 0){throw 'Unpack validation failed'}
 foreach($name in @('App/EndfieldChargePlus.exe','Runtime/EndfieldChargePlus.dll','Runtime/coreclr.dll')){
  $file=Join-Path $work "unpacked-$arch/$name";if(!(Test-Path $file)){throw "Missing installed file: $name"}
  if($name.EndsWith('.exe')){$b=[IO.File]::ReadAllBytes($file);$o=[BitConverter]::ToInt32($b,0x3c);if([BitConverter]::ToUInt16($b,$o+4) -ne @{x64=0x8664;x86=0x014c;arm64=0xaa64}[$arch]){throw 'Store executable architecture mismatch'}}
 }
 if((Get-FileHash (Join-Path $work "unpacked-$arch/Runtime/EndfieldChargePlus.dll")).Hash -ne (Get-FileHash (Join-Path $payloads[$arch] 'EndfieldChargePlus.dll')).Hash){throw 'Store source payload mismatch'}
}
$tempZip=Join-Path $work 'upload.zip';Compress-Archive -LiteralPath $bundle -DestinationPath $tempZip -CompressionLevel Optimal -Force;Move-Item -LiteralPath $tempZip -Destination (Join-Path $delivery "$base.msixupload") -Force
[IO.File]::WriteAllText((Join-Path $delivery 'store-build.json'),(@{work=$work;productVersion=$productVersion;packageVersion=$PackageVersion;versionMapping='software a.b.c -> Store (a+1).b.c.0';architectures=@('x64','x86','arm64');allowElevationRequiresStoreApproval=$true;nativeRuntime=$true;organizedRuntime=$true;singleApplication=$true}|ConvertTo-Json),[Text.UTF8Encoding]::new($false))
Write-Output 'PASS full-runtime Store packages: three native architectures, one App entry, original ordinary-app startup task. Store capability approval and installed Store runtime acceptance remain external checks.'
