using System;
using System.Reflection;

namespace RefreshShop;

internal static class BlacklistBridge
{
    public static bool IsBanned(string category, object model)
    {
        var store = GameApi.Find("MerchantBlacklist.Core.BlacklistStore");
        if (store == null) return false;
        // An installed but incompatible blacklist must fail the plan, not leak banned items.
        var method = GameApi.Method(store, "Is" + category + "Banned", typeof(string));
        return (bool)method.Invoke(null, new object[] { GameApi.Id(model) });
    }
}
