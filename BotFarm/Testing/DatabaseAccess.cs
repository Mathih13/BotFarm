using System;
using System.Collections.Generic;
using MySql.Data.MySqlClient;

namespace BotFarm.Testing
{
    /// <summary>
    /// Provides direct MySQL database access for test harness operations
    /// that cannot be performed via Remote Access (RA) commands.
    /// </summary>
    public class DatabaseAccess : IDisposable
    {
        private readonly string connectionString;
        private MySqlConnection connection;

        public DatabaseAccess(string host, int port, string user, string password, string database)
        {
            connectionString = $"Server={host};Port={port};Database={database};Uid={user};Pwd={password};";
        }

        public void Connect()
        {
            connection = new MySqlConnection(connectionString);
            connection.Open();
        }

        /// <summary>
        /// Marks quests as completed for a character by inserting into character_queststatus_rewarded.
        /// This allows dependent quests to be accepted without actually completing the prerequisite.
        /// </summary>
        /// <param name="characterName">The character name to mark quests completed for</param>
        /// <param name="questIds">List of quest IDs to mark as completed</param>
        public void CompleteQuestsForCharacter(string characterName, List<uint> questIds)
        {
            if (questIds == null || questIds.Count == 0) return;

            if (connection == null || connection.State != System.Data.ConnectionState.Open)
            {
                Console.WriteLine($"[MySQL] Cannot complete quests - connection not open");
                return;
            }

            // Get character GUID by name
            using var guidCmd = new MySqlCommand(
                "SELECT guid FROM characters WHERE name = @name", connection);
            guidCmd.Parameters.AddWithValue("@name", characterName);

            var guidResult = guidCmd.ExecuteScalar();
            if (guidResult == null)
            {
                Console.WriteLine($"[MySQL] Character '{characterName}' not found in database");
                return;
            }

            var guid = Convert.ToUInt64(guidResult);

            foreach (var questId in questIds)
            {
                try
                {
                    // Insert into character_queststatus_rewarded
                    // Using INSERT IGNORE to handle re-running tests where quest is already completed
                    using var cmd = new MySqlCommand(
                        @"INSERT IGNORE INTO character_queststatus_rewarded
                          (guid, quest, active) VALUES (@guid, @quest, 0)",
                        connection);
                    cmd.Parameters.AddWithValue("@guid", guid);
                    cmd.Parameters.AddWithValue("@quest", questId);
                    var rowsAffected = cmd.ExecuteNonQuery();

                    if (rowsAffected > 0)
                    {
                        Console.WriteLine($"[MySQL] Marked quest {questId} as completed for {characterName}");
                    }
                    else
                    {
                        Console.WriteLine($"[MySQL] Quest {questId} already completed for {characterName}");
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[MySQL] Failed to complete quest {questId} for {characterName}: {ex.Message}");
                }
            }
        }

        public void Dispose()
        {
            connection?.Close();
            connection?.Dispose();
        }
    }
}
