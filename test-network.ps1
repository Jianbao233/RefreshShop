param(
    [string]$GameDir = 'F:\Steam\steamapps\common\Slay the Spire 2',
    [string]$GodotExe = 'D:\A-Developing\tools\Godot_v4.5.1\Godot_v4.5.1\Godot_v4.5.1-stable_mono_win64.exe'
)
$ErrorActionPreference = 'Stop'
$testDir = Join-Path $PSScriptRoot 'tests\network'
$dataDir = Join-Path $GameDir 'data_sts2_windows_x86_64'
dotnet build (Join-Path $PSScriptRoot 'RefreshShop.csproj') -c Release --nologo -v:q
if ($LASTEXITCODE -ne 0) { throw 'Mod build failed' }
dotnet build (Join-Path $testDir 'RefreshShop.NetworkTests.csproj') "-p:GameDataDir=$dataDir" --nologo -v:q
if ($LASTEXITCODE -ne 0) { throw 'Network harness build failed' }
$modDll = Join-Path $PSScriptRoot '.godot\mono\temp\bin\Release\RefreshShop.dll'
$manifest = Get-Content -LiteralPath (Join-Path $PSScriptRoot 'mod_manifest.json') -Raw | ConvertFrom-Json
if ($manifest.affects_gameplay -ne $false) { throw 'Single-install handshake requires the non-gameplay mod list.' }
$output = Join-Path ([IO.Path]::GetTempPath()) ('RefreshShopENetTests-' + (Get-Date -Format 'yyyyMMdd-HHmmss'))
New-Item -ItemType Directory -Path $output | Out-Null
$results = @()
foreach ($installedRole in @('host','client')) {
    $scenario = Join-Path $output ($installedRole + '-only')
    New-Item -ItemType Directory -Path $scenario | Out-Null
    $probe = [Net.Sockets.UdpClient]::new(0)
    $port = $probe.Client.LocalEndPoint.Port
    $probe.Dispose()
    $processes = @()
    try {
        foreach ($role in @('host','client')) {
            $info = [Diagnostics.ProcessStartInfo]::new($GodotExe)
            $info.UseShellExecute = $false
            $info.CreateNoWindow = $true
            $info.WindowStyle = [Diagnostics.ProcessWindowStyle]::Hidden
            foreach ($arg in @('--path',$testDir,'--headless','--audio-driver','Dummy','--log-file',(Join-Path $scenario ($role + '.log')))) { $info.ArgumentList.Add($arg) }
            $info.Environment['REFRESHSHOP_GAME_PACK'] = Join-Path $GameDir 'SlayTheSpire2.pck'
            $info.Environment['REFRESHSHOP_TEST_BIN'] = Join-Path $testDir '.godot\mono\temp\bin\Debug'
            $info.Environment['REFRESHSHOP_PEER_DIR'] = $scenario
            $info.Environment['REFRESHSHOP_PEER_ROLE'] = $role
            $info.Environment['REFRESHSHOP_PEER_PORT'] = "$port"
            $info.Environment['REFRESHSHOP_PEER_INSTALLED_ROLE'] = $installedRole
            $info.Environment['REFRESHSHOP_PEER_MOD_DLL'] = $(if ($role -eq $installedRole) { $modDll } else { '' })
            $process = [Diagnostics.Process]::Start($info)
            $processes += $process
            if ($role -eq 'host') {
                $deadline = [DateTime]::UtcNow.AddSeconds(20)
                while (!(Test-Path -LiteralPath (Join-Path $scenario 'host-listening'))) {
                    if ($process.HasExited -or [DateTime]::UtcNow -gt $deadline) { throw "Host failed to start: $scenario" }
                    Start-Sleep -Milliseconds 100
                }
            }
        }
        foreach ($process in $processes) {
            if (!$process.WaitForExit(45000)) { throw "Network test timed out: $scenario" }
            if ($process.ExitCode -ne 0) { throw "Network test failed: $scenario" }
        }
        foreach ($role in @('host','client')) {
            $result = Get-Content -LiteralPath (Join-Path $scenario ($role + '-result.json')) -Raw | ConvertFrom-Json
            if (!$result.pass) { throw "Network assertion failed: $scenario" }
            if (Select-String -LiteralPath (Join-Path $scenario ($role + '.log')) -Pattern '^ERROR:') { throw "Unexpected runtime errors: $scenario" }
            $results += [pscustomobject]@{ installedRole = $installedRole; result = $result }
        }
    }
    finally {
        foreach ($process in $processes) { if (!$process.HasExited) { $process.Kill() }; $process.Dispose() }
    }
}
$report = [pscustomobject]@{ pass = $true; gameSha256 = (Get-FileHash -LiteralPath (Join-Path $dataDir 'sts2.dll')).Hash; modSha256 = (Get-FileHash -LiteralPath $modDll).Hash; scenarios = $results }
$report | ConvertTo-Json -Depth 10 | Set-Content -LiteralPath (Join-Path $output 'results.json') -Encoding utf8
$report | ConvertTo-Json -Depth 10
Write-Host "Evidence: $output"
