using System;
using System.Reflection;

namespace RefreshShop;

internal static class BlacklistBridge
{
    public static bool IsBanned(string category, object model)
    {
        var store = GameApi.Find("MerchantBlacklist.Core.BlacklistStore");
        if (store == null) return false;
        var name = "Is" + category + "Banned";
        var method = GameApi.FindMethod(store, name, typeof(string));
        if (method == null)
        {
            // Legacy versions only supported relics and potions.
            const BindingFlags flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance;
            if (category == "Card" && store.GetMember(name, MemberTypes.Method, flags).Length == 0)
                return false;
            throw new MissingMethodException(store.FullName, name);
        }
        return (bool)method.Invoke(null, new object[] { GameApi.Id(model) });
    }
}
