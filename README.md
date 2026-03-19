# ZSlayer Zombies — Server Mod

![Version](https://img.shields.io/badge/Version-v1.0.0-c8aa6e?style=flat-square)
![SPT](https://img.shields.io/badge/SPT-~4.0.x-blue?style=flat-square)
![.NET](https://img.shields.io/badge/.NET-9.0-purple?style=flat-square)
![Command Center](https://img.shields.io/badge/Command_Center-Optional-green?style=flat-square)

The server-side engine behind ZSlayer's zombie overhaul for SPT 4.0 / FIKA. This mod takes control of SPT's seasonal event system and rewires it into a fully configurable zombie apocalypse — per-map infection rates, custom AI tuning, health pools, spawn control, boss zombies, wave escalation, and a live HTTP API for real-time adjustments.

**This is the server mod.** It runs on the SPT server only. For client-side zombie behavior (BigBrain archetypes, horde coordination, custom audio), see the companion plugin: [ZSlayer SPT Zombies (Client)](https://github.com/ZSlayerHQ/ZSlayerZombieClient).

---

## What It Does

### The Core Problem

SPT's zombie system is tied to the Halloween seasonal event. If the event isn't active, no zombies spawn. If it is active, every map gets the same flat infection rate with no tuning. The vanilla system has no per-map control, no AI customization, no health scaling, and no way to adjust settings without editing database JSON and restarting the server.

### The Solution

ZSlayer Zombies forces the Halloween zombie event active year-round and takes over every parameter:

1. **Activates zombie spawning** — Sets `ActiveHalloweenZombiesEvent`, injects `EventType.Halloween` into globals, and writes `InfectionPercentage` on every location's `Halloween2024` event data. These are the three critical flags SPT's bot generator checks before spawning infected types.

2. **Injects zombie BossLocationSpawn entries** — SPT's `SeasonalEventService.ConfigureZombies()` only runs when the Halloween event passes date validation (Oct 28 - Nov 9). Outside that window, the ~28 zombie `BossLocationSpawn` entries per map (with `TriggerName: "botEvent"`) are never added, and no zombies spawn. This mod reads the spawn data directly from `seasonalevents.json` and injects it into every infected map's `BossLocationSpawn` array, making zombie spawning work year-round regardless of system date.

3. **Applies zombie hostility settings** — SPT's `ReplaceBotHostility("zombies")` is also date-gated. This mod reads the hostility rules from `seasonalevents.json` and applies them to all infected maps, ensuring zombies are hostile to all players and bots.

4. **Per-map infection rates** — Each map gets its own infection percentage (0-100%). Labs at 100% is a pure zombie map. Woods at 60% keeps you guessing. Ground Zero at 25% adds occasional tension without overwhelming new players.

5. **Snapshot-and-restore pattern** — Every database value the mod touches is snapshotted on first load. Before every apply cycle, all values are restored from snapshots, then fresh values are written. This prevents compounding errors (multiplying already-modified values) and allows clean reset to vanilla at any time.

6. **Live HTTP API** — All settings are adjustable at runtime via REST endpoints. No server restart needed.

---

## How Infection Works

When you set a map to 75% infection, the mod does the following:

1. **Seasonal event config** — Writes `mapInfectionAmount[mapKey] = 75` into the Halloween event settings. SPT's `SeasonalEventService` reads this to determine what percentage of bot spawns should be infected types.

2. **Location event data** — Sets `Halloween2024.InfectionPercentage = 75` on the location's database entry. SPT's bot generator reads this per-location value when deciding spawn types during a raid.

3. **Visual infection display** — Updates `globals.LocationInfection[mapKey] = 75` so the map selection screen shows the infection level.

4. **Spawn weight distribution** — The `CrowdAttackSpawnParams` on each location control which infected types spawn and at what ratio. Each entry has a role (e.g., `infectedAssault`), a difficulty tier (`easy`/`normal`/`hard`), and a weight. The mod overwrites these weights from config.

### What the Difficulty Tiers Mean

SPT's vanilla zombie brain uses difficulty to select behavior mode:

| Difficulty | Zombie Mode | Behavior |
|-----------|-------------|----------|
| `easy` | Slow | Shambling approach, zigzag pathing, melee at close range |
| `normal` | Fast | Sprint approach, zigzag, melee at close range |
| `hard` | Shooting | Uses equipped weapons at range, melee as fallback |

**Default spawn weights** (per `CrowdAttackSpawnParams` entry):

| Type | Easy | Normal | Hard | Result |
|------|------|--------|------|--------|
| infectedAssault | 30 | 110 | 40 | Most common — mix of shamble/sprint/shoot |
| infectedPmc | 15 | 55 | 20 | Geared PMC appearance, same zombie behavior |
| infectedCivil | 0 | 0 | 0 | Disabled by default |
| infectedLaborant | 0 | 0 | 0 | Disabled by default |

`infectedPmc` zombies spawn with full PMC gear and look like armed players. With `hard` difficulty, they will use those weapons. Set `infectedPmc.hard = 0` if you want pure melee zombies.

---

## Configuration Reference

All settings live in `user/mods/ZSlayerZombies/config/config.json`. The config supports JSONC (comments with `//` and `/* */`).

### Top-Level

| Field | Type | Default | Description |
|-------|------|---------|-------------|
| `enabled` | bool | `true` | Master switch — disabling restores all values to vanilla |
| `debug` | bool | `false` | Enable detailed spawn diagnostics in server log |

### Map Infection (`maps`)

Per-map infection percentage (0-100). Higher values = more zombie spawns replacing normal bots.

```json
{
  "Labs": 100,
  "Customs": 75,
  "Factory": 100,
  "Interchange": 50,
  "Lighthouse": 50,
  "Reserve": 60,
  "GroundZero": 25,
  "Shoreline": 50,
  "Streets": 50,
  "Woods": 60
}
```

### Disable Bosses (`disableBosses`)

Per-map boolean — prevents normal boss spawns on that map. Useful for maps where you want only zombies (Labs defaults to `true`).

### Zombie Settings (`zombieSettings`)

Core crowd attack and spawn behavior parameters:

| Field | Default | Description |
|-------|---------|-------------|
| `replaceBotHostility` | `true` | Zombies hostile to all humans (PMCs + scavs) |
| `enableSummoning` | `true` | Zombies attract nearby zombies when they spot a player |
| `removeLabsKeycard` | `true` | No keycard required to enter Labs |
| `disableNormalScavWaves` | `false` | Prevents normal scav waves entirely |
| `zombieMultiplier` | `5` | Spawn rate multiplier for crowd attacks |
| `crowdsLimit` | `2` | Max concurrent crowd attack waves |
| `maxCrowdAttackSpawnLimit` | `13` | Max zombies per crowd wave |
| `crowdCooldownPerPlayerSec` | `300` | Seconds between crowd attacks per player |
| `crowdAttackBlockRadius` | `100` | Min distance between crowd attack origins |
| `minSpawnDistToPlayer` | `40` | Min spawn distance from players |
| `targetPointSearchRadiusLimit` | `80` | Search radius for spawn points near target |
| `zombieCallDeltaRadius` | `20` | Alert propagation radius increment |
| `zombieCallPeriodSec` | `1` | How often zombies broadcast position |
| `zombieCallRadiusLimit` | `50` | Max alert propagation radius |
| `infectedLookCoeff` | `0.5` | Vision range modifier for infected bots |
| `minInfectionPercentage` | `0` | Floor value — never go below this infection % |

### Infection Effects (`infectionEffects`)

Player-facing effects when infected by zombie attacks:

| Field | Default | Description |
|-------|---------|-------------|
| `enabled` | `true` | Enable zombie infection effects on players |
| `displayUI` | `true` | Show infection status in player HUD |
| `zombieBleedMultiplier` | `10` | Bleed damage multiplier from zombie hits |
| `dehydrationRate` | `-0.84` | Hydration drain rate while infected |
| `hearingDebuffPercentage` | `0.2` | Hearing reduction while infected (0-1) |

### Spawn Weights (`spawnWeights`)

Per-type, per-difficulty spawn weights. Controls the ratio of zombie types in crowd attacks.

```json
{
  "infectedAssault": { "easy": 30, "normal": 110, "hard": 40 },
  "infectedPmc": { "easy": 15, "normal": 55, "hard": 20 },
  "infectedCivil": { "easy": 0, "normal": 0, "hard": 0 },
  "infectedLaborant": { "easy": 0, "normal": 0, "hard": 0 },
  "infectedTagilla": { "easy": 0, "normal": 0, "hard": 0 },
  "cursedAssault": { "easy": 0, "normal": 0, "hard": 0 }
}
```

Set all `hard` values to 0 to prevent zombies from using ranged weapons.

### Zombie AI (`zombieAI`)

Per-difficulty sensory and movement tuning applied to all infected bot types:

| Field | Easy | Normal | Hard | Description |
|-------|------|--------|------|-------------|
| `sightRange` | 110 | 120 | 130 | Detection range in meters |
| `fieldOfView` | 130 | 130 | 140 | Vision cone in degrees |
| `hearingSensitivity` | 1.05 | 1.85 | 2.85 | Sound detection multiplier |
| `hearingChance` | 0.45 | 0.65 | 0.70 | Probability of reacting to sounds |
| `aggressionChance` | 40 | 40 | 40 | Immediate attack chance on sight (%) |
| `reactionTime` | 1.0 | 0.75 | 0.5 | Seconds before engaging |
| `scatteringPerMeter` | 0.12 | 0.10 | 0.08 | Shot accuracy (higher = worse) |
| `rotateSpeed` | 270 | 270 | 270 | Turn speed (degrees/sec) |

### Zombie Health (`zombieHealth`)

Per-type body part HP values:

| Type | Head | Chest | Stomach | Arms | Legs |
|------|------|-------|---------|------|------|
| Standard (assault/pmc/civil/laborant) | 10 | 180 | 170 | 70 | 70 |
| infectedTagilla | 130 | 450 | 350 | 150 | 150 |
| cursedAssault | 35 | 200 | 150 | 60 | 60 |

Standard zombies are one-tappable to the head but spongy to body shots. Tagilla is a boss — bring AP rounds or aim for the head.

### Boss Zombies (`bossZombies`)

Inject special zombie types into crowd attack spawn pools:

```json
{
  "infectedTagilla": {
    "enabled": false,
    "spawnChance": 15,
    "maps": ["factory4", "laboratory"],
    "maxPerRaid": 1
  },
  "cursedAssault": {
    "enabled": false,
    "spawnChance": 10,
    "maps": ["all"],
    "maxPerRaid": 2
  }
}
```

### Spawn Control (`spawnControl`)

Advanced bot cap and wave management:

| Field | Default | Description |
|-------|---------|-------------|
| `overrideBotCaps` | `false` | Override per-map bot limits (skipped if ABPS detected) |
| `maxBotCap` | per-map | Bot cap per map (Labs: 25, Streets: 28, etc.) |
| `maxBotsPerZone` | `6` | Max bots in a single spawn zone |
| `injectBossZombies` | `true` | Add boss entries to crowd attack params |
| `enableExtraWaves` | `false` | Independent zombie wave spawns |
| `extraWavesPerMap` | `4` | Extra wave count when enabled |
| `zombiesPerWave` | `3` | Zombies per extra wave |

### Night Mode (`nightMode`)

| Field | Default | Description |
|-------|---------|-------------|
| `enabled` | `false` | Activate night mode |
| `nightInfectionMultiplier` | `1.5` | Infection rate boost at night |
| `forceNightWeather` | `false` | Force dark/foggy weather |
| `nightBossChanceMultiplier` | `2.0` | Boss zombie spawn chance boost |

### Wave Escalation (`waveEscalation`)

Infection ramps up during the raid:

| Field | Default | Description |
|-------|---------|-------------|
| `enabled` | `false` | Enable escalation |
| `startInfectionPercent` | `25` | Infection at raid start |
| `endInfectionPercent` | `150` | Infection at raid end |
| `escalationCurve` | `"linear"` | Curve shape |
| `hordeEventEnabled` | `false` | Periodic horde surge events |
| `hordeIntervalMinutes` | `10` | Minutes between surges |

### Difficulty Scaling (`difficultyScaling`)

Scale infection dynamically based on player count or level:

| Field | Default | Description |
|-------|---------|-------------|
| `enabled` | `false` | Enable scaling |
| `scalingMode` | `"playerCount"` | `"playerCount"` or `"level"` |

### Loot Modifiers (`lootModifiers`)

Adjust loot spawns on infected maps:

| Field | Default | Description |
|-------|---------|-------------|
| `globalLootMultiplier` | `1.0` | Overall loot amount |
| `medicalLootMultiplier` | `2.0` | Medical supplies (zombie raids need meds) |
| `ammoLootMultiplier` | `1.5` | Ammunition |
| `valuableLootMultiplier` | `0.5` | High-value items (zombies don't guard loot) |

### Advanced Per-Map Overrides (`advancedMaps`)

Override any crowd attack parameter on a per-map basis. Map-level values replace globals (not multiply).

```json
{
  "Labs": {
    "zombieMultiplier": 8,
    "maxCrowdAttackSpawnLimit": 20,
    "crowdCooldownPerPlayerSec": 180
  }
}
```

---

## HTTP API

All settings are live-adjustable via REST API at `/zslayer/zombies/`:

| Endpoint | Method | Description |
|----------|--------|-------------|
| `/zslayer/zombies/config` | `GET` | Get current config |
| `/zslayer/zombies/config` | `POST` | Update config and apply immediately |
| `/zslayer/zombies/status` | `GET` | Get active status (enabled maps, features) |
| `/zslayer/zombies/apply` | `POST` | Re-apply current config without changes |
| `/zslayer/zombies/reset` | `POST` | Reset to defaults (disables mod) |

### Example: Set Labs to 50% infection

```bash
curl -k https://127.0.0.1:6969/zslayer/zombies/config \
  -X POST -H "Content-Type: application/json" \
  -d '{"enabled":true,"maps":{"Labs":50}}'
```

Changes apply immediately — no server restart needed. The mod re-snapshots vanilla values on first load and restores them before every apply, so you can't corrupt the database by changing settings repeatedly.

---

## Mod Compatibility

### ABPS (acidphantasm-botplacementsystem)

Auto-detected at startup. When ABPS is installed, ZSlayer Zombies **skips bot cap overrides** to avoid conflicts — ABPS manages bot caps, we manage infection rates. Both work together.

### APBS (acidphantasm-progressivebotsystem)

APBS explicitly skips all infected bot types (classified as `EventBots` in its codebase). Gear generation for zombies uses vanilla SPT — APBS does not interfere.

### Phobos (Janky-Phobos)

Phobos registers BigBrain layers for PMC/Assault/Boss types only. No overlap with infected types. Safe to run alongside.

### SeasonRotator

ZSlayer Zombies loads at `PostSptModLoader + 2` — after SeasonRotator. Our event config overwrites anything SeasonRotator sets for the Halloween event.

### Command Center Integration

When [ZSlayer Command Center](https://github.com/ZSlayerHQ/ZSlayerCommandCenter) is installed, it auto-detects this mod and adds a dedicated **Zombies** tab to the admin panel with full browser-based control over every setting. Changes apply live through the HTTP API.

---

## Debug Diagnostics

Set `"debug": true` in config to get a comprehensive spawn state dump on every apply. The diagnostics log:

- **Critical flags** — `ActiveHalloweenZombiesEvent`, `EventType` list, Halloween presence
- **Seasonal event** — enabled state, date range, zombie settings
- **Per-location config** — InfectionPercentage, ZombieMultiplier, CrowdsLimit, CrowdAttackSpawnParams count
- **Spawn weight breakdown** — every type + difficulty with zombie mode mapping and percentage distribution
- **Per-type AI values** — Sight, FOV, Hearing, Scatter, Rotate per difficulty
- **Health per type** — Head/Chest/Stomach HP
- **Bot caps** — MaxBotCap per map
- **ABPS detection** — whether bot caps are managed externally

---

## Installation

### Requirements
- SPT ~4.0.x
- [ZSlayer SPT Zombies (Client Plugin)](https://github.com/ZSlayerHQ/ZSlayerZombieClient) — companion BepInEx plugin for client-side zombie behavior
- [ZSlayer Command Center](https://github.com/ZSlayerHQ/ZSlayerCommandCenter) (optional — browser-based config UI)

### Server Mod

Copy the `ZSlayerZombies/` folder to your SPT server:

```
SPT/user/mods/ZSlayerZombies/
├── ZSlayerZombies.dll
└── config/
    └── config.json
```

The mod activates on server startup. First launch generates `config.json` with defaults if it doesn't exist.

### Important: Server-Side Only

This mod runs **on the SPT server only**. It does not go on game clients. SPT pushes bot configuration data to clients automatically — the server mod controls *what* spawns, the client plugin controls *how* they behave.

---

## Architecture

```
ZSlayerZombies/
├── ModMetadata.cs           -- mod identity, version, GUID
├── ZSlayerZombiesMod.cs     -- entry point, config I/O, apply/reset
├── ZombieConfig.cs          -- full config model (15+ config sections)
├── ZombieService.cs         -- core engine (snapshot/restore/apply pipeline)
└── ZombieHttpListener.cs    -- REST API (5 endpoints)
```

### Apply Pipeline

`ZombieService.Apply()` runs a 9-step pipeline on every config change:

1. **Seasonal Event** — Force-enable Halloween event year-round (Jan 1 - Dec 31), set infection amounts per map
2. **Globals** — Set `ActiveHalloweenZombiesEvent`, inject `EventType.Halloween`, write `InfectionPercentage` per location, configure infection effects
3. **Zombie Spawn Injection** — Read zombie `BossLocationSpawn` entries from `seasonalevents.json` `eventBossSpawns.halloweenzombies` and inject into each infected map (~28 entries per map). Also apply zombie hostility rules from `hostilitySettingsForEvent.zombies` to make zombies hostile to all bots and vice versa. This replicates what SPT's `SeasonalEventService.ConfigureZombies()` does, but without requiring the Halloween event to pass date validation.
4. **Location Events** — Write crowd attack params (multiplier, limits, cooldowns, spawn weights) per location
5. **Raid Settings** — Raid time extension, scav cooldown multiplier
6. **Loot Modifiers** — Per-map global loot chance modifier
7. **Zombie AI** — Per-type, per-difficulty vision/hearing/movement/scatter overrides
8. **Zombie Health** — Per-type body part HP values
9. **Spawn Control** — Bot cap overrides, boss zombie injection, extra wave spawning

Every step follows the snapshot-and-restore pattern: restore original values from snapshot, then write fresh values from config. This guarantees idempotent applies — calling apply 100 times produces the same result as calling it once.

---

## Credits

- **Author:** ZSlayerHQ / Ben Cole
- **SPT Team** — for the modding platform and seasonal event system
- **acidphantasm** — compatibility testing with ABPS/APBS

---

## License

[CC BY-NC-SA 4.0](LICENSE) — Built by [ZSlayerHQ / Ben Cole](https://github.com/ZSlayerHQ)

---

*"Set the infection to 100% and disable bosses. Lock the doors. See how long you last."*
