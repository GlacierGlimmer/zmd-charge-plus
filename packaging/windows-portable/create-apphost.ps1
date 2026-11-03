param(
 [Parameter(Mandatory)][ValidateSet('win-x64','win-x86','win-arm64')][string]$Runtime,
 [Parameter(Mandatory)][string]$Payload,
 [Parameter(Mandatory)][string]$Output,
 [string]$ManagedAssemblyPath='Runtime\EndfieldChargePlus.dll',
 [string]$NativeLibraries=$env:ECP_NATIVE_ARM64_LIB,
 [string]$NativeCompiler=$env:ECP_NATIVE_ARM64_COMPILER
)
$ErrorActionPreference='Stop'
$repo=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$source=Join-Path ([IO.Path]::GetFullPath($Payload)) 'EndfieldChargePlus.exe'
$destination=[IO.Path]::GetFullPath($Output)
if(Test-Path -LiteralPath $destination){throw "Entry point already exists: $destination"}
if(!(Test-Path -LiteralPath $source)){throw 'A complete published application payload is required.'}
$vswhere=Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio/Installer/vswhere.exe'
$visualStudio=& $vswhere -latest -products '*' -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -property installationPath
if(!$visualStudio){throw 'Visual Studio C++ build tools are required for the native startup check.'}
$vcvars=Join-Path $visualStudio 'VC/Auxiliary/Build/vcvarsall.bat'
$work=Join-Path $repo ('artifacts/native-host/'+[guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Force -Path $work,(Split-Path $destination) | Out-Null
& python (Join-Path $PSScriptRoot 'build-host-inputs.py') $Payload $work $ManagedAssemblyPath
if($LASTEXITCODE -ne 0){throw 'Native host message/file manifest generation failed.'}
$binary=Join-Path $work 'EndfieldChargePlus.exe'
$response=@('/nologo','/std:c++17','/O2','/MT','/EHsc','/W4','/WX','/utf-8','/permissive-','/guard:cf',('/I"'+$work+'"'),('/Fo"'+(Join-Path $work 'portable-host.obj')+'"'),('"'+(Join-Path $PSScriptRoot 'portable-host.cpp')+'"'),'/link',('/OUT:"'+$binary+'"'),'/SUBSYSTEM:WINDOWS,10.00','/DYNAMICBASE','/NXCOMPAT','/GUARD:CF','/MANIFEST:NO','/OPT:REF','/OPT:ICF','user32.lib','shell32.lib') -join " "
$responsePath=Join-Path $work 'host.rsp';[IO.File]::WriteAllText($responsePath,$response,[Text.UTF8Encoding]::new($false))
if($Runtime -eq 'win-arm64' -and $NativeLibraries){
 $nativeLib=[IO.Path]::GetFullPath($NativeLibraries)
 if(!(Test-Path -LiteralPath (Join-Path $nativeLib 'libcpmt.lib'))){throw 'The supplied ARM64 C++ library directory is incomplete.'}
 # This host uses the modern wide-character CRT APIs; it has no legacy POSIX
 # aliases. Omit the optional OLDNAMES import library from minimal ARM64 tools.
 $response+=' /LIBPATH:"'+$nativeLib+'" /NODEFAULTLIB:OLDNAMES.lib';[IO.File]::WriteAllText($responsePath,$response,[Text.UTF8Encoding]::new($false))
}
$target=@{'win-x64'='amd64';'win-x86'='amd64_x86';'win-arm64'='amd64_arm64'}[$Runtime]
$toolVersion=(Get-Content -LiteralPath (Join-Path $visualStudio 'VC/Auxiliary/Build/Microsoft.VCToolsVersion.default.txt') -Raw).Trim()
$compiler=Join-Path $visualStudio ("VC/Tools/MSVC/$toolVersion/bin/Hostx64/"+$Runtime.Substring(4)+'/cl.exe')
if($Runtime -eq 'win-arm64' -and $NativeCompiler){$compiler=[IO.Path]::GetFullPath($NativeCompiler)}
if(!(Test-Path -LiteralPath $compiler)){throw "Install the $Runtime C++ compiler component; using a different architecture's compiler is not allowed."}
$response+=' /MACHINE:'+@{'win-x64'='X64';'win-x86'='X86';'win-arm64'='ARM64'}[$Runtime]
[IO.File]::WriteAllText($responsePath,$response,[Text.UTF8Encoding]::new($false))
$command="@echo off`r`ncall `"$vcvars`" $target >nul`r`nif errorlevel 1 exit /b %errorlevel%`r`nset `"PATH=$(Split-Path $compiler);%PATH%`"`r`n`"$compiler`" @`"$responsePath`"`r`nexit /b %errorlevel%`r`n"
$commandFile=Join-Path $work 'build.cmd';[IO.File]::WriteAllText($commandFile,$command,[Text.Encoding]::Default)
& $env:ComSpec /d /c $commandFile
if($LASTEXITCODE -ne 0 -or !(Test-Path -LiteralPath $binary)){throw "Native startup check build failed: $Runtime"}
$bytes=[IO.File]::ReadAllBytes($binary);$header=[BitConverter]::ToInt32($bytes,0x3c)
if([BitConverter]::ToUInt16($bytes,$header+4) -ne @{'win-x64'=0x8664;'win-x86'=0x14c;'win-arm64'=0xaa64}[$Runtime]){throw 'The compiler produced an entry point for the wrong architecture.'}
if(!('EcpNativeHostResources' -as [type])){Add-Type -Path (Join-Path $PSScriptRoot 'CopyHostResources.cs')}
[EcpNativeHostResources]::Copy($source,$binary)
Copy-Item -LiteralPath $binary -Destination $destination
Write-Output "PASS $Runtime native entry: 20-language recovery instructions, original icon/version/privilege resources, same-process self-contained startup."
