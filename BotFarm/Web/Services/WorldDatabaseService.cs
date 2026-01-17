using System;
using System.Collections.Generic;
using System.Linq;
using BotFarm.Web.Models;
using MySql.Data.MySqlClient;

namespace BotFarm.Web.Services
{
    /// <summary>
    /// Provides entity name lookups from the TrinityCore world database.
    /// Used by the Route Editor UI to display entity names for entry IDs.
    /// </summary>
    public class WorldDatabaseService : IDisposable
    {
        private readonly string connectionString;
        private MySqlConnection connection;
        private bool isConnected;

        public bool IsConnected => isConnected;

        public WorldDatabaseService(string host, int port, string user, string password, string worldDb)
        {
            connectionString = $"Server={host};Port={port};Database={worldDb};Uid={user};Pwd={password};";
            Console.WriteLine($"[WorldDB] Configured for {user}@{host}:{port}/{worldDb}");
        }

        public void Connect()
        {
            Console.WriteLine($"[WorldDB] Attempting connection...");
            try
            {
                connection = new MySqlConnection(connectionString);
                connection.Open();
                isConnected = true;
                Console.WriteLine($"[WorldDB] Connected to world database successfully");
            }
            catch (MySqlException ex)
            {
                Console.WriteLine($"[WorldDB] MySQL error {ex.Number}: {ex.Message}");
                isConnected = false;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[WorldDB] Failed to connect: {ex.GetType().Name}: {ex.Message}");
                isConnected = false;
            }
        }

        /// <summary>
        /// Get NPC names by entry IDs from creature_template
        /// </summary>
        public Dictionary<uint, string> GetNPCNames(uint[] entries)
        {
            if (!isConnected || entries == null || entries.Length == 0)
                return new Dictionary<uint, string>();

            return ExecuteLookupQuery(
                "SELECT entry, name FROM creature_template WHERE entry IN ({0})",
                entries,
                "entry",
                "name"
            );
        }

        /// <summary>
        /// Get quest names by quest IDs from quest_template
        /// </summary>
        public Dictionary<uint, string> GetQuestNames(uint[] questIds)
        {
            if (!isConnected || questIds == null || questIds.Length == 0)
                return new Dictionary<uint, string>();

            return ExecuteLookupQuery(
                "SELECT ID, LogTitle FROM quest_template WHERE ID IN ({0})",
                questIds,
                "ID",
                "LogTitle"
            );
        }

        /// <summary>
        /// Get item names by entry IDs from item_template
        /// </summary>
        public Dictionary<uint, string> GetItemNames(uint[] entries)
        {
            if (!isConnected || entries == null || entries.Length == 0)
                return new Dictionary<uint, string>();

            return ExecuteLookupQuery(
                "SELECT entry, name FROM item_template WHERE entry IN ({0})",
                entries,
                "entry",
                "name"
            );
        }

        /// <summary>
        /// Get game object names by entry IDs from gameobject_template
        /// </summary>
        public Dictionary<uint, string> GetGameObjectNames(uint[] entries)
        {
            if (!isConnected || entries == null || entries.Length == 0)
                return new Dictionary<uint, string>();

            return ExecuteLookupQuery(
                "SELECT entry, name FROM gameobject_template WHERE entry IN ({0})",
                entries,
                "entry",
                "name"
            );
        }

        // ============ Search Methods ============

        /// <summary>
        /// Search NPCs by name from creature_template
        /// </summary>
        public List<EntitySearchResult> SearchNPCs(string query, int limit = 20)
        {
            if (!isConnected || string.IsNullOrWhiteSpace(query))
                return new List<EntitySearchResult>();

            return ExecuteSearchQuery(
                "SELECT entry, name FROM creature_template WHERE name LIKE @query ORDER BY entry LIMIT @limit",
                query,
                limit,
                "entry",
                "name"
            );
        }

        /// <summary>
        /// Search quests by title from quest_template
        /// </summary>
        public List<EntitySearchResult> SearchQuests(string query, int limit = 20)
        {
            if (!isConnected || string.IsNullOrWhiteSpace(query))
                return new List<EntitySearchResult>();

            return ExecuteSearchQuery(
                "SELECT ID, LogTitle FROM quest_template WHERE LogTitle LIKE @query ORDER BY ID LIMIT @limit",
                query,
                limit,
                "ID",
                "LogTitle"
            );
        }

        /// <summary>
        /// Search items by name from item_template
        /// </summary>
        public List<EntitySearchResult> SearchItems(string query, int limit = 20)
        {
            if (!isConnected || string.IsNullOrWhiteSpace(query))
                return new List<EntitySearchResult>();

            return ExecuteSearchQuery(
                "SELECT entry, name FROM item_template WHERE name LIKE @query ORDER BY entry LIMIT @limit",
                query,
                limit,
                "entry",
                "name"
            );
        }

        /// <summary>
        /// Search game objects by name from gameobject_template
        /// </summary>
        public List<EntitySearchResult> SearchGameObjects(string query, int limit = 20)
        {
            if (!isConnected || string.IsNullOrWhiteSpace(query))
                return new List<EntitySearchResult>();

            return ExecuteSearchQuery(
                "SELECT entry, name FROM gameobject_template WHERE name LIKE @query ORDER BY entry LIMIT @limit",
                query,
                limit,
                "entry",
                "name"
            );
        }

        private List<EntitySearchResult> ExecuteSearchQuery(
            string queryTemplate,
            string searchQuery,
            int limit,
            string idColumn,
            string nameColumn)
        {
            var result = new List<EntitySearchResult>();

            if (connection == null || connection.State != System.Data.ConnectionState.Open)
            {
                Console.WriteLine($"[WorldDB] Cannot execute search - connection not open");
                return result;
            }

            try
            {
                using var cmd = new MySqlCommand(queryTemplate, connection);
                cmd.Parameters.AddWithValue("@query", $"%{searchQuery}%");
                cmd.Parameters.AddWithValue("@limit", limit);

                using var reader = cmd.ExecuteReader();
                while (reader.Read())
                {
                    var id = Convert.ToUInt32(reader[idColumn]);
                    var name = reader[nameColumn]?.ToString() ?? "";
                    result.Add(new EntitySearchResult { Entry = id, Name = name });
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[WorldDB] Search query failed: {ex.Message}");
            }

            return result;
        }

        private Dictionary<uint, string> ExecuteLookupQuery(
            string queryTemplate,
            uint[] ids,
            string idColumn,
            string nameColumn)
        {
            var result = new Dictionary<uint, string>();

            if (connection == null || connection.State != System.Data.ConnectionState.Open)
            {
                Console.WriteLine($"[WorldDB] Cannot execute query - connection not open");
                return result;
            }

            try
            {
                // Build parameterized query
                var paramNames = ids.Select((_, i) => $"@id{i}").ToList();
                var query = string.Format(queryTemplate, string.Join(",", paramNames));

                using var cmd = new MySqlCommand(query, connection);

                for (int i = 0; i < ids.Length; i++)
                {
                    cmd.Parameters.AddWithValue($"@id{i}", ids[i]);
                }

                using var reader = cmd.ExecuteReader();
                while (reader.Read())
                {
                    var id = Convert.ToUInt32(reader[idColumn]);
                    var name = reader[nameColumn]?.ToString() ?? "";
                    result[id] = name;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[WorldDB] Query failed: {ex.Message}");
            }

            return result;
        }

        public void Dispose()
        {
            if (connection != null)
            {
                connection.Close();
                connection.Dispose();
                connection = null;
                isConnected = false;
            }
        }
    }
}
