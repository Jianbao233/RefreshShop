using System;
using System.Reflection;

namespace RefreshShop;

/// <summary>
/// 商店重建服务：通过 MerchantInventory.CreateForNormalMerchant 重建整个商店。
/// 不手动修改条目、不碰遗物池回填，完全依赖游戏自带工厂和 bag 自动补充机制。
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

            // 3. 替换 MerchantRoom.Inventory
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

            // 5. 清空 NMerchantInventory 的 Inventory backing field
            var uiInvField = invType.GetField("<Inventory>k__BackingField",
                BindingFlags.NonPublic | BindingFlags.Instance);
            uiInvField?.SetValue(inventoryNode, null);

            // 6. 重新 Initialize
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