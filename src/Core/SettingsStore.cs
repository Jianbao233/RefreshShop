using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace RefreshShop;

internal static partial class RefreshShopConfig
{
    private static string _lastSavedJson;

    public static string SettingsPath
    {
        get
        {
            var appData = Environment.GetEnvironmentVariable("SLAY_THE_SPIRE2_APPDATA")
                ?? Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
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
            if (dto.OtherCharacterRelics.HasValue)
            {
                OtherCharacterRelics = dto.OtherCharacterRelics.Value;
                _hasOtherCharacterRelics = true;
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
                    OtherCharacterRelics = _hasOtherCharacterRelics ? OtherCharacterRelics : null,
                };
                var json = JsonSerializer.Serialize(dto, new JsonSerializerOptions
                {
                    WriteIndented = true,
                    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
                });
                if (json == _lastSavedJson && File.Exists(path)) return;
                var tmp = path + ".tmp";
                File.WriteAllText(tmp, json);
                File.Move(tmp, path, overwrite: true);
                _lastSavedJson = json;
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

        [JsonPropertyName("refresh_other_character_relics")]
        public bool? OtherCharacterRelics { get; set; }
    }

}
