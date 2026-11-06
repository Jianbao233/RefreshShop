using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace RefreshShop;

internal static partial class RefreshShopConfig
{
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

        var maxUsesEntry = BuildTextInputEntry(entryType, typeEnum,
            KeyMaxUses,
            labelEn: "Max Refreshes (0=Infinite)",
            labelZhs: "刷新次数 (0=无限)",
            descEn: "Number of refreshes available per shop visit. 0 = unlimited.",
            descZhs: "每次进入商店的可用刷新次数。0 = 无限次。",
            defaultValue: DefaultMaxUses.ToString());

        var costEntry = BuildTextInputEntry(entryType, typeEnum,
            KeyCost,
            labelEn: "Refresh Cost",
            labelZhs: "刷新价格",
            descEn: "Gold cost per refresh. 0 = free.",
            descZhs: "每次刷新消耗的金币。0 = 免费。",
            defaultValue: DefaultCost.ToString());

        var refillEntry = BuildToggleEntry(entryType, typeEnum,
            KeyRefillsPurchased,
            labelEn: "Refill Purchased Slots",
            labelZhs: "刷新时恢复已购槽位",
            descEn: "If enabled, refreshing restocks purchased (empty) slots. If disabled, only un-purchased slots are re-rolled.",
            descZhs: "开启时，刷新会恢复已购买的空槽位。关闭时，只重抽未购买的槽位。",
            defaultValue: DefaultRefillsPurchased);

        var otherRelicsEntry = BuildToggleEntry(entryType, typeEnum,
            KeyOtherCharacterRelics,
            labelEn: "Allow Other Characters' Relics",
            labelZhs: "允许刷出其他角色遗物",
            descEn: "Include unlocked relics from other characters when refreshing. Disabled by default.",
            descZhs: "刷新时加入其他角色已解锁的遗物。默认关闭。",
            defaultValue: DefaultOtherCharacterRelics);
        var entries = Array.CreateInstance(entryType, 4);
        entries.SetValue(maxUsesEntry, 0);
        entries.SetValue(costEntry, 1);
        entries.SetValue(refillEntry, 2);
        entries.SetValue(otherRelicsEntry, 3);

        var displayNames = new Dictionary<string, string>
        {
            ["en"] = "Shop Refresh",
            ["zhs"] = "商店刷新",
        };

        var registerLocalized = apiType.GetMethod(
            "Register",
            new[] { typeof(string), typeof(string), typeof(Dictionary<string, string>), entries.GetType() });
        var register = registerLocalized ?? apiType.GetMethod(
            "Register", new[] { typeof(string), typeof(string), entries.GetType() });
        if (register == null)
        {
            RefreshShopLog.Warn("ModConfigApi.Register not found.");
            return;
        }
        _registeringModConfig = true;
        try
        {
            register.Invoke(null, registerLocalized != null
                ? new object[] { RefreshShopMod.ModId, "Shop Refresh", displayNames, entries }
                : new object[] { RefreshShopMod.ModId, "Shop Refresh", entries });
            _modConfigReady = true;
        }
        finally
        {
            _registeringModConfig = false;
        }
        RefreshFromModConfig();
        // 旧设置页也显示 JSON 中的当前值，用户随后在任一设置页修改都即时保存。
        if (_hasMaxUses) TrySetModConfigValue(KeyMaxUses, MaxUses.ToString());
        if (_hasCost) TrySetModConfigValue(KeyCost, Cost.ToString());
        if (_hasRefillsPurchased) TrySetModConfigValue(KeyRefillsPurchased, RefillsPurchased);
        if (_hasOtherCharacterRelics) TrySetModConfigValue(KeyOtherCharacterRelics, OtherCharacterRelics);
        RefreshShopLog.Info($"Registered config (maxUses={MaxUses}, cost={Cost}, refill={RefillsPurchased}).");
    }

    private static object BuildTextInputEntry(
        Type entryType, Type typeEnum,
        string key, string labelEn, string labelZhs,
        string descEn, string descZhs,
        string defaultValue)
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

        SetProp(entry, "OnChanged", new Action<object>(value => SetValue(key, value, sync: false)));

        return entry;
    }

    private static object BuildToggleEntry(
        Type entryType, Type typeEnum,
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

        SetProp(entry, "OnChanged", new Action<object>(value => SetValue(key, value, sync: false)));

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
            _syncingModConfig = true;
            try
            {
                setValue?.Invoke(null, new object[] { RefreshShopMod.ModId, key, value });
            }
            finally
            {
                _syncingModConfig = false;
            }
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
