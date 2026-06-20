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
    public static bool RebuildShop(object inventoryNode, bool refillsPurchased = true)
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

            // 2b. 不补货模式：对旧 inventory 中 IsStocked==false 的位置，
            //     在新 inventory 对应 entry 上调 ClearAfterPurchase（protected abstract，反射）
            //     使其 IsStocked 变 false。后续 Initialize 的 FillSlot 会自然显示空槽。
            if (!refillsPurchased)
            {
                PreserveEmptySlots(oldInventory, newInventory);
            }

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

            // 8. 强制恢复 hidden slot 的可见性
            // NMerchantRelic.UpdateVisual 在购买后设 Visible=false，Initialize 的 FillSlot
            // 虽然更新了 _relicEntry，但 _relicNode 延迟释放导致 UpdateVisual 可能没重建节点。
            // 这里遍历所有 slot，对 !Visible 的强制恢复并重新调 UpdateVisual。
            ForceRestoreSlotVisibility(inventoryNode, newInventory);

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
    /// 遍历所有 slot，对 Visible=false 的强制恢复可见性 + 重新调 UpdateVisual。
    /// NMerchantRelic/NMerchantCard/NMerchantPotion 在购买后设 Visible=false，
    /// Initialize 的 FillSlot 虽然更新了 entry，但 _relicNode 等延迟释放导致
    /// UpdateVisual 可能没重建视觉节点。这里强制走一遍 UpdateVisual 确保恢复。
    /// </summary>
    private static void ForceRestoreSlotVisibility(object inventoryNode, object newInventory)
    {
        try
        {
            var invType = inventoryNode.GetType();
            var getAllSlots = invType.GetMethod("GetAllSlots", BindingFlags.Public | BindingFlags.Instance);
            if (getAllSlots == null) return;

            var slots = getAllSlots.Invoke(inventoryNode, null) as IEnumerable;
            if (slots == null) return;

            int restored = 0;
            foreach (var slotObj in slots)
            {
                if (slotObj is not Control slot) continue;

                // 检查 slot 上的 Entry 是否 IsStocked
                var entryProp = slot.GetType().GetProperty("Entry");
                var entry = entryProp?.GetValue(slot);
                if (entry == null) continue;

                var isStockedProp = entry.GetType().GetProperty("IsStocked");
                if (isStockedProp == null) continue;
                var stockedVal = isStockedProp.GetValue(entry);
                if (stockedVal is bool isStocked && !isStocked) continue; // 确实没货，不恢复

                // 有货但 Visible=false → 强制恢复
                if (!slot.Visible)
                {
                    slot.Visible = true;
                    slot.MouseFilter = Control.MouseFilterEnum.Stop;
                    restored++;
                }

                // 重新调 UpdateVisual（非 public virtual）
                var updateVisual = slot.GetType().GetMethod("UpdateVisual",
                    BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Public);
                updateVisual?.Invoke(slot, null);
            }

            if (restored > 0)
                RefreshShopLog.Info($"Force restored {restored} hidden slot(s) to visible.");
        }
        catch (Exception ex)
        {
            RefreshShopLog.Warn($"ForceRestoreSlotVisibility failed (non-fatal): {ex.Message}");
        }
    }

    /// <summary>
    /// 不补货模式：遍历旧 inventory 的 AllEntries，对 IsStocked==false 的位置，
    /// 在新 inventory 对应位置 entry 上调 ClearAfterPurchase（protected abstract，反射）
    /// 使其 IsStocked 变 false。后续 Initialize → FillSlot 会自然显示空槽。
    ///
    /// AllEntries 顺序：CharacterCardEntries + ColorlessCardEntries + RelicEntries + PotionEntries + CardRemovalEntry。
    /// 新旧 inventory 结构相同（同 CreateForNormalMerchant），按索引对应。
    /// </summary>
    private static void PreserveEmptySlots(object oldInventory, object newInventory)
    {
        try
        {
            var oldInvType = oldInventory.GetType();
            var newInvType = newInventory.GetType();

            var allEntriesProp = oldInvType.GetProperty("AllEntries");
            if (allEntriesProp == null) return;
            var oldEntries = allEntriesProp.GetValue(oldInventory) as IEnumerable;
            if (oldEntries == null) return;

            var newEntries = newInvType.GetProperty("AllEntries")?.GetValue(newInventory) as IEnumerable;
            if (newEntries == null) return;

            var oldList = new System.Collections.Generic.List<object>();
            foreach (var e in oldEntries) oldList.Add(e);
            var newList = new System.Collections.Generic.List<object>();
            foreach (var e in newEntries) newList.Add(e);

            int cleared = 0;
            int count = System.Math.Min(oldList.Count, newList.Count);
            for (int i = 0; i < count; i++)
            {
                var oldEntry = oldList[i];
                if (oldEntry == null) continue;

                var isStockedProp = oldEntry.GetType().GetProperty("IsStocked");
                if (isStockedProp == null) continue;
                var stocked = isStockedProp.GetValue(oldEntry);
                if (stocked is bool b && b) continue; // 仍有货，不用处理

                // 旧槽已空 → 对新 entry 调 ClearAfterPurchase
                var newEntry = newList[i];
                if (newEntry == null) continue;
                var clearMethod = newEntry.GetType().GetMethod(
                    "ClearAfterPurchase",
                    BindingFlags.NonPublic | BindingFlags.Instance);
                clearMethod?.Invoke(newEntry, null);
                cleared++;
            }
            if (cleared > 0)
                RefreshShopLog.Info($"Preserved {cleared} empty slot(s) (no refill).");
        }
        catch (Exception ex)
        {
            RefreshShopLog.Warn($"PreserveEmptySlots failed (non-fatal): {ex.Message}");
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
            // 需要断开的信号列表：focus_entered + NMerchantSlot 的 Hovered/Unhovered
            // NMerchantSlot.Initialize 里会重新 Connect Hovered/Unhovered，不断开会报 already connected
            var signalNames = new[] { "focus_entered", "Hovered", "Unhovered" };
            foreach (var slotObj in slots)
            {
                if (slotObj is not Node slotNode) continue;
                foreach (var sigName in signalNames)
                {
                    try
                    {
                        foreach (var dict in slotNode.GetSignalConnectionList(sigName))
                        {
                            if (dict.TryGetValue("callable", out var c) && c.Obj is Callable callable)
                            {
                                slotNode.Disconnect(sigName, callable);
                                count++;
                            }
                        }
                    }
                    catch { /* 单信号断开失败不影响其他 */ }
                }
            }
            RefreshShopLog.Info($"Disconnected {count} stale slot signal handlers (focus_entered + Hovered + Unhovered).");
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