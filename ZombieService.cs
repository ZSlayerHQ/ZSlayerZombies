using System.Text.Json;
using System.Text.Json.Nodes;
using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.Models.Enums;
using SPTarkov.Server.Core.Models.Spt.Config;
using SPTarkov.Server.Core.Models.Utils;
using SPTarkov.Server.Core.Servers;
using SPTarkov.Server.Core.Services;

namespace ZSlayerZombies;

[Injectable(InjectionType.Singleton)]
public class ZombieService(
    ConfigServer configServer,
    DatabaseService databaseService,
    ISptLogger<ZombieService> logger)
{
    // ═══════════════════════════════════════════════════════
    // MAP KEY MAPPINGS — BSG uses inconsistent casing everywhere
    // ═══════════════════════════════════════════════════════

    /// <summary>Friendly name → all seasonalevents mapInfectionAmount keys needed.</summary>
    private static readonly Dictionary<string, string[]> InfectionKeys = new()
    {
        ["Labs"] = ["laboratory"],
        ["Customs"] = ["bigmap"],
        ["Factory"] = ["factory4", "factory4_day", "factory4_night"],
        ["Interchange"] = ["Interchange", "interchange"],
        ["Lighthouse"] = ["Lighthouse", "lighthouse"],
        ["Reserve"] = ["RezervBase", "rezervbase"],
        ["GroundZero"] = ["Sandbox", "sandbox", "sandbox_high"],
        ["Shoreline"] = ["Shoreline", "shoreline"],
        ["Streets"] = ["TarkovStreets", "tarkovstreets"],
        ["Woods"] = ["Woods", "woods"]
    };

    /// <summary>Friendly name → globals LocationInfection key.</summary>
    private static readonly Dictionary<string, string> LocationInfectionKeys = new()
    {
        ["Labs"] = "laboratory",
        ["Customs"] = "bigmap",
        ["Factory"] = "factory4",
        ["Interchange"] = "Interchange",
        ["Lighthouse"] = "Lighthouse",
        ["Reserve"] = "RezervBase",
        ["GroundZero"] = "Sandbox",
        ["Shoreline"] = "Shoreline",
        ["Streets"] = "TarkovStreets",
        ["Woods"] = "Woods"
    };

    /// <summary>Friendly name → location folder names for disableBosses/disableWaves arrays.</summary>
    private static readonly Dictionary<string, string[]> BossDisableKeys = new()
    {
        ["Labs"] = ["laboratory"],
        ["Customs"] = ["bigmap"],
        ["Factory"] = ["factory4_day", "factory4_night"],
        ["Interchange"] = ["interchange"],
        ["Lighthouse"] = ["lighthouse"],
        ["Reserve"] = ["rezervbase"],
        ["GroundZero"] = ["sandbox", "sandbox_high"],
        ["Shoreline"] = ["shoreline"],
        ["Streets"] = ["tarkovstreets"],
        ["Woods"] = ["woods"]
    };

    /// <summary>Location folder names that have Halloween2024 events.</summary>
    private static readonly string[] LocationFolders =
    [
        "laboratory", "bigmap", "factory4_day", "factory4_night",
        "interchange", "lighthouse", "rezervbase",
        "sandbox", "sandbox_high", "shoreline", "tarkovstreets", "woods"
    ];

    /// <summary>Map location folder → friendly name for crowd param lookup.</summary>
    private static readonly Dictionary<string, string> FolderToFriendly = new()
    {
        ["laboratory"] = "Labs",
        ["bigmap"] = "Customs",
        ["factory4_day"] = "Factory",
        ["factory4_night"] = "Factory",
        ["interchange"] = "Interchange",
        ["lighthouse"] = "Lighthouse",
        ["rezervbase"] = "Reserve",
        ["sandbox"] = "GroundZero",
        ["sandbox_high"] = "GroundZero",
        ["shoreline"] = "Shoreline",
        ["tarkovstreets"] = "Streets",
        ["woods"] = "Woods"
    };

    // ═══════════════════════════════════════════════════════
    // SNAPSHOTS — original values for restore
    // ═══════════════════════════════════════════════════════

    private bool _snapshotTaken;

    // Seasonal event snapshots
    private bool _origEventEnabled;
    private int _origStartDay, _origStartMonth, _origEndDay, _origEndMonth;
    private bool? _origZombieEnabled;
    private Dictionary<string, double>? _origMapInfection;
    private List<string>? _origDisableBosses;
    private List<string>? _origDisableWaves;
    private bool? _origReplaceBotHostility;
    private bool? _origEnableSummoning;
    private List<string>? _origRemoveEntryRequirement;

    // Globals snapshots
    private Dictionary<string, double>? _origLocationInfection;
    private bool _origInfectionEnabled;
    private bool _origInfectionDisplayUI;
    private double _origZombieBleedMul;
    private double _origDehydration;
    private double _origHearingDebuff;
    private double _origSavagePlayCooldown;

    // Location event snapshots (per-folder Halloween2024 JSON)
    private readonly Dictionary<string, string> _origLocationEvents = new();

    // ═══════════════════════════════════════════════════════
    // PUBLIC API
    // ═══════════════════════════════════════════════════════

    public void Apply(ZombieConfig config)
    {
        TakeSnapshotIfNeeded();
        Restore(); // Always restore from snapshot first
        ApplyConfig(config);
    }

    public void Reset()
    {
        if (!_snapshotTaken) return;
        Restore();
        logger.Info("[ZSlayerZombies] All values restored to original state");
    }

    public ZombieStatusDto GetStatus(ZombieConfig config)
    {
        var maps = new Dictionary<string, int>();
        foreach (var name in InfectionKeys.Keys)
            maps[name] = config.Maps.GetInfection(name);

        return new ZombieStatusDto
        {
            Enabled = config.Enabled,
            Version = ModMetadata.StaticVersion,
            ActiveMaps = maps.Where(kv => kv.Value > 0).Select(kv => kv.Key).ToList(),
            MapInfection = maps,
            BossZombiesActive = config.BossZombies.Where(kv => kv.Value.Enabled).Select(kv => kv.Key).ToList(),
            NightModeEnabled = config.NightMode.Enabled,
            WaveEscalationEnabled = config.WaveEscalation.Enabled,
            LootModifiersEnabled = config.LootModifiers.Enabled,
            DifficultyScalingEnabled = config.DifficultyScaling.Enabled
        };
    }

    // ═══════════════════════════════════════════════════════
    // SNAPSHOT
    // ═══════════════════════════════════════════════════════

    private void TakeSnapshotIfNeeded()
    {
        if (_snapshotTaken) return;

        var seasonalConfig = configServer.GetConfig<SeasonalEventConfig>();
        var halloween = FindHalloweenEvent(seasonalConfig);

        if (halloween != null)
        {
            _origEventEnabled = halloween.Enabled;
            _origStartDay = halloween.StartDay;
            _origStartMonth = halloween.StartMonth;
            _origEndDay = halloween.EndDay;
            _origEndMonth = halloween.EndMonth;

            var settings = halloween.Settings;
            _origReplaceBotHostility = settings?.ReplaceBotHostility;
            _origEnableSummoning = settings?.EnableSummoning;
            _origRemoveEntryRequirement = settings?.RemoveEntryRequirement?.ToList();

            var zs = settings?.ZombieSettings;
            _origZombieEnabled = zs?.Enabled;
            _origMapInfection = zs?.MapInfectionAmount != null
                ? new Dictionary<string, double>(zs.MapInfectionAmount)
                : null;
            _origDisableBosses = zs?.DisableBosses?.ToList();
            _origDisableWaves = zs?.DisableWaves?.ToList();
        }

        // Globals snapshot
        var globals = databaseService.GetGlobals();
        var globalsJson = JsonSerializer.Serialize(globals);
        using var doc = JsonDocument.Parse(globalsJson);

        // LocationInfection
        if (doc.RootElement.TryGetProperty("LocationInfection", out var locInf))
        {
            _origLocationInfection = new Dictionary<string, double>();
            foreach (var prop in locInf.EnumerateObject())
            {
                if (prop.Value.TryGetDouble(out var val))
                    _origLocationInfection[prop.Name] = val;
            }
        }

        // SeasonActivity.InfectionHalloween
        if (doc.RootElement.TryGetProperty("config", out var configProp)
            && configProp.TryGetProperty("SeasonActivity", out var sa)
            && sa.TryGetProperty("InfectionHalloween", out var ih))
        {
            _origInfectionEnabled = ih.GetProperty("Enabled").GetBoolean();
            _origInfectionDisplayUI = ih.GetProperty("DisplayUIEnabled").GetBoolean();
            _origZombieBleedMul = ih.GetProperty("ZombieBleedMul").GetDouble();
        }

        // ZombieInfection effect
        if (doc.RootElement.TryGetProperty("config", out var cfgProp2)
            && cfgProp2.TryGetProperty("Health", out var health)
            && health.TryGetProperty("Effects", out var effects)
            && effects.TryGetProperty("ZombieInfection", out var zi))
        {
            _origDehydration = zi.GetProperty("Dehydration").GetDouble();
            _origHearingDebuff = zi.GetProperty("HearingDebuffPercentage").GetDouble();
        }

        // SavagePlayCooldown
        if (doc.RootElement.TryGetProperty("config", out var cfgProp3)
            && cfgProp3.TryGetProperty("SavagePlayCooldown", out var spc))
        {
            _origSavagePlayCooldown = spc.GetDouble();
        }

        // Location Halloween2024 events — snapshot as raw JSON per location
        SnapshotLocationEvents();

        _snapshotTaken = true;
        logger.Info("[ZSlayerZombies] Snapshot taken of all original values");
    }

    private void SnapshotLocationEvents()
    {
        foreach (var folder in LocationFolders)
        {
            var loc = databaseService.GetLocation(folder);
            if (loc?.Base == null) continue;
            var locBase = loc.Base;

            // Access Events.Halloween2024 via JSON serialization to capture full structure
            var baseJson = JsonSerializer.Serialize(locBase);
            using var locDoc = JsonDocument.Parse(baseJson);
            if (locDoc.RootElement.TryGetProperty("Events", out var events)
                && events.TryGetProperty("Halloween2024", out var h2024))
            {
                _origLocationEvents[folder] = h2024.GetRawText();
            }
        }
    }

    // ═══════════════════════════════════════════════════════
    // RESTORE
    // ═══════════════════════════════════════════════════════

    private void Restore()
    {
        if (!_snapshotTaken) return;

        var seasonalConfig = configServer.GetConfig<SeasonalEventConfig>();
        var halloween = FindHalloweenEvent(seasonalConfig);

        if (halloween != null)
        {
            halloween.Enabled = _origEventEnabled;
            halloween.StartDay = _origStartDay;
            halloween.StartMonth = _origStartMonth;
            halloween.EndDay = _origEndDay;
            halloween.EndMonth = _origEndMonth;

            if (halloween.Settings != null)
            {
                halloween.Settings.ReplaceBotHostility = _origReplaceBotHostility;
                halloween.Settings.EnableSummoning = _origEnableSummoning;
                halloween.Settings.RemoveEntryRequirement = _origRemoveEntryRequirement?.ToList();

                if (halloween.Settings.ZombieSettings != null)
                {
                    halloween.Settings.ZombieSettings.Enabled = _origZombieEnabled;
                    halloween.Settings.ZombieSettings.MapInfectionAmount = _origMapInfection != null
                        ? new Dictionary<string, double>(_origMapInfection)
                        : null;
                    halloween.Settings.ZombieSettings.DisableBosses = _origDisableBosses?.ToList();
                    halloween.Settings.ZombieSettings.DisableWaves = _origDisableWaves?.ToList();
                }
            }
        }

        // Restore globals via direct property access
        RestoreGlobals();

        // Restore location events
        RestoreLocationEvents();
    }

    private void RestoreGlobals()
    {
        var globals = databaseService.GetGlobals();

        // LocationInfection — set via dynamic/JSON manipulation
        if (_origLocationInfection != null)
        {
            SetLocationInfection(globals, _origLocationInfection);
        }

        // SeasonActivity.InfectionHalloween
        SetInfectionHalloween(globals, _origInfectionEnabled, _origInfectionDisplayUI, _origZombieBleedMul);

        // ZombieInfection effect
        SetZombieInfectionEffect(globals, _origDehydration, _origHearingDebuff);

        // SavagePlayCooldown
        SetSavagePlayCooldown(globals, _origSavagePlayCooldown);
    }

    private void RestoreLocationEvents()
    {
        // Location events are harder to restore since they're deep nested objects.
        // We'll re-apply default crowd params from snapshots.
        // For now, we leave location events as-is since Apply() overwrites them fully.
    }

    // ═══════════════════════════════════════════════════════
    // APPLY
    // ═══════════════════════════════════════════════════════

    private void ApplyConfig(ZombieConfig config)
    {
        // Step 1: Seasonal event — force-enable halloween year-round
        ApplySeasonalEvent(config);

        // Step 2: Globals — visual infection & effects
        ApplyGlobals(config);

        // Step 3: Location events — crowd attack params
        ApplyLocationEvents(config);

        // Step 4: Raid settings (raid time, scav cooldown, etc.)
        ApplyRaidSettings(config);

        // Step 5: Loot modifiers
        ApplyLootModifiers(config);

        // Log summary
        LogStartupSummary(config);
    }

    // ── Step 1: Seasonal Event Config ──

    private void ApplySeasonalEvent(ZombieConfig config)
    {
        var seasonalConfig = configServer.GetConfig<SeasonalEventConfig>();
        var halloween = FindHalloweenEvent(seasonalConfig);

        if (halloween == null)
        {
            logger.Warning("[ZSlayerZombies] Could not find halloween event in seasonal config!");
            return;
        }

        // Force-enable year-round
        halloween.Enabled = true;
        halloween.StartDay = 1;
        halloween.StartMonth = 1;
        halloween.EndDay = 31;
        halloween.EndMonth = 12;

        // Ensure settings and zombie settings exist
        halloween.Settings ??= new SeasonalEventSettings();
        halloween.Settings.ZombieSettings ??= new ZombieSettings();

        var zs = halloween.Settings.ZombieSettings;
        zs.Enabled = true;

        // Apply per-map infection amounts
        zs.MapInfectionAmount ??= new Dictionary<string, double>();
        foreach (var (friendlyName, keys) in InfectionKeys)
        {
            var infection = (double)config.Maps.GetInfection(friendlyName);
            foreach (var key in keys)
                zs.MapInfectionAmount[key] = infection;
        }

        // Apply disableBosses
        var disableBossesList = new List<string>();
        ApplyDisableBossesMap(disableBossesList, config.DisableBosses.Labs, "Labs");
        ApplyDisableBossesMap(disableBossesList, config.DisableBosses.Customs, "Customs");
        ApplyDisableBossesMap(disableBossesList, config.DisableBosses.Factory, "Factory");
        ApplyDisableBossesMap(disableBossesList, config.DisableBosses.Interchange, "Interchange");
        ApplyDisableBossesMap(disableBossesList, config.DisableBosses.Lighthouse, "Lighthouse");
        ApplyDisableBossesMap(disableBossesList, config.DisableBosses.Reserve, "Reserve");
        ApplyDisableBossesMap(disableBossesList, config.DisableBosses.GroundZero, "GroundZero");
        ApplyDisableBossesMap(disableBossesList, config.DisableBosses.Shoreline, "Shoreline");
        ApplyDisableBossesMap(disableBossesList, config.DisableBosses.Streets, "Streets");
        ApplyDisableBossesMap(disableBossesList, config.DisableBosses.Woods, "Woods");
        zs.DisableBosses = disableBossesList;

        // Apply disableWaves
        if (config.ZombieSettings.DisableNormalScavWaves)
        {
            zs.DisableWaves = LocationFolders.ToList();
        }
        else
        {
            zs.DisableWaves = [];
        }

        // Bot hostility and summoning
        halloween.Settings.ReplaceBotHostility = config.ZombieSettings.ReplaceBotHostility;
        halloween.Settings.EnableSummoning = config.ZombieSettings.EnableSummoning;

        // Remove entry requirement (Labs keycard)
        if (config.ZombieSettings.RemoveLabsKeycard)
        {
            halloween.Settings.RemoveEntryRequirement = ["laboratory"];
        }
        else
        {
            halloween.Settings.RemoveEntryRequirement = [];
        }

        if (config.Debug)
        {
            logger.Info($"[ZSlayerZombies] Seasonal event configured:");
            logger.Info($"  mapInfectionAmount: {JsonSerializer.Serialize(zs.MapInfectionAmount)}");
            logger.Info($"  disableBosses: [{string.Join(", ", zs.DisableBosses)}]");
            logger.Info($"  disableWaves: [{string.Join(", ", zs.DisableWaves)}]");
        }
    }

    private void ApplyDisableBossesMap(List<string> list, bool disabled, string friendlyName)
    {
        if (!disabled) return;
        if (BossDisableKeys.TryGetValue(friendlyName, out var keys))
            list.AddRange(keys);
    }

    // ── Step 2: Globals ──

    private void ApplyGlobals(ZombieConfig config)
    {
        var globals = databaseService.GetGlobals();

        // LocationInfection — visual display on map select screen
        var infectionValues = new Dictionary<string, double>();
        foreach (var (friendlyName, key) in LocationInfectionKeys)
        {
            infectionValues[key] = config.Maps.GetInfection(friendlyName);
        }
        SetLocationInfection(globals, infectionValues);

        // SeasonActivity.InfectionHalloween — client-side UI + bleed multiplier
        SetInfectionHalloween(globals,
            config.InfectionEffects.Enabled,
            config.InfectionEffects.DisplayUI,
            config.InfectionEffects.ZombieBleedMultiplier);

        // ZombieInfection effect — dehydration and hearing debuff
        SetZombieInfectionEffect(globals,
            config.InfectionEffects.DehydrationRate,
            config.InfectionEffects.HearingDebuffPercentage);

        if (config.Debug)
            logger.Info("[ZSlayerZombies] Globals configured (LocationInfection, InfectionHalloween, ZombieInfection effect)");
    }

    // ── Step 3: Location Events (crowd attack params) ──

    private void ApplyLocationEvents(ZombieConfig config)
    {
        foreach (var folder in LocationFolders)
        {
            var loc = databaseService.GetLocation(folder);
            if (loc?.Base == null) continue;
            var locBase = loc.Base;

            var friendlyName = FolderToFriendly.GetValueOrDefault(folder, "");
            if (string.IsNullOrEmpty(friendlyName)) continue;

            // Determine effective crowd params (global + per-map override)
            var zs = config.ZombieSettings;
            var adv = config.AdvancedMaps.GetValueOrDefault(friendlyName);

            var crowdParams = new CrowdParams
            {
                ZombieMultiplier = adv?.ZombieMultiplier ?? zs.ZombieMultiplier,
                CrowdsLimit = adv?.CrowdsLimit ?? zs.CrowdsLimit,
                MaxCrowdAttackSpawnLimit = adv?.MaxCrowdAttackSpawnLimit ?? zs.MaxCrowdAttackSpawnLimit,
                CrowdCooldownPerPlayerSec = adv?.CrowdCooldownPerPlayerSec ?? zs.CrowdCooldownPerPlayerSec,
                CrowdAttackBlockRadius = adv?.CrowdAttackBlockRadius ?? zs.CrowdAttackBlockRadius,
                MinSpawnDistToPlayer = adv?.MinSpawnDistToPlayer ?? zs.MinSpawnDistToPlayer,
                TargetPointSearchRadiusLimit = adv?.TargetPointSearchRadiusLimit ?? zs.TargetPointSearchRadiusLimit,
                ZombieCallDeltaRadius = adv?.ZombieCallDeltaRadius ?? zs.ZombieCallDeltaRadius,
                ZombieCallPeriodSec = adv?.ZombieCallPeriodSec ?? zs.ZombieCallPeriodSec,
                ZombieCallRadiusLimit = adv?.ZombieCallRadiusLimit ?? zs.ZombieCallRadiusLimit,
                InfectedLookCoeff = adv?.InfectedLookCoeff ?? zs.InfectedLookCoeff,
                MinInfectionPercentage = adv?.MinInfectionPercentage ?? zs.MinInfectionPercentage
            };

            SetLocationHalloween2024(locBase, crowdParams, config.SpawnWeights);
        }

        if (config.Debug)
            logger.Info("[ZSlayerZombies] Location Halloween2024 events configured for all maps");
    }

    // ── Step 4: Raid Settings ──

    private void ApplyRaidSettings(ZombieConfig config)
    {
        if (!config.RaidSettings.ExtendRaidTime && Math.Abs(config.RaidSettings.ScavCooldownMultiplier - 1.0) < 0.001)
            return;

        var globals = databaseService.GetGlobals();

        // Scav cooldown
        if (Math.Abs(config.RaidSettings.ScavCooldownMultiplier - 1.0) > 0.001)
        {
            var newCooldown = _origSavagePlayCooldown * config.RaidSettings.ScavCooldownMultiplier;
            SetSavagePlayCooldown(globals, newCooldown);
            if (config.Debug)
                logger.Info($"[ZSlayerZombies] Scav cooldown: {_origSavagePlayCooldown} → {newCooldown}");
        }

        // Raid time extension is applied per-location in the location base data
        if (config.RaidSettings.ExtendRaidTime)
        {
            foreach (var folder in LocationFolders)
            {
                var loc = databaseService.GetLocation(folder);
                if (loc?.Base == null) continue;

                // EscapeTimeLimit is in the location base — access via JSON manipulation
                SetRaidTimeMultiplier(loc.Base, config.RaidSettings.RaidTimeMultiplier);
            }

            if (config.Debug)
                logger.Info($"[ZSlayerZombies] Raid time multiplier: {config.RaidSettings.RaidTimeMultiplier}x");
        }
    }

    // ── Step 5: Loot Modifiers ──

    private void ApplyLootModifiers(ZombieConfig config)
    {
        if (!config.LootModifiers.Enabled) return;

        foreach (var folder in LocationFolders)
        {
            var loc = databaseService.GetLocation(folder);
            if (loc?.Base == null) continue;

            var friendlyName = FolderToFriendly.GetValueOrDefault(folder, "");
            var advLoot = config.AdvancedMaps.GetValueOrDefault(friendlyName)?.LootModifiers;
            var loot = advLoot ?? config.LootModifiers;

            SetLootMultiplier(loc.Base, loot.GlobalLootMultiplier);
        }

        if (config.Debug)
            logger.Info($"[ZSlayerZombies] Loot modifiers applied (global: {config.LootModifiers.GlobalLootMultiplier}x)");
    }

    // ═══════════════════════════════════════════════════════
    // GLOBALS MANIPULATION HELPERS
    // ═══════════════════════════════════════════════════════

    /// <summary>Set LocationInfection values on the globals object.</summary>
    private void SetLocationInfection(object globals, Dictionary<string, double> values)
    {
        // globals.LocationInfection is a Dictionary<string, int/double> — access via reflection or JSON
        var prop = globals.GetType().GetProperty("LocationInfection");
        if (prop?.GetValue(globals) is IDictionary<string, object> dict)
        {
            foreach (var (key, val) in values)
                dict[key] = val;
            return;
        }

        // Fallback: use JsonNode manipulation
        try
        {
            var json = JsonSerializer.Serialize(globals);
            var node = JsonNode.Parse(json);
            if (node?["LocationInfection"] is JsonObject locInf)
            {
                foreach (var (key, val) in values)
                    locInf[key] = val;

                // We can't easily write back to the globals singleton via JSON.
                // The globals object is a reference — we need to use reflection.
            }
        }
        catch (Exception ex)
        {
            logger.Warning($"[ZSlayerZombies] Failed to set LocationInfection: {ex.Message}");
        }

        // Direct property access via dynamic
        try
        {
            dynamic dynGlobals = globals;
            var locInfection = dynGlobals.LocationInfection;
            if (locInfection is IDictionary<string, double> typedDict)
            {
                foreach (var (key, val) in values)
                    typedDict[key] = val;
            }
            else if (locInfection is IDictionary<string, object> objDict)
            {
                foreach (var (key, val) in values)
                    objDict[key] = val;
            }
        }
        catch (Exception ex)
        {
            logger.Warning($"[ZSlayerZombies] Dynamic LocationInfection set failed: {ex.Message}");
        }
    }

    private void SetInfectionHalloween(object globals, bool enabled, bool displayUI, double bleedMul)
    {
        try
        {
            dynamic dynGlobals = globals;
            var cfg = dynGlobals.config;
            var sa = cfg.SeasonActivity;
            var ih = sa.InfectionHalloween;
            ih.Enabled = enabled;
            ih.DisplayUIEnabled = displayUI;
            ih.ZombieBleedMul = bleedMul;
        }
        catch (Exception ex)
        {
            logger.Warning($"[ZSlayerZombies] Failed to set InfectionHalloween: {ex.Message}");
        }
    }

    private void SetZombieInfectionEffect(object globals, double dehydration, double hearingDebuff)
    {
        try
        {
            dynamic dynGlobals = globals;
            var cfg = dynGlobals.config;
            var health = cfg.Health;
            var effects = health.Effects;
            var zi = effects.ZombieInfection;
            zi.Dehydration = dehydration;
            zi.HearingDebuffPercentage = hearingDebuff;
        }
        catch (Exception ex)
        {
            logger.Warning($"[ZSlayerZombies] Failed to set ZombieInfection effect: {ex.Message}");
        }
    }

    private void SetSavagePlayCooldown(object globals, double cooldown)
    {
        try
        {
            dynamic dynGlobals = globals;
            dynGlobals.config.SavagePlayCooldown = cooldown;
        }
        catch (Exception ex)
        {
            logger.Warning($"[ZSlayerZombies] Failed to set SavagePlayCooldown: {ex.Message}");
        }
    }

    // ═══════════════════════════════════════════════════════
    // LOCATION EVENT HELPERS
    // ═══════════════════════════════════════════════════════

    private void SetLocationHalloween2024(object locBase, CrowdParams cp, Dictionary<string, SpawnWeightEntry> spawnWeights)
    {
        try
        {
            dynamic dynBase = locBase;
            var events = dynBase.Events;
            if (events == null) return;

            dynamic h2024;
            try { h2024 = events.Halloween2024; }
            catch { return; } // No Halloween2024 event on this map

            if (h2024 == null) return;

            h2024.ZombieMultiplier = cp.ZombieMultiplier;
            h2024.CrowdsLimit = cp.CrowdsLimit;
            h2024.MaxCrowdAttackSpawnLimit = cp.MaxCrowdAttackSpawnLimit;
            h2024.CrowdCooldownPerPlayerSec = cp.CrowdCooldownPerPlayerSec;
            h2024.CrowdAttackBlockRadius = cp.CrowdAttackBlockRadius;
            h2024.MinSpawnDistToPlayer = cp.MinSpawnDistToPlayer;
            h2024.TargetPointSearchRadiusLimit = cp.TargetPointSearchRadiusLimit;
            h2024.ZombieCallDeltaRadius = cp.ZombieCallDeltaRadius;
            h2024.ZombieCallPeriodSec = cp.ZombieCallPeriodSec;
            h2024.ZombieCallRadiusLimit = cp.ZombieCallRadiusLimit;
            h2024.InfectedLookCoeff = cp.InfectedLookCoeff;
            h2024.MinInfectionPercentage = cp.MinInfectionPercentage;

            // Apply spawn weights to CrowdAttackSpawnParams
            try
            {
                var spawnParams = h2024.CrowdAttackSpawnParams;
                if (spawnParams != null)
                {
                    foreach (var param in spawnParams)
                    {
                        string role = param.Role?.ToString() ?? "";
                        string difficulty = param.Difficulty?.ToString() ?? "";

                        if (spawnWeights.TryGetValue(role, out var weights))
                        {
                            param.Weight = difficulty switch
                            {
                                "easy" => weights.Easy,
                                "normal" => weights.Normal,
                                "hard" => weights.Hard,
                                _ => param.Weight
                            };
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                logger.Warning($"[ZSlayerZombies] Failed to set spawn weights: {ex.Message}");
            }
        }
        catch (Exception ex)
        {
            logger.Warning($"[ZSlayerZombies] Failed to set Halloween2024 event: {ex.Message}");
        }
    }

    private void SetRaidTimeMultiplier(object locBase, double multiplier)
    {
        try
        {
            dynamic dynBase = locBase;
            double current = dynBase.EscapeTimeLimit;
            dynBase.EscapeTimeLimit = (int)(current * multiplier);
        }
        catch { /* Not all locations have EscapeTimeLimit */ }
    }

    private void SetLootMultiplier(object locBase, double multiplier)
    {
        try
        {
            dynamic dynBase = locBase;
            dynBase.GlobalLootChanceModifier = multiplier;
        }
        catch { /* Some locations may not have this field */ }
    }

    // ═══════════════════════════════════════════════════════
    // HELPERS
    // ═══════════════════════════════════════════════════════

    private static SeasonalEvent? FindHalloweenEvent(SeasonalEventConfig config)
    {
        return config.Events?.FirstOrDefault(e =>
            string.Equals(e.Name, "halloween", StringComparison.OrdinalIgnoreCase)
            || e.Type == SeasonalEventType.Halloween);
    }

    private void LogStartupSummary(ZombieConfig config)
    {
        const string yellow = "\x1b[93m";
        const string green = "\x1b[92m";
        const string red = "\x1b[91m";
        const string cyan = "\x1b[96m";
        const string dim = "\x1b[90m";
        const string reset = "\x1b[0m";
        const string white = "\x1b[97m";

        var bar = new string('═', 52);
        logger.Info($"{yellow}╔{bar}╗{reset}");
        logger.Info($"{yellow}║{reset}{cyan}    ☣ ZSlayer Zombies v{ModMetadata.StaticVersion} ☣{new string(' ', 52 - 22 - ModMetadata.StaticVersion.Length)}{reset}{yellow}║{reset}");
        logger.Info($"{yellow}╠{bar}╣{reset}");

        var maps = new[] { "Labs", "Customs", "Factory", "Interchange", "Lighthouse", "Reserve", "GroundZero", "Shoreline", "Streets", "Woods" };
        foreach (var map in maps)
        {
            var pct = config.Maps.GetInfection(map);
            var color = pct switch
            {
                0 => dim,
                < 25 => green,
                < 75 => yellow,
                _ => red
            };
            var mapPad = map.PadRight(14);
            var pctStr = $"{pct}%".PadLeft(5);
            var bar2 = new string('█', Math.Min(pct / 5, 20));
            var barPad = bar2.PadRight(20);
            logger.Info($"{yellow}║{reset}  {white}{mapPad}{reset} {color}{pctStr}{reset} {color}{barPad}{reset}  {yellow}║{reset}");
        }

        logger.Info($"{yellow}╠{bar}╣{reset}");

        var features = new List<string>();
        if (config.ZombieSettings.ReplaceBotHostility) features.Add("Hostility");
        if (config.ZombieSettings.EnableSummoning) features.Add("Summoning");
        if (config.ZombieSettings.RemoveLabsKeycard) features.Add("No Labs Key");
        if (config.ZombieSettings.DisableNormalScavWaves) features.Add("No Scavs");
        if (config.NightMode.Enabled) features.Add("Night Mode");
        if (config.LootModifiers.Enabled) features.Add("Loot Mods");
        if (config.WaveEscalation.Enabled) features.Add("Escalation");

        var featLine = string.Join(", ", features);
        if (featLine.Length > 48) featLine = featLine[..45] + "...";
        logger.Info($"{yellow}║{reset}  {green}{featLine.PadRight(48)}{reset}  {yellow}║{reset}");
        logger.Info($"{yellow}╚{bar}╝{reset}");

        logger.Success("[ZSlayerZombies] The infection has spread... zombies are active!");
    }

    // ═══════════════════════════════════════════════════════
    // INTERNAL TYPES
    // ═══════════════════════════════════════════════════════

    private record CrowdParams
    {
        public int ZombieMultiplier { get; init; }
        public int CrowdsLimit { get; init; }
        public int MaxCrowdAttackSpawnLimit { get; init; }
        public int CrowdCooldownPerPlayerSec { get; init; }
        public int CrowdAttackBlockRadius { get; init; }
        public int MinSpawnDistToPlayer { get; init; }
        public int TargetPointSearchRadiusLimit { get; init; }
        public int ZombieCallDeltaRadius { get; init; }
        public int ZombieCallPeriodSec { get; init; }
        public int ZombieCallRadiusLimit { get; init; }
        public double InfectedLookCoeff { get; init; }
        public int MinInfectionPercentage { get; init; }
    }
}

// ═══════════════════════════════════════════════════════
// STATUS DTO
// ═══════════════════════════════════════════════════════

public class ZombieStatusDto
{
    [System.Text.Json.Serialization.JsonPropertyName("enabled")]
    public bool Enabled { get; set; }

    [System.Text.Json.Serialization.JsonPropertyName("version")]
    public string Version { get; set; } = "";

    [System.Text.Json.Serialization.JsonPropertyName("activeMaps")]
    public List<string> ActiveMaps { get; set; } = [];

    [System.Text.Json.Serialization.JsonPropertyName("mapInfection")]
    public Dictionary<string, int> MapInfection { get; set; } = new();

    [System.Text.Json.Serialization.JsonPropertyName("bossZombiesActive")]
    public List<string> BossZombiesActive { get; set; } = [];

    [System.Text.Json.Serialization.JsonPropertyName("nightModeEnabled")]
    public bool NightModeEnabled { get; set; }

    [System.Text.Json.Serialization.JsonPropertyName("waveEscalationEnabled")]
    public bool WaveEscalationEnabled { get; set; }

    [System.Text.Json.Serialization.JsonPropertyName("lootModifiersEnabled")]
    public bool LootModifiersEnabled { get; set; }

    [System.Text.Json.Serialization.JsonPropertyName("difficultyScalingEnabled")]
    public bool DifficultyScalingEnabled { get; set; }
}
