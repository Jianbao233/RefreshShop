using System;
using System.Collections.Generic;
using System.Reflection;

namespace RefreshShop;

/// <summary>
/// ModConfig 配置注册 + 运行时静态值。
///
/// 三项配置（全部反射，运行时检测 ModConfig 是否在场）：
///   refresh_max_uses        TextInput  默认 "5"   (0 = 无限)
///   refresh_cost            TextInput  默认 "25"
///   refresh_refills_purchased Toggle    默认 false
///
/// 用 TextInput 而非 Slider：玩家可直接手动输入数值。
/// OnChanged 收到 string，解析成 int。
/// </summary>
internal static class RefreshShopConfig
{
    public const string KeyMaxUses = "refresh_max_uses";
    public const string KeyCost = "refresh_cost";
    public const string KeyRefillsPurchased = "refresh_refills_purchased";

    public const int DefaultMaxUses = 5;
    public const int DefaultCost = 25;
    public const bool DefaultRefillsPurchased = false;

    public static int MaxUses { get; private set; } = DefaultMaxUses;
    public static int Cost { get; private set; } = DefaultCost;
    public static bool RefillsPurchased { get; private set; } = DefaultRefillsPurchased;

    public static bool IsInfiniteUses => MaxUses == 0;

    internal static void SetMaxUsesFromRitsuLib(int value)
    {
        MaxUses = ClampInt(value.ToString(), 0, 999);
        RefreshCounter.ResetForNewShop();
        TrySetModConfigValue(KeyMaxUses, MaxUses.ToString());
    }

    internal static void SetCostFromRitsuLib(int value)
    {
        Cost = ClampInt(value.ToString(), 0, 999);
        TrySetModConfigValue(KeyCost, Cost.ToString());
    }

    internal static void SetRefillsPurchasedFromRitsuLib(bool value)
    {
        RefillsPurchased = value;
        TrySetModConfigValue(KeyRefillsPurchased, value);
    }

    /// <summary>
    /// 从 ModConfig 实时读取所有配置值。OnChanged 回调可能不被触发，
    /// 所以在关键操作前调用此方法确保读到最新值。
    /// </summary>
    public static void RefreshFromModConfig()
    {
        var apiType = FindType("ModConfig.ModConfigApi");
        if (apiType == null) return;

        try
        {
            var getValue = apiType.GetMethod("GetValue", BindingFlags.Public | BindingFlags.Static);
            if (getValue == null) return;

            // MaxUses
            var strGet = getValue.MakeGenericMethod(typeof(string));
            var maxUsesStr = strGet.Invoke(null, new object[] { RefreshShopMod.ModId, KeyMaxUses }) as string;
            if (!string.IsNullOrEmpty(maxUsesStr))
            {
                int parsed = ClampInt(maxUsesStr, 0, 999);
                if (parsed != MaxUses)
                {
                    MaxUses = parsed;
                    RefreshShopLog.Info($"Refreshed MaxUses={MaxUses} from ModConfig.");
                }
            }

            var costStr = strGet.Invoke(null, new object[] { RefreshShopMod.ModId, KeyCost }) as string;
            if (!string.IsNullOrEmpty(costStr))
            {
                int parsed = ClampInt(costStr, 0, 999);
                if (parsed != Cost)
                {
                    Cost = parsed;
                    RefreshShopLog.Info($"Refreshed Cost={Cost} from ModConfig.");
                }
            }

            // RefillsPurchased
            var boolGet = getValue.MakeGenericMethod(typeof(bool));
            var refillVal = boolGet.Invoke(null, new object[] { RefreshShopMod.ModId, KeyRefillsPurchased });
            if (refillVal is bool b && b != RefillsPurchased)
            {
                RefillsPurchased = b;
                RefreshShopLog.Info($"Refreshed RefillsPurchased={RefillsPurchased} from ModConfig.");
            }
        }
        catch (Exception ex)
        {
            RefreshShopLog.Warn($"RefreshFromModConfig failed: {ex.Message}");
        }
    }

    private static bool _initialized;

    public static void Initialize()
    {
        if (_initialized) return;
        _initialized = true;

        try
        {
            TryRegisterModConfig();
        }
        catch (Exception ex)
        {
            RefreshShopLog.Warn($"ModConfig hookup failed: {ex.Message}");
        }
    }

    private static void TryRegisterModConfig()
    {
        var apiType = FindType("ModConfig.ModConfigApi");
        var entryType = FindType("ModConfig.ConfigEntry");
        var typeEnum = FindType("ModConfig.ConfigType");
        if (apiType == null || entryType == null || typeEnum == null)
        {
            RefreshShopLog.Info("ModConfig not detected, skip config registration.");
            return;
        }

        var maxUsesEntry = BuildTextInputEntry(entryType, typeEnum, apiType,
            KeyMaxUses,
            labelEn: "Max Refreshes (0=Infinite)",
            labelZhs: "刷新次数 (0=无限)",
            descEn: "Number of refreshes available per shop visit. 0 = unlimited.",
            descZhs: "每次进入商店的可用刷新次数。0 = 无限次。",
            defaultValue: DefaultMaxUses.ToString(),
            applyExisting: v => MaxUses = v,
            parse: s => ClampInt(s, 0, 999));

        var costEntry = BuildTextInputEntry(entryType, typeEnum, apiType,
            KeyCost,
            labelEn: "Refresh Cost",
            labelZhs: "刷新价格",
            descEn: "Gold cost per refresh. 0 = free.",
            descZhs: "每次刷新消耗的金币。0 = 免费。",
            defaultValue: DefaultCost.ToString(),
            applyExisting: v => Cost = v,
            parse: s => ClampInt(s, 0, 999));

        var refillEntry = BuildToggleEntry(entryType, typeEnum, apiType,
            KeyRefillsPurchased,
            labelEn: "Refill Purchased Slots",
            labelZhs: "刷新时恢复已购槽位",
            descEn: "If enabled, refreshing restocks purchased (empty) slots. If disabled, only un-purchased slots are re-rolled.",
            descZhs: "开启时，刷新会恢复已购买的空槽位。关闭时，只重抽未购买的槽位。",
            defaultValue: DefaultRefillsPurchased);

        var entries = Array.CreateInstance(entryType, 3);
        entries.SetValue(maxUsesEntry, 0);
        entries.SetValue(costEntry, 1);
        entries.SetValue(refillEntry, 2);

        var displayNames = new Dictionary<string, string>
        {
            ["en"] = "Shop Refresh",
            ["zhs"] = "商店刷新",
        };

        var registerLocalized = apiType.GetMethod(
            "Register",
            new[] { typeof(string), typeof(string), typeof(Dictionary<string, string>), entries.GetType() });
        if (registerLocalized != null)
        {
            registerLocalized.Invoke(null, new object[] { RefreshShopMod.ModId, "Shop Refresh", displayNames, entries });
            RefreshShopLog.Info($"Registered config to ModConfig with i18n displayNames (maxUses={MaxUses}, cost={Cost}, refill={RefillsPurchased}).");
            return;
        }

        var register = apiType.GetMethod("Register", new[] { typeof(string), typeof(string), entries.GetType() });
        if (register == null)
        {
            RefreshShopLog.Warn("ModConfigApi.Register not found.");
            return;
        }
        register.Invoke(null, new object[] { RefreshShopMod.ModId, "Shop Refresh", entries });
        RefreshShopLog.Info($"Registered config to ModConfig (maxUses={MaxUses}, cost={Cost}, refill={RefillsPurchased}).");
    }

    private static object BuildTextInputEntry(
        Type entryType, Type typeEnum, Type apiType,
        string key, string labelEn, string labelZhs,
        string descEn, string descZhs,
        string defaultValue, Action<int> applyExisting, Func<string, int> parse)
    {
        var entry = Activator.CreateInstance(entryType);
        SetProp(entry, "Key", key);
        SetProp(entry, "Label", labelEn);
        SetProp(entry, "Labels", new Dictionary<string, string>
        {
            ["en"] = labelEn,
            ["zhs"] = labelZhs,
        });
        SetProp(entry, "Description", descEn);
        SetProp(entry, "Descriptions", new Dictionary<string, string>
        {
            ["en"] = descEn,
            ["zhs"] = descZhs,
        });
        SetProp(entry, "Type", Enum.Parse(typeEnum, "TextInput"));
        SetProp(entry, "DefaultValue", defaultValue);
        SetProp(entry, "MaxLength", 3);

        Action<object> onChanged = v =>
        {
            int parsed = parse(v?.ToString() ?? "");
            if (key == KeyMaxUses) { MaxUses = parsed; RefreshCounter.ResetForNewShop(); }
            else if (key == KeyCost) Cost = parsed;
            RefreshShopLog.Info($"Config {key} changed → {parsed}.");
        };
        SetProp(entry, "OnChanged", onChanged);

        // 注册前读一次已存在的值（用 string 泛型）
        TryReadExistingString(apiType, key, s =>
        {
            int parsed = parse(s);
            applyExisting(parsed);
            RefreshShopLog.Info($"Read existing {key}={parsed} from ModConfig.");
        });

        return entry;
    }

    private static object BuildToggleEntry(
        Type entryType, Type typeEnum, Type apiType,
        string key, string labelEn, string labelZhs,
        string descEn, string descZhs,
        bool defaultValue)
    {
        var entry = Activator.CreateInstance(entryType);
        SetProp(entry, "Key", key);
        SetProp(entry, "Label", labelEn);
        SetProp(entry, "Labels", new Dictionary<string, string>
        {
            ["en"] = labelEn,
            ["zhs"] = labelZhs,
        });
        SetProp(entry, "Description", descEn);
        SetProp(entry, "Descriptions", new Dictionary<string, string>
        {
            ["en"] = descEn,
            ["zhs"] = descZhs,
        });
        SetProp(entry, "Type", Enum.Parse(typeEnum, "Toggle"));
        SetProp(entry, "DefaultValue", defaultValue);

        Action<object> onChanged = v =>
        {
            RefillsPurchased = Convert.ToBoolean(v);
            RefreshShopLog.Info($"Config {key} changed → {RefillsPurchased}.");
        };
        SetProp(entry, "OnChanged", onChanged);

        TryReadExistingBool(apiType, key, b =>
        {
            RefillsPurchased = b;
            RefreshShopLog.Info($"Read existing {key}={b} from ModConfig.");
        });

        return entry;
    }

    /// <summary>
    /// 用 GetValue<string> 精确泛型读值。避免 object 装箱导致类型错误。
    /// </summary>
    private static void TryReadExistingString(Type apiType, string key, Action<string> apply)
    {
        try
        {
            var getValue = apiType.GetMethod("GetValue", BindingFlags.Public | BindingFlags.Static);
            if (getValue == null) return;
            var generic = getValue.MakeGenericMethod(typeof(string));
            var v = generic.Invoke(null, new object[] { RefreshShopMod.ModId, key }) as string;
            if (!string.IsNullOrEmpty(v)) apply(v);
        }
        catch { /* 第一次注册时还没值 */ }
    }

    private static void TryReadExistingBool(Type apiType, string key, Action<bool> apply)
    {
        try
        {
            var getValue = apiType.GetMethod("GetValue", BindingFlags.Public | BindingFlags.Static);
            if (getValue == null) return;
            var generic = getValue.MakeGenericMethod(typeof(bool));
            var v = generic.Invoke(null, new object[] { RefreshShopMod.ModId, key });
            if (v is bool b) apply(b);
        }
        catch { }
    }

    private static int ClampInt(string s, int min, int max)
    {
        if (int.TryParse(s?.Trim(), out int v))
            return Math.Clamp(v, min, max);
        return min;
    }

    private static void SetProp(object target, string name, object value)
    {
        if (target == null) return;
        var p = target.GetType().GetProperty(name);
        if (p != null && p.CanWrite)
        {
            try { p.SetValue(target, value); } catch { }
        }
    }

    private static void TrySetModConfigValue(string key, object value)
    {
        try
        {
            var apiType = FindType("ModConfig.ModConfigApi");
            var setValue = apiType?.GetMethod("SetValue", BindingFlags.Public | BindingFlags.Static);
            setValue?.Invoke(null, new object[] { RefreshShopMod.ModId, key, value });
        }
        catch (Exception ex)
        {
            RefreshShopLog.Warn($"ModConfig sync failed [{key}]: {ex.Message}");
        }
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
