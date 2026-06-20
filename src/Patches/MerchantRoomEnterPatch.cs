using HarmonyLib;
using System.Reflection;

namespace RefreshShop.Patches;

/// <summary>
/// Postfix MerchantRoom.EnterInternal —— 进入新商店节点时重置刷新次数。
///
/// MerchantRoom.EnterInternal 是新商店节点的逻辑入口（对每个 player 调
/// CreateForNormalMerchant）。Postfix 在方法返回时触发，此时商店已建好。
/// 同商店离开再回来不会再次触发 EnterInternal（走的是 Resume/重新打开 UI），
/// 所以次数不会误重置。
///
/// EnterInternal 是 async Task；Harmony Postfix 对 async 方法在同步返回时触发，
/// 此处足够——商店进入即重置次数，不需要等 async 内部 await 链全部完成。
/// </summary>
[HarmonyPatch]
internal static class MerchantRoomEnterPatch
{
    static MethodBase TargetMethod()
    {
        var t = AccessTools.TypeByName("MegaCrit.Sts2.Core.Rooms.MerchantRoom");
        return t?.GetMethod("EnterInternal", BindingFlags.Public | BindingFlags.Instance);
    }

    static void Postfix()
    {
        // 进新商店前先从 ModConfig 实时读取最新配置，
        // 确保 RefreshCounter.ResetForNewShop 用的是玩家设置的 MaxUses 而非默认值
        RefreshShopConfig.RefreshFromModConfig();
        RefreshCounter.ResetForNewShop();
    }
}