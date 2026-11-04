using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.ControllerInput;
using MegaCrit.Sts2.Core.Context;
using MegaCrit.Sts2.Core.Entities.Merchant;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Models.Characters;
using MegaCrit.Sts2.Core.Nodes.GodotExtensions;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.Unlocks;
using RefreshShop;
using RefreshShop.UI;

public partial class Runner
{
    private async Task NativeUi()
    {
        // Match the actual fallback ModManager loader. Deferred init must not call PatchAll again.
        var harmony = new Harmony("@Bilibili我叫煎包.RefreshShop");
        harmony.PatchAll(typeof(RefreshShopMod).Assembly);
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        var init = GameApi.Method(GameApi.Type("Nodes.Screens.Shops.NMerchantInventory"), "Initialize",
            GameApi.Type("Entities.Merchant.MerchantInventory"), GameApi.Type("Entities.Merchant.MerchantDialogueSet"));
        Check(Harmony.GetPatchInfo(init).Postfixes.Count(p => p.PatchMethod.DeclaringType.Assembly == typeof(RefreshShopMod).Assembly) == 1, "one bootstrap postfix");
        var slotInit = GameApi.Type("Nodes.Screens.Shops.NMerchantSlot").GetMethod("Initialize");
        Check(Harmony.GetPatchInfo(slotInit)?.Postfixes.Any(p => p.PatchMethod.DeclaringType.Assembly == typeof(RefreshShopMod).Assembly) != true, "obsolete slot guard removed");
        _passed.Add("native loader installs exactly one patch set");

        var p = Player.CreateForNewRun<Ironclad>(UnlockState.all, 11);
        var run = RunState.CreateForTest(new[] { p }, seed: "UITEST");
        LocalContext.NetId = 11;
        GameApi.Set(RunManager.Instance, "State", run);
        var net = new TestNet { NetId = 11, Type = MegaCrit.Sts2.Core.Multiplayer.Game.NetGameType.Singleplayer };
        var buffer = new MegaCrit.Sts2.Core.Multiplayer.Game.RunLocationTargetedMessageBuffer(net);
        using var sync = new MegaCrit.Sts2.Core.Multiplayer.Game.RewardSynchronizer(buffer, net, run, 11);
        GameApi.Set(RunManager.Instance, "NetService", net);
        GameApi.Set(RunManager.Instance, "RewardSynchronizer", sync);
        p.Gold = 500;
        RefreshShopConfig.SetMaxUsesFromRitsuLib(5);
        RefreshShopConfig.SetCostFromRitsuLib(0);
        var inv = MerchantInventory.CreateForNormalMerchant(p);
        var viewport = new SubViewport { Size = new Vector2I(1920, 1080) };
        AddChild(viewport);
        var shop = new TestShop { Inventory = inv, Size = new Vector2(1920,1080) };
        viewport.AddChild(shop);
        var slots = new Control { Position = new Vector2(300, 200), Size = new Vector2(800, 600) };
        shop.AddChild(slots);
        var removal = new TestSlot { Entry = inv.CardRemovalEntry, Position = new Vector2(400, 300), Size = new Vector2(240, 220), FocusMode = Control.FocusModeEnum.All };
        slots.AddChild(removal); shop.Slot = removal;
        int Subscribers() => (GameApi.Field(typeof(Player), "GoldChanged").GetValue(p) as Delegate)?.GetInvocationList().Length ?? 0;
        var subscriptions = Subscribers();
        for (int i = 0; i < 3; i++)
        {
            var wrapper = new RefreshShopButton { Name = "RefreshShopButton" };
            slots.AddChild(wrapper); wrapper.Attach(removal, shop);
            var button = (NButton)wrapper.NativeButton;
            Check(button.GetType().Name == "NPauseMenuButton", "real native scene/script");
            Check(button.FocusMode == Control.FocusModeEnum.All, "native controller focus enabled");
            Check(removal.FocusNeighborBottom == button.GetPath() && button.FocusNeighborTop == removal.GetPath(), "bidirectional focus navigation");
            Check(Subscribers() == subscriptions + 1, "one gold event subscription");
            var position = wrapper.GlobalPosition; slots.Position += new Vector2(0, 40);
            Check(wrapper.GlobalPosition == position + new Vector2(0, 40), "button follows native shop animation parent");
            RefreshCounter.Bind(inv); var count = RefreshCounter.RemainingUses;
            button._GuiInput(new InputEventAction { Action = MegaInput.select, Pressed = true });
            button._GuiInput(new InputEventAction { Action = MegaInput.select, Pressed = false });
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            Check(RefreshCounter.RemainingUses == count - 1, "native select press/release performs one refresh");
            Check(net.Sent.Count == 0, "free refresh sends no packets");
            if (i == 0)
            {
                var mouse = button.GetGlobalRect().GetCenter();
                viewport.PushInput(new InputEventMouseMotion { Position = mouse, GlobalPosition = mouse }, true);
                await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
                Check(viewport.GuiGetHoveredControl() == button, $"mouse hits native refresh button (rect={button.GetGlobalRect()}, hit={viewport.GuiGetHoveredControl()?.Name})");
                void Click()
                {
                    foreach (var pressed in new[] { true, false })
                        viewport.PushInput(new InputEventMouseButton { ButtonIndex = MouseButton.Left,
                            Pressed = pressed, Position = mouse, GlobalPosition = mouse }, true);
                }
                Click();
                await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
                Check(RefreshCounter.RemainingUses == count - 2, "mouse click performs one refresh");

                MerchantBlacklist.Core.BlacklistStore.BlockPotions = true;
                try
                {
                    Click();
                    await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
                    Check(button.GetNode<Label>("Label").Text.StartsWith("Refresh failed"), "failed mouse refresh gives visible feedback");
                    Check(RefreshCounter.RemainingUses == count - 2 && !shop._isInputBlocked, "failed click preserves uses and unlocks shop input");
                }
                finally { MerchantBlacklist.Core.BlacklistStore.BlockPotions = false; }
            }
            slots.RemoveChild(wrapper); wrapper.Free();
            Check(Subscribers() == subscriptions, "gold handler removed on close");
        }
        viewport.QueueFree();
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        harmony.UnpatchAll(harmony.Id);
        _passed.Add("native button: controller action, focus, animated parent, free refresh, three open/close cycles");
    }
}

public partial class TestShop : Control
{
    public MerchantInventory Inventory { get; set; }
    public Control Slot;
    public bool IsOpen => true;
    public bool _isInputBlocked;
    public IEnumerable<Control> GetAllSlots() => new[] { Slot };
    public void BlockInput() => _isInputBlocked = true;
    public void UnblockInput() => _isInputBlocked = false;
    public void UpdateNavigation() { }
}

public partial class TestSlot : Control { public MerchantEntry Entry { get; set; } }
