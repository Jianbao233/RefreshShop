using Godot;
using HarmonyLib;
using System;
using System.Reflection;

namespace RefreshShop;

public static class RefreshShopMod
{
    public const string ModId = "RefreshShop";
    private const string HarmonyId = "com.jianbao233.refreshshop";

    private static bool _initialized;
    private static bool _patched;

    internal static void EnsureInitialized()
    {
        if (_initialized) return;
        _initialized = true;
        GD.Print("[RefreshShop] Loaded. Merchant refresh design scaffold is active.");
    }

    internal static void ApplyHarmonyPatches()
    {
        if (_patched) return;

        try
        {
            var harmony = new Harmony(HarmonyId);
            harmony.PatchAll(Assembly.GetExecutingAssembly());
            _patched = true;
            GD.Print("[RefreshShop] Harmony patches applied.");
        }
        catch (Exception ex)
        {
            GD.PushError($"[RefreshShop] Harmony patch failed: {ex}");
        }
    }
}