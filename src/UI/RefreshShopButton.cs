using Godot;
using System;
using System.Linq;
using System.Reflection;

namespace RefreshShop.UI;

// Native pause-menu button supplies mouse/controller input, sounds, focus and tweens.
// This wrapper owns only placement, merchant state and the refresh command.
public partial class RefreshShopButton : Control
{
    internal Control NativeButton { get; private set; }
    private Control _removal, _inventoryNode;
    private object _inventory, _player;
    private Label _label;
    private EventInfo _goldEvent;
    private Action _goldChanged;
    private bool _running;
    private bool _refreshFailed;

    public override void _Ready()
    {
        MouseFilter = MouseFilterEnum.Ignore;
        Size = new Vector2(230, 54);
        NativeButton = GD.Load<PackedScene>("res://scenes/pause_menu/pause_menu_button.tscn").Instantiate<Control>();
        NativeButton.Name = "Action";
        NativeButton.FocusMode = FocusModeEnum.All;
        NativeButton.Scale = new Vector2(.62f, .62f);
        AddChild(NativeButton);
        _label = NativeButton.GetNode<Label>("Label");
        foreach (var child in NativeButton.GetChildren().OfType<Control>()) child.MouseFilter = MouseFilterEnum.Ignore;
        NativeButton.Connect("Released", Callable.From<GodotObject>(_ => OnReleased()));
        RefreshCounter.Changed += UpdateDisplay;
        RefreshShopConfig.Changed += UpdateDisplay;
    }

    public void Attach(Control removalNode, Control inventoryNode)
    {
        _removal = removalNode;
        _inventoryNode = inventoryNode;
        _inventory = GameApi.Inventory(inventoryNode);
        _player = GameApi.Player(_inventory);
        if (!GameApi.IsLocal(_player)) throw new InvalidOperationException("Non-local shop UI.");
        RefreshCounter.Bind(_inventory);
        _goldEvent = _player.GetType().GetEvent("GoldChanged");
        _goldChanged = UpdateDisplay;
        _goldEvent?.AddEventHandler(_player, _goldChanged);
        _removal.ItemRectChanged += Reposition;
        _inventoryNode.Resized += Reposition;
        _inventoryNode.VisibilityChanged += UpdateDisplay;
        Reposition();
        UpdateDisplay();
        UpdateNavigation();
    }

    private void Reposition()
    {
        if (_removal == null || !GodotObject.IsInstanceValid(_removal)) return;
        Position = GetParent<Control>().GetGlobalTransform().AffineInverse() * _removal.GlobalPosition
            + new Vector2((_removal.Size.X - Size.X) / 2, _removal.Size.Y + 12);
    }

    internal void UpdateDisplay()
    {
        if (_inventory == null || !IsInsideTree()) return;
        RefreshCounter.Bind(_inventory);
        var uses = RefreshCounter.IsInfinite ? "∞" : $"{RefreshCounter.RemainingUses}/{RefreshShopConfig.MaxUses}";
        var action = _refreshFailed ? (Chinese ? "刷新失败" : "Refresh failed") : (Chinese ? "刷新" : "Refresh");
        _label.Text = $"{action} {uses} · {RefreshShopConfig.Cost} {(Chinese ? "金" : "G")}";
        var blocked = (bool)GameApi.Read(_inventoryNode, "_isInputBlocked");
        var enabled = !_running && !blocked && RefreshCounter.CanRefresh()
            && ShopRefreshService.IsAvailable(_inventory) && GameApi.Gold(_player) >= RefreshShopConfig.Cost;
        GameApi.Call(NativeButton, "SetEnabled", enabled);
        NativeButton.TooltipText = !ShopRefreshService.IsAvailable(_inventory)
            ? (Chinese ? "金币同步失败，请重新连接。" : "Gold sync failed. Please reconnect.")
            : !RefreshCounter.CanRefresh() ? (Chinese ? "本商店的刷新次数已用完。" : "No refreshes left in this shop.")
            : GameApi.Gold(_player) < RefreshShopConfig.Cost ? (Chinese ? "金币不足。" : "Not enough gold.")
            : _refreshFailed ? (Chinese ? "刷新未完成，详情见游戏日志。" : "Refresh did not complete. See the game log.")
            : (Chinese ? "刷新自己的商店商品；删卡服务保持不变。" : "Refresh your shop items. Card removal is unchanged.");
    }

    internal void UpdateNavigation()
    {
        if (_inventory == null || !IsInsideTree()) return;
        var slots = GameApi.Items(GameApi.Call(_inventoryNode, "GetAllSlots")).OfType<Control>()
            .Where(c => c.Visible && c.FocusMode == FocusModeEnum.All).ToArray();
        if (slots.Length == 0) return;
        var nearest = slots.MinBy(c => c.GlobalPosition.DistanceSquaredTo(GlobalPosition));
        nearest.FocusNeighborBottom = NativeButton.GetPath();
        nearest.FocusNext = NativeButton.GetPath();
        NativeButton.FocusNeighborTop = nearest.GetPath();
        NativeButton.FocusNeighborLeft = nearest.GetPath();
        NativeButton.FocusNeighborRight = nearest.GetPath();
        NativeButton.FocusNeighborBottom = NativeButton.GetPath();
        NativeButton.FocusNext = nearest.GetPath();
        NativeButton.FocusPrevious = nearest.GetPath();
    }

    private async void OnReleased()
    {
        if (_running || _inventory == null || !(bool)GameApi.Get(_inventoryNode, "IsOpen")
            || (bool)GameApi.Read(_inventoryNode, "_isInputBlocked")) return;
        _running = true;
        _refreshFailed = false;
        try
        {
            GameApi.Call(_inventoryNode, "BlockInput");
            UpdateDisplay();
            _refreshFailed = !await ShopRefreshService.RefreshAsync(_inventory);
            if (!_refreshFailed && GodotObject.IsInstanceValid(_inventoryNode)) RefreshVisuals(_inventoryNode);
        }
        catch (Exception ex)
        {
            _refreshFailed = true;
            RefreshShopLog.Error("Refresh UI: " + ex.GetBaseException().Message);
        }
        finally
        {
            _running = false;
            if (GodotObject.IsInstanceValid(_inventoryNode)) GameApi.Call(_inventoryNode, "UnblockInput");
            if (GodotObject.IsInstanceValid(this)) { UpdateDisplay(); UpdateNavigation(); }
        }
    }

    internal static void RefreshVisuals(Control node)
    {
        foreach (var entry in GameApi.Items(GameApi.Get(GameApi.Inventory(node), "AllEntries")))
            GameApi.Call(entry, "OnMerchantInventoryUpdated");
        foreach (var slot in GameApi.Items(GameApi.Call(node, "GetAllSlots")).OfType<Control>())
        {
            var entry = GameApi.Get(slot, "Entry");
            if (entry != null && (bool)GameApi.Get(entry, "IsStocked"))
            { slot.Show(); slot.MouseFilter = MouseFilterEnum.Stop; slot.FocusMode = FocusModeEnum.All; }
        }
        GameApi.Call(node, "UpdateNavigation");
    }

    private static bool Chinese
    {
        get
        {
            try { return GameApi.Get(GameApi.Get(GameApi.Type("Localization.LocManager"), "Instance"), "Language").ToString().StartsWith("zh"); }
            catch { return System.Globalization.CultureInfo.CurrentUICulture.Name.StartsWith("zh"); }
        }
    }

    public override void _ExitTree()
    {
        RefreshCounter.Changed -= UpdateDisplay;
        RefreshShopConfig.Changed -= UpdateDisplay;
        if (_goldChanged != null) _goldEvent?.RemoveEventHandler(_player, _goldChanged);
        if (GodotObject.IsInstanceValid(_removal)) _removal.ItemRectChanged -= Reposition;
        if (GodotObject.IsInstanceValid(_inventoryNode))
        { _inventoryNode.Resized -= Reposition; _inventoryNode.VisibilityChanged -= UpdateDisplay; }
    }
}
