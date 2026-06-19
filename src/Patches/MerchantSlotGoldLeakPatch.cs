using System;
using System.Collections.Generic;
using System.Reflection;
using Godot;
using HarmonyLib;

namespace RefreshShop.Patches;

/// <summary>
/// 修复 STS2 0.107 vanilla 商店事件泄漏（v0.1.2 引入）。
///
/// 真因（已通过反编译验证）：
///   NMerchantSlot.Initialize 把 this.UpdateVisual 挂到 Player.GoldChanged（一个 System.Action 事件）。
///   NMerchantSlot._ExitTree 调 if (this.Player != null) this.Player.GoldChanged -= this.UpdateVisual
///   解订阅。但 Player getter = _merchantRug?.Inventory?.Player。
///   RefreshShop.RebuildShop 在重新 Initialize 之前会清掉 NMerchantInventory.Inventory backing field，
///   旧 slot 的 _ExitTree 此时 Player getter 返回 null → 跳过 -=，订阅泄漏。
///   下一家商店扣金币时，Player.set_Gold 同步 invoke 全部订阅者，命中已 dispose 的旧 slot 的
///   UpdateVisual → 访问 disposed _costLabel (MegaLabel) → ObjectDisposedException →
///   异常上抛吞掉 MerchantRelicEntry.OnTryPurchase 后半段 → 玩家金币扣了但遗物没拿到，
///   GoldLostMessage / RewardObtainedMessage 也没发出去，主机端看不到客机的购买。
///
/// 修复策略：
///   1. Postfix `NMerchantSlot.Initialize`：用一个"自愈守卫"委托替换 vanilla 直接挂的
///      `this.UpdateVisual`。守卫委托每次被 GoldChanged 调用时先检查 IsInstanceValid，
///      如果 slot 已 dispose 就把自己从 GoldChanged 上 -= 摘除并 return。
///   2. Postfix `NMerchantSlot._ExitTree`：保险摘除我们 cache 里登记的守卫委托，
///      处理"slot 离开但下次 set_Gold 没机会触发自愈"的场景，避免委托无限累积。
///
/// 这个 patch 仅在装了 RefreshShop 的玩家本机生效。未装 RefreshShop 的玩家不会碰到原 bug，
/// 也不需要此 patch。
/// </summary>
internal static class MerchantSlotGoldLeakPatch
{
    /// <summary>记录 vanilla 默认订阅 (UpdateVisual) 和我们替换上去的守卫委托，便于 ExitTree 时摘除。</summary>
    private static readonly Dictionary<object, GuardEntry> _guards = new();
    private static readonly object _lock = new();

    private struct GuardEntry
    {
        public WeakReference Player;
        public Action Vanilla;
        public Action Guard;
    }

    [HarmonyPatch]
    internal static class InitializePostfix
    {
        static MethodBase TargetMethod()
        {
            var t = AccessTools.TypeByName("MegaCrit.Sts2.Core.Nodes.Screens.Shops.NMerchantSlot");
            return t?.GetMethod("Initialize", BindingFlags.Public | BindingFlags.Instance);
        }

        [HarmonyPostfix]
        static void Postfix(object __instance)
        {
            try
            {
                if (__instance is not Node slotNode) return;
                var slotType = __instance.GetType();

                var playerProp = FindPropertyRecursive(slotType, "Player");
                var player = playerProp?.GetValue(__instance);
                if (player == null) return;

                var goldChangedEvent = FindEventRecursive(player.GetType(), "GoldChanged");
                if (goldChangedEvent == null) return;

                var updateVisualMethod = FindMethodRecursive(slotType, "UpdateVisual",
                    BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                if (updateVisualMethod == null) return;

                // vanilla 已经挂了的委托对象（参照 NMerchantSlot.Initialize：this.Player.GoldChanged += this.UpdateVisual）
                var vanillaDelegate = (Action)Delegate.CreateDelegate(typeof(Action), __instance, updateVisualMethod);
                goldChangedEvent.RemoveEventHandler(player, vanillaDelegate);

                // 自愈守卫
                Action guard = null;
                guard = () =>
                {
                    if (!GodotObject.IsInstanceValid(slotNode))
                    {
                        try { goldChangedEvent.RemoveEventHandler(player, guard); } catch { }
                        lock (_lock) _guards.Remove(slotNode);
                        return;
                    }
                    try { vanillaDelegate(); }
                    catch (ObjectDisposedException)
                    {
                        // _costLabel 等子节点单独 dispose 但 slot 还活着的边角情况：摘除自身防止后续噪声。
                        try { goldChangedEvent.RemoveEventHandler(player, guard); } catch { }
                        lock (_lock) _guards.Remove(slotNode);
                    }
                    catch (Exception ex)
                    {
                        RefreshShopLog.Warn($"NMerchantSlot guard delegate threw: {ex.Message}");
                    }
                };
                goldChangedEvent.AddEventHandler(player, guard);

                lock (_lock)
                {
                    _guards[slotNode] = new GuardEntry
                    {
                        Player = new WeakReference(player),
                        Vanilla = vanillaDelegate,
                        Guard = guard,
                    };
                }
            }
            catch (Exception ex)
            {
                RefreshShopLog.Warn($"InitializePostfix failed (non-fatal): {ex.Message}");
            }
        }
    }

    [HarmonyPatch]
    internal static class ExitTreePostfix
    {
        static MethodBase TargetMethod()
        {
            var t = AccessTools.TypeByName("MegaCrit.Sts2.Core.Nodes.Screens.Shops.NMerchantSlot");
            return t?.GetMethod("_ExitTree",
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
        }

        [HarmonyPostfix]
        static void Postfix(object __instance)
        {
            try
            {
                if (__instance is not Node slotNode) return;
                GuardEntry entry;
                lock (_lock)
                {
                    if (!_guards.TryGetValue(slotNode, out entry)) return;
                    _guards.Remove(slotNode);
                }

                var player = entry.Player?.Target;
                if (player == null || entry.Guard == null) return;

                var goldChangedEvent = FindEventRecursive(player.GetType(), "GoldChanged");
                if (goldChangedEvent == null) return;

                try { goldChangedEvent.RemoveEventHandler(player, entry.Guard); } catch { }
            }
            catch (Exception ex)
            {
                RefreshShopLog.Warn($"ExitTreePostfix failed (non-fatal): {ex.Message}");
            }
        }
    }

    private static EventInfo FindEventRecursive(Type type, string name)
    {
        var t = type;
        while (t != null)
        {
            var e = t.GetEvent(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            if (e != null) return e;
            t = t.BaseType;
        }
        return null;
    }

    private static MethodInfo FindMethodRecursive(Type type, string name, BindingFlags flags)
    {
        var t = type;
        while (t != null)
        {
            var m = t.GetMethod(name, flags | BindingFlags.DeclaredOnly);
            if (m != null) return m;
            t = t.BaseType;
        }
        return null;
    }

    private static PropertyInfo FindPropertyRecursive(Type type, string name)
    {
        var t = type;
        while (t != null)
        {
            var p = t.GetProperty(name,
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.DeclaredOnly);
            if (p != null) return p;
            t = t.BaseType;
        }
        return null;
    }
}