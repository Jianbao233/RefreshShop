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

// Native navigation is rebuilt after purchases. Reconnect our additional button then.
[HarmonyPatch]
internal static class MerchantNavigationPatch
{
    static MethodBase TargetMethod() => GameApi.Method(GameApi.Type("Nodes.Screens.Shops.NMerchantInventory"), "UpdateNavigation");
    static void Postfix(Godot.Control __instance) => RefreshShopButtonInjector.Find(__instance)?.UpdateNavigation();
}

[HarmonyPatch]
internal static class MerchantInputPatch
{
    static System.Collections.Generic.IEnumerable<MethodBase> TargetMethods()
    {
        var type = GameApi.Type("Nodes.Screens.Shops.NMerchantInventory");
        yield return GameApi.Method(type, "BlockInput");
        yield return GameApi.Method(type, "UnblockInput");
    }
    static void Postfix(Godot.Control __instance) => RefreshShopButtonInjector.Find(__instance)?.UpdateDisplay();
}
