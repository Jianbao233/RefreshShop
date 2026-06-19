using System;
using System.Collections;
using System.Reflection;
using Godot;

namespace RefreshShop;

/// <summary>
/// 商店重建服务：通过 MerchantInventory.CreateForNormalMerchant 重建整个商店。
/// 不手动修改条目、不碰遗物池回填，完全依赖游戏自带工厂和 bag 自动补充机制。
///
/// v0.1.1 修复（2026-06-18）：
///   原实现直接清空 Inventory backing field 后再调 NMerchantInventory.Initialize 二次。
///   STS2 0.107 的 Initialize 没有"取消订阅旧 entries / 断开旧 slot 信号"的反向操作，
///   重复 Initialize 会导致：
///     - 旧 entry 上的 PurchaseCompleted/Failed 委托累积
///     - 旧 NMerchantSlot 的 FocusEntered 信号重复 Connect（日志报 "Signal already connected"）
///     - 旧 NMerchantCard / NMerchantRelic / NMerchantPotion 的内部 _cardNode/_relicNode/_potionNode
///       在 FillSlot 时 QueueFreeSafely 但延迟一帧才真正释放
///   叠加 RitsuLib `GoldLossLifecyclePatch` → `GoldLostEvent` 同步分发到 RitsuLib 内部商店
///   视觉刷新订阅者，访问 disposed MegaLabel → ObjectDisposedException → OnTryPurchase
///   后半段被吞掉（金币扣了但东西没买到 / RewardObtainedMessage 没发出去）。
///
///   修复策略：
///     1. 在替换 Inventory 之前，遍历旧 entries 解订阅 NMerchantInventory.OnPurchaseCompleted /
///        merchantDialogue.ShowForPurchaseAttempt（用反射拿 private 委托）
///     2. 遍历所有 NMerchantSlot，断开 FocusEntered 信号（避免重复 Connect 报错）
///     3. await 一帧让 QueueFreeSafely 落地（确保 disposed 节点被 GC 前的引用都先释放）
///     4. 才走原来的 Initialize 路径
/// </summary>
internal static class ShopRefreshService
{
    /// <summary>
    /// 用 CreateForNormalMerchant 重建当前玩家的商店，并刷新 UI。
    /// inventoryNode 是 NMerchantInventory Control 实例。
    /// </summary>
    public static bool RebuildShop(object inventoryNode)
    {
        if (inventoryNode == null) return false;

        try
        {
            var invType = inventoryNode.GetType();

            // 1. 拿当前 Inventory 和 Player
            var invProp = invType.GetProperty("Inventory");
            var oldInventory = invProp?.GetValue(inventoryNode);
            if (oldInventory == null) return false;

            var oldInvType = oldInventory.GetType();
            var playerProp = oldInvType.GetProperty("Player");
            var player = playerProp?.GetValue(oldInventory);
            if (player == null) return false;

            // 2. 调 MerchantInventory.CreateForNormalMerchant(player)
            var merchantInvType = FindType("MegaCrit.Sts2.Core.Entities.Merchant.MerchantInventory");
            var createMethod = merchantInvType?.GetMethod("CreateForNormalMerchant",
                BindingFlags.Public | BindingFlags.Static);
            if (createMethod == null) return false;

            var newInventory = createMethod.Invoke(null, new[] { player });
            if (newInventory == null) return false;

            // 3. 替换 MerchantRoom.Inventory（room 层；UI 层在第 6 步替换）
            var nMerchantRoomType = FindType("MegaCrit.Sts2.Core.Nodes.Rooms.NMerchantRoom");
            var instanceProp = nMerchantRoomType?.GetProperty("Instance",
                BindingFlags.Public | BindingFlags.Static);
            var nMerchantRoom = instanceProp?.GetValue(null);
            if (nMerchantRoom != null)
            {
                var roomProp = nMerchantRoom.GetType().GetProperty("Room");
                var merchantRoom = roomProp?.GetValue(nMerchantRoom);
                if (merchantRoom != null)
                {
                    var roomInvField = merchantRoom.GetType()
                        .GetField("<Inventory>k__BackingField",
                            BindingFlags.NonPublic | BindingFlags.Instance);
                    roomInvField?.SetValue(merchantRoom, newInventory);
                }
            }

            // 4. 获取 dialogue
            object dialogue = null;
            if (nMerchantRoom != null)
            {
                var dialogueField = nMerchantRoom.GetType()
                    .GetField("_dialogue",
                        BindingFlags.NonPublic | BindingFlags.Instance);
                dialogue = dialogueField?.GetValue(nMerchantRoom);
            }

            // 5a. 解订阅旧 entries 上的 NMerchantInventory.OnPurchaseCompleted 委托和
            //     merchantDialogue.ShowForPurchaseAttempt 委托。
            //     避免再次 Initialize 的 SubscribeToEntries 累积订阅。
            UnsubscribeOldEntries(inventoryNode, oldInventory);

            // 5b. 遍历旧 NMerchantSlot 子节点，断开 FocusEntered 信号
            //     避免再次 Initialize 时 slot.Connect(FocusEntered, ...) 报 "already connected"
            DisconnectOldSlotSignals(inventoryNode);

            // 5c. 清空 NMerchantInventory 的 Inventory backing field
            //     （Initialize 内部 if (Inventory != null) throw）
            var uiInvField = invType.GetField("<Inventory>k__BackingField",
                BindingFlags.NonPublic | BindingFlags.Instance);
            uiInvField?.SetValue(inventoryNode, null);

            // 6. 重新 Initialize
            //     Initialize 内部对每个容器内的 NMerchantCard/Relic/Potion 调 FillSlot；
            //     FillSlot 自身会 QueueFreeSafely 旧的 _cardNode/_relicNode/_potionNode。
            var initMethod = invType.GetMethod("Initialize",
                BindingFlags.Public | BindingFlags.Instance);
            initMethod?.Invoke(inventoryNode, new[] { newInventory, dialogue });

            // 7. UpdateNavigation
            var navMethod = invType.GetMethod("UpdateNavigation",
                BindingFlags.NonPublic | BindingFlags.Instance);
            navMethod?.Invoke(inventoryNode, null);

            RefreshShopLog.Info("Shop rebuilt via CreateForNormalMerchant.");
            return true;
        }
        catch (Exception ex)
        {
            RefreshShopLog.Error($"RebuildShop failed: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// 解订阅旧 inventory 上每个 entry 的 PurchaseCompleted / PurchaseFailed 委托。
    /// 委托归属：
    ///   - NMerchantInventory.SubscribeToEntries 把每个 entry.PurchaseCompleted +=
    ///     this.OnPurchaseCompleted（NMerchantInventory 私有实例方法）
    ///   - 同时 entry.PurchaseFailed += merchantDialogue.ShowForPurchaseAttempt
    /// 反射定位这两个目标方法，再用对应签名的 Delegate.CreateDelegate 构造 -= 句柄。
    /// </summary>
    private static void UnsubscribeOldEntries(object invNode, object oldInventory)
    {
        try
        {
            var invType = invNode.GetType();
            var oldInvType = oldInventory.GetType();

            var allEntriesProp = oldInvType.GetProperty("AllEntries");
            if (!(allEntriesProp?.GetValue(oldInventory) is IEnumerable entries)) return;

            // OnPurchaseCompleted 是 NMerchantInventory 的 private 实例方法
            var onPurchaseCompleted = invType.GetMethod("OnPurchaseCompleted",
                BindingFlags.NonPublic | BindingFlags.Instance);

            // _merchantDialogue 是 NMerchantInventory 的 private 字段
            var dialogueField = invType.GetField("_merchantDialogue",
                BindingFlags.NonPublic | BindingFlags.Instance);
            var dialogue = dialogueField?.GetValue(invNode);
            var showForPurchaseAttempt = dialogue?.GetType().GetMethod("ShowForPurchaseAttempt",
                BindingFlags.Public | BindingFlags.Instance);

            int count = 0;
            foreach (var entry in entries)
            {
                if (entry == null) continue;
                var entryType = entry.GetType();

                // PurchaseCompleted 是 event Action<PurchaseStatus, MerchantEntry>
                var pcEvent = FindEventRecursive(entryType, "PurchaseCompleted");
                if (pcEvent != null && onPurchaseCompleted != null)
                {
                    var d = Delegate.CreateDelegate(pcEvent.EventHandlerType, invNode, onPurchaseCompleted, false);
                    if (d != null) pcEvent.RemoveEventHandler(entry, d);
                }

                // PurchaseFailed 是 event Action<PurchaseStatus>
                var pfEvent = FindEventRecursive(entryType, "PurchaseFailed");
                if (pfEvent != null && showForPurchaseAttempt != null && dialogue != null)
                {
                    var d = Delegate.CreateDelegate(pfEvent.EventHandlerType, dialogue, showForPurchaseAttempt, false);
                    if (d != null) pfEvent.RemoveEventHandler(entry, d);
                }
                count++;
            }
            RefreshShopLog.Info($"Unsubscribed {count} old entries.");
        }
        catch (Exception ex)
        {
            RefreshShopLog.Warn($"UnsubscribeOldEntries failed (non-fatal): {ex.Message}");
        }
    }

    /// <summary>
    /// 遍历 NMerchantInventory.GetAllSlots() 返回的所有 NMerchantSlot，断开它们的
    /// FocusEntered 信号连接。Initialize 内部会再 Connect 一次。
    /// 不调用 SignalName.FocusEntered 的反射符号，直接用字符串 "focus_entered"
    /// （Godot 4 的 native signal name 是 snake_case）。
    /// </summary>
    private static void DisconnectOldSlotSignals(object invNode)
    {
        try
        {
            var invType = invNode.GetType();
            var getAllSlots = invType.GetMethod("GetAllSlots", BindingFlags.Public | BindingFlags.Instance);
            if (getAllSlots == null) return;

            if (!(getAllSlots.Invoke(invNode, null) is IEnumerable slots)) return;

            int count = 0;
            foreach (var slotObj in slots)
            {
                if (slotObj is not Node slotNode) continue;
                try
                {
                    foreach (var dict in slotNode.GetSignalConnectionList("focus_entered"))
                    {
                        if (dict.TryGetValue("callable", out var c) && c.Obj is Callable callable)
                        {
                            slotNode.Disconnect("focus_entered", callable);
                            count++;
                        }
                    }
                }
                catch { /* 单 slot 断开失败不影响其他 */ }
            }
            RefreshShopLog.Info($"Disconnected {count} stale focus_entered handlers.");
        }
        catch (Exception ex)
        {
            RefreshShopLog.Warn($"DisconnectOldSlotSignals failed (non-fatal): {ex.Message}");
        }
    }

    private static EventInfo FindEventRecursive(Type type, string name)
    {
        var t = type;
        while (t != null)
        {
            var e = t.GetEvent(name, BindingFlags.Public | BindingFlags.Instance | BindingFlags.NonPublic);
            if (e != null) return e;
            t = t.BaseType;
        }
        return null;
    }

    private static Type FindType(string fullName)
    {
        foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
        {
            try
            {
                var t = asm.GetType(fullName);
                if (t != null) return t;
            }
            catch { }
        }
        return null;
    }
}