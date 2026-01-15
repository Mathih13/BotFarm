# BotFarm

[![Build status](https://ci.appveyor.com/api/projects/status/7tdp1nrwatndex5r/branch/master?svg=true)](https://ci.appveyor.com/project/jackpoz/botfarm)
[![SonarCloud](https://sonarcloud.io/api/project_badges/measure?project=jackpoz_BotFarm&metric=alert_status)](https://sonarcloud.io/dashboard?id=jackpoz_BotFarm)

A bot farm application for spawning multiple automated World of Warcraft players that connect to TrinityCore-based private servers.

## Features

- **Pathfinding Movement** - Uses TrinityCore's navigation mesh for realistic bot movement
- **JSON Task Routes** - Configure bot behavior with simple JSON files
- **Class-Specific Combat AI** - Warriors, Priests, Paladins with ability rotations
- **Multi-Bot Coordination** - Bots coordinate to avoid targeting the same mobs
- **Auto Account Creation** - Automatically creates accounts via Remote Access
- **Quest Support** - Accept quests, kill mobs, collect items, turn in quests
- **Test Framework** - E2E testing with harness configuration and assertion tasks

## Quick Start

### 1. Prerequisites

- .NET 8.0 SDK
- TrinityCore server with Remote Access enabled
- Map data files (mmaps, vmaps, maps, dbc)

### 2. Build

```bash
dotnet build BotFarm.sln -c Release -p:Platform=x64
```

### 3. Setup Native Libraries

Copy these files to `BotFarm/lib/` from a [TrinityCore CLI build](https://github.com/jackpoz/TrinityCore/tree/CLI):
- `cli.dll`
- `Ijwhost.dll`
- `cli.runtimeconfig.json`

> **Note**: Build TrinityCore with CMake options `SERVERS=ON` and `TOOLS=ON`

### 4. Configure

Edit `BotFarm.dll.config` in the output folder:

```xml
<!-- Server connection -->
<setting name="Hostname" serializeAs="String">
    <value>127.0.0.1</value>
</setting>
<setting name="Port" serializeAs="String">
    <value>3724</value>
</setting>

<!-- Account with RA permissions for auto-creation -->
<setting name="Username" serializeAs="String">
    <value>admin</value>
</setting>
<setting name="Password" serializeAs="String">
    <value>admin</value>
</setting>

<!-- Bot count -->
<setting name="MinBotsCount" serializeAs="String">
    <value>1</value>
</setting>
<setting name="MaxBotsCount" serializeAs="String">
    <value>5</value>
</setting>

<!-- Data paths (from your TrinityCore server) -->
<setting name="MMAPsFolderPath" serializeAs="String">
    <value>C:\TrinityCore\mmaps</value>
</setting>
<setting name="VMAPsFolderPath" serializeAs="String">
    <value>C:\TrinityCore\vmaps</value>
</setting>
<setting name="MAPsFolderPath" serializeAs="String">
    <value>C:\TrinityCore\maps</value>
</setting>
<setting name="DBCsFolderPath" serializeAs="String">
    <value>C:\TrinityCore\dbc</value>
</setting>
```

### 5. Run

```bash
cd BotFarm/bin/x64/Release/net8.0

# Test mode (default) - no auto-spawn, use test commands
./BotFarm.exe

# Auto-spawn mode (legacy behavior)
./BotFarm.exe --auto
```

## Task System

Bots follow JSON-defined routes that specify a sequence of tasks. Routes are loaded from the `BotFarm/routes/` folder.

### Route Structure

```json
{
  "name": "Route Name",
  "description": "What this route does",
  "loop": true,
  "tasks": [
    { "type": "TaskType", "parameters": { ... } }
  ]
}
```

### Available Task Types

| Task Type | Description | Key Parameters |
|-----------|-------------|----------------|
| `MoveToLocation` | Move to coordinates | `x`, `y`, `z`, `mapId`, `threshold` |
| `MoveToNPC` | Move to an NPC | `npcEntry`, `threshold`, `classNPCs` |
| `TalkToNPC` | Interact with NPC | `npcEntry`, `classNPCs` |
| `AcceptQuest` | Accept a quest | `npcEntry`, `questId`, `classQuests` |
| `TurnInQuest` | Turn in a quest | `npcEntry`, `questId`, `rewardChoice` |
| `KillMobs` | Kill mobs with looting | `targetEntries[]`, `killCount`, `searchRadius`, `collectItems[]` |
| `UseObject` | Interact with game objects | `objectEntry`, `useCount`, `waitForLoot` |
| `Adventure` | Combined combat + objects | `targetEntries[]`, `objectEntries[]`, `defendSelf` |
| `LearnSpells` | Learn from trainer | `npcEntry`, `spellIds[]`, `classSpells` |
| `EquipItems` | Auto-equip better gear | (no parameters) |
| `SellItems` | Sell to vendor | `npcEntry` |
| `Wait` | Wait for seconds | `seconds` |
| `LogMessage` | Log a message | `message`, `level` |
| `AssertQuestInLog` | Verify quest is in log | `questId`, `message` |
| `AssertQuestNotInLog` | Verify quest is NOT in log | `questId`, `message` |
| `AssertHasItem` | Verify player has item | `itemEntry`, `minCount`, `message` |
| `AssertLevel` | Verify player level | `minLevel`, `message` |

### Example: Kill Quest Route

```json
{
  "name": "Northshire - Kill Wolves",
  "description": "Kill 10 wolves and collect 5 meat",
  "loop": false,
  "tasks": [
    {
      "type": "MoveToNPC",
      "parameters": { "npcEntry": 823 }
    },
    {
      "type": "AcceptQuest",
      "parameters": { "npcEntry": 823, "questId": 33 }
    },
    {
      "type": "KillMobs",
      "parameters": {
        "targetEntries": [299],
        "killRequirements": [{ "entry": 299, "count": 10 }],
        "collectItems": [{ "itemEntry": 750, "count": 5, "droppedBy": [299] }],
        "searchRadius": 100
      }
    },
    {
      "type": "MoveToNPC",
      "parameters": { "npcEntry": 823 }
    },
    {
      "type": "TurnInQuest",
      "parameters": { "npcEntry": 823, "questId": 33, "rewardChoice": 0 }
    }
  ]
}
```

### Class-Specific Tasks

Some tasks support class-specific NPCs or quests:

```json
{
  "type": "TalkToNPC",
  "parameters": {
    "npcEntry": 197,
    "classNPCs": {
      "Warrior": 911,
      "Priest": 375,
      "Paladin": 925
    }
  }
}
```

**Supported classes**: Warrior, Paladin, Hunter, Rogue, Priest, DeathKnight, Shaman, Mage, Warlock, Druid

## Test Framework

The test framework enables automated E2E testing of bot behaviors. Routes can define their own bot requirements using a `harness` section.

### Test Route with Harness

```json
{
  "name": "First Quest Test",
  "harness": {
    "botCount": 1,
    "accountPrefix": "test_ns_",
    "classes": ["Warrior"],
    "race": "Human",
    "level": 1,
    "setupTimeoutSeconds": 60,
    "testTimeoutSeconds": 90
  },
  "tasks": [
    { "type": "AcceptQuest", "parameters": { "npcEntry": 823, "questId": 783 } },
    { "type": "AssertQuestInLog", "parameters": { "questId": 783 } },
    { "type": "TurnInQuest", "parameters": { "npcEntry": 197, "questId": 783 } },
    { "type": "AssertQuestNotInLog", "parameters": { "questId": 783 } }
  ]
}
```

### Test Commands

| Command | Description |
|---------|-------------|
| `test run <route>` | Start a test run with harness |
| `test status [runId]` | Show test run status |
| `test list` | List all test runs |
| `test stop <runId>` | Stop a running test |

See [docs/TEST_FRAMEWORK_PLAN.md](docs/TEST_FRAMEWORK_PLAN.md) for full documentation.

## Console Commands

| Command | Description |
|---------|-------------|
| `stats` or `info` | Display bot statistics |
| `quit` or `exit` | Clean shutdown (saves bot info) |

## Configuration Reference

| Setting | Description |
|---------|-------------|
| `Hostname` | Server address |
| `Port` | Auth server port (usually 3724) |
| `RealmID` | Realm to connect to |
| `Username` | Account with RA permissions |
| `Password` | Account password |
| `MinBotsCount` | Minimum bots to maintain |
| `MaxBotsCount` | Maximum bots to spawn |
| `MMAPsFolderPath` | Path to mmaps folder |
| `VMAPsFolderPath` | Path to vmaps folder |
| `MAPsFolderPath` | Path to maps folder |
| `DBCsFolderPath` | Path to dbc folder |

## Building from Source

### Requirements

- .NET 8.0 SDK
- Visual Studio 2022 (optional, for IDE)

### Build Commands

```bash
# Debug build
dotnet build BotFarm.sln -c Debug -p:Platform=x64

# Release build
dotnet build BotFarm.sln -c Release -p:Platform=x64

# Run tests
dotnet test "TrinityCore UnitTests/TrinityCore UnitTests.csproj"
```

### Native Library Compilation

The pathfinding requires native libraries from TrinityCore:

1. Clone [TrinityCore CLI branch](https://github.com/jackpoz/TrinityCore/tree/CLI)
2. Configure with CMake: `cmake -DSERVERS=ON -DTOOLS=ON ..`
3. Build the solution
4. Copy `cli.dll`, `Ijwhost.dll`, `cli.runtimeconfig.json` to `BotFarm/lib/`

## Credits

- Original BotFarm by [jackpoz](https://github.com/jackpoz/BotFarm)
- Authentication and base implementation from mangos, WCell, PseuWoW, TrinityCore
