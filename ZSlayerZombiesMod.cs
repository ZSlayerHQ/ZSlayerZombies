using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.DI;
using SPTarkov.Server.Core.Helpers;
using SPTarkov.Server.Core.Models.Utils;

namespace ZSlayerZombies;

[Injectable(TypePriority = OnLoadOrder.PostSptModLoader + 2)]
public class ZSlayerZombiesMod(
    ZombieService zombieService,
    ModHelper modHelper,
    ISptLogger<ZSlayerZombiesMod> logger) : IOnLoad
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true
    };

    private ZombieConfig _config = new();
    private string? _modPath;

    public string ModPath => _modPath ??= modHelper.GetAbsolutePathToModFolder(Assembly.GetExecutingAssembly());

    public Task OnLoad()
    {
        LoadConfig();

        if (!_config.Enabled)
        {
            logger.Warning("[ZSlayerZombies] Mod is disabled in config — zombie events will not be activated");
            return Task.CompletedTask;
        }

        // Apply all zombie settings
        zombieService.Apply(_config);

        logger.Info("[ZSlayerZombies] HTTP API active at /zslayer/zombies/");

        return Task.CompletedTask;
    }

    // ═══════════════════════════════════════════════════════
    // CONFIG MANAGEMENT
    // ═══════════════════════════════════════════════════════

    public ZombieConfig GetConfig() => _config;

    public void UpdateConfig(ZombieConfig newConfig)
    {
        _config = newConfig;
        SaveConfig();

        if (_config.Enabled)
        {
            zombieService.Apply(_config);
            logger.Info("[ZSlayerZombies] Config updated and re-applied");
        }
        else
        {
            zombieService.Reset();
            logger.Info("[ZSlayerZombies] Config updated — mod disabled, values restored");
        }
    }

    public void ReApply()
    {
        if (_config.Enabled)
        {
            zombieService.Apply(_config);
            logger.Info("[ZSlayerZombies] Config re-applied");
        }
    }

    public void ResetToDefaults()
    {
        zombieService.Reset();
        _config = new ZombieConfig { Enabled = false };
        SaveConfig();
        logger.Info("[ZSlayerZombies] Reset to defaults — all zombie effects disabled");
    }

    // ═══════════════════════════════════════════════════════
    // CONFIG I/O
    // ═══════════════════════════════════════════════════════

    private void LoadConfig()
    {
        _modPath ??= modHelper.GetAbsolutePathToModFolder(Assembly.GetExecutingAssembly());
        var configPath = Path.Combine(_modPath, "config", "config.json");

        if (!File.Exists(configPath))
        {
            logger.Warning("[ZSlayerZombies] No config found, using defaults");
            _config = new ZombieConfig();
            SaveConfig();
            return;
        }

        try
        {
            var raw = File.ReadAllText(configPath);

            // Strip comments (jsonc support)
            raw = StripJsonComments(raw);

            _config = JsonSerializer.Deserialize<ZombieConfig>(raw, JsonOptions) ?? new ZombieConfig();
        }
        catch (Exception ex)
        {
            logger.Error($"[ZSlayerZombies] Failed to load config: {ex.Message}");
            _config = new ZombieConfig();
        }

        // Re-save to persist any new fields added in updates (auto-upgrade)
        SaveConfig();
    }

    private void SaveConfig()
    {
        _modPath ??= modHelper.GetAbsolutePathToModFolder(Assembly.GetExecutingAssembly());
        var configPath = Path.Combine(_modPath, "config", "config.json");

        var dir = Path.GetDirectoryName(configPath);
        if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
            Directory.CreateDirectory(dir);

        var json = JsonSerializer.Serialize(_config, JsonOptions);
        File.WriteAllText(configPath, json);
    }

    /// <summary>Strip // and /* */ comments from JSON (basic JSONC support).</summary>
    private static string StripJsonComments(string json)
    {
        var result = new System.Text.StringBuilder(json.Length);
        var i = 0;
        var inString = false;

        while (i < json.Length)
        {
            if (inString)
            {
                if (json[i] == '\\' && i + 1 < json.Length)
                {
                    result.Append(json[i]);
                    result.Append(json[i + 1]);
                    i += 2;
                    continue;
                }
                if (json[i] == '"')
                    inString = false;
                result.Append(json[i]);
                i++;
                continue;
            }

            if (json[i] == '"')
            {
                inString = true;
                result.Append(json[i]);
                i++;
                continue;
            }

            // Line comment
            if (json[i] == '/' && i + 1 < json.Length && json[i + 1] == '/')
            {
                while (i < json.Length && json[i] != '\n')
                    i++;
                continue;
            }

            // Block comment
            if (json[i] == '/' && i + 1 < json.Length && json[i + 1] == '*')
            {
                i += 2;
                while (i + 1 < json.Length && !(json[i] == '*' && json[i + 1] == '/'))
                    i++;
                i += 2;
                continue;
            }

            result.Append(json[i]);
            i++;
        }

        return result.ToString();
    }
}
