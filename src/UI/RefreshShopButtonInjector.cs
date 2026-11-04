using Godot;
using System;

namespace RefreshShop.UI;

/// <summary>
/// 将按钮放在删卡服务的同级容器，跟随原生商店的开关动画。
/// </summary>
internal static class RefreshShopButtonInjector
{
    private const string ButtonName = "RefreshShopButton";

    public static void TryInject(object nMerchantInventory)
    {
        RefreshShopButton button = null;
        try
        {
            var invControl = nMerchantInventory as Control;
            if (invControl == null)
            {
                RefreshShopLog.Warn("nMerchantInventory is not a Control, cannot inject.");
                return;
            }

            if (!GodotObject.IsInstanceValid(invControl))
            {
                RefreshShopLog.Warn("NMerchantInventory is no longer valid.");
                return;
            }

            var type = nMerchantInventory.GetType();

            // 读取 _cardRemovalNode
            var field = type.GetField("_cardRemovalNode",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            if (field == null)
            {
                RefreshShopLog.Warn("_cardRemovalNode field not found on NMerchantInventory.");
                return;
            }

            var removalNode = field.GetValue(nMerchantInventory) as Control;
            if (removalNode == null)
            {
                RefreshShopLog.Warn("_cardRemovalNode is null, skipping inject.");
                return;
            }

            if (!GodotObject.IsInstanceValid(removalNode))
            {
                RefreshShopLog.Warn("_cardRemovalNode is no longer valid.");
                return;
            }

            // 按钮可能嵌在动画容器中，递归查找以避免重复注入。
            var existing = Find(invControl);
            if (existing != null)
            {
                RefreshShopLog.Info("Refresh button already exists, skipping.");
                return;
            }

            button = new RefreshShopButton
            {
                Name = ButtonName
            };

            // 不继承删卡槽位的点击行为，但跟随相同的容器动画。
            removalNode.GetParent().AddChild(button);
            button.Attach(removalNode, invControl);

            RefreshShopLog.Info("RefreshShopButton injected as sibling of removal node.");
        }
        catch (Exception ex)
        {
            if (GodotObject.IsInstanceValid(button)) button.QueueFree();
            RefreshShopLog.Error($"Inject failed: {ex.Message}");
        }
    }

    internal static RefreshShopButton Find(Control inventory) => inventory.FindChild(ButtonName, true, false) as RefreshShopButton;
}
