param(
    [switch]$SkipInstaller,

    # CI surumu etiketten hesaplayip buradan gecirir. Bos birakilirsa
    # Directory.Build.props icindeki deger kullanilir (yerel derleme).
    [string]$Version
)

$ErrorActionPreference = 'Stop'

$ScriptRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$RepoRoot = Resolve-Path (Join-Path $ScriptRoot '..')
$ProjectPath = Join-Path $ScriptRoot 'RevivalDPI\RevivalDPI.csproj'
$PublishDir = Join-Path $RepoRoot 'publish\RevivalDPI-win-x64'
$SetupScript = Join-Path $ScriptRoot 'RevivalDPI-Setup.iss'

# -Version verilmediginde Directory.Build.props baslangic degeri olarak okunur
# (yerel derleme ve ilk yayin). Yayimlanan surumun kaynagi son 'v*' etiketidir
# ve CI onu -Version ile buraya gecirir. Deger ISCC'ye de /D ile aktarilir.
$PropsPath = Join-Path $RepoRoot 'Directory.Build.props'
if (-not (Test-Path $PropsPath)) {
    throw "Directory.Build.props bulunamadi: $PropsPath"
}
if ($Version) {
    $AppVersion = $Version
    Write-Host "RevivalDPI surumu: $AppVersion (parametreden)"
}
else {
    $propsText = Get-Content $PropsPath -Raw
    if ($propsText -notmatch '<RevivalDpiVersion>([\d.]+)</RevivalDpiVersion>') {
        throw 'Directory.Build.props icinde RevivalDpiVersion bulunamadi.'
    }
    $AppVersion = $Matches[1]
    Write-Host "RevivalDPI surumu: $AppVersion (Directory.Build.props)"
}

$publishFullPath = [System.IO.Path]::GetFullPath($PublishDir)
$runningFromPublish = Get-Process RevivalDPI -ErrorAction SilentlyContinue | Where-Object {
    try {
        $_.Path -and [System.IO.Path]::GetFullPath($_.Path).StartsWith($publishFullPath, [System.StringComparison]::OrdinalIgnoreCase)
    }
    catch {
        $false
    }
}

if ($runningFromPublish) {
    $ids = ($runningFromPublish | ForEach-Object { $_.Id }) -join ', '
    throw "RevivalDPI is running from the publish output and is locking files. Close RevivalDPI.exe first. Process id(s): $ids"
}

Write-Host 'Publishing RevivalDPI win-x64 self-contained build...'
dotnet publish $ProjectPath `
    -c Release `
    -r win-x64 `
    -p:RevivalDpiVersion=$AppVersion `
    --self-contained true `
    -p:PublishSingleFile=false `
    -p:PublishTrimmed=false `
    -p:DebugType=none `
    -p:DebugSymbols=false `
    -o $PublishDir

if ($LASTEXITCODE -ne 0) {
    throw "dotnet publish failed with exit code $LASTEXITCODE"
}

# Uygulama on kosullari exe'nin yanindaki Prerequisites klasorunde arar
# (AppDomain.BaseDirectory). Installer bunlari zaten {app}\Prerequisites'e
# kopyaliyor; tasinabilir ZIP'e de koymazsak ZIP kullanicisi hicbir kurulum
# akisini tamamlayamaz.
$prereqTarget = Join-Path $PublishDir 'Prerequisites'
New-Item -ItemType Directory -Force -Path $prereqTarget | Out-Null
Copy-Item (Join-Path $ScriptRoot 'Prerequisites\VC_redist.x64.exe') $prereqTarget -Force
Copy-Item (Join-Path $ScriptRoot 'Prerequisites\Windows.Packet.Filter.3.6.1.1.x64.msi') $prereqTarget -Force

$fallbackLogoDir = Join-Path $PublishDir 'Resources'
New-Item -ItemType Directory -Force -Path $fallbackLogoDir | Out-Null
Copy-Item (Join-Path $ScriptRoot 'RevivalDPI\Resources\revivaldpi-logo-128.png') $fallbackLogoDir -Force
Copy-Item (Join-Path $ScriptRoot 'RevivalDPI\Resources\revivaldpi-logo-128_inverted.png') $fallbackLogoDir -Force

$requiredFiles = @(
    'RevivalDPI.exe',
    'RevivalDPI.dll',
    'RevivalDPI.deps.json',
    'RevivalDPI.runtimeconfig.json',
    'res\revivaldpi.ico',
    'res\revivaldpi-logo-128.png',
    'res\revivaldpi-logo-128_inverted.png',
    'Resources\revivaldpi-logo-128.png',
    'Resources\revivaldpi-logo-128_inverted.png',
    'res\Languages\en.json',
    'res\Languages\tr.json',
    'res\Languages\ru.json',
    'res\wgcf.exe',
    'res\wiresock-secure-connect-x64-2.4.23.1.exe',
    'res\wiresock-vpn-client-x64-1.4.7.1.msi',
    'Prerequisites\VC_redist.x64.exe',
    'Prerequisites\Windows.Packet.Filter.3.6.1.1.x64.msi'
)

foreach ($relativePath in $requiredFiles) {
    $fullPath = Join-Path $PublishDir $relativePath
    if (-not (Test-Path $fullPath)) {
        throw "Publish validation failed. Missing: $relativePath"
    }
}

$requiredPrereqs = @(
    'Prerequisites\VC_redist.x64.exe',
    'Prerequisites\Windows.Packet.Filter.3.6.1.1.x64.msi'
)

foreach ($relativePath in $requiredPrereqs) {
    $fullPath = Join-Path $ScriptRoot $relativePath
    if (-not (Test-Path $fullPath)) {
        throw "Installer prerequisite missing: $relativePath"
    }
}

Write-Host "Publish output ready: $PublishDir"

if (-not $SkipInstaller) {
    $isccCandidates = @(
        "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe",
        "$env:ProgramFiles\Inno Setup 6\ISCC.exe"
    )

    $iscc = $isccCandidates | Where-Object { $_ -and (Test-Path $_) } | Select-Object -First 1

    if ($iscc) {
        Write-Host "Compiling installer with Inno Setup (v$AppVersion)..."
        Push-Location $ScriptRoot
        try {
            & $iscc "/DMyAppVersion=$AppVersion" $SetupScript
            if ($LASTEXITCODE -ne 0) {
                throw "Inno Setup failed with exit code $LASTEXITCODE"
            }
        }
        finally {
            Pop-Location
        }
        Write-Host "Installer output ready: $(Join-Path $RepoRoot 'dist')"
    }
    else {
        Write-Warning 'Inno Setup 6 was not found. Publish output is ready, installer compile was skipped.'
    }
}
