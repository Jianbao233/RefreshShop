using Godot;
using HarmonyLib;
using System;
using System.Reflection;

namespace RefreshShop;

[HarmonyPatch]
internal static class ModManagerInitPostfix
{
    private static bool _initScheduled;

    static ModManagerInitPostfix()
    {
        ScheduleInit();
    }

    static MethodBase TargetMethod()
    {
        var t = AccessTools.TypeByName("MegaCrit.Sts2.Core.Modding.ModManager")
            ?? AccessTools.TypeByName("ModManager");
        return t?.GetMethod("Initialize", BindingFlags.Public | BindingFlags.Static);
    }

    static void Postfix()
    {
        ScheduleInit();
    }

    private static void ScheduleInit()
    {
        if (_initScheduled) return;

        try
        {
            var tree = Engine.GetMainLoop() as SceneTree;
            if (tree == null) return;

            _initScheduled = true;
            tree.ProcessFrame += OnInitFrame1;
        }
        catch
        {
            // 初始化兜底，不影响游戏启动。
        }
    }

    private static void OnInitFrame1()
    {
        var tree = Engine.GetMainLoop() as SceneTree;
        if (tree == null) return;

        tree.ProcessFrame -= OnInitFrame1;
        tree.ProcessFrame += OnInitFrame2;
    }

    private static void OnInitFrame2()
    {
        var tree = Engine.GetMainLoop() as SceneTree;
        if (tree == null) return;

        tree.ProcessFrame -= OnInitFrame2;
        RefreshShopMod.EnsureInitialized();
        RefreshShopMod.ApplyHarmonyPatches();
    }
}