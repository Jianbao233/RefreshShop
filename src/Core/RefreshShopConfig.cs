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

    static RefreshShopConfig()
    {
        LoadFromDisk();
    }

    public static string SettingsPath
    {
        get
        {
            var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            var dir = Path.Combine(appData, "SlayTheSpire2", "mods_settings");
            return Path.Combine(dir, "RefreshShop.json");
        }
    }

    /// <summary>从 mods_settings/RefreshShop.json 读取设置。只执行一次。</summary>
    public static void LoadFromDisk()
    {
        lock (_gate)
        {
            if (_loaded) return;
            _loaded = true;
        }

        try
        {
            var path = SettingsPath;
            if (!File.Exists(path))
            {
                RefreshShopLog.Info($"Settings not found, using defaults. Path={path}");
                return;
            }

            var json = File.ReadAllText(path);
            var dto = JsonSerializer.Deserialize<SettingsDto>(json);
            if (dto == null) return;

            if (dto.MaxUses.HasValue)
            {
                MaxUses = ClampInt(dto.MaxUses.Value.ToString(), 0, 999);
                _hasMaxUses = true;
            }

            if (dto.Cost.HasValue)
            {
                Cost = ClampInt(dto.Cost.Value.ToString(), 0, 999);
                _hasCost = true;
            }

            if (dto.RefillsPurchased.HasValue)
            {
                RefillsPurchased = dto.RefillsPurchased.Value;
                _hasRefillsPurchased = true;
            }

            RefreshShopLog.Info(
                $"Settings loaded: maxUses={MaxUses}, cost={Cost}, refill={RefillsPurchased}. Path={path}");
        }
        catch (Exception ex)
        {
            RefreshShopLog.Error($"LoadFromDisk failed: {ex.Message}");
        }
    }

    /// <summary>串行写入完整临时文件，再替换目标；失败时保留原配置。</summary>
    public static void SaveToDisk()
    {
        lock (_gate)
        {
            try
            {
                var path = SettingsPath;
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                var dto = new SettingsDto
                {
                    // 尚未迁移的键保持缺失，不能把默认值误标为用户配置。
                    MaxUses = _hasMaxUses ? MaxUses : null,
                    Cost = _hasCost ? Cost : null,
                    RefillsPurchased = _hasRefillsPurchased ? RefillsPurchased : null,
                };
                var json = JsonSerializer.Serialize(dto, new JsonSerializerOptions
                {
                    WriteIndented = true,
                    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
                });
                var tmp = path + ".tmp";
                File.WriteAllText(tmp, json);
                File.Move(tmp, path, overwrite: true);
                RefreshShopLog.Info($"Settings saved: maxUses={MaxUses}, cost={Cost}, refill={RefillsPurchased}.");
            }
            catch (Exception ex)
            {
                RefreshShopLog.Error($"SaveToDisk failed: {ex.Message}");
            }
        }
    }

    private sealed class SettingsDto
    {
        [JsonPropertyName("schema_version")]
        public int SchemaVersion { get; set; } = 1;

        [JsonPropertyName("refresh_max_uses")]
        public int? MaxUses { get; set; }

        [JsonPropertyName("refresh_cost")]
        public int? Cost { get; set; }

        [JsonPropertyName("refresh_refills_purchased")]
        public bool? RefillsPurchased { get; set; }
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
            _ => true,
        };
    }

    internal static void SetMaxUsesFromRitsuLib(int value) => SetValue(KeyMaxUses, value, sync: true);
    internal static void SetCostFromRitsuLib(int value) => SetValue(KeyCost, value, sync: true);
    internal static void SetRefillsPurchasedFromRitsuLib(bool value) => SetValue(KeyRefillsPurchased, value, sync: true);

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
                    MaxUses = Math.Clamp(maxUses, 0, 999);
                    _hasMaxUses = true;
                    RefreshCounter.ResetForNewShop();
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
                default:
                    return;
            }
            // 不依赖设置 UI 的防抖保存；即使马上退出也已落盘。
            SaveToDisk();
            if (sync && _modConfigReady) TrySetModConfigValue(key, normalized);
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
