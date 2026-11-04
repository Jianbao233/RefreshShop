using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Entities.Merchant;
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
}

// Optional external API fixture; no real player blacklist is read or changed.
namespace MerchantBlacklist.Core
{
    public static class BlacklistStore
    {
        public static bool BlockPotions;
        public static bool IsRelicBanned(string id) => false;
        public static bool IsPotionBanned(string id) => BlockPotions;
        public static bool IsCardBanned(string id) => false;
    }
}
