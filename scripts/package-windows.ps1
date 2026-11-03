param(
 [ValidateSet('all','win-x64','win-x86','win-arm64')][string]$Runtime='all',
 [string]$Output=(Join-Path $PSScriptRoot '../publish/windows-portable'),
 [string]$ZipOutput,
 [string]$SevenZipPath
)
$ErrorActionPreference='Stop'
$repo=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$delivery=[IO.Path]::GetFullPath($Output)
if($Runtime -eq 'all' -and $ZipOutput){throw 'Explicit archive filename requires a single Runtime.'}
if(!$SevenZipPath){
 $command=Get-Command 7z.exe -ErrorAction SilentlyContinue
 if($command){$SevenZipPath=$command.Source}
 elseif(Test-Path -LiteralPath (Join-Path $env:ProgramFiles '7-Zip/7z.exe')){$SevenZipPath=Join-Path $env:ProgramFiles '7-Zip/7z.exe'}
}
if(!$SevenZipPath -or !(Test-Path -LiteralPath $SevenZipPath)){throw 'Install 7-Zip or provide -SevenZipPath to create ZIP archives.'}
& python (Join-Path $PSScriptRoot 'localization/langtool.py') verify
if($LASTEXITCODE -ne 0){throw 'Invalid language resources'}
New-Item -ItemType Directory -Force -Path $delivery | Out-Null
$work=Join-Path $repo ('artifacts/portable-work/'+[guid]::NewGuid().ToString('N'))
$nugetRoot=if($env:NUGET_PACKAGES){$env:NUGET_PACKAGES}else{Join-Path $env:USERPROFILE '.nuget/packages'}
$runtimes=if($Runtime -eq 'all'){@('win-x64','win-x86','win-arm64')}else{@($Runtime)}
$products=@()
foreach($target in $runtimes){
 $version=([xml](Get-Content -LiteralPath (Join-Path $repo 'EndfieldChargePlus.csproj') -Raw)).Project.PropertyGroup.Version | Where-Object {$_} | Select-Object -First 1
 if($version -notmatch '^\d+\.\d+\.\d+$'){throw 'Expected a three-part product version.'}
 $rootName="EndfieldChargePlus$version"
 $stage=Join-Path $work $target
 $package=Join-Path $stage $rootName
 $payload=Join-Path $package 'Runtime'
 & dotnet publish (Join-Path $repo 'EndfieldChargePlus.csproj') -c Release -r $target --self-contained true -p:PublishSingleFile=false -p:IncludeNativeLibrariesForSelfExtract=false -p:PublishTrimmed=false -p:PublishReadyToRun=false -p:DebugType=none -p:DebugSymbols=false -p:NuGetAudit=false -o $payload
 if($LASTEXITCODE -ne 0){throw "Windows portable publish failed: $target"}
 foreach($required in @('EndfieldChargePlus.exe','EndfieldChargePlus.dll','EndfieldChargePlus.deps.json','EndfieldChargePlus.runtimeconfig.json','coreclr.dll','hostfxr.dll','hostpolicy.dll')){
  if(!(Test-Path -LiteralPath (Join-Path $payload $required))){throw "Missing self-contained application file: $required"}
 }
 $actualVersion=[Diagnostics.FileVersionInfo]::GetVersionInfo((Join-Path $payload 'EndfieldChargePlus.exe')).ProductVersion.Split('+')[0]
 if($actualVersion -ne $version){throw 'Built executable version differs from the requested package version.'}
 & (Join-Path $repo 'packaging/windows-portable/create-apphost.ps1') -Runtime $target -Payload $payload -Output (Join-Path $package "$rootName.exe")
 Remove-Item -LiteralPath (Join-Path $payload 'EndfieldChargePlus.exe')
 $documents=Join-Path $package 'Documents';$licenses=Join-Path $package 'Licenses'
 New-Item -ItemType Directory -Force -Path $documents,$licenses | Out-Null
 Copy-Item -LiteralPath (Join-Path $repo 'packaging/windows-portable/README.txt') -Destination $documents
 foreach($name in @('NOTICE.md','PRIVACY.md')){Copy-Item -LiteralPath (Join-Path $repo $name) -Destination $documents}
 Copy-Item -LiteralPath (Join-Path $repo 'LICENSE') -Destination (Join-Path $licenses 'LICENSE.txt')
 Copy-Item -LiteralPath (Join-Path $repo 'packaging/windows-portable/LICENSES.txt') -Destination (Join-Path $licenses 'README.txt')
 Copy-Item -LiteralPath (Join-Path $repo 'Assets/PresentMon/LICENSE.txt') -Destination (Join-Path $licenses 'PresentMon-LICENSE.txt')
 $fontSource=[IO.Path]::GetFullPath((Join-Path $payload 'licenses'));$fontTarget=[IO.Path]::GetFullPath((Join-Path $licenses 'Fonts'))
 if(!$fontSource.StartsWith($work+'\',[StringComparison]::OrdinalIgnoreCase) -or !$fontTarget.StartsWith($work+'\',[StringComparison]::OrdinalIgnoreCase)){throw 'Font license move escaped the build workspace.'}
 Move-Item -LiteralPath $fontSource -Destination $fontTarget
 $fileeLicense=Join-Path $fontTarget 'Filee-MIT.txt'
 if(Test-Path -LiteralPath $fileeLicense){Move-Item -LiteralPath $fileeLicense -Destination (Join-Path $licenses 'Filee-MIT.txt')}
 else{Copy-Item -LiteralPath (Join-Path $repo 'Licenses/Filee-MIT.txt') -Destination (Join-Path $licenses 'Filee-MIT.txt')}
 foreach($name in @('Lucide-LICENSE.txt','SimpleIcons-LICENSE.txt')) {
  $iconLicense=Join-Path $fontTarget $name
  if(Test-Path -LiteralPath $iconLicense){Move-Item -LiteralPath $iconLicense -Destination (Join-Path $licenses $name)}
 }
 Copy-Item -LiteralPath (Join-Path $repo 'Assets/Fonts/README.md') -Destination (Join-Path $licenses 'Fonts')
 $runtimeVersion=((Get-Content -LiteralPath (Join-Path $payload 'EndfieldChargePlus.runtimeconfig.json') -Raw | ConvertFrom-Json).runtimeOptions.includedFrameworks | Where-Object {$_.name -eq 'Microsoft.NETCore.App'}).version
 $runtimePack=Join-Path $nugetRoot "microsoft.netcore.app.runtime.$target/$runtimeVersion"
 $runtimeLicenses=Join-Path $licenses 'DotNet';New-Item -ItemType Directory -Force -Path $runtimeLicenses | Out-Null
 foreach($name in @('LICENSE.TXT','THIRD-PARTY-NOTICES.TXT')){Copy-Item -LiteralPath (Join-Path $runtimePack $name) -Destination $runtimeLicenses}
 if(@(Get-ChildItem -LiteralPath $package -Recurse -File -Filter '*.pdb').Count){throw 'Debug symbols must not be included in portable archives.'}
 $base="EndfieldChargePlus-v$version-$target-portable"
 $zip=if($ZipOutput){[IO.Path]::GetFullPath($ZipOutput)}else{Join-Path $delivery "$base.zip"}
 foreach($archive in @($zip)){if(Test-Path -LiteralPath $archive){throw "Archive already exists; choose a new output directory: $archive"};New-Item -ItemType Directory -Force -Path (Split-Path $archive) | Out-Null}
 Push-Location $stage
 try{
  & $SevenZipPath a -tzip -mx=7 -mmt=2 -bso0 -bsp0 $zip $rootName
  if($LASTEXITCODE -ne 0){throw 'ZIP creation failed'}
 }finally{Pop-Location}
 foreach($archive in @($zip)){
  & $SevenZipPath t -bso0 -bsp0 $archive
  if($LASTEXITCODE -ne 0){throw "Archive integrity check failed: $archive"}
  $products+=@{file=[IO.Path]::GetFileName($archive);runtime=$target;version=$version;root=$rootName;bytes=(Get-Item -LiteralPath $archive).Length;sha256=(Get-FileHash -LiteralPath $archive -Algorithm SHA256).Hash.ToLowerInvariant()}
 }
 Write-Output "PASS portable $target : $rootName/$rootName.exe with complete runtime; ZIP verified."
}
$reportName=if($Runtime -eq 'all'){'portable-build.json'}else{"portable-build-$Runtime.json"}
[IO.File]::WriteAllText((Join-Path $delivery $reportName),(@{format='self-contained-directory';work=$work;products=$products}|ConvertTo-Json -Depth 6),[Text.UTF8Encoding]::new($false))
$sums=($products | ForEach-Object {"$($_.sha256)  $($_.file)"}) -join "`n"
$sumsName=if($Runtime -eq 'all'){'SHA256SUMS.txt'}else{"SHA256SUMS-$Runtime.txt"}
[IO.File]::WriteAllText((Join-Path $delivery $sumsName),$sums+"`n",[Text.UTF8Encoding]::new($false))
