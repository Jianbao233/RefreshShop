using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;

namespace RefreshShop;

internal static class RitsuLibSettingsProvider
{
    private static readonly SettingPageDefinition[] Pages =
    {
        new(
            RefreshShopMod.ModId,
            Texts.SettingsPageTitle,
            Texts.SettingsPageDescription,
            new[]
            {
                new SettingSectionDefinition(
                    "refresh",
                    Texts.RefreshSectionTitle,
                    new[]
                    {
                        new SettingEntryDefinition(
                            RefreshShopConfig.KeyMaxUses,
                            "int-slider",
                            RefreshShopConfig.KeyMaxUses,
                            Texts.MaxUsesLabel,
                            Texts.MaxUsesDescription,
                            DefaultValue: RefreshShopConfig.DefaultMaxUses,
                            Min: 0,
                            Max: 999,
                            Step: 1),
                        new SettingEntryDefinition(
                            RefreshShopConfig.KeyCost,
                            "int-slider",
                            RefreshShopConfig.KeyCost,
                            Texts.CostLabel,
                            Texts.CostDescription,
                            DefaultValue: RefreshShopConfig.DefaultCost,
                            Min: 0,
                            Max: 999,
                            Step: 1),
                        new SettingEntryDefinition(
                            RefreshShopConfig.KeyRefillsPurchased,
                            "toggle",
                            RefreshShopConfig.KeyRefillsPurchased,
                            Texts.RefillsPurchasedLabel,
                            Texts.RefillsPurchasedDescription,
                            DefaultValue: RefreshShopConfig.DefaultRefillsPurchased)
                    })
            })
    };

    public static object CreateRitsuLibSettingsSchema()
    {
        return new
        {
            modId = RefreshShopMod.ModId,
            modDisplayName = Texts.ModDisplayName.ToSchema(),
            pages = Pages.Select(page => page.ToSchema()).ToArray()
        };
    }

    public static object GetRitsuLibSettingValue(string key)
    {
        return key switch
        {
            RefreshShopConfig.KeyMaxUses => RefreshShopConfig.MaxUses,
            RefreshShopConfig.KeyCost => RefreshShopConfig.Cost,
            RefreshShopConfig.KeyRefillsPurchased => RefreshShopConfig.RefillsPurchased,
            _ => null
        };
    }

    public static void SetRitsuLibSettingValue(string key, object value)
    {
        switch (key)
        {
            case RefreshShopConfig.KeyMaxUses:
                SetRitsuLibSettingInt(key, Convert.ToInt32(value));
                break;
            case RefreshShopConfig.KeyCost:
                SetRitsuLibSettingInt(key, Convert.ToInt32(value));
                break;
            case RefreshShopConfig.KeyRefillsPurchased:
                SetRitsuLibSettingBool(key, Convert.ToBoolean(value));
                break;
        }
    }

    public static int GetRitsuLibSettingInt(string key)
    {
        return key switch
        {
            RefreshShopConfig.KeyMaxUses => RefreshShopConfig.MaxUses,
            RefreshShopConfig.KeyCost => RefreshShopConfig.Cost,
            _ => 0
        };
    }

    public static void SetRitsuLibSettingInt(string key, int value)
    {
        switch (key)
        {
            case RefreshShopConfig.KeyMaxUses:
                RefreshShopConfig.SetMaxUsesFromRitsuLib(value);
                break;
            case RefreshShopConfig.KeyCost:
                RefreshShopConfig.SetCostFromRitsuLib(value);
                break;
        }
    }

    public static bool GetRitsuLibSettingBool(string key)
    {
        return key == RefreshShopConfig.KeyRefillsPurchased && RefreshShopConfig.RefillsPurchased;
    }

    public static void SetRitsuLibSettingBool(string key, bool value)
    {
        if (key == RefreshShopConfig.KeyRefillsPurchased)
            RefreshShopConfig.SetRefillsPurchasedFromRitsuLib(value);
    }

    public static void SaveRitsuLibSettings()
    {
    }

    private static class Texts
    {
        public static readonly LocalizedText ModDisplayName = new("Shop Refresh", "商店刷新");
        public static readonly LocalizedText SettingsPageTitle = new("Settings", "设置");
        public static readonly LocalizedText SettingsPageDescription = new(
            "Configure shop refresh behavior.",
            "配置商店刷新行为。");
        public static readonly LocalizedText RefreshSectionTitle = new("Refresh", "刷新");
        public static readonly LocalizedText MaxUsesLabel = new("Max Refreshes (0=Infinite)", "刷新次数 (0=无限)");

        public static readonly LocalizedText MaxUsesDescription = new(
            "Number of refreshes available per shop visit. 0 = unlimited.",
            "每次进入商店的可用刷新次数。0 = 无限次。");

        public static readonly LocalizedText CostLabel = new("Refresh Cost", "刷新价格");
        public static readonly LocalizedText CostDescription = new("Gold cost per refresh. 0 = free.", "每次刷新消耗的金币。0 = 免费。");
        public static readonly LocalizedText RefillsPurchasedLabel = new("Refill Purchased Slots", "刷新时恢复已购槽位");

        public static readonly LocalizedText RefillsPurchasedDescription = new(
            "If enabled, refreshing restocks purchased slots. If disabled, only un-purchased slots are re-rolled.",
            "开启时，刷新会恢复已购买的空槽位。关闭时，只重抽未购买的槽位。");
    }

    private sealed record LocalizedText(string En, string Zhs)
    {
        public object ToSchema()
        {
            return new Hashtable
            {
                ["langMap"] = new Hashtable
                {
                    ["en"] = En,
                    ["zhs"] = Zhs,
                    ["zh"] = Zhs
                },
                ["fallback"] = En
            };
        }
    }

    private sealed record SettingPageDefinition(
        string PageId,
        LocalizedText Title,
        LocalizedText Description,
        IReadOnlyList<SettingSectionDefinition> Sections)
    {
        public object ToSchema()
        {
            return new
            {
                pageId = PageId,
                title = Title.ToSchema(),
                description = Description.ToSchema(),
                sections = Sections.Select(section => section.ToSchema()).ToArray()
            };
        }
    }

    private sealed record SettingSectionDefinition(
        string Id,
        LocalizedText Title,
        IReadOnlyList<SettingEntryDefinition> Entries)
    {
        public object ToSchema()
        {
            return new
            {
                id = Id,
                title = Title.ToSchema(),
                entries = Entries.Select(entry => entry.ToSchema()).ToArray()
            };
        }
    }

    private sealed record SettingEntryDefinition(
        string Id,
        string Type,
        string Key,
        LocalizedText Label,
        LocalizedText Description,
        object DefaultValue = null,
        double? Min = null,
        double? Max = null,
        double? Step = null)
    {
        public object ToSchema()
        {
            return new
            {
                id = Id,
                type = Type,
                key = Key,
                label = Label.ToSchema(),
                description = Description.ToSchema(),
                defaultValue = DefaultValue,
                min = Min,
                max = Max,
                step = Step
            };
        }
    }
}
