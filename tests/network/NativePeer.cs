using Godot;
using HarmonyLib;
using System.Reflection;
using System.Text.Json;
using MegaCrit.Sts2.Core.Context;
using MegaCrit.Sts2.Core.Entities.Merchant;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Characters;
using MegaCrit.Sts2.Core.Modding;
using MegaCrit.Sts2.Core.Multiplayer;
using MegaCrit.Sts2.Core.Multiplayer.Game;
using MegaCrit.Sts2.Core.Multiplayer.Serialization;
using MegaCrit.Sts2.Core.Multiplayer.Transport.ENet;
using MegaCrit.Sts2.Core.Platform;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.Saves;
using MegaCrit.Sts2.Core.TestSupport;
using MegaCrit.Sts2.Core.Unlocks;
using Environment = System.Environment;
using File = System.IO.File;
using Path = System.IO.Path;

// Two separate headless processes. Files are readiness/expected-result barriers only;
// all gameplay changes travel through the unmodified ENet transport and game receivers.
public partial class NativePeer : Node
{
    private INetGameService _net;
    private string _dir, _role;
    private int _assertions;
    private Assembly _mod;
    private const BindingFlags Flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance;
    private static void Set(object target, string name, object value) => target.GetType().GetProperty(name, Flags).SetValue(target, value);
    private static string Snapshot(Player p) => JsonSerializer.Serialize(new {
        p.Gold,
        cards = p.Deck.Cards.Select(c => c.Id.Entry + ":" + c.CurrentUpgradeLevel).ToArray(),
        relics = p.Relics.Select(r => r.Id.Entry).ToArray(),
        potions = p.Potions.Select(p => p?.Id.Entry).ToArray(),
        rng = JsonSerializer.Serialize(p.PlayerRng.ToSerializable(), new JsonSerializerOptions { IncludeFields = true })
    });
    private object ModCall(string type, string method, params object[] args) =>
        _mod.GetType("RefreshShop." + type).GetMethod(method, Flags).Invoke(null, args);
    private void Check(bool condition, string message) { _assertions++; if (!condition) throw new Exception(message); }
    private void Publish(string name, string contents)
    {
        var path = Path.Combine(_dir, name);
        File.WriteAllText(path + ".tmp", contents);
        File.Move(path + ".tmp", path, true);
    }
    private async Task Until(Func<bool> condition, string description)
    {
        var deadline = DateTime.UtcNow.AddSeconds(20);
        while (!condition())
        {
            if (DateTime.UtcNow > deadline) throw new TimeoutException(description);
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        }
    }
    public override void _Process(double delta) => _net?.Update();
    public override async void _Ready()
    {
        _dir = Environment.GetEnvironmentVariable("REFRESHSHOP_PEER_DIR");
        _role = Environment.GetEnvironmentVariable("REFRESHSHOP_PEER_ROLE");
        bool host = _role == "host";
        var modPath = Environment.GetEnvironmentVariable("REFRESHSHOP_PEER_MOD_DLL");
        try
        {
            TestMode.IsOn = true;
            Environment.SetEnvironmentVariable("SLAY_THE_SPIRE2_APPDATA", Path.Combine(_dir, _role + "-settings"));
            typeof(ModManager).GetProperty("State").SetValue(null, ModManagerState.Initialized);
            var init = typeof(ModelDb).GetMethods().Single(m => m.Name == "Init");
            init.Invoke(null, init.GetParameters().Length == 0 ? Array.Empty<object>() : new object[] {
                typeof(AbstractModel).Assembly.GetTypes().Where(t => !t.IsAbstract && t.IsSubclassOf(typeof(AbstractModel))).ToArray() });
            typeof(ModelDb).Assembly.GetType("MegaCrit.Sts2.Core.Modding.AssemblyInfo")?.GetMethod("Init")?.Invoke(null, null);
            ModelIdSerializationCache.Init();
            SaveManager.Instance.InitProfileId(1); SaveManager.Instance.InitProgressData();
            Check(ProjectSettings.LoadResourcePack(Environment.GetEnvironmentVariable("REFRESHSHOP_GAME_PACK"), false), "game PCK");
            Godot.Bridge.ScriptManagerBridge.LookupScriptsInAssembly(typeof(ModelDb).Assembly);
            SaveManager.Instance.InitSettingsDataForTest();
            SaveManager.Instance.SettingsSave.Language = "eng";
            MegaCrit.Sts2.Core.Localization.LocManager.Initialize();
            if (!string.IsNullOrEmpty(modPath))
            {
                _mod = System.Runtime.Loader.AssemblyLoadContext.GetLoadContext(typeof(NativePeer).Assembly).LoadFromAssemblyPath(modPath);
                new Harmony("RefreshShop.NetworkTests.Loader").PatchAll(_mod);
                ModCall("RefreshShopConfig", "SetMaxUsesFromRitsuLib", 5);
                ModCall("RefreshShopConfig", "SetCostFromRitsuLib", 25);
                ModCall("RefreshShopConfig", "SetRefillsPurchasedFromRitsuLib", true);
            }
            Check(AppDomain.CurrentDomain.GetAssemblies().Any(a => a.GetName().Name == "RefreshShop") == (_mod != null), "only installing process loads RefreshShop");
            MessageTypes.Initialize();
            var version = new PeerVersionInfo { version = "0.111.0", idDatabaseHash = ModelIdSerializationCache.Hash,
                gameplayAffectingMods = new(), otherMods = _mod == null ? new() : new() { "RefreshShop" } };
            ushort port = ushort.Parse(Environment.GetEnvironmentVariable("REFRESHSHOP_PEER_PORT"));
            if (host)
            {
                var server = new NetHostGameService(version); _net = server;
                Check(server.StartENetHost(port, 2) == null, "ENet host started");
                Publish("host-listening", "ready");
                await Until(() => server.ConnectedPeers.Count == 1, "host handshake");
                server.SetPeerReadyForBroadcasting(2);
            }
            else
            {
                var client = new NetClientGameService(version); _net = client;
                var transport = new ENetClient(client); client.Initialize(transport, PlatformType.None);
                Check(await transport.ConnectToHost(2, "127.0.0.1", port) == null, "ENet client connected");
                await Until(() => client.IsConnected, "client handshake");
            }
            var p1 = Player.CreateForNewRun<Ironclad>(UnlockState.all, 1);
            var p2 = Player.CreateForNewRun<Silent>(UnlockState.all, 2);
            var run = RunState.CreateForTest(new[] { p1, p2 }, seed: "NATIVEENET");
            p1.Gold = p2.Gold = 2000;
            LocalContext.NetId = _net.NetId;
            Set(RunManager.Instance, "State", run);
            Set(RunManager.Instance, "AscensionManager", new MegaCrit.Sts2.Core.Entities.Ascension.AscensionManager(0));
            Set(RunManager.Instance, "NetService", _net);
            using var sync = new RewardSynchronizer(new RunLocationTargetedMessageBuffer(_net), _net, run, _net.NetId);
            Set(RunManager.Instance, "RewardSynchronizer", sync);
            var inv1 = MerchantInventory.CreateForNormalMerchant(p1);
            var inv2 = MerchantInventory.CreateForNormalMerchant(p2);
            bool installedHost = Environment.GetEnvironmentVariable("REFRESHSHOP_PEER_INSTALLED_ROLE") == "host";
            var active = installedHost ? p1 : p2;
            var other = installedHost ? p2 : p1;
            var untouched = Snapshot(other);
            var inventory = installedHost ? inv1 : inv2;
            Publish(_role + "-ready", "ready");
            await Until(() => File.Exists(Path.Combine(_dir, host ? "client-ready" : "host-ready")), "receiver readiness");
            for (int step = 0; step < 4; step++)
            {
                if (_mod != null)
                {
                    if (step == 0)
                    {
                        Check(await (Task<bool>)ModCall("ShopRefreshService", "RefreshAsync", inventory), "refresh committed");
                        Check(active.Gold == 1975, "one refresh payment");
                    }
                    else
                    {
                        MerchantEntry item = step == 1 ? inventory.CharacterCardEntries[0] : step == 2 ? inventory.PotionEntries[0] : inventory.RelicEntries[0];
                        Check(await item.OnTryPurchaseWrapper(inventory), "native purchase step " + step);
                    }
                    Publish("expected-" + step, Snapshot(active));
                    await Until(() => File.Exists(Path.Combine(_dir, "received-" + step)), "remote application step " + step);
                    Check(File.ReadAllText(Path.Combine(_dir, "received-" + step)) == Snapshot(active), "remote snapshot matches step " + step);
                }
                else
                {
                    await Until(() => File.Exists(Path.Combine(_dir, "expected-" + step)), "sender step " + step);
                    var expected = File.ReadAllText(Path.Combine(_dir, "expected-" + step));
                    await Until(() => Snapshot(active) == expected, "native receiver snapshot step " + step);
                    Check(Snapshot(other) == untouched, "other player unchanged step " + step);
                    Check(!AppDomain.CurrentDomain.GetAssemblies().Any(a => a.GetName().Name == "RefreshShop"), "receiver still has no mod");
                    Publish("received-" + step, Snapshot(active));
                }
            }
            Publish(_role + "-done", "ready");
            await Until(() => File.Exists(Path.Combine(_dir, host ? "client-done" : "host-done")), "completion barrier");
            Write(true, null);
        }
        catch (Exception ex) { GD.PrintErr(ex); Write(false, ex.ToString()); }
    }
    private void Write(bool pass, string error)
    {
        Publish(_role + "-result.json", JsonSerializer.Serialize(new { pass, role = _role, modLoaded = _mod != null,
            assertions = _assertions, transport = "native ENet loopback", error }, new JsonSerializerOptions { WriteIndented = true }));
        GetTree().Quit(pass ? 0 : 1);
    }
}
