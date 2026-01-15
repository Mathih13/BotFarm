# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project Overview

BotFarm is a console application for spawning multiple automated World of Warcraft players (bots) that connect to TrinityCore-based private servers. It handles authentication, world server communication, pathfinding, and bot behaviors.

**CRITICAL**: See "Movement System" section below for required packet handlers and initialization sequence. Movement will NOT work without `CMSG_SET_ACTIVE_MOVER`, time sync responses, and speed change acknowledgments.

## Build Commands

```bash
# Build entire solution (Debug, x64)
dotnet build BotFarm.sln -c Debug -p:Platform=x64

# Build release
dotnet build BotFarm.sln -c Release -p:Platform=x64

# Run tests
dotnet test "TrinityCore UnitTests/TrinityCore UnitTests.csproj"
```

Note: The BotFarm UnitTests project uses .NET Framework 4.7.2 with MSTest and may require Visual Studio test runner.

## Parallel Development with Git Worktrees

**IMPORTANT**: Multiple workers may be working on this codebase simultaneously. Always use git worktrees for feature development to avoid conflicts.

### Creating a Worktree

```bash
# From the repo root, create a worktree in a sibling directory
git worktree add -b feat/your-feature-name ../BotFarm-yourfeature HEAD

# Copy the lib/ folder (not tracked by git, contains native DLLs)
cp -r BotFarm/lib/ ../BotFarm-yourfeature/BotFarm/
```

### Why Worktrees?

- Multiple Claude instances or developers can work in parallel without conflicts
- Each worktree has its own working directory but shares the git history
- Changes can be tested independently before merging
- The main directory stays stable for running tests

### Workflow

1. **Create worktree** for your feature branch
2. **Copy `BotFarm/lib/`** folder (native dependencies not in git)
3. **Make changes** in the worktree
4. **Build and test** in the worktree
5. **Merge back** to main when ready, or cherry-pick commits

### Cleanup

```bash
# Remove a worktree when done
git worktree remove ../BotFarm-yourfeature

# Delete the feature branch if no longer needed
git branch -d feat/your-feature-name
```

### Merging Changes Back

If safe to merge (no conflicts expected):
```bash
# From main repo, merge the feature branch
git merge feat/your-feature-name

# Or cherry-pick specific commits
git cherry-pick <commit-hash>
```

## Required Dependencies

Before running, copy these files to `BotFarm/lib/` from a TrinityCore CLI build (https://github.com/jackpoz/TrinityCore/tree/CLI):
- `cli.dll` - Native C++/CLI wrapper for TrinityCore pathfinding/map libraries
- `Ijwhost.dll` - .NET interop host
- `cli.runtimeconfig.json`

The TrinityCore CLI build requires CMake with SERVERS and TOOLS enabled.

## Architecture

### Solution Structure

- **Client/** - Core WoW client protocol library (.NET 8.0)
- **BotFarm/** - Main application that orchestrates multiple bots (.NET 8.0)
- **BotFarm UnitTests/** - MSTest unit tests (.NET Framework 4.7.2)
- **TrinityCore UnitTests/** - MSTest unit tests for client library (.NET 8.0)

### Key Components

**BotFactory** (`BotFarm/BotFactory.cs`)
- Singleton that manages bot lifecycle
- Initializes native TrinityCore libraries (Detour for pathfinding, VMap for visibility, Map for terrain, DBCStores for game data)
- Creates/loads bots from `botsinfos.xml`
- Handles Remote Access (RA) connection for account creation

**AutomatedGame** (`Client/AutomatedGame.cs`)
- Base class for automated WoW clients
- Manages socket connections (AuthSocket -> WorldSocket transition)
- Implements packet handlers via `[PacketHandler]` attribute
- Contains scheduled action system for delayed/repeating tasks
- Three-tier AI stack: Strategic, Tactical, Operational (push/pop pattern)
- Trigger system for event-driven responses

**BotGame** (`BotFarm/BotGame.cs`)
- Extends AutomatedGame with bot-specific behaviors
- Pathfinding-based movement via `MoveTo()`
- Behaviors configured via `BotBehaviorSettings`: AutoResurrect, Begger, FollowGroupLeader, Explorer

### Network Layer

- **AuthSocket** (`Client/Authentication/Network/AuthSocket.cs`) - Handles authentication protocol
- **WorldSocket** (`Client/World/Network/WorldSocket.cs`) - Handles world server protocol with encryption
- Packet handlers use reflection-based registration via `[PacketHandler(WorldCommand.OPCODE)]`

### Configuration

Settings in `BotFarm/App.config` (copied to output as `BotFarm.dll.config`):
- Server connection: Hostname, Port, RealmID, Username, Password
- Bot count: MinBotsCount, MaxBotsCount
- Data paths: MMAPsFolderPath, VMAPsFolderPath, MAPsFolderPath, DBCsFolderPath
- Behaviors array with probability-weighted selection

### AI System

Three hierarchical AI layers (`Client/AI/`):
- **IStrategicAI** - High-level goals (e.g., BeggerAI, FollowGroupLeaderAI)
- **ITacticalAI** - Mid-level decisions
- **IOperationalAI** - Low-level actions

AIs use push/pop stack pattern allowing temporary behavior overrides.

## Movement System

### Critical Requirements for Movement to Work

**IMPORTANT**: The following requirements must ALL be met for bot movement to function. Missing any of these will cause the server to silently ignore movement packets:

1. **CMSG_SET_ACTIVE_MOVER** - MUST be sent after login
   - Location: `AutomatedGame.HandleLoginVerifyWorld()` and when player object is created
   - Without this, the server ignores ALL movement packets as a security measure
   - The server needs to know which unit you're controlling before accepting movement

2. **Time Synchronization** - MUST respond to `SMSG_TIME_SYNC_REQ`
   - Handler: `AutomatedGame.HandleTimeSyncRequest()`
   - Server uses this for anti-cheat validation
   - Send `CMSG_TIME_SYNC_RESP` with counter and client time (ms since process start)

3. **Speed Change Acknowledgments** - MUST ACK all speed changes
   - `SMSG_FORCE_RUN_SPEED_CHANGE` → `CMSG_FORCE_RUN_SPEED_CHANGE_ACK`
   - `SMSG_FORCE_WALK_SPEED_CHANGE` → `CMSG_FORCE_WALK_SPEED_CHANGE_ACK`
   - `SMSG_FORCE_SWIM_SPEED_CHANGE` → `CMSG_FORCE_SWIM_SPEED_CHANGE_ACK`
   - ACK packets must include: movement data + counter + speed value

4. **Movement Speed Application** - Apply speeds from update packets
   - Read from `UPDATETYPE_CREATE_OBJECT` movement speeds (MOVE_RUN, MOVE_WALK, etc.)
   - Update `Player.Speed` when received
   - Server expects client to use correct speed in movement calculations

5. **Movement Packet Structure** (`MovementPacket` in `OutPacket.cs`)
   ```csharp
   WritePacketGuid(GUID);           // Packed GUID
   Write((uint)flags);               // MovementFlags
   Write((ushort)flags2);            // MovementFlags2
   Write(time);                      // Client timestamp (ms since process start)
   Write(X);                         // Position X
   Write(Y);                         // Position Y
   Write(Z);                         // Position Z
   Write(O);                         // Orientation
   Write(fallTime);                  // Fall time (usually 0)
   ```

6. **Server Position Corrections** - Trust server position updates
   - When server sends movement updates for player, apply them immediately
   - Prevents client-side prediction drift and rubber-banding
   - Location: `UpdateObjectHandler.HandleMovementPacket()`

### Movement Update Frequency

- **Current setting**: 200ms + 0-50ms random jitter (~4-5 updates/sec) for realistic simulation with many bots
- **Real WoW clients**: ~30-50ms between heartbeats
- **For smooth single-bot testing**: 50ms (20 updates/sec)
- Set via `GetMovementInterval()` in `AutomatedGame.cs`, used by `MoveTo()` and `Follow()`

### Movement Opcodes

Client-to-Server (Movement Initiation):
- `MSG_MOVE_SET_FACING` - Set facing direction
- `MSG_MOVE_START_FORWARD` - Begin moving forward
- `MSG_MOVE_HEARTBEAT` - Continuous position updates while moving
- `MSG_MOVE_STOP` - Stop moving

Server-to-Client (for our player):
- Server may echo back `MSG_MOVE_*` packets with corrections
- Apply these corrections to prevent desync

### Debugging Movement Issues

If bots aren't moving:

1. **Enable TrinityCore server logging** (`worldserver.conf`):
   ```
   Logger.cheat=1,Console Server
   Logger.movement.motionmaster=1,Console Server
   Logger.network.opcode=1,Console Server
   ```

2. **Check server console** - Look for:
   - Anti-cheat warnings (missing time sync, speed mismatch)
   - "PENDING_MOVE" timeouts
   - Received opcodes when bot tries to move

3. **Verify CMSG_SET_ACTIVE_MOVER** was sent:
   - Look for "Enabling movement for Player" log
   - Should appear right after login

4. **Common issues**:
   - No server response to movement packets → Missing CMSG_SET_ACTIVE_MOVER
   - Stuttering/rubber-banding → Movement frequency too low or missing position corrections
   - Speed mismatch kicks → Not acknowledging speed change packets

### Reference Implementation

For working movement packet examples, see `wow-bots` project in tswow install directory:
- `tswow-install/wow-bots/packets/Movement.h` - Packet structure
- Shows proper CMSG_SET_ACTIVE_MOVER usage

### Key Files Modified for Movement Fix

The following files contain critical movement functionality:

- **`Client/AutomatedGame.cs`**:
  - `SendSetActiveMover()` - Sends CMSG_SET_ACTIVE_MOVER (line ~96)
  - `HandleLoginVerifyWorld()` - Calls SendSetActiveMover after login (line ~863)
  - `HandleTimeSyncRequest()` - Responds to time sync (line ~1083)
  - `HandleForceRunSpeedChange()` - ACKs run speed changes (line ~1010)
  - `HandleForceWalkSpeedChange()` - ACKs walk speed changes (line ~1032)
  - `HandleForceSwimSpeedChange()` - ACKs swim speed changes (line ~1053)
  - `UpdateObjectHandler.HandleMovementPacket()` - Applies server position corrections (line ~1183)
  - `UpdateObjectHandler.HandleUpdateData()` - Applies movement speeds from update packets (line ~1331)

- **`Client/World/Entities/Unit.cs`**:
  - `Speed` property - Changed setter from `private` to `public` to allow speed updates

- **`BotFarm/BotGame.cs`**:
  - `MoveTo()` - Sends movement packets with 50ms frequency (line ~299)

- **`Client/AutomatedGame.cs`** (Follow system):
  - `Follow()` - Sends movement packets with 50ms frequency for follow behavior (line ~677)

- **`Client/World/Network/WorldSocket.cs`**:
  - Removed movement-related opcodes from `unhandledOpcodes` list (lines ~50-56 were removed)

## Console Commands

When running, the application accepts:
- `stats` / `info` - Display bot statistics
- `quit` / `exit` - Clean shutdown (saves bot info, allows logout)

## Task System

The task system (`BotFarm/AI/Tasks/`) provides JSON-configurable bot behavior routes loaded via `TaskRouteLoader`.

### Route File Structure

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

- **MoveToLocation**: Move to coordinates `{x, y, z, mapId, threshold}`
- **MoveToNPC**: Move to NPC by entry `{npcEntry, threshold, classNPCs}`
- **TalkToNPC**: Interact with NPC `{npcEntry, classNPCs}`
- **AcceptQuest**: Accept quest `{npcEntry, questId, classQuests, classNPCs}`
- **TurnInQuest**: Turn in quest `{npcEntry, questId, rewardChoice, classQuests, classNPCs}`
- **KillMobs**: Kill mobs `{targetEntries[], killCount, searchRadius, maxDurationSeconds, centerX/Y/Z, mapId, collectItemEntry, collectItemCount, killRequirements[], collectItems[]}`
- **UseObject**: Interact with game objects `{objectEntry, useCount, searchRadius, waitForLoot, maxWaitSeconds}`
- **Adventure**: Comprehensive quest task combining combat, object interaction, and reactive defense `{targetEntries[], objectEntries[], killRequirements[], objectRequirements[], collectItems[], defendSelf, ...}`
- **LearnSpells**: Learn spells from trainer `{npcEntry, spellIds[], classSpells, classNPCs}`
- **Wait**: Wait seconds `{seconds}`
- **LogMessage**: Log message `{message, level}`

### KillMobs Multiple Requirements (AND Logic)

The KillMobs task supports multiple completion conditions that must ALL be satisfied before the task completes:

**Kill requirements by mob type:**
```json
{
  "type": "KillMobs",
  "parameters": {
    "targetEntries": [1501, 1502],
    "killRequirements": [
      {"entry": 1501, "count": 10},
      {"entry": 1502, "count": 5}
    ],
    "searchRadius": 100
  }
}
```

**Multiple item collection requirements with drop sources:**
```json
{
  "type": "KillMobs",
  "parameters": {
    "targetEntries": [1501, 1502],
    "collectItems": [
      {"itemEntry": 2589, "count": 5, "droppedBy": [1501]},
      {"itemEntry": 2590, "count": 3, "droppedBy": 1502}
    ]
  }
}
```

Note: `droppedBy` can be either a single number (`299`) or an array (`[1501, 1502]`).

**Combined kill + item requirements (AND logic):**
```json
{
  "type": "KillMobs",
  "parameters": {
    "targetEntries": [1501, 1502],
    "killRequirements": [
      {"entry": 1502, "count": 5}
    ],
    "collectItems": [
      {"itemEntry": 2589, "count": 5, "droppedBy": 1501}
    ]
  }
}
```

### Target Prioritization

The bot intelligently prioritizes targets based on incomplete requirements:

1. **Highest priority**: Mobs that drop items still needed (via `droppedBy`)
2. **Second priority**: Mobs needed for incomplete kill requirements
3. **Lowest priority**: Any other valid target in `targetEntries`

This means with the config above, the bot will:
1. First kill entry 1501 until it has 5 of item 2589
2. Then kill entry 1502 until it has 5 kills
3. Complete when ALL conditions are met

**Backward compatibility:** The old `killCount`, `collectItemEntry`, and `collectItemCount` parameters still work. If `killRequirements` is specified, it takes precedence over `killCount`. If `collectItems` is specified, it takes precedence over `collectItemEntry`/`collectItemCount`.

### UseObject Task

The UseObject task interacts with game objects (chests, quest items, levers, etc.).

**Simple usage:**
```json
{
  "type": "UseObject",
  "parameters": {
    "objectEntry": 12345
  }
}
```

**Full parameters:**
```json
{
  "type": "UseObject",
  "parameters": {
    "objectEntry": 12345,
    "useCount": 3,
    "searchRadius": 50,
    "waitForLoot": true,
    "maxWaitSeconds": 5
  }
}
```

**Parameters:**
- `objectEntry` (required): Game object entry ID to interact with
- `useCount` (default 1): Number of objects to use before completing
- `searchRadius` (default 50): Search radius for finding objects
- `waitForLoot` (default false): Set to true if the object opens a loot window (e.g., chests)
- `maxWaitSeconds` (default 5): Timeout for completion detection

**Behavior:**
- Searches for objects with matching entry within radius
- Moves to object and sends `CMSG_GAMEOBJ_USE` packet
- Waits for object despawn, state change, or loot window
- Supports multi-bot coordination via claim system

### Adventure Task

The Adventure task is a comprehensive quest task that combines:
- Killing specific mobs with count requirements
- Collecting items from mobs
- Interacting with game objects
- Collecting items from objects
- Reactive combat (defending when attacked)

**Simple kill + object usage:**
```json
{
  "type": "Adventure",
  "parameters": {
    "targetEntries": [1501, 1502],
    "objectEntries": [12345],
    "killRequirements": [{"entry": 1501, "count": 10}],
    "objectRequirements": [{"entry": 12345, "count": 3}],
    "searchRadius": 100
  }
}
```

**Full parameters example:**
```json
{
  "type": "Adventure",
  "parameters": {
    "targetEntries": [1501, 1502],
    "objectEntries": [12345, 12346],
    "killCount": 0,
    "searchRadius": 100,
    "maxDurationSeconds": 0,
    "centerX": 0, "centerY": 0, "centerZ": 0, "mapId": 0,
    "killRequirements": [
      {"entry": 1501, "count": 10},
      {"entry": 1502, "count": 5}
    ],
    "objectRequirements": [
      {"entry": 12345, "count": 3}
    ],
    "collectItems": [
      {"itemEntry": 2589, "count": 5, "droppedBy": [1501, 12345]}
    ],
    "objectsGiveLoot": true,
    "defendSelf": true
  }
}
```

**Parameters:**
- `targetEntries[]`: Mob entry IDs to kill
- `objectEntries[]`: Game object entry IDs to interact with
- `killRequirements[]`: Per-mob-type kill requirements `{entry, count}`
- `objectRequirements[]`: Per-object-type use requirements `{entry, count}`
- `collectItems[]`: Item collection requirements `{itemEntry, count, droppedBy[]}`
  - Note: `droppedBy` can include both mob and object entries
- `objectsGiveLoot` (default false): Whether objects open loot windows
- `defendSelf` (default true): React to attackers even if not on target list
- Plus all KillMobs parameters (searchRadius, maxDurationSeconds, center position, etc.)

**Target Prioritization:**
1. **Highest priority**: Mobs/objects that drop items still needed
2. **Second priority**: Mobs needed for kill requirements OR objects needed for use requirements
3. **Lowest priority**: Any other valid target

**Reactive Combat:**
When `defendSelf` is true, the bot will:
- Detect when enemies are targeting it
- Automatically enter combat with attackers
- Resume previous activity after defending

**Completion:**
Task completes when ALL of these are satisfied:
- All kill requirements met
- All object requirements met
- All item collection requirements met
- OR time limit reached

### LearnSpells Task

The LearnSpells task learns spells from a trainer NPC. It requires the bot to already be at the trainer (use `MoveToNPC` first) and have talked to them (use `TalkToNPC` first).

**Simple usage (same spells for all classes):**
```json
{
  "type": "LearnSpells",
  "parameters": {
    "npcEntry": 375,
    "spellIds": [2050, 589]
  }
}
```

**Class-specific spells and trainers:**
```json
{
  "type": "LearnSpells",
  "parameters": {
    "classSpells": {
      "Warrior": [100, 772],
      "Paladin": [635, 19740],
      "Priest": [2050, 589]
    },
    "classNPCs": {
      "Warrior": 911,
      "Paladin": 925,
      "Priest": 375
    }
  }
}
```

**Behavior:**
- Bot resolves trainer NPC from `classNPCs` based on player class (or uses `npcEntry` fallback)
- Bot resolves spell list from `classSpells` based on player class (or uses `spellIds` fallback)
- If no spells for the player's class and no fallback, task is skipped
- Sends `CMSG_TRAINER_BUY_SPELL` for each spell with 0.5s delay between

### Class-Specific Parameters

For tasks that vary by player class (e.g., class trainers or class-specific quests), use the `classNPCs` or `classQuests` parameter:

**TalkToNPC with class-specific NPCs:**
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

**AcceptQuest/TurnInQuest with class-specific quests and NPCs:**
```json
{
  "type": "AcceptQuest",
  "parameters": {
    "npcEntry": 197,
    "questId": 0,
    "classQuests": {
      "Warrior": 3100,
      "Priest": 3101,
      "Paladin": 3102
    },
    "classNPCs": {
      "Warrior": 911,
      "Priest": 375,
      "Paladin": 925
    }
  }
}
```

**Behavior:**
- Bot looks up the value matching its class from the map
- If class not found and `npcEntry`/`questId` is provided, uses it as fallback
- If class not found and no fallback, task is skipped (returns false from Start)
- Works identically for `TalkToNPC`, `AcceptQuest`, and `TurnInQuest`

**Class names** (case-insensitive): Warrior, Paladin, Hunter, Rogue, Priest, DeathKnight, Shaman, Mage, Warlock, Druid

### Key Files

- `TaskRouteLoader.cs` - Parses JSON routes, `GetClassQuestMap()` helper
- `TalkToNPCTask.cs` - NPC interaction with class resolution in `Start()`
- `AcceptQuestTask.cs` - Quest acceptance with class resolution in `Start()`
- `TurnInQuestTask.cs` - Quest turn-in with class resolution in `Start()`
- `KillMobsTask.cs` - Combat with looting support
- `UseObjectTask.cs` - Game object interaction
- `AdventureTask.cs` - Comprehensive quest task (combat + objects + reactive defense)
- `MoveToNPCTask.cs` - Also contains `FindGameObjectByEntry()` and `IsGameObjectUsable()` helpers
