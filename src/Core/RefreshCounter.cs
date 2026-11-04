namespace RefreshShop;

/// <summary>
/// 刷新次数状态（单商店周期，纯本地，不同步）。
/// 进入新商店节点时由 MerchantRoomEnterPatch 重置为 MaxUses。
/// </summary>
internal static class RefreshCounter
{
    private sealed class Count { public int Remaining = RefreshShopConfig.MaxUses; }
    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<object, Count> Counts = new();
    private static Count _current = new();
    private static int _remainingUses { get => _current.Remaining; set => _current.Remaining = value; }
    public static event System.Action Changed;
    public static void Bind(object inventory) { _current = Counts.GetOrCreateValue(inventory); }

    public static int RemainingUses => _remainingUses;

    public static bool IsInfinite => RefreshShopConfig.IsInfiniteUses;

    /// <summary>进入新商店节点时调用，重置为当前配置的最大次数。</summary>
    public static void ResetForNewShop()
    {
        _remainingUses = RefreshShopConfig.MaxUses;
        Notify();
        RefreshShopLog.Info($"Refresh counter reset to {_remainingUses} for new shop.");
    }

    /// <summary>无限模式恒 true；有限模式检查剩余次数。</summary>
    public static bool CanRefresh()
    {
        if (IsInfinite) return true;
        return _remainingUses > 0;
    }

    /// <summary>消耗一次。无限模式 no-op。</summary>
    public static void ConsumeOne()
    {
        if (IsInfinite) return;
        if (_remainingUses > 0) _remainingUses--;
        Notify();
        RefreshShopLog.Info($"Refresh consumed, remaining = {(IsInfinite ? "∞" : _remainingUses.ToString())}.");
    }

    private static void Notify()
    {
        if (Changed == null) return;
        foreach (System.Action handler in Changed.GetInvocationList())
        {
            try { handler(); }
            catch (System.Exception ex) { RefreshShopLog.Warn("Counter view update failed: " + ex.Message); }
        }
    }
}
