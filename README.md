# BotFarm

[![Build and Release](https://github.com/Mathih13/BotFarm/actions/workflows/build.yml/badge.svg)](https://github.com/Mathih13/BotFarm/actions/workflows/build.yml)

A bot farm application for spawning multiple automated World of Warcraft players that connect to TrinityCore-based private servers.

## Download

Download the latest release from the [Releases page](https://github.com/Mathih13/BotFarm/releases).

## Features

- **Pathfinding Movement** - Uses TrinityCore's navigation mesh for realistic bot movement
- **JSON Task Routes** - Configure bot behavior with simple JSON files
- **Class-Specific Combat AI** - Warriors, Priests, Paladins with ability rotations
- **Multi-Bot Coordination** - Bots coordinate to avoid targeting the same mobs
- **Auto Account Creation** - Automatically creates accounts via Remote Access
- **Quest Support** - Accept quests, kill mobs, collect items, turn in quests
- **Web UI** - Real-time monitoring dashboard at http://localhost:3000
- **Test Framework** - E2E testing with harness configuration and assertion tasks

## Quick Start (Release)

### Requirements

- Windows x64
- [.NET 8.0 Runtime](https://dotnet.microsoft.com/download/dotnet/8.0)
- [Node.js 20+](https://nodejs.org/)
- TrinityCore server with Remote Access enabled
- Map data files (mmaps, vmaps, maps, dbc)

### Installation

1. Download and extract the latest release
2. Edit `BotFarm.dll.config` with your server settings:

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

3. Run `BotFarm.exe`
4. Web UI opens automatically at http://localhost:3000

### Native Library Compatibility

The bundled `cli.dll` is built for tswow's TrinityCore fork. If you're using a different TrinityCore version and experience pathfinding issues, rebuild cli.dll from your TrinityCore source with the CLI branch: https://github.com/jackpoz/TrinityCore/tree/CLI

## Web UI

BotFarm includes a real-time web dashboard for monitoring and controlling bots.

**Features:**
- Live bot status and statistics
- Route assignment and management
- Test run monitoring

The UI starts automatically with BotFarm. For development mode (hot reloading):

```bash
# Start BotFarm with dev UI flag
./BotFarm.exe --dev-ui

# In another terminal, start the dev server
cd botfarm-ui
npm install
npm run dev
```

## Task System

Bots follow JSON-defined routes that specify a sequence of tasks. Routes are loaded from the `routes/` folder.

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

## Building from Source

### Requirements

- [.NET 8.0 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)
- [Node.js 20+](https://nodejs.org/)
- Visual Studio 2022 (optional, for IDE)

### Build Commands

```bash
# Debug build
dotnet build BotFarm.sln -c Debug -p:Platform=x64

# Release build
dotnet build BotFarm.sln -c Release -p:Platform=x64

# Run tests
dotnet test "TrinityCore UnitTests/TrinityCore UnitTests.csproj"

# Build UI
cd botfarm-ui
npm install
npm run build
```

### Local Release Build

Use the build script to create a full release package:

```bash
# Build with current version from Directory.Build.props
./build-release.sh

# Build with specific version
./build-release.sh -v 0.2.0

# Skip tests or UI
./build-release.sh --skip-tests --skip-ui

# See all options
./build-release.sh --help
```

Output is placed in the `release/` folder.

### Native Library Compilation

The pathfinding requires native libraries from TrinityCore:

1. Clone [TrinityCore CLI branch](https://github.com/jackpoz/TrinityCore/tree/CLI)
2. Configure with CMake: `cmake -DSERVERS=ON -DTOOLS=ON ..`
3. Build the solution
4. Copy `cli.dll`, `Ijwhost.dll`, `cli.runtimeconfig.json`, `cli.deps.json` to `BotFarm/lib/`

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

## Credits

- Original BotFarm by [jackpoz](https://github.com/jackpoz/BotFarm)
- Authentication and base implementation from mangos, WCell, PseuWoW, TrinityCore
