# Task-Based Quest System

## Overview

The Task-Based Quest System is a new feature that allows you to define repeatable sequences of high-level actions (called "tasks") for bots to execute. This is perfect for:

- **Stress testing**: Run 100 bots through the same quest over and over
- **Bug reproduction**: Create precise step-by-step sequences to trigger specific behaviors
- **Automated testing**: Validate server behavior with predictable bot actions

## Architecture

### Key Components

1. **ITask Interface** - Base interface for all tasks
   - `Start(game)` - Initialize the task
   - `Update(game)` - Called every game tick while running
   - `Cleanup(game)` - Clean up when task completes
   - Returns `TaskResult` (Running/Success/Failed/Skipped)

2. **TaskRoute** - A sequence of tasks with optional looping
   - Contains a list of ITask objects
   - Can be configured to loop endlessly or run once
   - Has a name and description for identification

3. **TaskExecutorAI** - IStrategicAI that executes a TaskRoute
   - Automatically progresses through tasks
   - Handles task failures (can restart route if looped)
   - Provides status/progress information

4. **TaskRouteLoader** - Loads routes from JSON files
   - Parses JSON into TaskRoute objects
   - Extensible for adding new task types

### Available Tasks

#### Client.AI.Tasks (Generic)
- **WaitTask** - Wait for a specified duration
- **WaitForConditionTask** - Wait until a condition is true
- **LogMessageTask** - Log a message to console

#### BotFarm.AI.Tasks (Bot-specific)
- **MoveToLocationTask** - Move to specific coordinates
- **MoveToNPCTask** - Find and move to an NPC by entry ID
- **TalkToNPCTask** - Interact with an NPC (gossip hello)
- **AcceptQuestTask** - Accept a quest from an NPC
- **TurnInQuestTask** - Turn in a completed quest to an NPC
- **KillMobsTask** - Kill mobs in an area using class-specific combat AI

#### BotFarm.AI.Combat (Combat System)
- **IClassCombatAI** - Interface for class-specific combat behaviors
- **BaseClassCombatAI** - Base implementation with rest/health logic
- **WarriorCombatAI** - Warrior rotation (Battle Shout, Rend, Heroic Strike)
- **PriestCombatAI** - Priest rotation (Shield, SW:P, Smite, healing)
- **GenericCombatAI** - Fallback auto-attack only behavior
- **CombatAIFactory** - Creates appropriate combat AI for player's class

## Usage

### Console Commands

```
route start <botname> <routefile>      - Start a route for a specific bot
route stop <botname>                    - Stop current route for a bot  
route status [botname]                  - Show route status (all or specific bot)
route startall <routefile>              - Start route for ALL bots
route stopall                           - Stop routes for all bots
```

### Route File Format

Routes are defined in JSON format and placed in the `BotFarm/routes/` directory.

```json
{
  "name": "Route Name",
  "description": "What this route does",
  "loop": true,
  "tasks": [
    {
      "type": "LogMessage",
      "parameters": {
        "message": "Starting route",
        "level": "Info"
      }
    },
    {
      "type": "Wait",
      "parameters": {
        "seconds": 2.0
      }
    },
    {
      "type": "MoveToLocation",
      "parameters": {
        "x": -8914.0,
        "y": -133.0,
        "z": 81.7,
        "mapId": 0,
        "threshold": 3.0
      }
    }
  ]
}
```

### Task Types Reference

**LogMessage**
```json
{
  "type": "LogMessage",
  "parameters": {
    "message": "Your message here",
    "level": "Info"  // Debug, Info, Warning, Error
  }
}
```

**Wait**
```json
{
  "type": "Wait",
  "parameters": {
    "seconds": 5.0
  }
}
```

**MoveToLocation**
```json
{
  "type": "MoveToLocation",
  "parameters": {
    "x": -8914.0,
    "y": -133.0,
    "z": 81.7,
    "mapId": 0,
    "threshold": 3.0  // Optional, default 3.0 (arrival distance)
  }
}
```

**MoveToNPC**
```json
{
  "type": "MoveToNPC",
  "parameters": {
    "npcEntry": 197,  // NPC entry ID from database (0 = closest NPC)
    "threshold": 5.0  // Optional, default 5.0
  }
}
```

**TalkToNPC**
```json
{
  "type": "TalkToNPC",
  "parameters": {
    "npcEntry": 197   // NPC entry ID (0 = closest NPC)
  }
}
```

**AcceptQuest**
```json
{
  "type": "AcceptQuest",
  "parameters": {
    "npcEntry": 197,  // NPC entry ID (0 = use pending quest offer)
    "questId": 783    // Quest ID to accept (0 = accept any offered quest)
  }
}
```

For accepting whatever quest was just offered after TalkToNPC:
```json
{
  "type": "AcceptQuest",
  "parameters": {}
}
```

**TurnInQuest**
```json
{
  "type": "TurnInQuest",
  "parameters": {
    "npcEntry": 197,    // NPC entry ID (0 = use pending turn-in)
    "questId": 783,     // Quest ID to turn in (0 = turn in pending quest)
    "rewardChoice": 0   // Optional, index of reward to choose (0 = first)
  }
}
```

For turning in whatever quest is pending after TalkToNPC:
```json
{
  "type": "TurnInQuest",
  "parameters": {
    "rewardChoice": 0   // Optional, pick first reward
  }
}
```

**KillMobs**
```json
{
  "type": "KillMobs",
  "parameters": {
    "targetEntries": [6, 80],  // Creature entry IDs to kill (empty/omit = any mob)
    "killCount": 10,           // Number of kills required (0 = unlimited)
    "searchRadius": 50.0,      // Radius to search for mobs (default 50)
    "maxDurationSeconds": 300, // Time limit in seconds (0 = no limit)
    "centerX": -8900.0,        // Optional: center of search area (0 = use current position)
    "centerY": -100.0,
    "centerZ": 80.0,
    "mapId": 0
  }
}
```

Simplified example (kill 8 Kobold Vermin in current area):
```json
{
  "type": "KillMobs",
  "parameters": {
    "targetEntries": [6],
    "killCount": 8
  }
}
```

The KillMobs task uses class-specific combat AI:
- **Warrior**: Battle Shout buff, Rend DoT, Heroic Strike rage dump
- **Priest**: Power Word: Shield, Shadow Word: Pain DoT, Smite, self-healing
- **Other classes**: Generic auto-attack (can be expanded)

Combat AI handles:
- Targeting and engaging mobs
- Using class abilities in priority order
- Resting when low health/mana
- Moving to targets and staying in combat range

## Example: First Quest Route

See `BotFarm/routes/northshire-test.json` for a working example that moves a bot around Northshire Abbey.

## Extending the System

### Adding New Tasks

1. Create a new class in `Client\AI\Tasks\` (generic) or `BotFarm\AI\Tasks\` (bot-specific)
2. Inherit from `BaseTask`
3. Implement `Name`, `Start()`, `Update()`, and optionally `Cleanup()`
4. Add a case in `TaskRouteLoader.CreateTask()` to parse it from JSON

Example:

```csharp
public class MyCustomTask : BaseTask
{
    public override string Name => "MyCustomTask";
    
    public override bool Start(AutomatedGame game)
    {
        // Initialize task
        return true;
    }
    
    public override TaskResult Update(AutomatedGame game)
    {
        // Do work here
        if (/* task complete */)
            return TaskResult.Success;
        if (/* task failed */)
            return TaskResult.Failed;
        return TaskResult.Running;
    }
}
```

Then add to TaskRouteLoader:
```csharp
case "mycustomtask":
    return new MyCustomTask();
```

## Benefits Over Old System

**Old Behavior System:**
- Static configuration set at bot creation
- Boolean flags (Begger, Explorer, etc.)
- Hard to change at runtime
- Not easily repeatable

**New Task System:**
- Dynamic - swap routes on the fly
- Composable - build complex behaviors from simple tasks
- Data-driven - routes are JSON files
- Repeatable - perfect for testing
- Status visibility - see exactly what each bot is doing

## Integration with Existing Behaviors

The task system integrates seamlessly with existing behaviors:
- Tasks use the AI stack system (IStrategicAI)
- Can be pushed/popped like any other AI
- Works alongside triggers and scheduled actions
- Uses existing movement/pathfinding from BotGame

## Tips

- Use `loop: true` for continuous stress testing
- Use `loop: false` for one-time quest sequences
- Set bot LogLevel to Info or Debug to see detailed task progress
- Routes can be hot-reloaded - just restart with `route start`
- Start simple (movement only) then add NPC interaction
- For quest tasks, use `npcEntry: 0` to use closest NPC or pending state
- Chain tasks: MoveToNPC → TalkToNPC → AcceptQuest for full quest flow

## Example: Complete Quest Flow

Here's a complete example of accepting and turning in a quest:

```json
{
  "name": "Northshire Quest Example",
  "description": "Accept quest from Deputy Willem and turn it in",
  "loop": false,
  "tasks": [
    {
      "type": "LogMessage",
      "parameters": { "message": "Moving to Deputy Willem", "level": "Info" }
    },
    {
      "type": "MoveToNPC",
      "parameters": { "npcEntry": 823, "threshold": 4.0 }
    },
    {
      "type": "TalkToNPC", 
      "parameters": { "npcEntry": 823 }
    },
    {
      "type": "Wait",
      "parameters": { "seconds": 1.0 }
    },
    {
      "type": "AcceptQuest",
      "parameters": { "npcEntry": 823, "questId": 783 }
    },
    {
      "type": "LogMessage",
      "parameters": { "message": "Quest accepted! Do objectives...", "level": "Info" }
    },
    {
      "type": "Wait",
      "parameters": { "seconds": 5.0 }
    },
    {
      "type": "MoveToNPC",
      "parameters": { "npcEntry": 823, "threshold": 4.0 }
    },
    {
      "type": "TalkToNPC",
      "parameters": { "npcEntry": 823 }
    },
    {
      "type": "Wait",
      "parameters": { "seconds": 1.0 }
    },
    {
      "type": "TurnInQuest",
      "parameters": { "npcEntry": 823, "questId": 783, "rewardChoice": 0 }
    },
    {
      "type": "LogMessage",
      "parameters": { "message": "Quest complete!", "level": "Info" }
    }
  ]
}
```

## Future Enhancements

Potential additions to the system:
- **KillMobsInAreaTask** - Hunt specific mobs in an area
- **UseGameObjectTask** - Interact with objects (chests, doors, etc.)
- **CastSpellTask** - Cast a specific spell
- **EquipItemTask** - Equip gear
- **BuyFromVendorTask** - Purchase items
- **ParallelTasks** - Execute multiple tasks simultaneously
- **ConditionalBranching** - If/else logic in routes
