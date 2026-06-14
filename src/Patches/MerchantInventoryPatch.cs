using HarmonyLib;
using System.Reflection;
using RefreshShop.UI;

namespace RefreshShop.Patches;

/// <summary>
/// 在 NMerchantInventory.Initialize 完成后注入刷新按钮。
/// </summary>
[HarmonyPatch]
internal static class MerchantInventoryInitializePatch
{
    static MethodBase TargetMethod()
    {
        var t = AccessTools.TypeByName("MegaCrit.Sts2.Core.Nodes.Screens.Shops.NMerchantInventory");
        return t?.GetMethod("Initialize", BindingFlags.Public | BindingFlags.Instance);
    }

    static void Postfix(object __instance)
    {
        if (__instance == null) return;
        RefreshShopButtonInjector.TryInject(__instance);
    }
}