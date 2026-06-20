using Godot;
using System;
using System.Reflection;
using System.Threading.Tasks;

namespace RefreshShop.UI;

/// <summary>
/// 商店刷新按钮。复用删卡服务的原生图标样式（Sprite2D + Cost 容器结构），
/// 作为 NMerchantInventory 的直接子节点（与删卡服务同级），独立 hitbox。
///
/// v0.2.0：显示剩余次数/上限 + 价格；点击时实时从 ModConfig 读配置、检查次数/金币、
/// 扣金币（联机同步）、刷新、消耗次数；次数耗尽或金币不足时变灰 + 播 bad sound。
/// </summary>
public partial class RefreshShopButton : Control
{
    private Control _removalNode;
    private Control _inventoryNode;
    private Sprite2D _coinIcon;
    private TextureRect _smallCoinIcon;
    private Label _priceLabel;
    private Label _usesLabel;

    private const float ButtonSize = 200f;
    // 删卡服务场景里的金币贴图路径
    private const string GoldTexturePath = "res://images/atlases/ui_atlas.sprites/top_bar/top_bar_gold.tres";

    public override void _Ready()
    {
        MouseFilter = MouseFilterEnum.Stop;
        SetSize(new Vector2(ButtonSize, ButtonSize));
        Scale = new Vector2(0.5f, 0.5f);

        // 大金币图标（复用删卡服务的 Sprite2D 贴图），水平居中
        _coinIcon = new Sprite2D();
        _coinIcon.Position = new Vector2(ButtonSize * 0.5f, ButtonSize * 0.2f);
        AddChild(_coinIcon);

        // 价格容器：HBoxContainer（小金币 TextureRect + 价格 Label）
        // 放在大金币图标正下方，水平居中
        var costContainer = new HBoxContainer();
        costContainer.Alignment = BoxContainer.AlignmentMode.Center;
        costContainer.SetSize(new Vector2(ButtonSize, 60));
        costContainer.Position = new Vector2(0, ButtonSize * 0.42f);
        costContainer.AddThemeConstantOverride("separation", 6);
        AddChild(costContainer);

        // 小金币图标（从游戏资源加载）
        _smallCoinIcon = new TextureRect();
        _smallCoinIcon.CustomMinimumSize = new Vector2(54, 54);
        _smallCoinIcon.ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize;
        _smallCoinIcon.StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered;
        var goldTex = GD.Load<Texture2D>(GoldTexturePath);
        if (goldTex != null)
            _smallCoinIcon.Texture = goldTex;
        costContainer.AddChild(_smallCoinIcon);

        // 价格数字（字号 39，跟删卡服务一致）
        _priceLabel = new Label();
        _priceLabel.AddThemeColorOverride("font_color", new Color(1f, 0.85f, 0.4f));
        _priceLabel.AddThemeColorOverride("font_outline_color", new Color(0f, 0f, 0f, 0.8f));
        _priceLabel.AddThemeConstantOverride("outline_size", 4);
        _priceLabel.AddThemeFontSizeOverride("font_size", 39);
        _priceLabel.VerticalAlignment = VerticalAlignment.Center;
        costContainer.AddChild(_priceLabel);

        // 次数 Label：显示在价格容器下方，水平居中（字号 39，跟删卡服务一致）
        _usesLabel = new Label();
        _usesLabel.AddThemeColorOverride("font_color", new Color(0.85f, 0.85f, 1f));
        _usesLabel.AddThemeColorOverride("font_outline_color", new Color(0f, 0f, 0f, 0.8f));
        _usesLabel.AddThemeConstantOverride("outline_size", 4);
        _usesLabel.AddThemeFontSizeOverride("font_size", 39);
        _usesLabel.HorizontalAlignment = HorizontalAlignment.Center;
        _usesLabel.VerticalAlignment = VerticalAlignment.Center;
        _usesLabel.SetSize(new Vector2(ButtonSize, 50));
        _usesLabel.Position = new Vector2(0, ButtonSize * 0.72f);
        AddChild(_usesLabel);

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

        // 复用删卡服务的 Sprite2D 贴图和缩放
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
        UpdateDisplay();
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

    private void UpdateDisplay()
    {
        // 次数文本
        if (RefreshCounter.IsInfinite)
            _usesLabel.Text = "∞";
        else
            _usesLabel.Text = $"{RefreshCounter.RemainingUses}/{RefreshShopConfig.MaxUses}";

        // 价格文本 + 小金币图标可见性
        int cost = RefreshShopConfig.Cost;
        if (cost > 0)
        {
            _priceLabel.Text = cost.ToString();
            _priceLabel.Visible = true;
            _smallCoinIcon.Visible = true;
        }
        else
        {
            _priceLabel.Text = "";
            _priceLabel.Visible = false;
            _smallCoinIcon.Visible = false;
        }

        // 变灰 + 鼠标拦截
        bool canInteract = RefreshCounter.CanRefresh();
        if (canInteract)
        {
            Modulate = new Color(1f, 1f, 1f, 1f);
            MouseFilter = MouseFilterEnum.Stop;
        }
        else
        {
            Modulate = new Color(0.5f, 0.5f, 0.5f, 1f);
            MouseFilter = MouseFilterEnum.Ignore;
        }
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

        // 0. 实时从 ModConfig 读取最新配置（OnChanged 可能不被触发）
        RefreshShopConfig.RefreshFromModConfig();

        // 1. 次数检查
        if (!RefreshCounter.CanRefresh())
        {
            RefreshShopLog.Info("No refreshes remaining.");
            PlayBadSound();
            return;
        }

        // 2. 金币检查
        int cost = RefreshShopConfig.Cost;
        object player = TryGetPlayer();
        if (cost > 0 && player != null)
        {
            int gold = TryGetPlayerGold(player);
            if (gold < cost)
            {
                RefreshShopLog.Info($"Not enough gold ({gold} < {cost}).");
                PlayBadSound();
                return;
            }
        }

        // 3. 刷新
        bool refill = RefreshShopConfig.RefillsPurchased;
        RefreshShopLog.Info($"RebuildShop with refill={refill}.");
        bool ok = ShopRefreshService.RebuildShop(_inventoryNode, refill);
        if (!ok)
        {
            RefreshShopLog.Warn("Refresh failed.");
            PlayBadSound();
            return;
        }

        // 4. 扣金币 + 联机同步
        if (cost > 0 && player != null)
        {
            SpendGold(player, cost);
        }

        // 5. 消耗次数
        RefreshCounter.ConsumeOne();

        RefreshShopLog.Info(
            $"Refresh OK. Remaining = {(RefreshCounter.IsInfinite ? "∞" : RefreshCounter.RemainingUses.ToString())}, " +
            $"cost = {cost}, refill = {refill}.");
    }

    private object TryGetPlayer()
    {
        try
        {
            var invType = _inventoryNode.GetType();
            var invProp = invType.GetProperty("Inventory");
            var inventory = invProp?.GetValue(_inventoryNode);
            if (inventory == null) return null;
            var playerProp = inventory.GetType().GetProperty("Player");
            return playerProp?.GetValue(inventory);
        }
        catch (Exception ex)
        {
            RefreshShopLog.Warn($"TryGetPlayer failed: {ex.Message}");
            return null;
        }
    }

    private static int TryGetPlayerGold(object player)
    {
        try
        {
            var goldProp = player.GetType().GetProperty("Gold");
            if (goldProp == null) return 0;
            return (int)goldProp.GetValue(player);
        }
        catch
        {
            return 0;
        }
    }

    /// <summary>
    /// 反射调 PlayerCmd.LoseGold(cost, player, GoldLossType.Spent) + RewardSynchronizer.SyncLocalGoldLost(cost)。
    /// </summary>
    private static void SpendGold(object player, int cost)
    {
        try
        {
            var playerCmdType = FindType("MegaCrit.Sts2.Core.Commands.PlayerCmd");
            if (playerCmdType == null)
            {
                RefreshShopLog.Warn("PlayerCmd type not found, cannot spend gold.");
                return;
            }

            var goldLossType = FindType("MegaCrit.Sts2.Core.Entities.Gold.GoldLossType");
            object spentValue = goldLossType != null
                ? Enum.Parse(goldLossType, "Spent")
                : 0;

            var loseGoldMethod = playerCmdType.GetMethod("LoseGold",
                BindingFlags.Public | BindingFlags.Static,
                null,
                new[] { typeof(decimal), player.GetType(), goldLossType ?? typeof(int) },
                null);

            if (loseGoldMethod != null)
            {
                var task = loseGoldMethod.Invoke(null, new object[] { (decimal)cost, player, spentValue }) as Task;
                _ = task?.ContinueWith(t =>
                {
                    if (t.Exception != null)
                        RefreshShopLog.Warn($"LoseGold faulted: {t.Exception.InnerException?.Message ?? t.Exception.Message}");
                });
            }
            else
            {
                RefreshShopLog.Warn("PlayerCmd.LoseGold not found, falling back to direct Gold set.");
                var goldProp = player.GetType().GetProperty("Gold");
                if (goldProp != null)
                {
                    int cur = (int)goldProp.GetValue(player);
                    goldProp.SetValue(player, System.Math.Max(0, cur - cost));
                }
            }

            // 联机同步
            var runManagerType = FindType("MegaCrit.Sts2.Core.Runs.RunManager");
            var instanceProp = runManagerType?.GetProperty("Instance",
                BindingFlags.Public | BindingFlags.Static);
            var runManager = instanceProp?.GetValue(null);
            if (runManager != null)
            {
                var rewardSyncProp = runManager.GetType().GetProperty("RewardSynchronizer");
                var rewardSync = rewardSyncProp?.GetValue(runManager);
                if (rewardSync != null)
                {
                    var syncMethod = rewardSync.GetType().GetMethod("SyncLocalGoldLost",
                        BindingFlags.Public | BindingFlags.Instance);
                    syncMethod?.Invoke(rewardSync, new object[] { cost });
                }
            }
        }
        catch (Exception ex)
        {
            RefreshShopLog.Warn($"SpendGold failed: {ex.Message}");
        }
    }

    private static void PlayBadSound()
    {
        try
        {
            var sfxCmdType = FindType("MegaCrit.Sts2.Core.Commands.SfxCmd");
            if (sfxCmdType == null) return;
            var playMethod = sfxCmdType.GetMethod("Play",
                BindingFlags.Public | BindingFlags.Static,
                null,
                new[] { typeof(string), typeof(float) },
                null);
            playMethod?.Invoke(null, new object[] { "event:/sfx/ui/cancel", 1f });
        }
        catch { }
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