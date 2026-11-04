using Godot;
using HarmonyLib;
using System;
using System.Reflection;

namespace RefreshShop;

public static class RefreshShopMod
{
    public const string ModId = "RefreshShop";

    private static bool _initialized;

    internal static void EnsureInitialized()
    {
        if (_initialized) return;
        _initialized = true;
        RefreshShopConfig.Initialize();
        // ModManager's fallback loader owns PatchAll. Do not install the same patches twice.
        GD.Print("[RefreshShop] Initialized (native loader patches, local refresh protocol).");
    }
}
