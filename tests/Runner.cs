using Godot;
using System.Reflection;
using System.Text.Json;
using MegaCrit.Sts2.Core.Context;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Entities.Merchant;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Characters;
using MegaCrit.Sts2.Core.Modding;
using MegaCrit.Sts2.Core.Multiplayer.Serialization;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.Saves;
using MegaCrit.Sts2.Core.TestSupport;
using MegaCrit.Sts2.Core.Unlocks;
using RefreshShop;
using Environment = System.Environment;

public partial class Runner : Node
{
    private int _assertions;
    private readonly List<string> _passed = new();
    public override async void _Ready()
    {
        try
        {
            TestMode.IsOn = true;
            Environment.SetEnvironmentVariable("SLAY_THE_SPIRE2_APPDATA", System.IO.Path.Combine(System.IO.Path.GetTempPath(), "RefreshShopTests", Guid.NewGuid().ToString("N")));
            typeof(ModManager).GetProperty("State").SetValue(null, ModManagerState.Initialized);
            var init = typeof(ModelDb).GetMethods().Single(m => m.Name == "Init");
            init.Invoke(null, init.GetParameters().Length == 0 ? Array.Empty<object>() : new object[] {
                typeof(AbstractModel).Assembly.GetTypes().Where(t => !t.IsAbstract && t.IsSubclassOf(typeof(AbstractModel))).ToArray() });
            typeof(ModelDb).Assembly.GetType("MegaCrit.Sts2.Core.Modding.AssemblyInfo")?.GetMethod("Init")?.Invoke(null, null);
            ModelIdSerializationCache.Init();
            SaveManager.Instance.InitProfileId(1);
            SaveManager.Instance.InitProgressData();
            Check(ProjectSettings.LoadResourcePack(Environment.GetEnvironmentVariable("REFRESHSHOP_GAME_PACK"), false), "game resource pack mounted");
            Godot.Bridge.ScriptManagerBridge.LookupScriptsInAssembly(typeof(ModelDb).Assembly);
            SaveManager.Instance.InitSettingsDataForTest();
            SaveManager.Instance.SettingsSave.Language = "eng";
            MegaCrit.Sts2.Core.Localization.LocManager.Initialize();
            var player = Player.CreateForNewRun<Ironclad>(UnlockState.all, 11);
            var remote = Player.CreateForNewRun<Silent>(UnlockState.all, 22);
            var run = RunState.CreateForTest(new[] { player, remote }, seed: "REFRESHSHOP");
            LocalContext.NetId = 11;
            var inventory = MerchantInventory.CreateForNormalMerchant(player);
            var remoteInventory = MerchantInventory.CreateForNormalMerchant(remote);
            var remoteBefore = Items(remoteInventory);
            var rngBefore = Rng(player);
            var poolBefore = JsonSerializer.Serialize(player.RelicGrabBag.ToSerializable());
            var localRng = player.PlayerRng.ToSerializable();
            var validRelics = player.Character.RelicPool.GetUnlockedRelics(player.UnlockState)
                .Concat(ModelDb.RelicPool<MegaCrit.Sts2.Core.Models.RelicPools.SharedRelicPool>().GetUnlockedRelics(player.UnlockState)).Select(r => r.Id).ToHashSet();
            var validPotions = player.Character.PotionPool.GetUnlockedPotions(player.UnlockState)
                .Concat(ModelDb.PotionPool<MegaCrit.Sts2.Core.Models.PotionPools.SharedPotionPool>().GetUnlockedPotions(player.UnlockState)).Select(p => p.Id).ToHashSet();
            for (int i = 0; i < 30; i++)
            {
                var plan = RefreshPlan.Prepare(inventory, true, localRng);
                Check(Rng(player) == rngBefore, "vanilla RNG unchanged by planning");
                plan.Apply(); plan.Commit(); localRng = (SerializablePlayerRngSet)plan.NextRng;
                Check(inventory.RelicEntries[2].Model.Rarity.ToString() == "Shop", "shop rarity retained");
                Check(inventory.RelicEntries.All(r => validRelics.Contains(r.Model.Id)), "unlocked character/shared relic pool");
                Check(inventory.PotionEntries.All(p => validPotions.Contains(p.Model.Id)), "legal potion pool");
                Check(inventory.RelicEntries.Select(r => r.Model.Id).Distinct().Count() == 3, "unique relics");
                Check(Items(remoteInventory) == remoteBefore, "other inventory unchanged");
            }
            Check(JsonSerializer.Serialize(player.RelicGrabBag.ToSerializable()) == poolBefore, "relic bag unchanged");
            _passed.Add("native generation: 30 refreshes, slots/pools/RNG/remote inventory");
            var before = Items(inventory);
            var rollback = RefreshPlan.Prepare(inventory, true, localRng);
            rollback.Apply(); rollback.Rollback();
            Check(Items(inventory) == before, "full item/cost rollback");
            GameApi.Set(inventory.RelicEntries[0], "Model", null);
            GameApi.Set(inventory.PotionEntries[0], "Model", null);
            GameApi.Set(inventory.CharacterCardEntries[0], "CreationResult", null);
            var noRefill = RefreshPlan.Prepare(inventory, false, localRng);
            noRefill.Apply(); noRefill.Commit();
            Check(!inventory.RelicEntries[0].IsStocked && !inventory.PotionEntries[0].IsStocked && !inventory.CharacterCardEntries[0].IsStocked, "purchased slots stay empty");
            _passed.Add("rollback and no-refill");
            Pools();
            await Network();
            await NativeUi();
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            Write(true, null);
        }
        catch (Exception ex) { GD.PrintErr(ex); Write(false, ex.ToString()); }
    }
    private static string Rng(Player p) => JsonSerializer.Serialize(p.PlayerRng.ToSerializable(), new JsonSerializerOptions { IncludeFields = true });
    private static string Items(MerchantInventory inv) => string.Join(";", inv.AllEntries.Select(e => e.IsStocked ? e switch {
        MerchantRelicEntry r => r.Model.Id + ":" + r.Cost,
        MerchantPotionEntry p => p.Model.Id + ":" + p.Cost,
        MerchantCardEntry c => c.CreationResult.Card.Id + ":" + c.Cost + ":" + c.IsOnSale,
        _ => e.Cost.ToString() } : "empty"));
    private void Check(bool condition, string message) { _assertions++; if (!condition) throw new Exception(message); }
    private void Write(bool pass, string error)
    {
        var report = new { pass, assertions = _assertions, groups = _passed, error };
        System.IO.File.WriteAllText(Environment.GetEnvironmentVariable("REFRESHSHOP_TEST_REPORT"), JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));
        GD.Print(JsonSerializer.Serialize(report)); GetTree().Quit(pass ? 0 : 1);
    }
}
