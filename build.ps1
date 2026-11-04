param(
    [ValidateSet('Debug', 'Release')][string]$Configuration = 'Release',
    [string]$GameDir = $env:STS2_GAME_DIR,
    [switch]$Stage,
    [switch]$Deploy,
    [switch]$Archive
)
$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot
$manifestPath = Join-Path $root 'mod_manifest.json'
$manifest = Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json
dotnet build (Join-Path $root 'RefreshShop.csproj') -c $Configuration --nologo -v:q
if ($LASTEXITCODE -ne 0) { throw 'Build failed' }
$dll = Join-Path $root ".godot\mono\temp\bin\$Configuration\RefreshShop.dll"
$numericVersion = $manifest.version.Split('-')[0] + '.0'
if ([Reflection.AssemblyName]::GetAssemblyName($dll).Version.ToString() -ne $numericVersion) { throw 'DLL / manifest version mismatch' }
if (!(Test-Path -LiteralPath (Join-Path $root 'release'))) { New-Item -ItemType Directory -Path (Join-Path $root 'release') | Out-Null }
if ($Stage -or $Deploy -or $Archive) {
    $staging = Join-Path $root 'torelease'
    New-Item -ItemType Directory -Path $staging -Force | Out-Null
    Copy-Item -LiteralPath $dll -Destination (Join-Path $staging 'RefreshShop.dll') -Force
    Copy-Item -LiteralPath $manifestPath -Destination (Join-Path $staging 'mod_manifest.json') -Force
    $extra = Get-ChildItem -LiteralPath $staging -Force | Where-Object Name -notin @('RefreshShop.dll','mod_manifest.json')
    if ($extra) { throw 'Unexpected files in staging; review before packaging.' }
}
if ($Deploy) {
    if (!$GameDir) {
        $GameDir = @('F:\Steam\steamapps\common\Slay the Spire 2', 'C:\Program Files (x86)\Steam\steamapps\common\Slay the Spire 2') |
            Where-Object { Test-Path -LiteralPath (Join-Path $_ 'SlayTheSpire2.exe') } | Select-Object -First 1
    }
    if (!$GameDir -or !(Test-Path -LiteralPath (Join-Path $GameDir 'SlayTheSpire2.exe'))) { throw 'Provide -GameDir or STS2_GAME_DIR.' }
    if (Get-Process -Name SlayTheSpire2 -ErrorAction SilentlyContinue) { throw 'Exit the game before deploying.' }
    $target = Join-Path $GameDir 'mods\RefreshShop'
    if (Test-Path -LiteralPath $target) {
        $backup = Join-Path $root ('release\deploy-backups\' + (Get-Date -Format 'yyyyMMdd-HHmmssfff'))
        New-Item -ItemType Directory -Path $backup -Force | Out-Null
        Get-ChildItem -LiteralPath $target -File | Copy-Item -Destination $backup
        Write-Host "Deployment backup: $backup"
    }
    New-Item -ItemType Directory -Path $target -Force | Out-Null
    foreach ($name in @('RefreshShop.dll','mod_manifest.json')) {
        $source = Join-Path $staging $name
        $destination = Join-Path $target $name
        Copy-Item -LiteralPath $source -Destination ($destination + '.pending') -Force
        Move-Item -LiteralPath ($destination + '.pending') -Destination $destination -Force
        if ((Get-FileHash -LiteralPath $source).Hash -ne (Get-FileHash -LiteralPath $destination).Hash) { throw "Deployment hash mismatch: $name" }
    }
    Write-Host "Local deployment: $target"
}
if ($Archive) {
    $archivePath = Join-Path $root ('release\RefreshShop_v' + $manifest.version + '.zip')
    if (Test-Path -LiteralPath $archivePath) { throw "Archive exists; preserve it: $archivePath" }
    Compress-Archive -LiteralPath (Join-Path $staging 'RefreshShop.dll'),(Join-Path $staging 'mod_manifest.json') -DestinationPath $archivePath
    Write-Host "Local archive: $archivePath"
}
Write-Host "Built $($manifest.version). Stage=$Stage Deploy=$Deploy Archive=$Archive"
