using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using Client.AI.Tasks;
using Client.UI;
using Client.World.Definitions;

namespace BotFarm.AI.Tasks
{
    public class TaskRouteData
    {
        [JsonPropertyName("name")]
        public string Name { get; set; }

        [JsonPropertyName("description")]
        public string Description { get; set; }

        [JsonPropertyName("loop")]
        public bool Loop { get; set; }

        [JsonPropertyName("tasks")]
        public List<TaskData> Tasks { get; set; }

        [JsonPropertyName("harness")]
        public HarnessData Harness { get; set; }
    }

    public class TaskData
    {
        [JsonPropertyName("type")]
        public string Type { get; set; }

        [JsonPropertyName("parameters")]
        public Dictionary<string, JsonElement> Parameters { get; set; }
    }

    public class HarnessData
    {
        [JsonPropertyName("botCount")]
        public int BotCount { get; set; } = 1;

        [JsonPropertyName("accountPrefix")]
        public string AccountPrefix { get; set; } = "testbot_";

        [JsonPropertyName("classes")]
        public List<string> Classes { get; set; }

        [JsonPropertyName("race")]
        public string Race { get; set; } = "Human";

        [JsonPropertyName("level")]
        public int Level { get; set; } = 1;

        [JsonPropertyName("items")]
        public List<ItemGrantData> Items { get; set; }

        [JsonPropertyName("completedQuests")]
        public List<uint> CompletedQuests { get; set; }

        [JsonPropertyName("startPosition")]
        public StartPositionData StartPosition { get; set; }

        [JsonPropertyName("setupTimeoutSeconds")]
        public int SetupTimeoutSeconds { get; set; } = 120;

        [JsonPropertyName("testTimeoutSeconds")]
        public int TestTimeoutSeconds { get; set; } = 600;

        [JsonPropertyName("restoreSnapshot")]
        public string RestoreSnapshot { get; set; }

        [JsonPropertyName("saveSnapshot")]
        public string SaveSnapshot { get; set; }
    }

    public class ItemGrantData
    {
        [JsonPropertyName("entry")]
        public uint Entry { get; set; }

        [JsonPropertyName("count")]
        public int Count { get; set; } = 1;
    }

    public class StartPositionData
    {
        [JsonPropertyName("mapId")]
        public uint MapId { get; set; }

        [JsonPropertyName("x")]
        public float X { get; set; }

        [JsonPropertyName("y")]
        public float Y { get; set; }

        [JsonPropertyName("z")]
        public float Z { get; set; }
    }
    
    public static class TaskRouteLoader
    {
        public static TaskRoute LoadFromJson(string jsonPath)
        {
            if (!File.Exists(jsonPath))
            {
                throw new FileNotFoundException($"Route file not found: {jsonPath}");
            }
            
            string json = File.ReadAllText(jsonPath);
            var routeData = JsonSerializer.Deserialize<TaskRouteData>(json, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true,
                ReadCommentHandling = JsonCommentHandling.Skip
            });
            
            if (routeData == null)
            {
                throw new InvalidOperationException($"Failed to parse route file: {jsonPath}");
            }
            
            return BuildRoute(routeData, jsonPath);
        }

        public static TaskRoute LoadFromJsonString(string json)
        {
            var routeData = JsonSerializer.Deserialize<TaskRouteData>(json, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true,
                ReadCommentHandling = JsonCommentHandling.Skip
            });
            
            if (routeData == null)
            {
                throw new InvalidOperationException("Failed to parse route JSON");
            }
            
            return BuildRoute(routeData);
        }
        
        private static TaskRoute BuildRoute(TaskRouteData routeData, string routeFilePath = null)
        {
            var route = new TaskRoute(routeData.Name, routeData.Description)
            {
                Loop = routeData.Loop,
                FilePath = routeFilePath
            };

            // Parse harness settings if present
            if (routeData.Harness != null)
            {
                route.Harness = BuildHarnessSettings(routeData.Harness);
            }

            foreach (var taskData in routeData.Tasks)
            {
                ITask task = CreateTask(taskData);
                if (task != null)
                {
                    // Set route context for tasks that need it (for multi-bot coordination)
                    if (!string.IsNullOrEmpty(routeFilePath))
                    {
                        if (task is KillMobsTask killTask)
                        {
                            killTask.SetRouteContext(routeFilePath);
                        }
                        else if (task is UseObjectTask useObjectTask)
                        {
                            useObjectTask.SetRouteContext(routeFilePath);
                        }
                        else if (task is AdventureTask adventureTask)
                        {
                            adventureTask.SetRouteContext(routeFilePath);
                        }
                    }
                    route.AddTask(task);
                }
            }

            return route;
        }

        private static HarnessSettings BuildHarnessSettings(HarnessData data)
        {
            var settings = new HarnessSettings
            {
                BotCount = data.BotCount,
                AccountPrefix = data.AccountPrefix ?? "testbot_",
                Race = data.Race ?? "Human",
                Level = data.Level,
                SetupTimeoutSeconds = data.SetupTimeoutSeconds,
                TestTimeoutSeconds = data.TestTimeoutSeconds
            };

            // Parse classes
            if (data.Classes != null && data.Classes.Count > 0)
            {
                settings.Classes = new List<string>(data.Classes);
            }

            // Parse items
            if (data.Items != null)
            {
                settings.Items = new List<ItemGrant>();
                foreach (var item in data.Items)
                {
                    settings.Items.Add(new ItemGrant { Entry = item.Entry, Count = item.Count });
                }
            }

            // Parse completed quests
            if (data.CompletedQuests != null && data.CompletedQuests.Count > 0)
            {
                settings.CompletedQuests = new List<uint>(data.CompletedQuests);
            }

            // Parse start position
            if (data.StartPosition != null)
            {
                settings.StartPosition = new StartPosition
                {
                    MapId = data.StartPosition.MapId,
                    X = data.StartPosition.X,
                    Y = data.StartPosition.Y,
                    Z = data.StartPosition.Z
                };
            }

            // Parse snapshot settings
            settings.RestoreSnapshot = data.RestoreSnapshot;
            settings.SaveSnapshot = data.SaveSnapshot;

            return settings;
        }
        
        private static ITask CreateTask(TaskData taskData)
        {
            var type = taskData.Type.ToLowerInvariant();
            var p = taskData.Parameters;
            
            try
            {
                switch (type)
                {
                    case "movetolocation":
                        return new MoveToLocationTask(
                            GetFloat(p, "x"),
                            GetFloat(p, "y"),
                            GetFloat(p, "z"),
                            GetInt(p, "mapId"),
                            GetFloat(p, "threshold", 3.0f)
                        );
                    
                    case "movetonpc":
                        var moveClassNPCs = GetClassQuestMap(p, "classNPCs");
                        if (moveClassNPCs != null)
                        {
                            return new MoveToNPCTask(
                                GetUInt(p, "npcEntry", 0),
                                GetFloat(p, "threshold", 5.0f),
                                moveClassNPCs
                            );
                        }
                        return new MoveToNPCTask(
                            GetUInt(p, "npcEntry"),
                            GetFloat(p, "threshold", 5.0f)
                        );
                    
                    case "talktonpc":
                        var talkClassNPCs = GetClassQuestMap(p, "classNPCs");
                        if (talkClassNPCs != null)
                        {
                            return new TalkToNPCTask(
                                GetUInt(p, "npcEntry", 0),
                                talkClassNPCs
                            );
                        }
                        return new TalkToNPCTask(GetUInt(p, "npcEntry"));
                    
                    case "acceptquest":
                        var acceptClassQuests = GetClassQuestMap(p, "classQuests");
                        var acceptClassNPCs = GetClassQuestMap(p, "classNPCs");
                        if (acceptClassQuests != null || acceptClassNPCs != null)
                        {
                            return new AcceptQuestTask(
                                GetUInt(p, "npcEntry", 0),
                                GetUInt(p, "questId", 0),
                                acceptClassQuests,
                                acceptClassNPCs
                            );
                        }
                        return new AcceptQuestTask(
                            GetUInt(p, "npcEntry", 0),
                            GetUInt(p, "questId", 0)
                        );

                    case "turninquest":
                        var turnInClassQuests = GetClassQuestMap(p, "classQuests");
                        var turnInClassNPCs = GetClassQuestMap(p, "classNPCs");
                        if (turnInClassQuests != null || turnInClassNPCs != null)
                        {
                            return new TurnInQuestTask(
                                GetUInt(p, "npcEntry", 0),
                                GetUInt(p, "questId", 0),
                                GetUInt(p, "rewardChoice", 0),
                                turnInClassQuests,
                                turnInClassNPCs
                            );
                        }
                        return new TurnInQuestTask(
                            GetUInt(p, "npcEntry", 0),
                            GetUInt(p, "questId", 0),
                            GetUInt(p, "rewardChoice", 0)
                        );
                    
                    case "killmobs":
                        return new KillMobsTask(
                            GetUIntArray(p, "targetEntries"),
                            GetInt(p, "killCount", 0),
                            GetFloat(p, "searchRadius", 50f),
                            GetFloat(p, "maxDurationSeconds", 0),
                            GetFloat(p, "centerX", 0),
                            GetFloat(p, "centerY", 0),
                            GetFloat(p, "centerZ", 0),
                            GetInt(p, "mapId", 0),
                            GetUInt(p, "collectItemEntry", 0),
                            GetInt(p, "collectItemCount", 0),
                            GetKillRequirements(p, "killRequirements"),
                            GetItemCollectionRequirements(p, "collectItems")
                        );
                    
                    case "wait":
                        return new WaitTask(GetDouble(p, "seconds", 1.0));
                    
                    case "logmessage":
                        return new LogMessageTask(
                            GetString(p, "message"),
                            ParseLogLevel(GetString(p, "level", "Info"))
                        );

                    case "equipitems":
                        return new EquipItemTask();

                    case "sellitems":
                        return new SellItemsTask(GetUInt(p, "npcEntry", 0));

                    case "learnspells":
                        return new LearnSpellsTask(
                            GetUInt(p, "npcEntry", 0),
                            GetUIntArray(p, "spellIds"),
                            GetClassSpellsMap(p, "classSpells"),
                            GetClassQuestMap(p, "classNPCs")
                        );

                    case "useobject":
                        return new UseObjectTask(
                            GetUInt(p, "objectEntry"),
                            GetInt(p, "useCount", 1),
                            GetFloat(p, "searchRadius", 50f),
                            GetBool(p, "waitForLoot", false),
                            GetFloat(p, "maxWaitSeconds", 5f)
                        );

                    case "adventure":
                        return new AdventureTask(
                            GetUIntArray(p, "targetEntries"),
                            GetUIntArray(p, "objectEntries"),
                            GetInt(p, "killCount", 0),
                            GetFloat(p, "searchRadius", 50f),
                            GetFloat(p, "maxDurationSeconds", 0),
                            GetFloat(p, "centerX", 0),
                            GetFloat(p, "centerY", 0),
                            GetFloat(p, "centerZ", 0),
                            GetInt(p, "mapId", 0),
                            GetUInt(p, "collectItemEntry", 0),
                            GetInt(p, "collectItemCount", 0),
                            GetKillRequirements(p, "killRequirements"),
                            GetItemCollectionRequirements(p, "collectItems"),
                            GetObjectUseRequirements(p, "objectRequirements"),
                            GetBool(p, "objectsGiveLoot", false),
                            GetBool(p, "defendSelf", true)
                        );

                    // Assertion tasks for test framework
                    case "assertquestinlog":
                        return new AssertQuestInLogTask(
                            GetUInt(p, "questId"),
                            GetString(p, "message", null)
                        );

                    case "assertquestnotinlog":
                        return new AssertQuestNotInLogTask(
                            GetUInt(p, "questId"),
                            GetString(p, "message", null)
                        );

                    case "asserthasitem":
                        return new AssertHasItemTask(
                            GetUInt(p, "itemEntry"),
                            GetInt(p, "minCount", 1),
                            GetString(p, "message", null)
                        );

                    case "assertlevel":
                        return new AssertLevelTask(
                            GetUInt(p, "minLevel"),
                            GetString(p, "message", null)
                        );

                    case "movetotaskstart":
                        return new MoveToTaskStartTask(
                            GetNullableInt(p, "taskIndex"),
                            GetNullableInt(p, "relativeOffset")
                        );

                    default:
                        throw new NotSupportedException($"Unknown task type: {taskData.Type}");
                }
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException($"Error creating task of type '{taskData.Type}': {ex.Message}", ex);
            }
        }
        
        private static string GetString(Dictionary<string, JsonElement> p, string key, string defaultValue = "")
        {
            return p != null && p.ContainsKey(key) ? p[key].GetString() : defaultValue;
        }
        
        private static int GetInt(Dictionary<string, JsonElement> p, string key, int defaultValue = 0)
        {
            return p != null && p.ContainsKey(key) ? p[key].GetInt32() : defaultValue;
        }
        
        private static uint GetUInt(Dictionary<string, JsonElement> p, string key, uint defaultValue = 0)
        {
            return p != null && p.ContainsKey(key) ? p[key].GetUInt32() : defaultValue;
        }
        
        private static uint[] GetUIntArray(Dictionary<string, JsonElement> p, string key)
        {
            if (p == null || !p.ContainsKey(key))
                return null;
            
            var element = p[key];
            if (element.ValueKind != JsonValueKind.Array)
                return null;
            
            var list = new List<uint>();
            foreach (var item in element.EnumerateArray())
            {
                list.Add(item.GetUInt32());
            }
            return list.ToArray();
        }
        
        private static float GetFloat(Dictionary<string, JsonElement> p, string key, float defaultValue = 0.0f)
        {
            return p != null && p.ContainsKey(key) ? p[key].GetSingle() : defaultValue;
        }
        
        private static double GetDouble(Dictionary<string, JsonElement> p, string key, double defaultValue = 0.0)
        {
            return p != null && p.ContainsKey(key) ? p[key].GetDouble() : defaultValue;
        }

        private static bool GetBool(Dictionary<string, JsonElement> p, string key, bool defaultValue = false)
        {
            return p != null && p.ContainsKey(key) ? p[key].GetBoolean() : defaultValue;
        }

        private static int? GetNullableInt(Dictionary<string, JsonElement> p, string key)
        {
            if (p == null || !p.ContainsKey(key))
                return null;
            return p[key].GetInt32();
        }

        private static LogLevel ParseLogLevel(string level)
        {
            return Enum.TryParse<LogLevel>(level, true, out var result) ? result : LogLevel.Info;
        }

        private static Dictionary<Class, uint> GetClassQuestMap(Dictionary<string, JsonElement> p, string key)
        {
            if (p == null || !p.TryGetValue(key, out var element) || element.ValueKind != JsonValueKind.Object)
                return null;

            var map = new Dictionary<Class, uint>();
            foreach (var prop in element.EnumerateObject())
            {
                if (Enum.TryParse<Class>(prop.Name, true, out var classEnum))
                {
                    map[classEnum] = prop.Value.GetUInt32();
                }
            }
            return map.Count > 0 ? map : null;
        }

        private static List<KillRequirement> GetKillRequirements(Dictionary<string, JsonElement> p, string key)
        {
            if (p == null || !p.TryGetValue(key, out var element) || element.ValueKind != JsonValueKind.Array)
                return null;

            var list = new List<KillRequirement>();
            foreach (var item in element.EnumerateArray())
            {
                list.Add(new KillRequirement
                {
                    Entry = item.GetProperty("entry").GetUInt32(),
                    Count = item.GetProperty("count").GetInt32()
                });
            }
            return list.Count > 0 ? list : null;
        }

        private static List<ItemCollectionRequirement> GetItemCollectionRequirements(Dictionary<string, JsonElement> p, string key)
        {
            if (p == null || !p.TryGetValue(key, out var element) || element.ValueKind != JsonValueKind.Array)
                return null;

            var list = new List<ItemCollectionRequirement>();
            foreach (var item in element.EnumerateArray())
            {
                var req = new ItemCollectionRequirement
                {
                    ItemEntry = item.GetProperty("itemEntry").GetUInt32(),
                    Count = item.GetProperty("count").GetInt32()
                };

                // Parse optional droppedBy - supports both array and single number
                if (item.TryGetProperty("droppedBy", out var droppedByElement))
                {
                    req.DroppedBy = new HashSet<uint>();
                    if (droppedByElement.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var entry in droppedByElement.EnumerateArray())
                        {
                            req.DroppedBy.Add(entry.GetUInt32());
                        }
                    }
                    else if (droppedByElement.ValueKind == JsonValueKind.Number)
                    {
                        req.DroppedBy.Add(droppedByElement.GetUInt32());
                    }
                }

                list.Add(req);
            }
            return list.Count > 0 ? list : null;
        }

        private static Dictionary<Class, uint[]> GetClassSpellsMap(Dictionary<string, JsonElement> p, string key)
        {
            if (p == null || !p.TryGetValue(key, out var element) || element.ValueKind != JsonValueKind.Object)
                return null;

            var map = new Dictionary<Class, uint[]>();
            foreach (var prop in element.EnumerateObject())
            {
                if (Enum.TryParse<Class>(prop.Name, true, out var classEnum))
                {
                    var spells = new List<uint>();
                    foreach (var spell in prop.Value.EnumerateArray())
                    {
                        spells.Add(spell.GetUInt32());
                    }
                    map[classEnum] = spells.ToArray();
                }
            }
            return map.Count > 0 ? map : null;
        }

        private static List<ObjectUseRequirement> GetObjectUseRequirements(Dictionary<string, JsonElement> p, string key)
        {
            if (p == null || !p.TryGetValue(key, out var element) || element.ValueKind != JsonValueKind.Array)
                return null;

            var list = new List<ObjectUseRequirement>();
            foreach (var item in element.EnumerateArray())
            {
                list.Add(new ObjectUseRequirement
                {
                    Entry = item.GetProperty("entry").GetUInt32(),
                    Count = item.GetProperty("count").GetInt32()
                });
            }
            return list.Count > 0 ? list : null;
        }
    }
}
