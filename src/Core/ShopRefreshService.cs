using System;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;

namespace RefreshShop;

internal static class ShopRefreshService
{
    private sealed class State { public object Rng; public bool Busy; public bool SyncUncertain; }
    private static readonly ConditionalWeakTable<object, State> States = new();
    internal static bool IsBusy(object inventory) => States.GetOrCreateValue(inventory).Busy;
    internal static bool IsAvailable(object inventory) => !States.GetOrCreateValue(inventory).SyncUncertain;

    // Local inventory only. Native RewardSynchronizer sends GoldLostMessage;
    // purchases retain their native RewardObtainedMessage path, including on unmodded peers.
    internal static async Task<bool> RefreshAsync(object inventory)
    {
        var state = States.GetOrCreateValue(inventory);
        if (state.Busy || state.SyncUncertain) return false;
        state.Busy = true;
        RefreshPlan plan = null;
        object player = null, history = null;
        int goldBefore = 0, spentBefore = 0;
        bool paymentStarted = false, committed = false;
        try
        {
            player = GameApi.Player(inventory);
            if (!GameApi.IsLocal(player)) throw new InvalidOperationException("Only the local player's inventory can be refreshed.");
            RefreshCounter.Bind(inventory);
            if (!RefreshCounter.CanRefresh()) return false;
            var cost = RefreshShopConfig.Cost;
            goldBefore = GameApi.Gold(player);
            if (goldBefore < cost) return false;
            var manager = GameApi.RunManager;
            var net = GameApi.Get(manager, "NetService");
            if (!(bool)GameApi.Get(net, "IsConnected")) throw new InvalidOperationException("Not connected to the run.");
            var sync = GameApi.Get(manager, "RewardSynchronizer") ?? throw new InvalidOperationException("Reward synchronizer unavailable.");
            var syncGold = GameApi.Method(sync.GetType(), "SyncLocalGoldLost", typeof(int));
            var goldLoss = GameApi.Type("Entities.Gold.GoldLossType");
            var loseGold = GameApi.Method(GameApi.Type("Commands.PlayerCmd"), "LoseGold", typeof(decimal), player.GetType(), goldLoss);
            var run = GameApi.Get(player, "RunState");
            var currentHistory = GameApi.Get(run, "CurrentMapPointHistoryEntry");
            history = currentHistory == null ? null : GameApi.Call(currentHistory, "GetEntry", GameApi.Get(player, "NetId"));
            if (history != null) spentBefore = (int)GameApi.Get(history, "GoldSpent");
            state.Rng ??= GameApi.Call(GameApi.Get(player, "PlayerRng"), "ToSerializable");
            plan = RefreshPlan.Prepare(inventory, RefreshShopConfig.RefillsPurchased, state.Rng);
            plan.Apply();
            if (cost > 0)
            {
                paymentStarted = true;
                await (Task)loseGold.Invoke(null, new object[] { (decimal)cost, player, Enum.Parse(goldLoss, "Spent") });
                if (GameApi.Gold(player) != goldBefore - cost) throw new InvalidOperationException("Refresh payment did not complete.");
            }
            // A send can reach peers before throwing. Never refund/retry an ambiguous send.
            plan.Commit();
            committed = true;
            state.Rng = plan.NextRng;
            RefreshCounter.ConsumeOne();
            if (cost > 0)
            {
                try { syncGold.Invoke(sync, new object[] { cost }); }
                catch (Exception ex)
                {
                    state.SyncUncertain = true;
                    RefreshShopLog.Error("Gold synchronization failed; reconnect before refreshing again. " + ex.GetBaseException().Message);
                }
            }
            RefreshShopLog.Info($"Refresh committed: slots={plan.Changes.Count}, cost={cost}, nativeSync={!state.SyncUncertain}.");
            return true;
        }
        catch (Exception ex)
        {
            if (!committed)
            {
                plan?.Rollback();
                if (paymentStarted)
                {
                    GameApi.Set(player, "Gold", goldBefore);
                    if (history != null) GameApi.Set(history, "GoldSpent", spentBefore);
                }
            }
            RefreshShopLog.Error("Refresh cancelled: " + ex.GetBaseException().Message);
            return false;
        }
        finally { state.Busy = false; }
    }
}
