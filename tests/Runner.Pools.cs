using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Entities.Merchant;
using MegaCrit.Sts2.Core.Entities.Relics;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Characters;
using MegaCrit.Sts2.Core.Models.RelicPools;
using MegaCrit.Sts2.Core.Models.PotionPools;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.Unlocks;
using RefreshShop;

public partial class Runner
{
    private void Pools()
    {
        foreach (var character in ModelDb.AllCharacters.Where(c => c is not Deprived))
        foreach (var unlocks in new[] { UnlockState.none, UnlockState.all })
        {
            var player = Player.CreateForNewRun(character, unlocks, 11);
            var run = RunState.CreateForTest(new[] { player }, seed: "POOLTEST");
            var inv = MerchantInventory.CreateForNormalMerchant(player);
            var rng = player.PlayerRng.ToSerializable();
            var relics = character.RelicPool.GetUnlockedRelics(unlocks).Concat(ModelDb.RelicPool<SharedRelicPool>().GetUnlockedRelics(unlocks)).Select(r => r.Id).ToHashSet();
            var potions = character.PotionPool.GetUnlockedPotions(unlocks).Concat(ModelDb.PotionPool<SharedPotionPool>().GetUnlockedPotions(unlocks)).Select(p => p.Id).ToHashSet();
            for (int i = 0; i < 5; i++)
            {
                var before = Rng(player);
                var plan = RefreshPlan.Prepare(inv, true, rng);
                plan.Apply(); plan.Commit(); rng = (MegaCrit.Sts2.Core.Saves.SerializablePlayerRngSet)plan.NextRng;
                Check(inv.RelicEntries.All(e => relics.Contains(e.Model.Id) && e.Model.IsAllowed(run)), "character/unlock/run relic constraints");
                Check(inv.PotionEntries.All(e => potions.Contains(e.Model.Id)), "character/unlock potion constraints");
                Check(Rng(player) == before, "all roles retain original RNGs");
            }
        }
        _passed.Add("all playable characters with none/all unlocks: 50 native refreshes");
        RegisteredModRelicPool();

        var p = Player.CreateForNewRun<Ironclad>(UnlockState.all, 11);
        var state = RunState.CreateForTest(new[] { p }, seed: "BANREFRESH");
        var inventory = MerchantInventory.CreateForNormalMerchant(p);
        var items = Items(inventory); var rngBefore = Rng(p);
        MerchantBlacklist.Core.BlacklistStore.BlockPotions = true;
        try
        {
            bool rejected = false;
            try { RefreshPlan.Prepare(inventory, true, p.PlayerRng.ToSerializable()); }
            catch (InvalidOperationException) { rejected = true; }
            Check(rejected && Items(inventory) == items && Rng(p) == rngBefore, "empty blacklist pool leaves shop intact");
        }
        finally { MerchantBlacklist.Core.BlacklistStore.BlockPotions = false; }
        _passed.Add("blacklist bridge rejects empty pools before committing");
    }

    private void RegisteredModRelicPool()
    {
        foreach (var type in new[] { typeof(SharedFixtureRelicPool), typeof(ForeignFixtureRelicPool),
            typeof(EligibleShopRelic), typeof(LockedShopRelic), typeof(DisallowedShopRelic), typeof(ForeignShopRelic) })
            ModelDb.Inject(type);

        var player = Player.CreateForNewRun<Ironclad>(UnlockState.all, 11);
        var run = RunState.CreateForTest(new[] { player }, seed: "MODSHOP");
        var inventory = MerchantInventory.CreateForNormalMerchant(player);
        var rng = player.PlayerRng.ToSerializable();
        var rngBefore = Rng(player);
        var vanillaShopIds = ModelDb.RelicPool<SharedRelicPool>().AllRelics.Concat(player.Character.RelicPool.AllRelics)
            .Where(relic => relic.Rarity == RelicRarity.Shop).Select(relic => relic.Id.Entry).ToHashSet();
        var h = new HarmonyLib.Harmony("RefreshShop.Tests.RegisteredPools");
        var all = typeof(ModelDb).GetProperty(nameof(ModelDb.AllRelicPools)).GetMethod;
        var characters = typeof(ModelDb).GetProperty(nameof(ModelDb.CharacterRelicPools)).GetMethod;
        try
        {
            h.Patch(all, postfix: new HarmonyLib.HarmonyMethod(typeof(Runner), nameof(AddSharedFixturePool)));
            h.Patch(characters, postfix: new HarmonyLib.HarmonyMethod(typeof(Runner), nameof(AddForeignFixturePool)));
            MerchantBlacklist.Core.BlacklistStore.BlockedRelics = vanillaShopIds;
            var plan = RefreshPlan.Prepare(inventory, true, rng);
            plan.Apply(); plan.Commit();
            Check(inventory.RelicEntries[2].Model is EligibleShopRelic, "registered shared Mod relic reaches the Shop slot");
            Check(Rng(player) == rngBefore, "custom relic refresh retains vanilla RNGs");

            vanillaShopIds.Add(ModelDb.Relic<EligibleShopRelic>().Id.Entry);
            var shopEntry = inventory.RelicEntries[2];
            var before = shopEntry.Model;
            var withoutCandidate = RefreshPlan.Prepare(inventory, true, rng);
            Check(withoutCandidate.Changes.Count > 0 && withoutCandidate.Changes.All(c => c.Entry != shopEntry),
                "an empty Shop candidate pool does not cancel other slots or admit ineligible relics");
            withoutCandidate.Apply(); withoutCandidate.Commit();
            Check(ReferenceEquals(shopEntry.Model, before), "a relic slot with no legal replacement is left as it was");
        }
        finally
        {
            h.Unpatch(all, HarmonyLib.HarmonyPatchType.Postfix, h.Id);
            h.Unpatch(characters, HarmonyLib.HarmonyPatchType.Postfix, h.Id);
            MerchantBlacklist.Core.BlacklistStore.BlockedRelics = new();
        }
        _passed.Add("registered shared Mod relics retain unlock/shop/character/blacklist restrictions");
    }

    private static void AddSharedFixturePool(ref IEnumerable<RelicPoolModel> __result) =>
        __result = __result.Append(ModelDb.RelicPool<SharedFixtureRelicPool>());

    private static void AddForeignFixturePool(ref IEnumerable<RelicPoolModel> __result) =>
        __result = __result.Append(ModelDb.RelicPool<ForeignFixtureRelicPool>());
}

public sealed class SharedFixtureRelicPool : RelicPoolModel
{
    public override string EnergyColorName => "colorless";
    protected override IEnumerable<RelicModel> GenerateAllRelics() =>
        new RelicModel[] { ModelDb.Relic<EligibleShopRelic>(), ModelDb.Relic<LockedShopRelic>(), ModelDb.Relic<DisallowedShopRelic>() };
    public override IEnumerable<RelicModel> GetUnlockedRelics(UnlockState unlockState) => AllRelics.Where(relic => relic is not LockedShopRelic);
}

public sealed class ForeignFixtureRelicPool : RelicPoolModel
{
    public override string EnergyColorName => "colorless";
    protected override IEnumerable<RelicModel> GenerateAllRelics() => new[] { ModelDb.Relic<ForeignShopRelic>() };
}

public sealed class EligibleShopRelic : RelicModel { public override RelicRarity Rarity => RelicRarity.Shop; }
public sealed class LockedShopRelic : RelicModel { public override RelicRarity Rarity => RelicRarity.Shop; }
public sealed class ForeignShopRelic : RelicModel { public override RelicRarity Rarity => RelicRarity.Shop; }
public sealed class DisallowedShopRelic : RelicModel
{
    public override RelicRarity Rarity => RelicRarity.Shop;
    public override bool IsAllowedInShops => false;
}

// Optional external API fixture; no real player blacklist is read or changed.
namespace MerchantBlacklist.Core
{
    public static class BlacklistStore
    {
        public static bool BlockPotions;
        public static HashSet<string> BlockedRelics = new();
        public static bool IsRelicBanned(string id) => BlockedRelics.Contains(id);
        public static bool IsPotionBanned(string id) => BlockPotions;
        public static bool IsCardBanned(string id) => false;
    }
}
