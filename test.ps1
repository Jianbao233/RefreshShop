param(
    [string]$GameDir = 'F:\Steam\steamapps\common\Slay the Spire 2',
    [string]$GameDataDir = '',
    [string]$GodotExe = 'D:\A-Developing\tools\Godot_v4.5.1\Godot_v4.5.1\Godot_v4.5.1-stable_mono_win64.exe',
    [switch]$LegacyApi
)
$ErrorActionPreference = 'Stop'
if (!$GameDataDir) { $GameDataDir = Join-Path $GameDir 'data_sts2_windows_x86_64' }
$testDir = Join-Path $PSScriptRoot 'tests'
dotnet build (Join-Path $testDir 'RefreshShop.Tests.csproj') "-p:GameDataDir=$GameDataDir" "-p:LegacyApi=$($LegacyApi.IsPresent.ToString().ToLowerInvariant())" --nologo -v:q
if ($LASTEXITCODE -ne 0) { throw 'Native test build failed' }
$bin = Join-Path $testDir '.godot\mono\temp\bin\Debug'
foreach ($file in Get-ChildItem -LiteralPath (Join-Path $GameDir 'data_sts2_windows_x86_64') -Filter '*.dll') {
    $destination = Join-Path $bin $file.Name
    if (!(Test-Path -LiteralPath $destination)) { Copy-Item -LiteralPath $file.FullName -Destination $destination }
}
foreach ($file in Get-ChildItem -LiteralPath $GameDataDir -Filter '*.dll') {
    Copy-Item -LiteralPath $file.FullName -Destination (Join-Path $bin $file.Name) -Force
}
$output = Join-Path ([IO.Path]::GetTempPath()) ('RefreshShopNativeTests-' + (Get-Date -Format 'yyyyMMdd-HHmmss'))
New-Item -ItemType Directory -Path $output | Out-Null
$previousReport = $env:REFRESHSHOP_TEST_REPORT
$previousPack = $env:REFRESHSHOP_GAME_PACK
$previousBin = $env:REFRESHSHOP_TEST_BIN
try {
    $env:REFRESHSHOP_TEST_BIN = $bin
    $env:REFRESHSHOP_GAME_PACK = Join-Path $GameDir 'SlayTheSpire2.pck'
    $env:REFRESHSHOP_TEST_REPORT = Join-Path $output 'results.json'
    $log = Join-Path $output 'godot.log'
    $process = Start-Process -FilePath $GodotExe -ArgumentList @('--path',('"'+$testDir+'"'),'--headless','--audio-driver','Dummy','--log-file',('"'+$log+'"')) -WindowStyle Hidden -PassThru
    if (!$process.WaitForExit(60000)) { Stop-Process -Id $process.Id; throw "Test timed out: $log" }
    if (!(Test-Path -LiteralPath $env:REFRESHSHOP_TEST_REPORT)) { throw "No report: $log" }
    $result = Get-Content -LiteralPath $env:REFRESHSHOP_TEST_REPORT -Raw | ConvertFrom-Json
    $result | Add-Member -NotePropertyName gameSha256 -NotePropertyValue (Get-FileHash -LiteralPath (Join-Path $GameDataDir 'sts2.dll')).Hash
    $result | ConvertTo-Json -Depth 20 | Set-Content -LiteralPath $env:REFRESHSHOP_TEST_REPORT -Encoding utf8
    Get-Content -LiteralPath $env:REFRESHSHOP_TEST_REPORT
    $unexpected = Select-String -LiteralPath $log -Pattern '^ERROR:' | Where-Object {
        $_.Line -notmatch '^ERROR: \[RefreshShop\] (Refresh cancelled: (Only the local player|Injected|No allowed)|Gold synchronization failed;.*Injected)'
    }
    if ($unexpected) { $unexpected | ForEach-Object Line; throw "Unexpected runtime errors: $log" }
    if (!$result.pass -or $process.ExitCode -ne 0) { throw "Native tests failed: $log" }
}
finally { $env:REFRESHSHOP_TEST_REPORT = $previousReport; $env:REFRESHSHOP_GAME_PACK = $previousPack; $env:REFRESHSHOP_TEST_BIN = $previousBin }
