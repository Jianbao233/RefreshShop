namespace RefreshShop;

/// <summary>
/// 刷新次数状态（单商店周期，纯本地，不同步）。
/// 进入新商店节点时由 MerchantRoomEnterPatch 重置为 MaxUses。
/// </summary>
internal static class RefreshCounter
{
    private static int _remainingUses = RefreshShopConfig.MaxUses;

    public static int RemainingUses => _remainingUses;

    public static bool IsInfinite => RefreshShopConfig.IsInfiniteUses;

    /// <summary>进入新商店节点时调用，重置为当前配置的最大次数。</summary>
    public static void ResetForNewShop()
    {
        _remainingUses = RefreshShopConfig.MaxUses;
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
        RefreshShopLog.Info($"Refresh consumed, remaining = {(IsInfinite ? "∞" : _remainingUses.ToString())}.");
    }
}