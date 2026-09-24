param(
    [switch]$NoClean,
    [ValidateSet('all', 'win-x64', 'win-x86', 'win-arm64')]
    [string]$Runtime = 'all'
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$Root = Split-Path -Parent $MyInvocation.MyCommand.Path
Set-Location $Root

$Project = Join-Path $Root 'EndfieldChargePlus.csproj'
if (-not (Test-Path -LiteralPath $Project)) {
    throw 'EndfieldChargePlus.csproj not found.'
}

[xml]$ProjectXml = Get-Content -LiteralPath $Project -Raw
$Version = [string]($ProjectXml.Project.PropertyGroup.Version | Select-Object -First 1)
$AssemblyVersion = [string]($ProjectXml.Project.PropertyGroup.AssemblyVersion | Select-Object -First 1)
$FileVersion = [string]($ProjectXml.Project.PropertyGroup.FileVersion | Select-Object -First 1)

if ([string]::IsNullOrWhiteSpace($Version)) {
    throw 'Project Version is missing.'
}

$versionParts = $Version.Split('.')
if ($versionParts.Count -ne 3) {
    throw "Project Version must use three-part SemVer (for example 0.1.0). Current: $Version"
}

$ExpectedFourPartVersion = "$Version.0"
if ($AssemblyVersion -ne $ExpectedFourPartVersion) {
    throw "AssemblyVersion mismatch. Expected $ExpectedFourPartVersion, got $AssemblyVersion."
}
if ($FileVersion -ne $ExpectedFourPartVersion) {
    throw "FileVersion mismatch. Expected $ExpectedFourPartVersion, got $FileVersion."
}

$ManifestPath = Join-Path $Root 'app.manifest'
if (-not (Test-Path -LiteralPath $ManifestPath)) {
    throw 'app.manifest not found.'
}
[xml]$ManifestXml = Get-Content -LiteralPath $ManifestPath -Raw
$Identity = $ManifestXml.SelectSingleNode("/*[local-name()='assembly']/*[local-name()='assemblyIdentity']")
if ($null -eq $Identity) {
    throw 'app.manifest assemblyIdentity is missing.'
}
if (([string]$Identity.version) -ne $ExpectedFourPartVersion) {
    throw "app.manifest version mismatch. Expected $ExpectedFourPartVersion, got $($Identity.version)."
}

$SupportedOs = $ManifestXml.SelectSingleNode("//*[local-name()='supportedOS']")
$Windows10PlusGuid = '{8e0f7a12-bfb3-4fe8-b9a5-48fd50a15a9a}'
if ($null -eq $SupportedOs -or ([string]$SupportedOs.Id) -ne $Windows10PlusGuid) {
    throw "app.manifest must declare the Windows 10/11 supportedOS GUID $Windows10PlusGuid."
}

$Architectures = @(
    [pscustomobject]@{ Rid = 'win-x64';   Label = 'x64' },
    [pscustomobject]@{ Rid = 'win-x86';   Label = 'x86' },
    [pscustomobject]@{ Rid = 'win-arm64'; Label = 'ARM64' }
)

$SelectedArchitectures = @(
    if ($Runtime -eq 'all') {
        $Architectures
    }
    else {
        $Architectures | Where-Object { $_.Rid -eq $Runtime }
    }
)

if ($SelectedArchitectures.Count -eq 0) {
    throw "No matching runtime selected: $Runtime"
}

$PublishRoot = Join-Path $Root 'publish'
$DistRoot = Join-Path $Root 'dist'
$PortableDir = Join-Path $DistRoot 'Portable'

function Invoke-DotNet {
    param([Parameter(Mandatory = $true)][string[]]$Arguments)
    & dotnet @Arguments
    if ($LASTEXITCODE -ne 0) {
        throw "dotnet $($Arguments -join ' ') failed with exit code $LASTEXITCODE."
    }
}

function Test-AbsoluteSingleExe {
    param(
        [Parameter(Mandatory = $true)][string]$Directory,
        [Parameter(Mandatory = $true)][string]$Rid
    )

    $files = @(Get-ChildItem -LiteralPath $Directory -File -Recurse)
    $dirs = @(Get-ChildItem -LiteralPath $Directory -Directory -Recurse)
    $ok = (
        $files.Count -eq 1 -and
        $files[0].Name -eq 'EndfieldChargePlus.exe' -and
        $dirs.Count -eq 0
    )

    if (-not $ok) {
        Write-Host "Single-file verification failed for $Rid. Publish output:" -ForegroundColor Red
        Get-ChildItem -LiteralPath $Directory -Recurse | ForEach-Object {
            Write-Host ('  ' + $_.FullName) -ForegroundColor Red
        }
        throw "$Rid publish output is not an absolute single-file EXE."
    }

    Write-Host "  [$Rid] single-file verification passed." -ForegroundColor Green
}

Write-Host ''
Write-Host '========================================================' -ForegroundColor Cyan
Write-Host " Endfield Charge Plus v$Version Portable Build" -ForegroundColor Cyan
if ($Runtime -eq 'all') {
    Write-Host ' x64 + x86 + ARM64 | Direct single-file EXE releases' -ForegroundColor Cyan
}
else {
    Write-Host " Selected runtime: $Runtime | Direct single-file EXE test/release build" -ForegroundColor Cyan
}
Write-Host '========================================================' -ForegroundColor Cyan
Write-Host ''

Write-Host '[preflight] Verifying variable implementation coverage...' -ForegroundColor Yellow
$VariableAudit = Join-Path $Root 'verify-variable-coverage.ps1'
if (-not (Test-Path -LiteralPath $VariableAudit)) {
    throw 'verify-variable-coverage.ps1 not found.'
}
& $VariableAudit

if (-not $NoClean) {
    Write-Host '[1/5] Cleaning...' -ForegroundColor Yellow
    Invoke-DotNet @('clean', $Project, '-c', 'Release')
}
else {
    Write-Host '[1/5] Cleaning skipped.' -ForegroundColor DarkGray
}

Write-Host '[2/5] Restoring packages for all configured RIDs...' -ForegroundColor Yellow
Invoke-DotNet @('restore', $Project)

Write-Host '[3/5] Building Release...' -ForegroundColor Yellow
Invoke-DotNet @('build', $Project, '-c', 'Release', '--no-restore')

Write-Host '[4/5] Publishing absolute single-file executables...' -ForegroundColor Yellow
if (Test-Path -LiteralPath $PublishRoot) { Remove-Item -LiteralPath $PublishRoot -Recurse -Force }
New-Item -ItemType Directory -Path $PublishRoot -Force | Out-Null

foreach ($arch in $SelectedArchitectures) {
    $publishDir = Join-Path $PublishRoot $arch.Rid
    New-Item -ItemType Directory -Path $publishDir -Force | Out-Null

    Write-Host "  Publishing $($arch.Rid)..." -ForegroundColor Cyan
    Invoke-DotNet @(
        'publish', $Project,
        '-c', 'Release',
        '-r', $arch.Rid,
        '--self-contained', 'true',
        '--no-restore',
        '-p:PublishSingleFile=true',
        '-p:IncludeNativeLibrariesForSelfExtract=true',
        '-p:IncludeAllContentForSelfExtract=true',
        '-p:PublishTrimmed=false',
        '-p:DebugType=None',
        '-p:DebugSymbols=false',
        '-o', $publishDir
    )

    $exe = Join-Path $publishDir 'EndfieldChargePlus.exe'
    if (-not (Test-Path -LiteralPath $exe)) {
        throw "$($arch.Rid): EndfieldChargePlus.exe was not produced."
    }

    Test-AbsoluteSingleExe -Directory $publishDir -Rid $arch.Rid
}

Write-Host '[5/5] Creating final direct-download EXE releases...' -ForegroundColor Yellow
if (Test-Path -LiteralPath $DistRoot) { Remove-Item -LiteralPath $DistRoot -Recurse -Force }
New-Item -ItemType Directory -Path $PortableDir -Force | Out-Null

foreach ($arch in $SelectedArchitectures) {
    $sourceExe = Join-Path (Join-Path $PublishRoot $arch.Rid) 'EndfieldChargePlus.exe'
    $releaseName = "EndfieldChargePlus-v$Version-$($arch.Rid)-portable.exe"
    $releasePath = Join-Path $PortableDir $releaseName

    Copy-Item -LiteralPath $sourceExe -Destination $releasePath -Force

    if (-not (Test-Path -LiteralPath $releasePath -PathType Leaf)) {
        throw "Missing final portable EXE: $releasePath"
    }

    Write-Host "  [$($arch.Rid)] created: $releaseName" -ForegroundColor Green
}

$finalFiles = @(Get-ChildItem -LiteralPath $PortableDir -File)
$finalDirs = @(Get-ChildItem -LiteralPath $PortableDir -Directory)
$expectedNames = @($SelectedArchitectures | ForEach-Object { "EndfieldChargePlus-v$Version-$($_.Rid)-portable.exe" })
$actualNames = @($finalFiles | ForEach-Object { $_.Name })

$expectedFileCount = $SelectedArchitectures.Count
if ($finalDirs.Count -ne 0 -or $finalFiles.Count -ne $expectedFileCount) {
    throw "Final Portable directory must contain exactly $expectedFileCount EXE file(s) and no subdirectories."
}

foreach ($name in $expectedNames) {
    if ($actualNames -notcontains $name) {
        throw "Final package verification failed. Missing: $name"
    }
}

$hashLines = @()
foreach ($name in $expectedNames) {
    $path = Join-Path $PortableDir $name
    $hash = (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash.ToLowerInvariant()
    $hashLines += "$hash  $name"
}
$hashPath = Join-Path $DistRoot 'SHA256SUMS.txt'
Set-Content -LiteralPath $hashPath -Value $hashLines -Encoding ASCII
Write-Host "  SHA-256 manifest created: $hashPath" -ForegroundColor Green

Write-Host ''
Write-Host 'Release build completed.' -ForegroundColor Green
Write-Host ''
Write-Host 'Direct-download portable executables:' -ForegroundColor Green
foreach ($arch in $SelectedArchitectures) {
    Write-Host ('  ' + (Join-Path $PortableDir "EndfieldChargePlus-v$Version-$($arch.Rid)-portable.exe")) -ForegroundColor Green
}
Write-Host ''
Write-Host ('SHA-256 manifest: ' + (Join-Path $DistRoot 'SHA256SUMS.txt')) -ForegroundColor Green
Write-Host ''
Write-Host 'No ZIP/7z and no Setup/MSI packages are generated.' -ForegroundColor Cyan
Write-Host 'Microsoft Store packaging is maintained separately and will provide the WinGet/msstore installation path.' -ForegroundColor Cyan
if ($Runtime -eq 'all') {
    Write-Host 'IMPORTANT: test x64, x86 and ARM64 on suitable Windows environments before advertising full architecture support.' -ForegroundColor Cyan
}
else {
    Write-Host "IMPORTANT: this build contains only $Runtime. Run the full multi-architecture build later with the default script." -ForegroundColor Cyan
}
