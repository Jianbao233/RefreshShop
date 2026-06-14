using Godot;

namespace RefreshShop.UI;

/// <summary>
/// 商店刷新按钮。显示在删卡服务右下角，复用删卡服务的金币图标。
/// 作为 NMerchantInventory 的直接子节点（与删卡服务同级），独立 hitbox。
/// </summary>
public partial class RefreshShopButton : Control
{
    private Control _removalNode;
    private Control _inventoryNode;
    private Sprite2D _coinIcon;
    private Label _priceLabel;

    private const float ButtonSize = 200f;

    public override void _Ready()
    {
        MouseFilter = MouseFilterEnum.Stop;
        SetSize(new Vector2(ButtonSize, ButtonSize));
        Scale = new Vector2(0.5f, 0.5f);

        _coinIcon = new Sprite2D();
        _coinIcon.Position = new Vector2(ButtonSize * 0.3f, ButtonSize * 0.2f);
        AddChild(_coinIcon);

        _priceLabel = new Label();
        _priceLabel.Text = "0";
        _priceLabel.AddThemeColorOverride("font_color", new Color(1f, 0.95f, 0.7f));
        _priceLabel.Position = new Vector2(ButtonSize * 0.15f, ButtonSize * 0.5f);
        AddChild(_priceLabel);

        MouseEntered += () =>
        {
            var tween = CreateTween();
            tween.TweenProperty(this, "scale", new Vector2(0.6f, 0.6f), 0.1f);
        };

        MouseExited += () =>
        {
            var tween = CreateTween();
            tween.TweenProperty(this, "scale", new Vector2(0.5f, 0.5f), 0.1f);
        };

        GuiInput += OnGuiInput;
    }

    public void Attach(Control removalNode, Control inventoryNode)
    {
        _removalNode = removalNode;
        _inventoryNode = inventoryNode;

        var srcVisual = removalNode.GetNodeOrNull<Sprite2D>("%Visual");
        if (srcVisual != null && srcVisual.Texture != null)
        {
            _coinIcon.Texture = srcVisual.Texture;
            _coinIcon.Scale = srcVisual.Scale;
        }

        Reposition();
    }

    public override void _Process(double delta)
    {
        if (_removalNode == null || !GodotObject.IsInstanceValid(_removalNode))
            return;
        Reposition();
    }

    private void Reposition()
    {
        if (_removalNode == null || _inventoryNode == null)
            return;

        var removalGlobalPos = _removalNode.GlobalPosition;
        var removalSize = _removalNode.Size;
        float visible = ButtonSize * Scale.X;

        GlobalPosition = new Vector2(
            removalGlobalPos.X + removalSize.X - visible * 0.9f,
            removalGlobalPos.Y + removalSize.Y - visible * 0.9f
        );
    }

    private void OnGuiInput(InputEvent @event)
    {
        if (@event is InputEventMouseButton mouseBtn
            && mouseBtn.ButtonIndex == MouseButton.Left
            && mouseBtn.Pressed)
        {
            RefreshShopLog.Info("Refresh button clicked.");
            TryRefresh();
        }
    }

    private void TryRefresh()
    {
        if (_inventoryNode == null) return;

        bool ok = ShopRefreshService.RebuildShop(_inventoryNode);
        if (!ok)
            RefreshShopLog.Warn("Refresh failed.");
    }
}