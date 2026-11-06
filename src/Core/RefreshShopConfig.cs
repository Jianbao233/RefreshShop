using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace RefreshShop;

/// <summary>
/// ModConfig 配置注册 + 运行时静态值。
///
/// 四项配置（全部反射，运行时检测 ModConfig 是否在场）：
///   refresh_max_uses        TextInput  默认 "5"   (0 = 无限)
///   refresh_cost            TextInput  默认 "25"
///   refresh_refills_purchased Toggle    默认 false
///   refresh_other_character_relics Toggle 默认 false
///
/// 用 TextInput 而非 Slider：玩家可直接手动输入数值。
/// OnChanged 收到 string，解析成 int。
/// </summary>
internal static partial class RefreshShopConfig
{
    public static event Action Changed;
    public const string KeyMaxUses = "refresh_max_uses";
    public const string KeyCost = "refresh_cost";
    public const string KeyRefillsPurchased = "refresh_refills_purchased";
    public const string KeyOtherCharacterRelics = "refresh_other_character_relics";

    public const int DefaultMaxUses = 5;
    public const int DefaultCost = 25;
    public const bool DefaultRefillsPurchased = false;
    public const bool DefaultOtherCharacterRelics = false;

    public static int MaxUses { get; private set; } = DefaultMaxUses;
    public static int Cost { get; private set; } = DefaultCost;
    public static bool RefillsPurchased { get; private set; } = DefaultRefillsPurchased;
    public static bool OtherCharacterRelics { get; private set; } = DefaultOtherCharacterRelics;

    public static bool IsInfiniteUses => MaxUses == 0;

    // ── 持久化 ────────────────────────────────────────────────────────
    //
    // 本 mod 自己落盘，不依赖 ModConfig。
    //
    // 原因：RitsuLib 的 runtime-interop 设置通道用 ModSettingsBindings.Callback 绑定，
    // 该绑定明确「不使用 RitsuLibFramework.GetDataStore(modId)」，持久化责任在 mod 自己：
    // 设置 UI 会在防抖后调 binding.Save() → 提供器的 SaveRitsuLibSettings()。
    // 旧实现 SaveRitsuLibSettings() 是空方法，且 ModConfig 镜像源已在 csproj 中禁用，
    // 导致每次启动都回落到默认值（玩家反馈 "custom settings go back to default on game start"）。

    private static readonly object _gate = new();
    private static bool _loaded;
    private static bool _modConfigReady;
    private static bool _registeringModConfig;
    private static bool _syncingModConfig;

    /// <summary>该键是否已由本 mod 的 JSON 提供（用于决定是否还从 ModConfig 导入）。</summary>
    private static bool _hasMaxUses;
    private static bool _hasCost;
    private static bool _hasRefillsPurchased;
    private static bool _hasOtherCharacterRelics;

    static RefreshShopConfig()
    {
        LoadFromDisk();
    }

    /// <summary>
    /// 该键是否还应该从 ModConfig 读取。
    ///
    /// 本 mod 的 JSON 一旦接管某个键，就不再从 ModConfig 取值：
    /// ModConfig 是可选的老式第三方通道，其存量值可能是陈旧默认值，
    /// 若在每次进商店时轮询覆盖，会把玩家在 RitsuLib 设置页改的值改回去。
    /// 因此 ModConfig 只作为首次运行（本 mod 尚无 JSON）的迁移来源。
    /// </summary>
    private static bool ShouldImportFromModConfig(string key)
    {
        return key switch
        {
            KeyMaxUses => !_hasMaxUses,
            KeyCost => !_hasCost,
            KeyRefillsPurchased => !_hasRefillsPurchased,
            KeyOtherCharacterRelics => !_hasOtherCharacterRelics,
            _ => true,
        };
    }

    internal static void SetMaxUsesFromRitsuLib(int value) => SetValue(KeyMaxUses, value, sync: true);
    internal static void SetCostFromRitsuLib(int value) => SetValue(KeyCost, value, sync: true);
    internal static void SetRefillsPurchasedFromRitsuLib(bool value) => SetValue(KeyRefillsPurchased, value, sync: true);
    internal static void SetOtherCharacterRelicsFromRitsuLib(bool value) => SetValue(KeyOtherCharacterRelics, value, sync: true);

    private static void SetValue(string key, object value, bool sync)
    {
        lock (_gate)
        {
            // Register/SetValue 可能同步回调；不能让初始化旧值覆盖 JSON，也不能递归保存。
            if (_registeringModConfig || _syncingModConfig) return;
            object normalized;
            switch (key)
            {
                case KeyMaxUses:
                    if (!int.TryParse(value?.ToString(), out var maxUses)) return;
                    var nextUses = Math.Clamp(maxUses, 0, 999);
                    var changedUses = MaxUses != nextUses;
                    MaxUses = nextUses;
                    _hasMaxUses = true;
                    if (changedUses) RefreshCounter.ResetForNewShop();
                    normalized = MaxUses.ToString();
                    break;
                case KeyCost:
                    if (!int.TryParse(value?.ToString(), out var cost)) return;
                    Cost = Math.Clamp(cost, 0, 999);
                    _hasCost = true;
                    normalized = Cost.ToString();
                    break;
                case KeyRefillsPurchased:
                    if (!bool.TryParse(value?.ToString(), out var refill)) return;
                    RefillsPurchased = refill;
                    _hasRefillsPurchased = true;
                    normalized = refill;
                    break;
                case KeyOtherCharacterRelics:
                    if (!bool.TryParse(value?.ToString(), out var allowOther)) return;
                    OtherCharacterRelics = allowOther;
                    _hasOtherCharacterRelics = true;
                    normalized = allowOther;
                    break;
                default:
                    return;
            }
            // 不依赖设置 UI 的防抖保存；即使马上退出也已落盘。
            SaveToDisk();
            if (sync && _modConfigReady) TrySetModConfigValue(key, normalized);
            Changed?.Invoke();
        }
    }

    /// <summary>注册完成后批量迁移缺失的键，再一次性保存；已接管的键永不轮询覆盖。</summary>
    public static void RefreshFromModConfig()
    {
        lock (_gate)
        {
            // ModConfig.Register 才会加载旧文件；注册前 GetValue 可能只是默认值。
            if (!_modConfigReady) return;
            var apiType = FindType("ModConfig.ModConfigApi");
            if (apiType == null) return;
            bool imported = false;
            if (ShouldImportFromModConfig(KeyMaxUses))
                TryReadExistingString(apiType, KeyMaxUses, value =>
                {
                    if (!int.TryParse(value, out var parsed)) return;
                    MaxUses = Math.Clamp(parsed, 0, 999);
                    _hasMaxUses = true;
                    imported = true;
                    RefreshCounter.ResetForNewShop();
                });
            if (ShouldImportFromModConfig(KeyCost))
                TryReadExistingString(apiType, KeyCost, value =>
                {
                    if (!int.TryParse(value, out var parsed)) return;
                    Cost = Math.Clamp(parsed, 0, 999);
                    _hasCost = true;
                    imported = true;
                });
            if (ShouldImportFromModConfig(KeyRefillsPurchased))
                TryReadExistingBool(apiType, KeyRefillsPurchased, value =>
                {
                    RefillsPurchased = value;
                    _hasRefillsPurchased = true;
                    imported = true;
                });
            if (ShouldImportFromModConfig(KeyOtherCharacterRelics))
                TryReadExistingBool(apiType, KeyOtherCharacterRelics, value =>
                {
                    OtherCharacterRelics = value;
                    _hasOtherCharacterRelics = true;
                    imported = true;
                });
            if (imported) SaveToDisk();
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

    private static int ClampInt(string s, int min, int max)
    {
        if (int.TryParse(s?.Trim(), out int v))
            return Math.Clamp(v, min, max);
        return min;
    }

}
