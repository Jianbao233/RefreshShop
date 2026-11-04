$ErrorActionPreference = 'Stop'
$testRoot = Join-Path ([IO.Path]::GetTempPath()) ('RefreshShopConfigTests-' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $testRoot | Out-Null
$utf8 = New-Object System.Text.UTF8Encoding($false)
function Write-TestFile($name, $text) { [IO.File]::WriteAllText((Join-Path $testRoot $name), $text, $utf8) }

# Compile unchanged production sources; use the supported configuration-root override.
# Godot logging and the optional ModConfig API are replaced by small behavioral test doubles.
foreach ($name in @('SettingsStore.cs', 'RefreshShopConfig.cs', 'ModConfigSettingsAdapter.cs', 'RitsuLibSettingsProvider.cs', 'RefreshCounter.cs')) {
    Copy-Item -LiteralPath (Join-Path $PSScriptRoot "src/Core/$name") -Destination (Join-Path $testRoot $name)
}
Write-TestFile 'ConfigTests.csproj' @'
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>Exe</OutputType><TargetFramework>net8.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings><Nullable>disable</Nullable>
    <RollForward>Major</RollForward>
  </PropertyGroup>
</Project>
'@
Write-TestFile 'Program.cs' @'
using System.Text.Json;
using RefreshShop;
var mode = args[0];
var root = Environment.GetEnvironmentVariable("SLAY_THE_SPIRE2_APPDATA");
if (string.IsNullOrEmpty(root) || !root.Contains("RefreshShopConfigTests-")) throw new Exception("Unsafe test path");
var path = Path.Combine(root, "SlayTheSpire2", "mods_settings", "RefreshShop.json");
void Seed(string json) { Directory.CreateDirectory(Path.GetDirectoryName(path)); File.WriteAllText(path, json); }
void Expect(int max, int cost, bool refill) {
    if (RefreshShopConfig.MaxUses != max || RefreshShopConfig.Cost != cost || RefreshShopConfig.RefillsPurchased != refill)
        throw new Exception($"{mode}: expected {max}/{cost}/{refill}, got {RefreshShopConfig.MaxUses}/{RefreshShopConfig.Cost}/{RefreshShopConfig.RefillsPurchased}");
}
void Legacy(string max = "12", string cost = "7", bool refill = true) {
    ModConfig.ModConfigApi.Stored = new() { ["refresh_max_uses"]=max, ["refresh_cost"]=cost, ["refresh_refills_purchased"]=refill };
}
void SetAll(int max, int cost, bool refill) {
    RitsuLibSettingsProvider.SetRitsuLibSettingInt("refresh_max_uses", max);
    RitsuLibSettingsProvider.SetRitsuLibSettingInt("refresh_cost", cost);
    RitsuLibSettingsProvider.SetRitsuLibSettingBool("refresh_refills_purchased", refill);
}
switch (mode) {
    case "defaults": Expect(5,25,false); if (File.Exists(path)) throw new Exception("Unexpected write"); break;
    case "write": SetAll(12,7,true); Expect(12,7,true); break; // No debounce/save callback needed.
    case "read": Expect(12,7,true); break; // A separate process initializes static state from disk.
    case "protect": Legacy("5","25",false); RefreshShopConfig.Initialize(); RefreshShopConfig.RefreshFromModConfig(); Expect(12,7,true); break;
    case "migrate":
        Legacy(); RefreshShopConfig.RefreshFromModConfig(); Expect(5,25,false);
        RefreshShopConfig.Initialize(); Expect(12,7,true); break;
    case "partial":
        Seed("{\"refresh_max_uses\":3}"); Legacy(); RefreshShopConfig.Initialize(); Expect(3,7,true); break;
    case "partial-read": Expect(3,7,true); break;
    case "early-edit":
        RitsuLibSettingsProvider.SetRitsuLibSettingInt("refresh_max_uses",5);
        Legacy(); RefreshShopConfig.Initialize(); Expect(5,7,true); break;
    case "early-read": Expect(5,7,true); break;
    case "callback":
        Legacy(); RefreshShopConfig.Initialize();
        ModConfig.ModConfigApi.SetValue("RefreshShop","refresh_max_uses","0");
        ModConfig.ModConfigApi.SetValue("RefreshShop","refresh_cost","0");
        ModConfig.ModConfigApi.SetValue("RefreshShop","refresh_refills_purchased",false);
        Expect(0,0,false); break;
    case "callback-read": Expect(0,0,false); break;
    case "clamp": SetAll(2000,-7,true); Expect(999,0,true); break;
    case "clamp-read": Expect(999,0,true); break;
    case "corrupt": Seed("{broken json"); Expect(5,25,false); if (RefreshShopLog.Errors != 1) throw new Exception("Missing read error"); break;
    case "write-failure":
        SetAll(12,7,true); var before=File.ReadAllText(path);
        Directory.CreateDirectory(path+".tmp");
        RitsuLibSettingsProvider.SetRitsuLibSettingInt("refresh_cost",88);
        if (File.ReadAllText(path)!=before || RefreshShopLog.Errors!=1) throw new Exception("Old file lost on failed write");
        Directory.Delete(path+".tmp");
        RitsuLibSettingsProvider.SaveRitsuLibSettings(); Expect(12,88,true); break;
    case "failure-read": Expect(12,88,true); break;
    case "concurrent":
        Parallel.For(0,50,i => SetAll(i,i%10,i%2==0)); SetAll(12,7,true);
        if (RefreshShopLog.Errors!=0) throw new Exception("Concurrent save failed"); break;
    default: throw new Exception("Unknown mode");
}
if (mode != "defaults" && mode != "corrupt") {
    using var json = JsonDocument.Parse(File.ReadAllText(path));
    if (json.RootElement.GetProperty("schema_version").GetInt32()!=1) throw new Exception("Bad schema");
}
Console.WriteLine("PASS " + mode);
namespace RefreshShop {
    public static class RefreshShopMod { public const string ModId="RefreshShop"; }
    internal static class RefreshShopLog {
        internal static int Errors;
        internal static void Info(string m) { }
        internal static void Warn(string m) { throw new Exception(m); }
        internal static void Error(string m) { Errors++; Console.WriteLine("EXPECTED ERROR: "+m); }
    }
}
namespace ModConfig {
    public enum ConfigType { TextInput, Toggle }
    public class ConfigEntry {
        public string Key {get;set;} public string Label {get;set;}
        public Dictionary<string,string> Labels {get;set;}
        public string Description {get;set;} public Dictionary<string,string> Descriptions {get;set;}
        public ConfigType Type {get;set;} public object DefaultValue {get;set;}
        public int MaxLength {get;set;} public Action<object> OnChanged {get;set;}
    }
    public static class ModConfigApi {
        public static Dictionary<string,object> Stored = new();
        private static Dictionary<string,object> values = new();
        private static Dictionary<string,ConfigEntry> entries = new();
        public static T GetValue<T>(string modId,string key) => values.TryGetValue(key,out var val) ? (T)Convert.ChangeType(val,typeof(T)) : default;
        public static void SetValue(string modId,string key,object val) {
            values[key]=val;
            if(entries.TryGetValue(key,out var entry)) entry.OnChanged?.Invoke(val);
        }
        public static void Register(string id,string title,Dictionary<string,string> displayNames,ConfigEntry[] list) {
            entries=list.ToDictionary(e=>e.Key);
            foreach(var entry in list) {
                var value=Stored.TryGetValue(entry.Key,out var old) ? old : entry.DefaultValue;
                values[entry.Key]=value;
                entry.OnChanged?.Invoke(value); // Initialization callbacks must not overwrite JSON.
            }
        }
    }
}
'@
dotnet build (Join-Path $testRoot 'ConfigTests.csproj') --nologo -v:q
if ($LASTEXITCODE -ne 0) { throw 'Configuration test build failed' }
$dll = Join-Path $testRoot 'bin/Debug/net8.0/ConfigTests.dll'
$oldRoot = $env:SLAY_THE_SPIRE2_APPDATA
$cases = @(
    @('defaults','defaults'), @('roundtrip','write'), @('roundtrip','read'), @('roundtrip','protect'),
    @('migration','migrate'), @('migration','read'), @('partial','partial'), @('partial','partial-read'),
    @('early','early-edit'), @('early','early-read'), @('callback','callback'), @('callback','callback-read'),
    @('clamp','clamp'), @('clamp','clamp-read'), @('corrupt','corrupt'),
    @('failure','write-failure'), @('failure','failure-read'), @('concurrent','concurrent'), @('concurrent','read')
)
try {
    foreach ($case in $cases) {
        $env:SLAY_THE_SPIRE2_APPDATA = Join-Path $testRoot $case[0]
        dotnet $dll $case[1]
        if ($LASTEXITCODE -ne 0) { throw "Failed: $($case[1]); evidence: $testRoot" }
    }
} finally { $env:SLAY_THE_SPIRE2_APPDATA = $oldRoot }
Write-Host "All $($cases.Count) configuration checks passed. Evidence: $testRoot"
