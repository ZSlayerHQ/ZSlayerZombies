using System.Text.Json.Serialization;
using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Eft.Common;
using SPTarkov.Server.Core.Models.Eft.Common.Tables;
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
    // MOD DETECTION — avoid conflicts with other bot mods
    // ═══════════════════════════════════════════════════════

    private bool? _abpsDetected;

    /// <summary>Detect if acidphantasm-botplacementsystem is installed (manages bot caps).</summary>
    private bool IsAbpsInstalled()
    {
        if (_abpsDetected.HasValue) return _abpsDetected.Value;

        // Our DLL lives in user/mods/ZSlayerZombies/ — go up one level to get user/mods/
        var dllDir = System.IO.Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location) ?? "";
        var modsPath = Directory.GetParent(dllDir)?.FullName ?? System.IO.Path.Combine(dllDir, "..");
        _abpsDetected = Directory.Exists(System.IO.Path.Combine(modsPath, "acidphantasm-botplacementsystem"));

        if (_abpsDetected.Value)
            logger.Info("[ZSlayerZombies] Detected acidphantasm-botplacementsystem — bot cap overrides will be skipped to avoid conflicts");

        return _abpsDetected.Value;
    }
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

    // Globals snapshots (strongly typed)
    private Dictionary<string, int>? _origLocationInfection;
    private bool _origInfectionEnabled;
    private bool _origInfectionDisplayUI;
    private double _origZombieBleedMul;
    private double _origDehydration;
    private double _origHearingDebuff;
    private int _origSavagePlayCooldown;

    // Location event snapshots — per-folder Halloween2024 crowd params
    private readonly Dictionary<string, Halloween2024Snapshot> _origLocationEvents = new();

    // Bot AI snapshots — botType → difficulty → snapshot of AI values
    private readonly Dictionary<string, Dictionary<string, AISnapshot>> _origBotAI = new();

    // Bot health snapshots — botType → first BodyPart HP values
    private readonly Dictionary<string, HealthSnapshot> _origBotHealth = new();

    // Bot config MaxBotCap snapshot
    private Dictionary<string, int>? _origMaxBotCap;

    // Per-location MaxBotPerZone + original BossLocationSpawn count (for extra wave cleanup)
    private readonly Dictionary<string, int> _origMaxBotPerZone = new();
    private readonly Dictionary<string, int> _origBossSpawnCount = new();

    // Per-location original CrowdAttackSpawnParams count (for boss zombie injection cleanup)
    private readonly Dictionary<string, int> _origCrowdParamCount = new();

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

        // Seasonal event config snapshot
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

        // Globals snapshot — strongly typed
        var globals = databaseService.GetGlobals();

        // LocationInfection: Dictionary<string, int>
        if (globals.LocationInfection != null)
            _origLocationInfection = new Dictionary<string, int>(globals.LocationInfection);

        // SeasonActivity.InfectionHalloween
        var ih = globals.Configuration?.SeasonActivity?.InfectionHalloween;
        if (ih != null)
        {
            _origInfectionEnabled = ih.Enabled;
            _origInfectionDisplayUI = ih.DisplayUIEnabled;
            _origZombieBleedMul = ih.ZombieBleedMul;
        }

        // ZombieInfection effect
        var zi = globals.Configuration?.Health?.Effects?.ZombieInfection;
        if (zi != null)
        {
            _origDehydration = zi.Dehydration;
            _origHearingDebuff = zi.HearingDebuffPercentage;
        }

        // SavagePlayCooldown: int
        _origSavagePlayCooldown = globals.Configuration?.SavagePlayCooldown ?? 0;

        // Location Halloween2024 events — snapshot crowd params per location
        SnapshotLocationEvents();

        // Bot AI difficulty + health snapshots
        SnapshotBotTypes();

        // Bot config MaxBotCap + location MaxBotPerZone + BossLocationSpawn counts
        SnapshotSpawnControl();

        _snapshotTaken = true;
        logger.Info("[ZSlayerZombies] Snapshot taken of all original values");
    }

    private void SnapshotLocationEvents()
    {
        foreach (var folder in LocationFolders)
        {
            var loc = databaseService.GetLocation(folder);
            var h2024 = loc?.Base?.Events?.Halloween2024;
            if (h2024 == null) continue;

            _origLocationEvents[folder] = new Halloween2024Snapshot
            {
                ZombieMultiplier = h2024.ZombieMultiplier,
                CrowdsLimit = h2024.CrowdsLimit,
                MaxCrowdAttackSpawnLimit = h2024.MaxCrowdAttackSpawnLimit,
                CrowdCooldownPerPlayerSec = h2024.CrowdCooldownPerPlayerSec,
                CrowdAttackBlockRadius = h2024.CrowdAttackBlockRadius,
                MinSpawnDistToPlayer = h2024.MinSpawnDistToPlayer,
                TargetPointSearchRadiusLimit = h2024.TargetPointSearchRadiusLimit,
                ZombieCallDeltaRadius = h2024.ZombieCallDeltaRadius,
                ZombieCallPeriodSec = h2024.ZombieCallPeriodSec,
                ZombieCallRadiusLimit = h2024.ZombieCallRadiusLimit,
                InfectedLookCoeff = h2024.InfectedLookCoeff,
                MinInfectionPercentage = h2024.MinInfectionPercentage,
                // Snapshot spawn weights per param
                SpawnWeights = h2024.CrowdAttackSpawnParams?
                    .Select(p => new SpawnParamSnapshot
                    {
                        Role = p.Role,
                        Difficulty = p.Difficulty,
                        Weight = p.Weight
                    }).ToList()
            };
        }
    }

    private void SnapshotBotTypes()
    {
        var bots = databaseService.GetBots();
        string[] zombieTypes = ["infectedAssault", "infectedPmc", "infectedCivil",
            "infectedLaborant", "infectedTagilla", "cursedAssault"];

        foreach (var typeName in zombieTypes)
        {
            if (!bots.Types.TryGetValue(typeName, out var botType) || botType == null) continue;

            // Snapshot AI difficulty values
            _origBotAI[typeName] = new Dictionary<string, AISnapshot>();
            foreach (var (diffName, diff) in botType.BotDifficulty)
            {
                _origBotAI[typeName][diffName] = new AISnapshot
                {
                    VisibleDistance = diff.Core?.VisibleDistance,
                    VisibleAngle = diff.Core?.VisibleAngle,
                    HearingSense = diff.Core?.HearingSense,
                    ScatteringPerMeter = diff.Core?.ScatteringPerMeter,
                    ChanceToHearSimpleSound01 = diff.Hearing?.ChanceToHearSimpleSound01,
                    BaseRotateSpeed = diff.Move?.BaseRotateSpeed
                };
            }

            // Snapshot health — first BodyPart entry
            var bp = botType.BotHealth?.BodyParts?.FirstOrDefault();
            if (bp != null)
            {
                _origBotHealth[typeName] = new HealthSnapshot
                {
                    HeadMin = bp.Head?.Min ?? 0, HeadMax = bp.Head?.Max ?? 0,
                    ChestMin = bp.Chest?.Min ?? 0, ChestMax = bp.Chest?.Max ?? 0,
                    StomachMin = bp.Stomach?.Min ?? 0, StomachMax = bp.Stomach?.Max ?? 0,
                    LeftArmMin = bp.LeftArm?.Min ?? 0, LeftArmMax = bp.LeftArm?.Max ?? 0,
                    RightArmMin = bp.RightArm?.Min ?? 0, RightArmMax = bp.RightArm?.Max ?? 0,
                    LeftLegMin = bp.LeftLeg?.Min ?? 0, LeftLegMax = bp.LeftLeg?.Max ?? 0,
                    RightLegMin = bp.RightLeg?.Min ?? 0, RightLegMax = bp.RightLeg?.Max ?? 0
                };
            }
        }
    }

    private void SnapshotSpawnControl()
    {
        // BotConfig MaxBotCap
        var botConfig = configServer.GetConfig<BotConfig>();
        if (botConfig.MaxBotCap != null)
            _origMaxBotCap = new Dictionary<string, int>(botConfig.MaxBotCap);

        // Per-location MaxBotPerZone + BossLocationSpawn count + CrowdAttackSpawnParams count
        foreach (var folder in LocationFolders)
        {
            var loc = databaseService.GetLocation(folder);
            if (loc?.Base == null) continue;

            _origMaxBotPerZone[folder] = loc.Base.MaxBotPerZone ?? 4;
            _origBossSpawnCount[folder] = loc.Base.BossLocationSpawn?.Count() ?? 0;

            var h2024 = loc.Base.Events?.Halloween2024;
            if (h2024?.CrowdAttackSpawnParams != null)
                _origCrowdParamCount[folder] = h2024.CrowdAttackSpawnParams.Count();
        }
    }

    // ═══════════════════════════════════════════════════════
    // RESTORE
    // ═══════════════════════════════════════════════════════

    private void Restore()
    {
        if (!_snapshotTaken) return;

        // Restore seasonal event config
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

        // Restore globals
        RestoreGlobals();

        // Restore location events
        RestoreLocationEvents();

        // Restore bot AI + health
        RestoreBotTypes();

        // Restore spawn control
        RestoreSpawnControl();
    }

    private void RestoreGlobals()
    {
        var globals = databaseService.GetGlobals();

        // LocationInfection: Dictionary<string, int>
        if (_origLocationInfection != null && globals.LocationInfection != null)
        {
            foreach (var (key, val) in _origLocationInfection)
                globals.LocationInfection[key] = val;
        }

        // SeasonActivity.InfectionHalloween
        var ih = globals.Configuration?.SeasonActivity?.InfectionHalloween;
        if (ih != null)
        {
            ih.Enabled = _origInfectionEnabled;
            ih.DisplayUIEnabled = _origInfectionDisplayUI;
            ih.ZombieBleedMul = _origZombieBleedMul;
        }

        // ZombieInfection effect
        var zi = globals.Configuration?.Health?.Effects?.ZombieInfection;
        if (zi != null)
        {
            zi.Dehydration = _origDehydration;
            zi.HearingDebuffPercentage = _origHearingDebuff;
        }

        // SavagePlayCooldown: int
        if (globals.Configuration != null)
            globals.Configuration.SavagePlayCooldown = _origSavagePlayCooldown;
    }

    private void RestoreLocationEvents()
    {
        foreach (var (folder, snapshot) in _origLocationEvents)
        {
            var loc = databaseService.GetLocation(folder);
            var h2024 = loc?.Base?.Events?.Halloween2024;
            if (h2024 == null) continue;

            h2024.ZombieMultiplier = snapshot.ZombieMultiplier;
            h2024.CrowdsLimit = snapshot.CrowdsLimit;
            h2024.MaxCrowdAttackSpawnLimit = snapshot.MaxCrowdAttackSpawnLimit;
            h2024.CrowdCooldownPerPlayerSec = snapshot.CrowdCooldownPerPlayerSec;
            h2024.CrowdAttackBlockRadius = snapshot.CrowdAttackBlockRadius;
            h2024.MinSpawnDistToPlayer = snapshot.MinSpawnDistToPlayer;
            h2024.TargetPointSearchRadiusLimit = snapshot.TargetPointSearchRadiusLimit;
            h2024.ZombieCallDeltaRadius = snapshot.ZombieCallDeltaRadius;
            h2024.ZombieCallPeriodSec = snapshot.ZombieCallPeriodSec;
            h2024.ZombieCallRadiusLimit = snapshot.ZombieCallRadiusLimit;
            h2024.InfectedLookCoeff = snapshot.InfectedLookCoeff;
            h2024.MinInfectionPercentage = snapshot.MinInfectionPercentage;

            // Restore spawn weights
            if (snapshot.SpawnWeights != null && h2024.CrowdAttackSpawnParams != null)
            {
                var paramList = h2024.CrowdAttackSpawnParams.ToList();
                for (var i = 0; i < paramList.Count && i < snapshot.SpawnWeights.Count; i++)
                    paramList[i].Weight = snapshot.SpawnWeights[i].Weight;
            }
        }
    }

    private void RestoreBotTypes()
    {
        var bots = databaseService.GetBots();

        // Restore AI difficulty values
        foreach (var (typeName, diffs) in _origBotAI)
        {
            if (!bots.Types.TryGetValue(typeName, out var botType) || botType == null) continue;
            foreach (var (diffName, snap) in diffs)
            {
                if (!botType.BotDifficulty.TryGetValue(diffName, out var diff)) continue;
                if (diff.Core != null)
                {
                    diff.Core.VisibleDistance = snap.VisibleDistance;
                    diff.Core.VisibleAngle = snap.VisibleAngle;
                    diff.Core.HearingSense = snap.HearingSense;
                    diff.Core.ScatteringPerMeter = snap.ScatteringPerMeter;
                }
                if (diff.Hearing != null)
                    diff.Hearing.ChanceToHearSimpleSound01 = snap.ChanceToHearSimpleSound01;
                if (diff.Move != null)
                    diff.Move.BaseRotateSpeed = snap.BaseRotateSpeed;
            }
        }

        // Restore health
        foreach (var (typeName, snap) in _origBotHealth)
        {
            if (!bots.Types.TryGetValue(typeName, out var botType) || botType == null) continue;
            var bp = botType.BotHealth?.BodyParts?.FirstOrDefault();
            if (bp == null) continue;

            SetBodyPartHp(bp.Head, snap.HeadMin, snap.HeadMax);
            SetBodyPartHp(bp.Chest, snap.ChestMin, snap.ChestMax);
            SetBodyPartHp(bp.Stomach, snap.StomachMin, snap.StomachMax);
            SetBodyPartHp(bp.LeftArm, snap.LeftArmMin, snap.LeftArmMax);
            SetBodyPartHp(bp.RightArm, snap.RightArmMin, snap.RightArmMax);
            SetBodyPartHp(bp.LeftLeg, snap.LeftLegMin, snap.LeftLegMax);
            SetBodyPartHp(bp.RightLeg, snap.RightLegMin, snap.RightLegMax);
        }
    }

    private void RestoreSpawnControl()
    {
        // Restore BotConfig MaxBotCap
        if (_origMaxBotCap != null)
        {
            var botConfig = configServer.GetConfig<BotConfig>();
            foreach (var (key, val) in _origMaxBotCap)
                botConfig.MaxBotCap[key] = val;
        }

        // Restore per-location MaxBotPerZone + remove injected BossLocationSpawn entries + CrowdAttackSpawnParams
        foreach (var folder in LocationFolders)
        {
            var loc = databaseService.GetLocation(folder);
            if (loc?.Base == null) continue;

            if (_origMaxBotPerZone.TryGetValue(folder, out var origZone))
                loc.Base.MaxBotPerZone = origZone;

            // Trim BossLocationSpawn back to original count (removes extra waves we added)
            if (_origBossSpawnCount.TryGetValue(folder, out var origCount) && loc.Base.BossLocationSpawn != null)
            {
                var current = loc.Base.BossLocationSpawn.ToList();
                if (current.Count > origCount)
                    loc.Base.BossLocationSpawn = current.Take(origCount).ToList();
            }

            // Trim CrowdAttackSpawnParams back to original count (removes boss zombie injections)
            var h2024 = loc.Base.Events?.Halloween2024;
            if (h2024?.CrowdAttackSpawnParams != null && _origCrowdParamCount.TryGetValue(folder, out var origCrowdCount))
            {
                var currentParams = h2024.CrowdAttackSpawnParams.ToList();
                if (currentParams.Count > origCrowdCount)
                    h2024.CrowdAttackSpawnParams = currentParams.Take(origCrowdCount).ToList();
            }
        }
    }

    private static void SetBodyPartHp(MinMax<double>? part, double min, double max)
    {
        if (part == null) return;
        part.Min = min;
        part.Max = max;
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

        // Step 6: Zombie AI — vision, hearing, aggression, speed
        ApplyZombieAI(config);

        // Step 7: Zombie Health — body part HP per type
        ApplyZombieHealth(config);

        // Step 8: Spawn Control — bot caps, boss injection, extra waves
        ApplySpawnControl(config);

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
            logger.Info($"  mapInfectionAmount: {System.Text.Json.JsonSerializer.Serialize(zs.MapInfectionAmount)}");
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

        // LocationInfection — visual display on map select screen (Dictionary<string, int>)
        if (globals.LocationInfection != null)
        {
            foreach (var (friendlyName, key) in LocationInfectionKeys)
                globals.LocationInfection[key] = config.Maps.GetInfection(friendlyName);
        }

        // SeasonActivity.InfectionHalloween — client-side UI + bleed multiplier
        var ih = globals.Configuration?.SeasonActivity?.InfectionHalloween;
        if (ih != null)
        {
            ih.Enabled = config.InfectionEffects.Enabled;
            ih.DisplayUIEnabled = config.InfectionEffects.DisplayUI;
            ih.ZombieBleedMul = config.InfectionEffects.ZombieBleedMultiplier;
        }

        // ZombieInfection effect — dehydration and hearing debuff
        var zi = globals.Configuration?.Health?.Effects?.ZombieInfection;
        if (zi != null)
        {
            zi.Dehydration = config.InfectionEffects.DehydrationRate;
            zi.HearingDebuffPercentage = config.InfectionEffects.HearingDebuffPercentage;
        }

        if (config.Debug)
            logger.Info("[ZSlayerZombies] Globals configured (LocationInfection, InfectionHalloween, ZombieInfection effect)");
    }

    // ── Step 3: Location Events (crowd attack params) ──

    private void ApplyLocationEvents(ZombieConfig config)
    {
        foreach (var folder in LocationFolders)
        {
            var loc = databaseService.GetLocation(folder);
            var h2024 = loc?.Base?.Events?.Halloween2024;
            if (h2024 == null) continue;

            var friendlyName = FolderToFriendly.GetValueOrDefault(folder, "");
            if (string.IsNullOrEmpty(friendlyName)) continue;

            // Determine effective crowd params (global + per-map override)
            var zs = config.ZombieSettings;
            var adv = config.AdvancedMaps.GetValueOrDefault(friendlyName);

            // Apply crowd attack params (SPT types are double?/int? — cast from config int values)
            h2024.ZombieMultiplier = (double?)(adv?.ZombieMultiplier ?? zs.ZombieMultiplier);
            h2024.CrowdsLimit = adv?.CrowdsLimit ?? zs.CrowdsLimit;
            h2024.MaxCrowdAttackSpawnLimit = adv?.MaxCrowdAttackSpawnLimit ?? zs.MaxCrowdAttackSpawnLimit;
            h2024.CrowdCooldownPerPlayerSec = (double?)(adv?.CrowdCooldownPerPlayerSec ?? zs.CrowdCooldownPerPlayerSec);
            h2024.CrowdAttackBlockRadius = (double?)(adv?.CrowdAttackBlockRadius ?? zs.CrowdAttackBlockRadius);
            h2024.MinSpawnDistToPlayer = (double?)(adv?.MinSpawnDistToPlayer ?? zs.MinSpawnDistToPlayer);
            h2024.TargetPointSearchRadiusLimit = (double?)(adv?.TargetPointSearchRadiusLimit ?? zs.TargetPointSearchRadiusLimit);
            h2024.ZombieCallDeltaRadius = (double?)(adv?.ZombieCallDeltaRadius ?? zs.ZombieCallDeltaRadius);
            h2024.ZombieCallPeriodSec = (double?)(adv?.ZombieCallPeriodSec ?? zs.ZombieCallPeriodSec);
            h2024.ZombieCallRadiusLimit = (double?)(adv?.ZombieCallRadiusLimit ?? zs.ZombieCallRadiusLimit);
            h2024.InfectedLookCoeff = adv?.InfectedLookCoeff ?? zs.InfectedLookCoeff;
            h2024.MinInfectionPercentage = (double?)(adv?.MinInfectionPercentage ?? zs.MinInfectionPercentage);

            // Apply spawn weights to CrowdAttackSpawnParams
            if (h2024.CrowdAttackSpawnParams != null)
            {
                foreach (var param in h2024.CrowdAttackSpawnParams)
                {
                    var role = param.Role ?? "";
                    var difficulty = param.Difficulty ?? "";

                    if (config.SpawnWeights.TryGetValue(role, out var weights))
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

        if (config.Debug)
            logger.Info("[ZSlayerZombies] Location Halloween2024 events configured for all maps");
    }

    // ── Step 4: Raid Settings ──

    private void ApplyRaidSettings(ZombieConfig config)
    {
        if (!config.RaidSettings.ExtendRaidTime && Math.Abs(config.RaidSettings.ScavCooldownMultiplier - 1.0) < 0.001)
            return;

        var globals = databaseService.GetGlobals();

        // Scav cooldown: SavagePlayCooldown is int
        if (Math.Abs(config.RaidSettings.ScavCooldownMultiplier - 1.0) > 0.001 && globals.Configuration != null)
        {
            var newCooldown = (int)(_origSavagePlayCooldown * config.RaidSettings.ScavCooldownMultiplier);
            globals.Configuration.SavagePlayCooldown = newCooldown;
            if (config.Debug)
                logger.Info($"[ZSlayerZombies] Scav cooldown: {_origSavagePlayCooldown} → {newCooldown}");
        }

        // Raid time extension: EscapeTimeLimit is double?
        if (config.RaidSettings.ExtendRaidTime)
        {
            foreach (var folder in LocationFolders)
            {
                var loc = databaseService.GetLocation(folder);
                var locBase = loc?.Base;
                if (locBase?.EscapeTimeLimit == null) continue;

                locBase.EscapeTimeLimit = locBase.EscapeTimeLimit.Value * config.RaidSettings.RaidTimeMultiplier;
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
            var locBase = loc?.Base;
            if (locBase == null) continue;

            var friendlyName = FolderToFriendly.GetValueOrDefault(folder, "");
            var advLoot = config.AdvancedMaps.GetValueOrDefault(friendlyName)?.LootModifiers;
            var loot = advLoot ?? config.LootModifiers;

            // GlobalLootChanceModifier is double?
            locBase.GlobalLootChanceModifier = loot.GlobalLootMultiplier;
        }

        if (config.Debug)
            logger.Info($"[ZSlayerZombies] Loot modifiers applied (global: {config.LootModifiers.GlobalLootMultiplier}x)");
    }

    // ── Step 6: Zombie AI ──

    private void ApplyZombieAI(ZombieConfig config)
    {
        if (!config.ZombieAI.Enabled) return;

        var bots = databaseService.GetBots();
        var ai = config.ZombieAI;

        foreach (var typeName in ai.AffectedTypes)
        {
            if (!bots.Types.TryGetValue(typeName, out var botType) || botType == null) continue;

            foreach (var (diffName, diff) in botType.BotDifficulty)
            {
                // Core — vision and hearing sense
                if (diff.Core != null)
                {
                    diff.Core.VisibleDistance = ai.SightRange.Get(diffName);
                    diff.Core.VisibleAngle = ai.FieldOfView.Get(diffName);
                    diff.Core.HearingSense = ai.HearingSensitivity.Get(diffName);
                    diff.Core.ScatteringPerMeter = ai.ScatteringPerMeter.Get(diffName);
                }

                // Hearing — chance to detect sounds
                if (diff.Hearing != null)
                    diff.Hearing.ChanceToHearSimpleSound01 = ai.HearingChance.Get(diffName);

                // Move — rotation speed
                if (diff.Move != null)
                    diff.Move.BaseRotateSpeed = ai.RotateSpeed;
            }
        }

        if (config.Debug)
            logger.Info($"[ZSlayerZombies] Zombie AI configured for {ai.AffectedTypes.Count} bot types");
    }

    // ── Step 7: Zombie Health ──

    private void ApplyZombieHealth(ZombieConfig config)
    {
        if (!config.ZombieHealth.Enabled) return;

        var bots = databaseService.GetBots();
        var health = config.ZombieHealth;

        // Standard zombie types
        foreach (var typeName in health.StandardTypes)
        {
            if (!bots.Types.TryGetValue(typeName, out var botType) || botType == null) continue;
            ApplyHealthToBot(botType, health.Standard);
        }

        // Tagilla
        if (bots.Types.TryGetValue("infectedTagilla", out var tagilla) && tagilla != null)
            ApplyHealthToBot(tagilla, health.Tagilla);

        // Cursed assault
        if (bots.Types.TryGetValue("cursedAssault", out var cursed) && cursed != null)
            ApplyHealthToBot(cursed, health.CursedAssault);

        if (config.Debug)
            logger.Info($"[ZSlayerZombies] Zombie health configured (standard: {health.Standard.Head}/{health.Standard.Chest} HP)");
    }

    private static void ApplyHealthToBot(BotType botType, BodyPartHealthConfig hp)
    {
        var bp = botType.BotHealth?.BodyParts?.FirstOrDefault();
        if (bp == null) return;

        SetBodyPartHp(bp.Head, hp.Head, hp.Head);
        SetBodyPartHp(bp.Chest, hp.Chest, hp.Chest);
        SetBodyPartHp(bp.Stomach, hp.Stomach, hp.Stomach);
        SetBodyPartHp(bp.LeftArm, hp.Arms, hp.Arms);
        SetBodyPartHp(bp.RightArm, hp.Arms, hp.Arms);
        SetBodyPartHp(bp.LeftLeg, hp.Legs, hp.Legs);
        SetBodyPartHp(bp.RightLeg, hp.Legs, hp.Legs);
    }

    // ── Step 8: Spawn Control ──

    private void ApplySpawnControl(ZombieConfig config)
    {
        var sc = config.SpawnControl;

        // 8a: Override bot caps per map (skip if ABPS is managing bot caps)
        if (sc.OverrideBotCaps && !IsAbpsInstalled())
        {
            var botConfig = configServer.GetConfig<BotConfig>();
            foreach (var (friendlyName, cap) in sc.MaxBotCap)
            {
                // Map friendly names to botConfig keys (lowercase folder names)
                if (BossDisableKeys.TryGetValue(friendlyName, out var folders))
                {
                    foreach (var folder in folders)
                    {
                        if (botConfig.MaxBotCap.ContainsKey(folder))
                            botConfig.MaxBotCap[folder] = cap;
                    }
                }
            }

            // Override MaxBotPerZone per location
            foreach (var folder in LocationFolders)
            {
                var loc = databaseService.GetLocation(folder);
                if (loc?.Base == null) continue;

                var friendly = FolderToFriendly.GetValueOrDefault(folder, "");
                var mapOverride = sc.MapWaveOverrides.GetValueOrDefault(friendly);
                loc.Base.MaxBotPerZone = mapOverride?.MaxBotsPerZone ?? sc.MaxBotsPerZone;
            }

            if (config.Debug)
                logger.Info("[ZSlayerZombies] Bot caps overridden");
        }
        else if (sc.OverrideBotCaps && IsAbpsInstalled())
        {
            logger.Info("[ZSlayerZombies] Bot cap overrides skipped — acidphantasm-botplacementsystem is managing bot caps");
        }

        // 8b: Inject boss zombies into CrowdAttackSpawnParams
        if (sc.InjectBossZombies)
            InjectBossZombieCrowdParams(config);

        // 8c: Add extra zombie-only BossLocationSpawn waves
        if (sc.EnableExtraWaves)
            InjectExtraZombieWaves(config);
    }

    private void InjectBossZombieCrowdParams(ZombieConfig config)
    {
        foreach (var (bossType, bossCfg) in config.BossZombies)
        {
            if (!bossCfg.Enabled) continue;

            // Get spawn weights for this boss type
            config.SpawnWeights.TryGetValue(bossType, out var weights);
            var defaultWeight = bossType == "infectedTagilla" ? 5 : 3;

            foreach (var folder in LocationFolders)
            {
                var friendly = FolderToFriendly.GetValueOrDefault(folder, "");
                if (string.IsNullOrEmpty(friendly)) continue;

                // Check if this boss is allowed on this map
                if (!bossCfg.Maps.Contains("all") && !bossCfg.Maps.Contains(folder)) continue;

                var loc = databaseService.GetLocation(folder);
                var h2024 = loc?.Base?.Events?.Halloween2024;
                if (h2024 == null) continue;

                var spawnParams = h2024.CrowdAttackSpawnParams?.ToList() ?? [];

                // Only add if not already present for this role
                if (spawnParams.Any(p => p.Role == bossType)) continue;

                // Add entries for each difficulty
                if (weights != null && weights.Easy > 0)
                    spawnParams.Add(new CrowdAttackSpawnParam { Role = bossType, Difficulty = "easy", Weight = weights.Easy });
                if (weights != null && weights.Normal > 0)
                    spawnParams.Add(new CrowdAttackSpawnParam { Role = bossType, Difficulty = "normal", Weight = weights.Normal });
                else
                    spawnParams.Add(new CrowdAttackSpawnParam { Role = bossType, Difficulty = "normal", Weight = defaultWeight });
                if (weights != null && weights.Hard > 0)
                    spawnParams.Add(new CrowdAttackSpawnParam { Role = bossType, Difficulty = "hard", Weight = weights.Hard });

                h2024.CrowdAttackSpawnParams = spawnParams;
            }
        }

        if (config.Debug)
        {
            var injected = config.BossZombies.Where(kv => kv.Value.Enabled).Select(kv => kv.Key);
            logger.Info($"[ZSlayerZombies] Boss zombies injected: {string.Join(", ", injected)}");
        }
    }

    private void InjectExtraZombieWaves(ZombieConfig config)
    {
        var sc = config.SpawnControl;

        foreach (var folder in LocationFolders)
        {
            var loc = databaseService.GetLocation(folder);
            if (loc?.Base == null) continue;

            var friendly = FolderToFriendly.GetValueOrDefault(folder, "");
            var mapOverride = sc.MapWaveOverrides.GetValueOrDefault(friendly);
            var waveCount = mapOverride?.ExtraWaves ?? sc.ExtraWavesPerMap;
            var perWave = mapOverride?.ZombiesPerWave ?? sc.ZombiesPerWave;
            var chance = mapOverride?.WaveSpawnChance ?? sc.WaveSpawnChance;

            var spawns = loc.Base.BossLocationSpawn?.ToList() ?? [];

            for (var i = 0; i < waveCount; i++)
            {
                var escortCount = Math.Max(0, perWave - 1);
                spawns.Add(new BossLocationSpawn
                {
                    BossName = "infectedAssault",
                    BossChance = chance,
                    BossDifficulty = sc.WaveDifficulty,
                    BossEscortAmount = escortCount.ToString(),
                    BossEscortDifficulty = sc.WaveDifficulty,
                    BossEscortType = "infectedAssault",
                    BossZone = "",
                    ForceSpawn = sc.ForceSpawn,
                    IgnoreMaxBots = sc.IgnoreMaxBots,
                    Time = 9999,
                    TriggerName = "botEvent",
                    TriggerId = $"InfectedSpawn{(i + 1) * 10}",
                    SpawnMode = ["regular", "pve"],
                    SptId = $"zslayer_extra_wave_{i}"
                });
            }

            loc.Base.BossLocationSpawn = spawns;
        }

        if (config.Debug)
            logger.Info($"[ZSlayerZombies] Extra zombie waves: {sc.ExtraWavesPerMap} per map, {sc.ZombiesPerWave} per wave");
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
        if (config.ZombieAI.Enabled) features.Add("Custom AI");
        if (config.ZombieHealth.Enabled) features.Add("Custom HP");
        if (config.SpawnControl.InjectBossZombies) features.Add("Boss Zombies");
        if (config.SpawnControl.EnableExtraWaves) features.Add("Extra Waves");
        if (config.SpawnControl.OverrideBotCaps) features.Add("Bot Caps");
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

    /// <summary>Snapshot of a location's Halloween2024 crowd params for restore.</summary>
    private record Halloween2024Snapshot
    {
        public double? ZombieMultiplier { get; init; }
        public int? CrowdsLimit { get; init; }
        public int? MaxCrowdAttackSpawnLimit { get; init; }
        public double? CrowdCooldownPerPlayerSec { get; init; }
        public double? CrowdAttackBlockRadius { get; init; }
        public double? MinSpawnDistToPlayer { get; init; }
        public double? TargetPointSearchRadiusLimit { get; init; }
        public double? ZombieCallDeltaRadius { get; init; }
        public double? ZombieCallPeriodSec { get; init; }
        public double? ZombieCallRadiusLimit { get; init; }
        public double? InfectedLookCoeff { get; init; }
        public double? MinInfectionPercentage { get; init; }
        public List<SpawnParamSnapshot>? SpawnWeights { get; init; }
    }

    private record SpawnParamSnapshot
    {
        public string? Role { get; init; }
        public string? Difficulty { get; init; }
        public int? Weight { get; init; }
    }

    /// <summary>Snapshot of bot AI difficulty values for restore.</summary>
    private record AISnapshot
    {
        public float? VisibleDistance { get; init; }
        public float? VisibleAngle { get; init; }
        public float? HearingSense { get; init; }
        public float? ScatteringPerMeter { get; init; }
        public float? ChanceToHearSimpleSound01 { get; init; }
        public float? BaseRotateSpeed { get; init; }
    }

    /// <summary>Snapshot of bot health body part values for restore.</summary>
    private record HealthSnapshot
    {
        public double HeadMin { get; init; }
        public double HeadMax { get; init; }
        public double ChestMin { get; init; }
        public double ChestMax { get; init; }
        public double StomachMin { get; init; }
        public double StomachMax { get; init; }
        public double LeftArmMin { get; init; }
        public double LeftArmMax { get; init; }
        public double RightArmMin { get; init; }
        public double RightArmMax { get; init; }
        public double LeftLegMin { get; init; }
        public double LeftLegMax { get; init; }
        public double RightLegMin { get; init; }
        public double RightLegMax { get; init; }
    }
}

// ═══════════════════════════════════════════════════════
// STATUS DTO
// ═══════════════════════════════════════════════════════

public class ZombieStatusDto
{
    [JsonPropertyName("enabled")]
    public bool Enabled { get; set; }

    [JsonPropertyName("version")]
    public string Version { get; set; } = "";

    [JsonPropertyName("activeMaps")]
    public List<string> ActiveMaps { get; set; } = [];

    [JsonPropertyName("mapInfection")]
    public Dictionary<string, int> MapInfection { get; set; } = new();

    [JsonPropertyName("bossZombiesActive")]
    public List<string> BossZombiesActive { get; set; } = [];

    [JsonPropertyName("nightModeEnabled")]
    public bool NightModeEnabled { get; set; }

    [JsonPropertyName("waveEscalationEnabled")]
    public bool WaveEscalationEnabled { get; set; }

    [JsonPropertyName("lootModifiersEnabled")]
    public bool LootModifiersEnabled { get; set; }

    [JsonPropertyName("difficultyScalingEnabled")]
    public bool DifficultyScalingEnabled { get; set; }
}
