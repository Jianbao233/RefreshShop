using HarmonyLib;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Context;
using MegaCrit.Sts2.Core.Entities.Merchant;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Models.Characters;
using MegaCrit.Sts2.Core.Multiplayer.Game;
using MegaCrit.Sts2.Core.Multiplayer.Messages.Game.Sync;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.Unlocks;
using RefreshShop;

public partial class Runner
{
    private async Task Network()
    {
        foreach (var side in new[] { NetGameType.Client, NetGameType.Host })
        {
            var player = Player.CreateForNewRun<Ironclad>(UnlockState.all, 11);
            var other = Player.CreateForNewRun<Silent>(UnlockState.all, 22);
            var run = RunState.CreateForTest(new[] { player, other }, seed: "NETREFRESH");
            var replica = Player.CreateForNewRun<Ironclad>(UnlockState.all, 11);
            var peerPlayer = Player.CreateForNewRun<Silent>(UnlockState.all, 22);
            var peerRun = RunState.CreateForTest(new[] { replica, peerPlayer }, seed: "NETREFRESH");
            LocalContext.NetId = 11;
            var net = new TestNet { NetId = 11, Type = side };
            var peerNet = new TestNet { NetId = 22, Type = side == NetGameType.Client ? NetGameType.Host : NetGameType.Client };
            net.Peer = peerNet;
            using var sync = new RewardSynchronizer(new RunLocationTargetedMessageBuffer(net), net, run, 11);
            using var receiver = new RewardSynchronizer(new RunLocationTargetedMessageBuffer(peerNet), peerNet, peerRun, 22);
            var manager = RunManager.Instance;
            GameApi.Set(manager, "State", run);
            GameApi.Set(manager, "AscensionManager", new MegaCrit.Sts2.Core.Entities.Ascension.AscensionManager(0));
            GameApi.Set(manager, "NetService", net);
            GameApi.Set(manager, "RewardSynchronizer", sync);
            player.Gold = replica.Gold = 2000;
            RefreshShopConfig.SetCostFromRitsuLib(25);
            RefreshShopConfig.SetMaxUsesFromRitsuLib(5);
            RefreshShopConfig.SetRefillsPurchasedFromRitsuLib(true);
            var inventory = MerchantInventory.CreateForNormalMerchant(player);
            var rngBefore = Rng(player);
            Check(await ShopRefreshService.RefreshAsync(inventory), side + " refresh");
            Check(player.Gold == 1975 && replica.Gold == 1975, "native GoldLost decoded by unmodified receiver");
            Check(net.Sent.Count == 1 && net.Sent[0] is GoldLostMessage, "exactly one native gold message");
            Check(RefreshCounter.RemainingUses == 4 && Rng(player) == rngBefore, "one use / vanilla RNG unchanged");
            var expectedOtherGold = other.Gold;
            var remoteInv = MerchantInventory.CreateForNormalMerchant(other);
            Check(!await ShopRefreshService.RefreshAsync(remoteInv), "reject foreign inventory");
            Check(other.Gold == expectedOtherGold && net.Sent.Count == 1, "foreign player untouched");

            var card = inventory.CharacterCardEntries[0];
            var cardId = card.CreationResult.Card.Id;
            Check(await card.OnTryPurchaseWrapper(inventory), "native card purchase");
            await ToSignal(GetTree(), Godot.SceneTree.SignalName.ProcessFrame);
            Check(player.Gold == replica.Gold, "purchase gold agrees with vanilla peer");
            Check(player.Deck.Cards.Last().Id == cardId && replica.Deck.Cards.Last().Id == cardId, "purchased card decoded and added on vanilla peer");
            Check(net.Sent.All(m => m.GetType().Assembly == typeof(Player).Assembly), "no custom network messages");
            var potion = inventory.PotionEntries[0]; var potionId = potion.Model.Id;
            Check(await potion.OnTryPurchaseWrapper(inventory), "native potion purchase");
            await ToSignal(GetTree(), Godot.SceneTree.SignalName.ProcessFrame);
            Check(replica.Potions.Any(p => p?.Id == potionId) && player.Gold == replica.Gold, "potion and cost agree on vanilla receiver");
            var relic = inventory.RelicEntries[0]; var relicId = relic.Model.Id;
            Check(await relic.OnTryPurchaseWrapper(inventory), "native relic purchase");
            await ToSignal(GetTree(), Godot.SceneTree.SignalName.ProcessFrame);
            Check(replica.Relics.Any(r => r.Id == relicId) && player.Gold == replica.Gold, "relic and cost agree on vanilla receiver");
            _passed.Add(side + " only installation: native gold/card packet roundtrip and peer application");

            // Inject a factory failure after relic planning: no live mutation/charge/use/message.
            var before = Items(inventory); var gold = player.Gold; var messages = net.Sent.Count;
            RefreshCounter.Bind(inventory); var remaining = RefreshCounter.RemainingUses;
            var h = new Harmony("RefreshShop.Tests.Failure");
            var target = typeof(MerchantPotionEntry).GetMethod("CalcCost");
            h.Patch(target, prefix: new HarmonyMethod(typeof(Runner).GetMethod(nameof(FailFactory), System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic)));
            try { Check(!await ShopRefreshService.RefreshAsync(inventory), "factory failure rejected"); }
            finally { h.UnpatchAll(h.Id); }
            Check(Items(inventory) == before && player.Gold == gold && net.Sent.Count == messages, "factory failure atomic");
            Check(RefreshCounter.RemainingUses == remaining && Rng(player) == rngBefore, "failure preserves count/RNG");

            h.Patch(typeof(PlayerCmd).GetMethod("LoseGold"), prefix: new HarmonyMethod(typeof(Runner).GetMethod(nameof(FailPayment), System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic)));
            try { Check(!await ShopRefreshService.RefreshAsync(inventory), "failed async payment rejected"); }
            finally { h.UnpatchAll(h.Id); }
            Check(Items(inventory) == before && player.Gold == gold && net.Sent.Count == messages && RefreshCounter.RemainingUses == remaining, "payment failure rolled back");
            _passed.Add(side + " failure atomicity: native factory and async payment");

            _paymentGate = new TaskCompletionSource();
            h.Patch(typeof(PlayerCmd).GetMethod("LoseGold"), prefix: new HarmonyMethod(typeof(Runner).GetMethod(nameof(DelayPayment), System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic)));
            try
            {
                var pending = ShopRefreshService.RefreshAsync(inventory);
                Check(!pending.IsCompleted && !await ShopRefreshService.RefreshAsync(inventory), "in-flight refresh rejects second click");
                _paymentGate.SetResult();
                Check(await pending, "first refresh completes once");
                Check(net.Sent.Count == messages + 1, "one message after delayed payment");
            }
            finally { h.UnpatchAll(h.Id); }
            gold = player.Gold; messages = net.Sent.Count; remaining = RefreshCounter.RemainingUses;

            net.FailSend = true;
            Check(await ShopRefreshService.RefreshAsync(inventory), "ambiguous send keeps local commit");
            Check(player.Gold == gold - 25 && RefreshCounter.RemainingUses == remaining - 1, "send failure never refunds committed charge");
            Check(!await ShopRefreshService.RefreshAsync(inventory) && net.Sent.Count == messages, "no retry after ambiguous send");
            _passed.Add(side + " send failure is blocked without duplicate charge/retry");
        }
    }
    private static void FailFactory() => throw new InvalidOperationException("Injected potion failure");
    private static bool FailPayment(Player player, ref Task __result)
    { player.Gold -= 1; __result = Task.FromException(new InvalidOperationException("Injected payment failure")); return false; }
    private static TaskCompletionSource _paymentGate;
    private static bool DelayPayment(decimal amount, Player player, ref Task __result)
    { player.Gold -= (int)amount; __result = _paymentGate.Task; return false; }
}
