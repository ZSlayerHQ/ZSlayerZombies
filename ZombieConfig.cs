using System.Text.Json.Serialization;

namespace ZSlayerZombies;

public class ZombieConfig
{
    [JsonPropertyName("enabled")]
    public bool Enabled { get; set; } = true;

    [JsonPropertyName("debug")]
    public bool Debug { get; set; } = false;

    [JsonPropertyName("maps")]
    public MapInfectionConfig Maps { get; set; } = new();

    [JsonPropertyName("disableBosses")]
    public MapBoolConfig DisableBosses { get; set; } = new();

    [JsonPropertyName("zombieSettings")]
    public ZombieBehaviourConfig ZombieSettings { get; set; } = new();

    [JsonPropertyName("infectionEffects")]
    public InfectionEffectsConfig InfectionEffects { get; set; } = new();

    [JsonPropertyName("spawnWeights")]
    public Dictionary<string, SpawnWeightEntry> SpawnWeights { get; set; } = new()
    {
        ["infectedAssault"] = new SpawnWeightEntry { Easy = 30, Normal = 110, Hard = 40 },
        ["infectedPmc"] = new SpawnWeightEntry { Easy = 15, Normal = 55, Hard = 20 },
        ["infectedCivil"] = new SpawnWeightEntry(),
        ["infectedLaborant"] = new SpawnWeightEntry(),
        ["infectedTagilla"] = new SpawnWeightEntry(),
        ["cursedAssault"] = new SpawnWeightEntry()
    };

    [JsonPropertyName("bossZombies")]
    public Dictionary<string, BossZombieConfig> BossZombies { get; set; } = new()
    {
        ["infectedTagilla"] = new BossZombieConfig
        {
            SpawnChance = 15,
            Maps = ["factory4", "laboratory"],
            MaxPerRaid = 1
        },
        ["cursedAssault"] = new BossZombieConfig
        {
            SpawnChance = 10,
            Maps = ["all"],
            MaxPerRaid = 2
        }
    };

    [JsonPropertyName("nightMode")]
    public NightModeConfig NightMode { get; set; } = new();

    [JsonPropertyName("raidSettings")]
    public RaidSettingsConfig RaidSettings { get; set; } = new();

    [JsonPropertyName("waveEscalation")]
    public WaveEscalationConfig WaveEscalation { get; set; } = new();

    [JsonPropertyName("lootModifiers")]
    public LootModifiersConfig LootModifiers { get; set; } = new();

    [JsonPropertyName("rewards")]
    public RewardsConfig Rewards { get; set; } = new();

    [JsonPropertyName("difficultyScaling")]
    public DifficultyScalingConfig DifficultyScaling { get; set; } = new();

    [JsonPropertyName("advancedMaps")]
    public Dictionary<string, AdvancedMapOverride> AdvancedMaps { get; set; } = new();
}

public class MapInfectionConfig
{
    [JsonPropertyName("Labs")] public int Labs { get; set; } = 100;
    [JsonPropertyName("Customs")] public int Customs { get; set; } = 75;
    [JsonPropertyName("Factory")] public int Factory { get; set; } = 100;
    [JsonPropertyName("Interchange")] public int Interchange { get; set; } = 50;
    [JsonPropertyName("Lighthouse")] public int Lighthouse { get; set; } = 50;
    [JsonPropertyName("Reserve")] public int Reserve { get; set; } = 60;
    [JsonPropertyName("GroundZero")] public int GroundZero { get; set; } = 25;
    [JsonPropertyName("Shoreline")] public int Shoreline { get; set; } = 50;
    [JsonPropertyName("Streets")] public int Streets { get; set; } = 50;
    [JsonPropertyName("Woods")] public int Woods { get; set; } = 60;

    /// <summary>Get infection % for a friendly map name.</summary>
    public int GetInfection(string friendlyName) => friendlyName switch
    {
        "Labs" => Labs,
        "Customs" => Customs,
        "Factory" => Factory,
        "Interchange" => Interchange,
        "Lighthouse" => Lighthouse,
        "Reserve" => Reserve,
        "GroundZero" => GroundZero,
        "Shoreline" => Shoreline,
        "Streets" => Streets,
        "Woods" => Woods,
        _ => 0
    };
}

public class MapBoolConfig
{
    [JsonPropertyName("Labs")] public bool Labs { get; set; } = true;
    [JsonPropertyName("Customs")] public bool Customs { get; set; }
    [JsonPropertyName("Factory")] public bool Factory { get; set; }
    [JsonPropertyName("Interchange")] public bool Interchange { get; set; }
    [JsonPropertyName("Lighthouse")] public bool Lighthouse { get; set; }
    [JsonPropertyName("Reserve")] public bool Reserve { get; set; }
    [JsonPropertyName("GroundZero")] public bool GroundZero { get; set; }
    [JsonPropertyName("Shoreline")] public bool Shoreline { get; set; }
    [JsonPropertyName("Streets")] public bool Streets { get; set; }
    [JsonPropertyName("Woods")] public bool Woods { get; set; }
}

public class ZombieBehaviourConfig
{
    [JsonPropertyName("replaceBotHostility")] public bool ReplaceBotHostility { get; set; } = true;
    [JsonPropertyName("enableSummoning")] public bool EnableSummoning { get; set; } = true;
    [JsonPropertyName("removeLabsKeycard")] public bool RemoveLabsKeycard { get; set; } = true;
    [JsonPropertyName("disableNormalScavWaves")] public bool DisableNormalScavWaves { get; set; }

    [JsonPropertyName("zombieMultiplier")] public int ZombieMultiplier { get; set; } = 5;
    [JsonPropertyName("crowdsLimit")] public int CrowdsLimit { get; set; } = 2;
    [JsonPropertyName("maxCrowdAttackSpawnLimit")] public int MaxCrowdAttackSpawnLimit { get; set; } = 13;
    [JsonPropertyName("crowdCooldownPerPlayerSec")] public int CrowdCooldownPerPlayerSec { get; set; } = 300;
    [JsonPropertyName("crowdAttackBlockRadius")] public int CrowdAttackBlockRadius { get; set; } = 100;
    [JsonPropertyName("minSpawnDistToPlayer")] public int MinSpawnDistToPlayer { get; set; } = 40;
    [JsonPropertyName("targetPointSearchRadiusLimit")] public int TargetPointSearchRadiusLimit { get; set; } = 80;
    [JsonPropertyName("zombieCallDeltaRadius")] public int ZombieCallDeltaRadius { get; set; } = 20;
    [JsonPropertyName("zombieCallPeriodSec")] public int ZombieCallPeriodSec { get; set; } = 1;
    [JsonPropertyName("zombieCallRadiusLimit")] public int ZombieCallRadiusLimit { get; set; } = 50;
    [JsonPropertyName("infectedLookCoeff")] public double InfectedLookCoeff { get; set; } = 0.5;
    [JsonPropertyName("minInfectionPercentage")] public int MinInfectionPercentage { get; set; }
}

public class InfectionEffectsConfig
{
    [JsonPropertyName("enabled")] public bool Enabled { get; set; } = true;
    [JsonPropertyName("displayUI")] public bool DisplayUI { get; set; } = true;
    [JsonPropertyName("zombieBleedMultiplier")] public double ZombieBleedMultiplier { get; set; } = 10;
    [JsonPropertyName("dehydrationRate")] public double DehydrationRate { get; set; } = -0.84;
    [JsonPropertyName("hearingDebuffPercentage")] public double HearingDebuffPercentage { get; set; } = 0.2;
}

public class SpawnWeightEntry
{
    [JsonPropertyName("easy")] public int Easy { get; set; }
    [JsonPropertyName("normal")] public int Normal { get; set; }
    [JsonPropertyName("hard")] public int Hard { get; set; }
}

public class BossZombieConfig
{
    [JsonPropertyName("enabled")] public bool Enabled { get; set; }
    [JsonPropertyName("spawnChance")] public int SpawnChance { get; set; } = 15;
    [JsonPropertyName("maps")] public List<string> Maps { get; set; } = [];
    [JsonPropertyName("maxPerRaid")] public int MaxPerRaid { get; set; } = 1;
}

public class NightModeConfig
{
    [JsonPropertyName("enabled")] public bool Enabled { get; set; }
    [JsonPropertyName("nightInfectionMultiplier")] public double NightInfectionMultiplier { get; set; } = 1.5;
    [JsonPropertyName("forceNightWeather")] public bool ForceNightWeather { get; set; }
    [JsonPropertyName("nightBossChanceMultiplier")] public double NightBossChanceMultiplier { get; set; } = 2.0;
}

public class RaidSettingsConfig
{
    [JsonPropertyName("extendRaidTime")] public bool ExtendRaidTime { get; set; }
    [JsonPropertyName("raidTimeMultiplier")] public double RaidTimeMultiplier { get; set; } = 1.5;
    [JsonPropertyName("scavCooldownMultiplier")] public double ScavCooldownMultiplier { get; set; } = 1.0;
    [JsonPropertyName("scavsAffected")] public bool ScavsAffected { get; set; } = true;
    [JsonPropertyName("forceWeather")] public string ForceWeather { get; set; } = "none";
    [JsonPropertyName("forceSeason")] public string ForceSeason { get; set; } = "none";
}

public class WaveEscalationConfig
{
    [JsonPropertyName("enabled")] public bool Enabled { get; set; }
    [JsonPropertyName("startInfectionPercent")] public int StartInfectionPercent { get; set; } = 25;
    [JsonPropertyName("endInfectionPercent")] public int EndInfectionPercent { get; set; } = 150;
    [JsonPropertyName("escalationCurve")] public string EscalationCurve { get; set; } = "linear";
    [JsonPropertyName("hordeEventEnabled")] public bool HordeEventEnabled { get; set; }
    [JsonPropertyName("hordeIntervalMinutes")] public int HordeIntervalMinutes { get; set; } = 10;
    [JsonPropertyName("hordeMultiplier")] public double HordeMultiplier { get; set; } = 3.0;
}

public class LootModifiersConfig
{
    [JsonPropertyName("enabled")] public bool Enabled { get; set; }
    [JsonPropertyName("globalLootMultiplier")] public double GlobalLootMultiplier { get; set; } = 1.0;
    [JsonPropertyName("medicalLootMultiplier")] public double MedicalLootMultiplier { get; set; } = 2.0;
    [JsonPropertyName("ammoLootMultiplier")] public double AmmoLootMultiplier { get; set; } = 1.5;
    [JsonPropertyName("valuableLootMultiplier")] public double ValuableLootMultiplier { get; set; } = 0.5;
    [JsonPropertyName("looseLootMultiplier")] public double LooseLootMultiplier { get; set; } = 1.0;
    [JsonPropertyName("containerLootMultiplier")] public double ContainerLootMultiplier { get; set; } = 1.0;
}

public class RewardsConfig
{
    [JsonPropertyName("zombieKillXpMultiplier")] public double ZombieKillXpMultiplier { get; set; } = 1.0;
    [JsonPropertyName("survivalBonusXp")] public int SurvivalBonusXp { get; set; }
    [JsonPropertyName("raidXpMultiplier")] public double RaidXpMultiplier { get; set; } = 1.0;
}

public class DifficultyScalingConfig
{
    [JsonPropertyName("enabled")] public bool Enabled { get; set; }
    [JsonPropertyName("scalingMode")] public string ScalingMode { get; set; } = "playerCount";
    [JsonPropertyName("playerCountInfectionScale")] public Dictionary<string, double> PlayerCountInfectionScale { get; set; } = new()
    {
        ["1"] = 1.0,
        ["2"] = 1.25,
        ["3"] = 1.5,
        ["4"] = 1.75,
        ["5"] = 2.0
    };
    [JsonPropertyName("levelScaling")] public LevelScalingConfig LevelScaling { get; set; } = new();
}

public class LevelScalingConfig
{
    [JsonPropertyName("minLevel")] public int MinLevel { get; set; } = 1;
    [JsonPropertyName("maxLevel")] public int MaxLevel { get; set; } = 60;
    [JsonPropertyName("minMultiplier")] public double MinMultiplier { get; set; } = 0.5;
    [JsonPropertyName("maxMultiplier")] public double MaxMultiplier { get; set; } = 1.5;
}

public class AdvancedMapOverride
{
    [JsonPropertyName("zombieMultiplier")] public int? ZombieMultiplier { get; set; }
    [JsonPropertyName("crowdsLimit")] public int? CrowdsLimit { get; set; }
    [JsonPropertyName("maxCrowdAttackSpawnLimit")] public int? MaxCrowdAttackSpawnLimit { get; set; }
    [JsonPropertyName("crowdCooldownPerPlayerSec")] public int? CrowdCooldownPerPlayerSec { get; set; }
    [JsonPropertyName("crowdAttackBlockRadius")] public int? CrowdAttackBlockRadius { get; set; }
    [JsonPropertyName("minSpawnDistToPlayer")] public int? MinSpawnDistToPlayer { get; set; }
    [JsonPropertyName("targetPointSearchRadiusLimit")] public int? TargetPointSearchRadiusLimit { get; set; }
    [JsonPropertyName("zombieCallDeltaRadius")] public int? ZombieCallDeltaRadius { get; set; }
    [JsonPropertyName("zombieCallPeriodSec")] public int? ZombieCallPeriodSec { get; set; }
    [JsonPropertyName("zombieCallRadiusLimit")] public int? ZombieCallRadiusLimit { get; set; }
    [JsonPropertyName("infectedLookCoeff")] public double? InfectedLookCoeff { get; set; }
    [JsonPropertyName("minInfectionPercentage")] public int? MinInfectionPercentage { get; set; }
    [JsonPropertyName("lootModifiers")] public LootModifiersConfig? LootModifiers { get; set; }
}
