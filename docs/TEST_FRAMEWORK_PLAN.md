# BotFarm Test Framework - Long Term Plan

This document outlines the complete vision for the BotFarm test framework, including implemented features and future phases.

## Overview

The test framework enables automated E2E testing of bot behaviors against a TrinityCore server. Routes define their own bot requirements (class, level, items, spawn position), and the framework orchestrates multi-bot test runs with pass/fail reporting.

---

## Phase 1: Core Test Framework (Implemented)

### Task Event System

**File:** `Client/AI/Tasks/TaskEvents.cs`

Events fired by TaskExecutorAI for tracking task/route completion:
- `TaskCompletedEventArgs` - Individual task results with duration, status, error info
- `RouteCompletedEventArgs` - Route-level results with task counts and total duration

**File:** `Client/AI/Tasks/TaskExecutorAI.cs`

Added events:
```csharp
public event EventHandler<TaskCompletedEventArgs> TaskCompleted;
public event EventHandler<RouteCompletedEventArgs> RouteCompleted;
```

### Harness Settings Model

**File:** `BotFarm/Testing/HarnessSettings.cs`

Routes define bot requirements in a `harness` section:

```json
{
  "name": "Test Name",
  "harness": {
    "botCount": 2,
    "accountPrefix": "test_ns_",
    "classes": ["Warrior", "Priest"],
    "race": "Human",
    "level": 1,
    "items": [
      { "entry": 2362, "count": 1 }
    ],
    "startPosition": {
      "mapId": 0,
      "x": -8949.95,
      "y": -132.493,
      "z": 83.5312
    },
    "setupTimeoutSeconds": 60,
    "testTimeoutSeconds": 300
  },
  "tasks": [...]
}
```

**Harness Fields:**
| Field | Description |
|-------|-------------|
| `botCount` | Number of bots to spawn |
| `accountPrefix` | Prefix for account names (e.g., "test_ns_1") |
| `classes` | Array of classes - bots distributed round-robin |
| `race` | Character race (must be compatible with classes) |
| `level` | Starting level (set via RA command) |
| `items` | Starting items `{entry, count}` (given via RA) |
| `startPosition` | World coordinates where bots spawn/teleport |
| `setupTimeoutSeconds` | Timeout for bot creation and login |
| `testTimeoutSeconds` | Timeout for route completion |

### Test Run Coordinator

**File:** `BotFarm/Testing/TestRunCoordinator.cs`

Orchestrates multi-bot test runs:

1. Load route and harness settings
2. Create bot accounts via RA (fixed password "test1234" for reuse)
3. Create BotGame instances with harness settings
4. Wait for all bots to create characters and login
5. Setup characters via RA (level, items) - *planned*
6. Teleport bots to start position (if specified) - *planned*
7. Start route on all bots
8. Subscribe to task/route events
9. Wait for completion or timeout
10. Generate results

### Test Result Models

**File:** `BotFarm/Testing/TestRun.cs`

```csharp
public enum TestRunStatus { Pending, SettingUp, Running, Completed, Failed, TimedOut }

public class TestRun
{
    public string Id { get; init; }
    public string RoutePath { get; init; }
    public HarnessSettings Harness { get; init; }
    public TestRunStatus Status { get; set; }
    public List<BotTestResult> BotResults { get; }
    public TimeSpan Duration { get; }
}

public class BotTestResult
{
    public string BotName { get; init; }
    public string CharacterName { get; init; }
    public string Class { get; init; }
    public bool Success { get; set; }
    public List<TaskTestResult> TaskResults { get; }
}
```

### Report Generation

**File:** `BotFarm/Testing/TestReportGenerator.cs`

Console output format:
```
═══════════════════════════════════════════════════════════════
TEST RUN: Northshire Level 1-5 Test
═══════════════════════════════════════════════════════════════
Status: COMPLETED
Duration: 5m 23s
Bots: 3/3 succeeded

BOT RESULTS:
┌─────────────────┬──────────┬────────┬───────────┬──────────┐
│ Bot             │ Class    │ Tasks  │ Duration  │ Status   │
├─────────────────┼──────────┼────────┼───────────┼──────────┤
│ test_north_1    │ Warrior  │ 12/12  │ 5m 20s    │ ✓ PASS   │
│ test_north_2    │ Priest   │ 12/12  │ 5m 18s    │ ✓ PASS   │
│ test_north_3    │ Paladin  │ 12/12  │ 5m 23s    │ ✓ PASS   │
└─────────────────┴──────────┴────────┴───────────┴──────────┘
```

### Console Commands

**File:** `BotFarm/BotFactory.cs`

```
test run <routefile>     - Start test run with harness
test status [runId]      - Show test run status
test list                - List all test runs
test stop <runId>        - Stop running test
```

### Program Mode Change

**File:** `BotFarm/Program.cs`

- Default: Test mode (no auto-spawn bots)
- `--auto` or `-a` flag: Legacy auto-spawn behavior

---

## Phase 1.5: Task Improvements (Implemented)

### Combat Stall Detection

**Files:** `KillMobsTask.cs`, `AdventureTask.cs`

Problem: Bot and mob sometimes desync and stand apart not attacking.

Solution: Track target health changes, re-engage after 6 seconds of no damage:
```csharp
if ((DateTime.Now - lastHealthChangeTime).TotalSeconds > CombatStallTimeoutSeconds)
{
    game.CancelActionsByFlag(ActionFlag.Movement);
    game.StopAttack();
    game.MoveTo(currentTarget.GetPosition());
    game.StartAttack(currentTarget.GUID);
}
```

### Return to Center on Path Failure

**File:** `AdventureTask.cs`

Problem: Bots get stuck on unreachable targets with repeated "Cannot reach destination".

Solution: After 3 consecutive path failures, return to center position:
```csharp
if (consecutivePathFailures >= MaxPathFailuresBeforeSkip)
{
    unpathableTargets.Add(currentTarget.GUID);
    ReleaseCurrentTarget(game);
    state = AdventureState.ReturningToCenter;
    game.MoveTo(centerPosition);
}
```

### Item Collection Progress Logging

**File:** `AdventureTask.cs`

Added item counts to progress logging:
```
AdventureTask: Progress - kills: entry 6: 5/8, items: item 50432: 3/8
```

---

## Test Organization

### Directory Structure

```
routes/
├── northshire/
│   ├── test-ns-first-quest.json      # Quest 783 only (90s)
│   ├── test-ns-kobold-combat.json    # Combat + Adventure (300s)
│   └── test-ns-class-trainer.json    # Class-specific routing (120s)
├── test-northshire-human.json        # Full E2E test (600s)
└── test-simple-movement.json         # Basic movement test
```

### Test Design Guidelines

1. **Focused tests** - Each test validates one feature or quest chain
2. **Appropriate timeouts** - Match timeout to expected completion time + buffer
3. **Unique account prefixes** - Prevent interference between parallel tests
4. **LogMessage tasks** - Add checkpoints for debugging

---

## Phase 2: Character Setup via RA (Planned)

### Goal
Set character level and give items after creation using RA commands.

### Implementation

**File:** `BotFarm/BotFactory.cs`

```csharp
public void SetupCharacterViaRA(string charName, int level, List<ItemGrant> items)
{
    // Character must be offline for these commands
    remoteAccess.SendCommand($".character level {charName} {level}");

    foreach (var item in items)
    {
        remoteAccess.SendCommand(
            $".send items {charName} \"Test Setup\" \"Items\" {item.Entry}:{item.Count}");
    }
}
```

### Teleport to Start Position

```csharp
// After login, teleport to harness start position
remoteAccess.SendCommand(
    $".tele name {charName} {startPosition.MapId} {startPosition.X} {startPosition.Y} {startPosition.Z}");
```

---

## Phase 2.5: Assertion Tasks (Implemented)

Assertion tasks verify game state at specific points in a route. They return Success if the condition is met, Failed otherwise (stopping the route).

### AssertQuestInLog

Verify a quest is in the player's quest log:
```json
{
  "type": "AssertQuestInLog",
  "parameters": {
    "questId": 783,
    "message": "Quest 783 should be in log after accepting"
  }
}
```

### AssertQuestNotInLog

Verify a quest is NOT in the player's quest log (completed or never accepted):
```json
{
  "type": "AssertQuestNotInLog",
  "parameters": {
    "questId": 783,
    "message": "Quest 783 should be complete after turn-in"
  }
}
```

### AssertHasItem

Verify player has at least N of an item:
```json
{
  "type": "AssertHasItem",
  "parameters": {
    "itemEntry": 2362,
    "minCount": 1,
    "message": "Should have received quest reward"
  }
}
```

### AssertLevel

Verify player level is at least N:
```json
{
  "type": "AssertLevel",
  "parameters": {
    "minLevel": 2,
    "message": "Should have leveled up from quest XP"
  }
}
```

### Assertion Files

```
BotFarm/AI/Tasks/AssertQuestInLogTask.cs
BotFarm/AI/Tasks/AssertQuestNotInLogTask.cs
BotFarm/AI/Tasks/AssertHasItemTask.cs
BotFarm/AI/Tasks/AssertLevelTask.cs
Client/AutomatedGame.cs - Added IsQuestInLog() helper
```

---

## Phase 3: Advanced Test Features (Planned)

### Test Dependencies

Run tests in order with dependencies:
```json
{
  "name": "Full Northshire Suite",
  "tests": [
    { "route": "test-ns-first-quest.json" },
    { "route": "test-ns-kobold-combat.json", "dependsOn": ["test-ns-first-quest"] },
    { "route": "test-ns-class-trainer.json", "dependsOn": ["test-ns-kobold-combat"] }
  ]
}
```

### Parallel Test Execution

Run multiple independent tests simultaneously:
```
test run-suite northshire-suite.json --parallel
```

### Test Snapshots

Save and restore character state between tests:
```json
{
  "harness": {
    "restoreSnapshot": "after-first-quest",
    "saveSnapshot": "after-combat"
  }
}
```

---

## Phase 4: Service Layer Extraction (Future)

### Goal
Decouple business logic from console UI to enable web interface.

### Architecture

```
┌─────────────────────────────────────────────────────────────┐
│                    Presentation Layer                        │
│  ┌──────────────────┐  ┌──────────────────────────────────┐ │
│  │   Console App    │  │         Web Application          │ │
│  │   (BotFarm.exe)  │  │    (React SPA + REST API)       │ │
│  └────────┬─────────┘  └────────────────┬─────────────────┘ │
│           │                              │                   │
│           └──────────────┬───────────────┘                   │
│                          ▼                                   │
│  ┌─────────────────────────────────────────────────────────┐│
│  │                   Service Layer                          ││
│  │  BotService, TestService, RouteService, ReportService   ││
│  └─────────────────────────────────────────────────────────┘│
│                          │                                   │
│                          ▼                                   │
│  ┌─────────────────────────────────────────────────────────┐│
│  │                    Core Layer                            ││
│  │  BotFactory, TestRunCoordinator, TaskExecutorAI         ││
│  └─────────────────────────────────────────────────────────┘│
└─────────────────────────────────────────────────────────────┘
```

### Services

```csharp
public interface IBotService
{
    IReadOnlyList<BotInfo> GetAllBots();
    BotInfo GetBot(string id);
    Task<BotInfo> CreateBotAsync(CreateBotRequest request);
    Task StopBotAsync(string id);
}

public interface ITestService
{
    Task<TestRun> StartTestRunAsync(string routePath);
    TestRun GetTestRun(string runId);
    IReadOnlyList<TestRun> GetAllTestRuns();
    Task StopTestRunAsync(string runId);
}

public interface IRouteService
{
    IReadOnlyList<RouteInfo> GetAllRoutes();
    TaskRoute LoadRoute(string path);
    void SaveRoute(string path, TaskRoute route);
}
```

---

## Phase 5: Web Application (Future)

### Technology Stack

- **Frontend:** React + TypeScript + Vite (Tanstack Start)
- **Backend:** ASP.NET Core Web API
- **Real-time:** SignalR for live updates
- **State:** React Query for server state

### Features

1. **Dashboard**
   - Active bots with real-time status
   - Running tests with progress
   - Recent test results

2. **Route Editor**
   - Visual task builder
   - JSON preview/edit
   - Validation

3. **Test Runner**
   - Start/stop tests
   - Live bot logs
   - Progress visualization

4. **Reports**
   - Historical test results
   - Trend analysis
   - Export to JSON/CSV

### API Endpoints

```
GET    /api/bots                    - List all bots
GET    /api/bots/{id}               - Get bot details
POST   /api/bots                    - Create bot
DELETE /api/bots/{id}               - Stop bot

GET    /api/tests                   - List test runs
GET    /api/tests/{id}              - Get test run details
POST   /api/tests                   - Start test run
DELETE /api/tests/{id}              - Stop test run

GET    /api/routes                  - List routes
GET    /api/routes/{path}           - Get route
PUT    /api/routes/{path}           - Save route

Hub    /hubs/botfarm                - SignalR for real-time updates
```

---

## Phase 6: Server Log Correlation (Future)

### Goal
Parse TrinityCore server logs and correlate with test runs for debugging.

### Implementation

1. **Log Watcher** - Monitor TrinityCore log files
2. **Log Parser** - Extract relevant events (quest completion, deaths, errors)
3. **Correlation** - Match log entries to test runs by timestamp and character name
4. **Display** - Show server-side events alongside bot logs

### Example Output

```
[Bot: test_ns_1] AcceptQuest: Accepted quest 783 from NPC 823
[Server] Character Testnsb accepted quest 783 "A Threat Within"
[Bot: test_ns_1] MoveToNPC: Moving to NPC 197
[Server] Character Testnsb entered combat with Kobold Vermin (GUID: 12345)
[Bot: test_ns_1] Combat: Engaged Kobold Vermin
```

---

## File Summary

### Implemented Files

```
Client/AI/Tasks/TaskEvents.cs           - Event args classes
Client/AI/Tasks/TaskExecutorAI.cs       - Added events
Client/AI/Tasks/TaskRoute.cs            - Added Harness property
Client/AI/Tasks/LogMessageTask.cs       - Fixed base.Start() call
Client/AutomatedGame.cs                 - Added IsQuestInLog() helper

BotFarm/Testing/HarnessSettings.cs      - Harness configuration model
BotFarm/Testing/TestRun.cs              - Test run and result models
BotFarm/Testing/TestRunCoordinator.cs   - Test orchestration
BotFarm/Testing/TestReportGenerator.cs  - Report generation

BotFarm/AI/Tasks/TaskRouteLoader.cs     - Parse harness config + assertion tasks
BotFarm/AI/Tasks/KillMobsTask.cs        - Combat stall detection
BotFarm/AI/Tasks/AdventureTask.cs       - Stall detection + return to center
BotFarm/AI/Tasks/AssertQuestInLogTask.cs    - Quest in log assertion
BotFarm/AI/Tasks/AssertQuestNotInLogTask.cs - Quest not in log assertion
BotFarm/AI/Tasks/AssertHasItemTask.cs       - Item count assertion
BotFarm/AI/Tasks/AssertLevelTask.cs         - Level assertion

BotFarm/BotFactory.cs                   - Test commands, CreateTestBot
BotFarm/BotGame.cs                      - Harness-aware character creation
BotFarm/Program.cs                      - Default test mode
```

### Test Routes

```
routes/test-northshire-human.json           - Full E2E (600s)
routes/test-simple-movement.json            - Basic movement
routes/northshire/test-ns-first-quest.json  - Quest 783 (90s)
routes/northshire/test-ns-kobold-combat.json - Combat test (300s)
routes/northshire/test-ns-class-trainer.json - Class routing (120s)
```

---

## Running Tests

```bash
# Build
dotnet build BotFarm.sln -c Debug -p:Platform=x64

# Run (test mode by default)
cd BotFarm/bin/x64/Debug/net8.0
./BotFarm.exe

# Run tests
test run routes/northshire/test-ns-first-quest.json
test run routes/test-northshire-human.json

# Check status
test list
test status <runId>

# Legacy auto-spawn mode
./BotFarm.exe --auto
```
