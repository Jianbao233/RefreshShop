using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace RefreshShop;

/// <summary>
/// 商店重建服务（v0.2.1）：不调 CreateForNormalMerchant，直接在现有 inventory 上替换 entries 的 Model。
/// 不消耗 grab bag / 药水池，实现无限刷新不影响商店外事件。
/// </summary>
internal static class ShopRefreshService
{
    // 反射缓存
    private static Type _merchantInventoryType;
    private static Type _merchantRelicEntryType;
    private static Type _merchantPotionEntryType;
    private static Type _merchantCardEntryType;
    private static Type _relicModelType;
    private static Type _potionModelType;
    private static Type _modelIdType;
    private static Type _modelDbType;
    private static Type _relicFactoryType;
    private static Type _playerType;
    private static Type _relicRarityType;

    private static PropertyInfo _inventoryProp;
    private static PropertyInfo _playerProp;
    private static PropertyInfo _relicEntriesProp;
    private static PropertyInfo _potionEntriesProp;
    private static PropertyInfo _characterCardEntriesProp;
    private static PropertyInfo _colorlessCardEntriesProp;
    private static PropertyInfo _allEntriesProp;

    private static PropertyInfo _relicEntryModelProp;
    private static MethodInfo _relicEntrySetModelMethod;
    private static PropertyInfo _relicEntryIsStockedProp;
    private static PropertyInfo _relicModelIdProp;
    private static PropertyInfo _relicModelRarityProp;
    private static PropertyInfo _relicModelIsAllowedInShopsProp;
    private static MethodInfo _relicModelToMutableMethod;

    private static PropertyInfo _potionEntryModelProp;
    private static PropertyInfo _potionEntryIsStockedProp;
    private static PropertyInfo _potionModelIdProp;
    private static MethodInfo _potionModelToMutableMethod;
    private static MethodInfo _potionEntryCalcCostMethod;

    private static MethodInfo _cardEntryPopulateMethod;
    private static PropertyInfo _cardEntryIsStockedProp;

    private static PropertyInfo _allRelicsProp;
    private static PropertyInfo _allPotionsProp;

    private static MethodInfo _rollRarityMethod;
    private static PropertyInfo _playerRngProp;
    private static PropertyInfo _shopsRngProp;
    private static MethodInfo _rngNextIntMethod;

    private static PropertyInfo _playerRelicsProp;
    private static PropertyInfo _modelIdEntryProp;

    private static MethodInfo _onMerchantInventoryUpdatedMethod;

    private static bool _init;
    private static bool _initFailed;

    private static void EnsureInit()
    {
        if (_init || _initFailed) return;
        _init = true;

        try
        {
            _merchantInventoryType = FindType("MegaCrit.Sts2.Core.Entities.Merchant.MerchantInventory");
            _merchantRelicEntryType = FindType("MegaCrit.Sts2.Core.Entities.Merchant.MerchantRelicEntry");
            _merchantPotionEntryType = FindType("MegaCrit.Sts2.Core.Entities.Merchant.MerchantPotionEntry");
            _merchantCardEntryType = FindType("MegaCrit.Sts2.Core.Entities.Merchant.MerchantCardEntry");
            _relicModelType = FindType("MegaCrit.Sts2.Core.Models.RelicModel");
            _potionModelType = FindType("MegaCrit.Sts2.Core.Models.PotionModel");
            _modelIdType = FindType("MegaCrit.Sts2.Core.Models.ModelId");
            _modelDbType = FindType("MegaCrit.Sts2.Core.Models.ModelDb");
            _relicFactoryType = FindType("MegaCrit.Sts2.Core.Factories.RelicFactory");
            _playerType = FindType("MegaCrit.Sts2.Core.Entities.Players.Player");
            _relicRarityType = FindType("MegaCrit.Sts2.Core.Models.RelicRarity") ?? FindType("MegaCrit.Sts2.Core.Models.Relics.RelicRarity");

            if (_merchantInventoryType == null || _merchantRelicEntryType == null || _merchantPotionEntryType == null
                || _relicModelType == null || _potionModelType == null || _modelIdType == null
                || _modelDbType == null || _relicFactoryType == null || _playerType == null)
            {
                _initFailed = true;
                return;
            }

            // NMerchantInventory 是 UI 节点，MerchantInventory 是数据对象
            // inventoryNode 是 NMerchantInventory，需要从它的类型获取 Inventory 属性
            // Player / RelicEntries / PotionEntries 等在 MerchantInventory 上
            var nMerchantInventoryType = FindType("MegaCrit.Sts2.Core.Nodes.Screens.Shops.NMerchantInventory");
            _inventoryProp = nMerchantInventoryType?.GetProperty("Inventory") ?? _merchantInventoryType.GetProperty("Inventory");
            _playerProp = _merchantInventoryType.GetProperty("Player");
            _relicEntriesProp = _merchantInventoryType.GetProperty("RelicEntries");
            _potionEntriesProp = _merchantInventoryType.GetProperty("PotionEntries");
            _characterCardEntriesProp = _merchantInventoryType.GetProperty("CharacterCardEntries");
            _colorlessCardEntriesProp = _merchantInventoryType.GetProperty("ColorlessCardEntries");
            _allEntriesProp = _merchantInventoryType.GetProperty("AllEntries");

            // Relic entry
            _relicEntryModelProp = _merchantRelicEntryType.GetProperty("Model");
            _relicEntrySetModelMethod = _merchantRelicEntryType.GetMethod("SetModel", BindingFlags.NonPublic | BindingFlags.Instance);
            _relicEntryIsStockedProp = FindAbstractProp(_merchantRelicEntryType, "IsStocked");

            // Relic model
            _relicModelIdProp = FindAbstractProp(_relicModelType, "Id") ?? _relicModelType.GetProperty("Id");
            _relicModelRarityProp = _relicModelType.GetProperty("Rarity");
            _relicModelIsAllowedInShopsProp = _relicModelType.GetProperty("IsAllowedInShops");
            _relicModelToMutableMethod = _relicModelType.GetMethod("ToMutable", Type.EmptyTypes);

            // Potion entry
            _potionEntryModelProp = _merchantPotionEntryType.GetProperty("Model");
            _potionEntryIsStockedProp = FindAbstractProp(_merchantPotionEntryType, "IsStocked");
            _potionEntryCalcCostMethod = _merchantPotionEntryType.GetMethod("CalcCost", BindingFlags.Public | BindingFlags.Instance);

            // Potion model
            _potionModelIdProp = FindAbstractProp(_potionModelType, "Id") ?? _potionModelType.GetProperty("Id");
            _potionModelToMutableMethod = _potionModelType.GetMethod("ToMutable", Type.EmptyTypes);

            // Card entry
            _cardEntryPopulateMethod = _merchantCardEntryType.GetMethod("Populate", BindingFlags.Public | BindingFlags.Instance);
            _cardEntryIsStockedProp = FindAbstractProp(_merchantCardEntryType, "IsStocked");

            // ModelDb
            _allRelicsProp = _modelDbType.GetProperty("AllRelics", BindingFlags.Public | BindingFlags.Static);
            _allPotionsProp = _modelDbType.GetProperty("AllPotions", BindingFlags.Public | BindingFlags.Static);

            // RelicFactory
            foreach (var m in _relicFactoryType.GetMethods(BindingFlags.Public | BindingFlags.Static))
            {
                if (m.Name == "RollRarity" && m.GetParameters().Length == 1)
                {
                    _rollRarityMethod = m;
                    break;
                }
            }

            // Player
            _playerRngProp = _playerType.GetProperty("PlayerRng");
            _shopsRngProp = _playerRngProp?.PropertyType.GetProperty("Shops");
            _rngNextIntMethod = _shopsRngProp?.PropertyType.GetMethod("NextInt", new[] { typeof(int) });
            _playerRelicsProp = _playerType.GetProperty("Relics");

            // ModelId
            _modelIdEntryProp = _modelIdType.GetProperty("Entry");

            // MerchantEntry.OnMerchantInventoryUpdated
            var merchantEntryType = FindType("MegaCrit.Sts2.Core.Entities.Merchant.MerchantEntry");
            _onMerchantInventoryUpdatedMethod = merchantEntryType?.GetMethod("OnMerchantInventoryUpdated", BindingFlags.Public | BindingFlags.Instance);

            if (_relicEntrySetModelMethod == null || _rollRarityMethod == null || _allRelicsProp == null || _allPotionsProp == null)
            {
                RefreshShopLog.Warn("ShopRefreshService: some reflection targets not found.");
                _initFailed = true;
            }
        }
        catch (Exception ex)
        {
            RefreshShopLog.Error($"ShopRefreshService init failed: {ex.Message}");
            _initFailed = true;
        }
    }

    public static bool RebuildShop(object inventoryNode, bool refillsPurchased = true)
    {
        if (inventoryNode == null) return false;

        EnsureInit();
        if (_initFailed)
        {
            RefreshShopLog.Warn("ShopRefreshService not initialized.");
            return false;
        }

        try
        {
            var invType = inventoryNode.GetType();
            var inventory = _inventoryProp?.GetValue(inventoryNode);
            if (inventory == null) return false;

            var player = _playerProp?.GetValue(inventory);
            if (player == null) return false;

            var shopsRng = GetShopsRng(player);

            // 1. 替换遗物
            RerollRelics(inventory, player, shopsRng, refillsPurchased);

            // 2. 替换药水
            RerollPotions(inventory, player, shopsRng, refillsPurchased);

            // 3. 替换卡牌
            RerollCards(inventory, refillsPurchased);

            // 4. 触发 UI 刷新（含补货 slot 恢复）
            TriggerUiRefresh(inventoryNode, inventory);

            // 5. MerchantBlacklist 兼容
            TryApplyBlacklistFilter(inventory);

            RefreshShopLog.Info("Shop refreshed (no grab bag consumption).");
            return true;
        }
        catch (Exception ex)
        {
            RefreshShopLog.Error($"RebuildShop failed: {ex.Message}");
            return false;
        }
    }

    private static void RerollRelics(object inventory, object player, object shopsRng, bool refillsPurchased)
    {
        var relics = _relicEntriesProp?.GetValue(inventory) as IEnumerable;
        if (relics == null) return;

        var alreadyChosen = new HashSet<string>(StringComparer.Ordinal);
        var relicList = new List<object>();
        foreach (var r in relics) relicList.Add(r);

        // 收集已有遗物 ID
        var playerRelics = _playerRelicsProp?.GetValue(player) as IEnumerable;
        var ownedRelicIds = new HashSet<string>(StringComparer.Ordinal);
        if (playerRelics != null)
        {
            foreach (var pr in playerRelics)
            {
                var id = GetModelId(pr, _relicModelIdProp);
                if (id != null) ownedRelicIds.Add(id);
            }
        }

        foreach (var entry in relicList)
        {
            var isStocked = (bool)(_relicEntryIsStockedProp?.GetValue(entry) ?? false);
            if (!isStocked && !refillsPurchased) continue;

            // Roll rarity
            var rarity = _rollRarityMethod.Invoke(null, new[] { player });

            // 从 ModelDb.AllRelics 选候选
            var allRelics = _allRelicsProp.GetValue(null) as IEnumerable;
            if (allRelics == null) continue;

            var candidates = new List<object>();
            foreach (var relic in allRelics)
            {
                if (relic == null) continue;
                var id = GetModelId(relic, _relicModelIdProp);
                if (string.IsNullOrEmpty(id)) continue;
                if (alreadyChosen.Contains(id)) continue;
                if (ownedRelicIds.Contains(id)) continue;
                if (IsRelicBannedByBlacklist(id)) continue;

                var relicRarity = _relicModelRarityProp?.GetValue(relic);
                if (relicRarity == null || !relicRarity.Equals(rarity)) continue;

                var allowed = (bool)(_relicModelIsAllowedInShopsProp?.GetValue(relic) ?? false);
                if (!allowed) continue;

                candidates.Add(relic);
            }

            if (candidates.Count == 0) continue;

            // 随机选一个
            int index = RngNextInt(shopsRng, candidates.Count);
            var chosen = candidates[index % candidates.Count];
            var chosenId = GetModelId(chosen, _relicModelIdProp);
            if (chosenId != null) alreadyChosen.Add(chosenId);

            // 反射调 SetModel(chosen.ToMutable())
            var mutable = _relicModelToMutableMethod?.Invoke(chosen, null) ?? chosen;
            _relicEntrySetModelMethod?.Invoke(entry, new[] { mutable });
        }
    }

    private static void RerollPotions(object inventory, object player, object shopsRng, bool refillsPurchased)
    {
        var potions = _potionEntriesProp?.GetValue(inventory) as IEnumerable;
        if (potions == null) return;

        var alreadyChosen = new HashSet<string>(StringComparer.Ordinal);
        var potionList = new List<object>();
        foreach (var p in potions) potionList.Add(p);

        var allPotions = _allPotionsProp?.GetValue(null) as IEnumerable;
        if (allPotions == null) return;

        // 预收集所有药水 canonical 实例
        var allPotionList = new List<object>();
        foreach (var potion in allPotions)
        {
            if (potion == null) continue;
            allPotionList.Add(potion);
        }

        foreach (var entry in potionList)
        {
            var isStocked = (bool)(_potionEntryIsStockedProp?.GetValue(entry) ?? false);
            if (!isStocked && !refillsPurchased) continue;

            var candidates = new List<object>();
            foreach (var potion in allPotionList)
            {
                var id = GetModelId(potion, _potionModelIdProp);
                if (string.IsNullOrEmpty(id)) continue;
                if (alreadyChosen.Contains(id)) continue;
                if (IsPotionBannedByBlacklist(id)) continue;
                candidates.Add(potion);
            }

            if (candidates.Count == 0) continue;

            int index = RngNextInt(shopsRng, candidates.Count);
            var chosen = candidates[index % candidates.Count];
            var chosenId = GetModelId(chosen, _potionModelIdProp);
            if (chosenId != null) alreadyChosen.Add(chosenId);

            // 反射设 Model = chosen.ToMutable()
            var mutable = _potionModelToMutableMethod?.Invoke(chosen, null) ?? chosen;
            // Model 是 private set，用反射
            var modelSetter = _potionEntryModelProp.GetSetMethod(true);
            modelSetter?.Invoke(entry, new[] { mutable });
            _potionEntryCalcCostMethod?.Invoke(entry, null);
        }
    }

    private static void RerollCards(object inventory, bool refillsPurchased)
    {
        // CharacterCardEntries
        RerollCardEntries(_characterCardEntriesProp?.GetValue(inventory) as IEnumerable, refillsPurchased);
        // ColorlessCardEntries
        RerollCardEntries(_colorlessCardEntriesProp?.GetValue(inventory) as IEnumerable, refillsPurchased);
    }

    private static void RerollCardEntries(IEnumerable entries, bool refillsPurchased)
    {
        if (entries == null) return;
        foreach (var entry in entries)
        {
            if (entry == null) continue;
            var isStocked = (bool)(_cardEntryIsStockedProp?.GetValue(entry) ?? false);
            if (!isStocked && !refillsPurchased) continue;

            // Populate() 是 public，从卡池重新选卡，不消耗池子
            _cardEntryPopulateMethod?.Invoke(entry, null);
        }
    }

    private static void TriggerUiRefresh(object inventoryNode, object inventory)
    {
        // 1. 遍历 AllEntries，调 OnMerchantInventoryUpdated() 触发 EntryUpdated 事件
        var allEntries = _allEntriesProp?.GetValue(inventory) as IEnumerable;
        if (allEntries != null)
        {
            foreach (var entry in allEntries)
            {
                if (entry == null) continue;
                try { _onMerchantInventoryUpdatedMethod?.Invoke(entry, null); }
                catch { }
            }
        }

        // 2. 遍历 GetAllSlots()，对 IsStocked 但 !Visible 的 slot 强制恢复 + UpdateVisual
        var invType = inventoryNode.GetType();
        var getAllSlotsMethod = invType.GetMethod("GetAllSlots", BindingFlags.Public | BindingFlags.Instance);
        var slots = getAllSlotsMethod?.Invoke(inventoryNode, null) as IEnumerable;
        if (slots == null) return;

        foreach (var slotObj in slots)
        {
            if (slotObj is not Godot.Control slot) continue;
            try
            {
                // 获取 slot 的 Entry
                var entryProp = slot.GetType().GetProperty("Entry");
                var entry = entryProp?.GetValue(slot);
                if (entry == null) continue;

                // 检查 IsStocked
                var isStockedProp = entry.GetType().GetProperty("IsStocked");
                if (isStockedProp == null) continue;
                var isStocked = (bool)isStockedProp.GetValue(entry);

                if (isStocked && !slot.Visible)
                {
                    // 补货的 slot：强制恢复可见性
                    slot.Visible = true;
                    slot.MouseFilter = Godot.Control.MouseFilterEnum.Stop;
                }

                // 调 UpdateVisual 重建视觉节点
                var updateVisual = slot.GetType().GetMethod("UpdateVisual",
                    BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Public);
                updateVisual?.Invoke(slot, null);
            }
            catch { }
        }
    }

    private static void TryApplyBlacklistFilter(object inventory)
    {
        try
        {
            var filterType = FindType("MerchantBlacklist.Core.InventoryFilter");
            if (filterType == null) return;

            var applyMethod = filterType.GetMethod("ApplyToInventory", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
            applyMethod?.Invoke(null, new[] { inventory });
        }
        catch { /* MerchantBlacklist 不在场或调用失败，忽略 */ }
    }

    private static bool IsRelicBannedByBlacklist(string relicId)
    {
        try
        {
            var storeType = FindType("MerchantBlacklist.Core.BlacklistStore");
            if (storeType == null) return false;
            var method = storeType.GetMethod("IsRelicBanned", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
            return (bool)(method?.Invoke(null, new object[] { relicId }) ?? false);
        }
        catch { return false; }
    }

    private static bool IsPotionBannedByBlacklist(string potionId)
    {
        try
        {
            var storeType = FindType("MerchantBlacklist.Core.BlacklistStore");
            if (storeType == null) return false;
            var method = storeType.GetMethod("IsPotionBanned", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
            return (bool)(method?.Invoke(null, new object[] { potionId }) ?? false);
        }
        catch { return false; }
    }

    // ── 工具方法 ─────────────────────────────────────────────────────

    private static object GetShopsRng(object player)
    {
        var rng = _playerRngProp?.GetValue(player);
        return _shopsRngProp?.GetValue(rng);
    }

    private static int RngNextInt(object shopsRng, int max)
    {
        if (shopsRng == null || _rngNextIntMethod == null) return new Random().Next(max);
        return (int)(_rngNextIntMethod.Invoke(shopsRng, new object[] { max }) ?? 0);
    }

    private static string GetModelId(object model, PropertyInfo idProp)
    {
        if (model == null || idProp == null) return null;
        var id = idProp.GetValue(model);
        if (id == null) return null;
        return _modelIdEntryProp?.GetValue(id) as string;
    }

    private static PropertyInfo FindAbstractProp(Type type, string name)
    {
        var t = type;
        while (t != null)
        {
            var p = t.GetProperty(name, BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly);
            if (p != null) return p;
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