using Godot;
using System;

namespace RefreshShop.UI;

/// <summary>
/// 在商店 UI 初始化完成后，将刷新按钮作为 NMerchantInventory 的直接子节点注入。
/// 按钮通过全局坐标定位到删卡服务右下角，不与删卡服务 hitbox 重合。
/// </summary>
internal static class RefreshShopButtonInjector
{
    private const string ButtonName = "RefreshShopButton";

    public static void TryInject(object nMerchantInventory)
    {
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

            // 避免重复注入（这次在 invControl 下查找，不再是 removalNode 下）
            var existing = invControl.GetNodeOrNull<Control>(ButtonName);
            if (existing != null)
            {
                RefreshShopLog.Info("Refresh button already exists, skipping.");
                return;
            }

            var button = new RefreshShopButton
            {
                Name = ButtonName
            };

            // 关键修复：添加到 NMerchantInventory 而非删卡节点
            invControl.AddChild(button);
            button.Attach(removalNode, invControl);

            RefreshShopLog.Info("RefreshShopButton injected as sibling of removal node.");
        }
        catch (Exception ex)
        {
            RefreshShopLog.Error($"Inject failed: {ex.Message}");
        }
    }
}