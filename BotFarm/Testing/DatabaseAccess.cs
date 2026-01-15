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

        #region Snapshot Methods

        /// <summary>
        /// Creates the snapshot tables if they don't exist
        /// </summary>
        public void CreateSnapshotTables()
        {
            if (connection == null || connection.State != System.Data.ConnectionState.Open)
            {
                Console.WriteLine($"[MySQL] Cannot create snapshot tables - connection not open");
                return;
            }

            try
            {
                // Main snapshot table
                using var cmd1 = new MySqlCommand(@"
                    CREATE TABLE IF NOT EXISTS test_snapshots (
                        id INT AUTO_INCREMENT PRIMARY KEY,
                        name VARCHAR(64) NOT NULL UNIQUE,
                        character_guid INT UNSIGNED NOT NULL,
                        level TINYINT UNSIGNED NOT NULL,
                        xp INT UNSIGNED NOT NULL DEFAULT 0,
                        money INT UNSIGNED NOT NULL DEFAULT 0,
                        position_x FLOAT NOT NULL,
                        position_y FLOAT NOT NULL,
                        position_z FLOAT NOT NULL,
                        map_id SMALLINT UNSIGNED NOT NULL,
                        orientation FLOAT NOT NULL DEFAULT 0,
                        created_at DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP,
                        INDEX idx_name (name)
                    )", connection);
                cmd1.ExecuteNonQuery();

                // Quest snapshot table
                using var cmd2 = new MySqlCommand(@"
                    CREATE TABLE IF NOT EXISTS test_snapshot_quests (
                        id INT AUTO_INCREMENT PRIMARY KEY,
                        snapshot_name VARCHAR(64) NOT NULL,
                        quest_id INT UNSIGNED NOT NULL,
                        UNIQUE KEY unique_snapshot_quest (snapshot_name, quest_id),
                        INDEX idx_snapshot_name (snapshot_name)
                    )", connection);
                cmd2.ExecuteNonQuery();

                Console.WriteLine($"[MySQL] Snapshot tables created/verified");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[MySQL] Failed to create snapshot tables: {ex.Message}");
            }
        }

        /// <summary>
        /// Check if a snapshot exists
        /// </summary>
        public bool SnapshotExists(string snapshotName)
        {
            if (connection == null || connection.State != System.Data.ConnectionState.Open)
            {
                return false;
            }

            try
            {
                using var cmd = new MySqlCommand(
                    "SELECT COUNT(*) FROM test_snapshots WHERE name = @name", connection);
                cmd.Parameters.AddWithValue("@name", snapshotName);
                var count = Convert.ToInt32(cmd.ExecuteScalar());
                return count > 0;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Save character state to a snapshot
        /// </summary>
        public bool SaveSnapshot(string snapshotName, string characterName)
        {
            if (connection == null || connection.State != System.Data.ConnectionState.Open)
            {
                Console.WriteLine($"[MySQL] Cannot save snapshot - connection not open");
                return false;
            }

            try
            {
                // Get character data
                using var charCmd = new MySqlCommand(@"
                    SELECT guid, level, xp, money, position_x, position_y, position_z, map, orientation
                    FROM characters WHERE name = @name", connection);
                charCmd.Parameters.AddWithValue("@name", characterName);

                using var reader = charCmd.ExecuteReader();
                if (!reader.Read())
                {
                    Console.WriteLine($"[MySQL] Character '{characterName}' not found");
                    return false;
                }

                var guid = reader.GetUInt32("guid");
                var level = reader.GetByte("level");
                var xp = reader.GetUInt32("xp");
                var money = reader.GetUInt32("money");
                var posX = reader.GetFloat("position_x");
                var posY = reader.GetFloat("position_y");
                var posZ = reader.GetFloat("position_z");
                var mapId = reader.GetUInt16("map");
                var orientation = reader.GetFloat("orientation");
                reader.Close();

                // Delete existing snapshot with same name
                using var deleteCmd = new MySqlCommand(
                    "DELETE FROM test_snapshots WHERE name = @name", connection);
                deleteCmd.Parameters.AddWithValue("@name", snapshotName);
                deleteCmd.ExecuteNonQuery();

                // Also delete related quest data
                using var deleteQuestsCmd = new MySqlCommand(
                    "DELETE FROM test_snapshot_quests WHERE snapshot_name = @name", connection);
                deleteQuestsCmd.Parameters.AddWithValue("@name", snapshotName);
                deleteQuestsCmd.ExecuteNonQuery();

                // Insert new snapshot
                using var insertCmd = new MySqlCommand(@"
                    INSERT INTO test_snapshots
                    (name, character_guid, level, xp, money, position_x, position_y, position_z, map_id, orientation)
                    VALUES (@name, @guid, @level, @xp, @money, @posX, @posY, @posZ, @mapId, @orientation)",
                    connection);
                insertCmd.Parameters.AddWithValue("@name", snapshotName);
                insertCmd.Parameters.AddWithValue("@guid", guid);
                insertCmd.Parameters.AddWithValue("@level", level);
                insertCmd.Parameters.AddWithValue("@xp", xp);
                insertCmd.Parameters.AddWithValue("@money", money);
                insertCmd.Parameters.AddWithValue("@posX", posX);
                insertCmd.Parameters.AddWithValue("@posY", posY);
                insertCmd.Parameters.AddWithValue("@posZ", posZ);
                insertCmd.Parameters.AddWithValue("@mapId", mapId);
                insertCmd.Parameters.AddWithValue("@orientation", orientation);
                insertCmd.ExecuteNonQuery();

                // Save completed quests
                using var questsCmd = new MySqlCommand(@"
                    SELECT quest FROM character_queststatus_rewarded WHERE guid = @guid", connection);
                questsCmd.Parameters.AddWithValue("@guid", guid);

                var quests = new List<uint>();
                using (var questReader = questsCmd.ExecuteReader())
                {
                    while (questReader.Read())
                    {
                        quests.Add(questReader.GetUInt32("quest"));
                    }
                }

                foreach (var questId in quests)
                {
                    using var insertQuestCmd = new MySqlCommand(@"
                        INSERT IGNORE INTO test_snapshot_quests (snapshot_name, quest_id)
                        VALUES (@name, @questId)", connection);
                    insertQuestCmd.Parameters.AddWithValue("@name", snapshotName);
                    insertQuestCmd.Parameters.AddWithValue("@questId", questId);
                    insertQuestCmd.ExecuteNonQuery();
                }

                Console.WriteLine($"[MySQL] Saved snapshot '{snapshotName}' for {characterName} (level {level}, {quests.Count} quests)");
                return true;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[MySQL] Failed to save snapshot: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Restore character state from a snapshot
        /// </summary>
        public bool RestoreSnapshot(string snapshotName, string characterName)
        {
            if (connection == null || connection.State != System.Data.ConnectionState.Open)
            {
                Console.WriteLine($"[MySQL] Cannot restore snapshot - connection not open");
                return false;
            }

            try
            {
                // Get snapshot data
                using var snapCmd = new MySqlCommand(@"
                    SELECT level, xp, money, position_x, position_y, position_z, map_id, orientation
                    FROM test_snapshots WHERE name = @name", connection);
                snapCmd.Parameters.AddWithValue("@name", snapshotName);

                using var reader = snapCmd.ExecuteReader();
                if (!reader.Read())
                {
                    Console.WriteLine($"[MySQL] Snapshot '{snapshotName}' not found");
                    return false;
                }

                var level = reader.GetByte("level");
                var xp = reader.GetUInt32("xp");
                var money = reader.GetUInt32("money");
                var posX = reader.GetFloat("position_x");
                var posY = reader.GetFloat("position_y");
                var posZ = reader.GetFloat("position_z");
                var mapId = reader.GetUInt16("map_id");
                var orientation = reader.GetFloat("orientation");
                reader.Close();

                // Get character GUID
                using var guidCmd = new MySqlCommand(
                    "SELECT guid FROM characters WHERE name = @name", connection);
                guidCmd.Parameters.AddWithValue("@name", characterName);
                var guidResult = guidCmd.ExecuteScalar();
                if (guidResult == null)
                {
                    Console.WriteLine($"[MySQL] Character '{characterName}' not found");
                    return false;
                }
                var guid = Convert.ToUInt64(guidResult);

                // Update character state
                using var updateCmd = new MySqlCommand(@"
                    UPDATE characters SET
                        level = @level,
                        xp = @xp,
                        money = @money,
                        position_x = @posX,
                        position_y = @posY,
                        position_z = @posZ,
                        map = @mapId,
                        orientation = @orientation
                    WHERE name = @name", connection);
                updateCmd.Parameters.AddWithValue("@level", level);
                updateCmd.Parameters.AddWithValue("@xp", xp);
                updateCmd.Parameters.AddWithValue("@money", money);
                updateCmd.Parameters.AddWithValue("@posX", posX);
                updateCmd.Parameters.AddWithValue("@posY", posY);
                updateCmd.Parameters.AddWithValue("@posZ", posZ);
                updateCmd.Parameters.AddWithValue("@mapId", mapId);
                updateCmd.Parameters.AddWithValue("@orientation", orientation);
                updateCmd.Parameters.AddWithValue("@name", characterName);
                updateCmd.ExecuteNonQuery();

                // Clear existing quest completions
                using var clearQuestsCmd = new MySqlCommand(
                    "DELETE FROM character_queststatus_rewarded WHERE guid = @guid", connection);
                clearQuestsCmd.Parameters.AddWithValue("@guid", guid);
                clearQuestsCmd.ExecuteNonQuery();

                // Restore quest completions from snapshot
                using var questsCmd = new MySqlCommand(
                    "SELECT quest_id FROM test_snapshot_quests WHERE snapshot_name = @name", connection);
                questsCmd.Parameters.AddWithValue("@name", snapshotName);

                var quests = new List<uint>();
                using (var questReader = questsCmd.ExecuteReader())
                {
                    while (questReader.Read())
                    {
                        quests.Add(questReader.GetUInt32("quest_id"));
                    }
                }

                foreach (var questId in quests)
                {
                    using var insertQuestCmd = new MySqlCommand(@"
                        INSERT IGNORE INTO character_queststatus_rewarded (guid, quest, active)
                        VALUES (@guid, @questId, 0)", connection);
                    insertQuestCmd.Parameters.AddWithValue("@guid", guid);
                    insertQuestCmd.Parameters.AddWithValue("@questId", questId);
                    insertQuestCmd.ExecuteNonQuery();
                }

                Console.WriteLine($"[MySQL] Restored snapshot '{snapshotName}' to {characterName} (level {level}, {quests.Count} quests)");
                return true;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[MySQL] Failed to restore snapshot: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Delete a snapshot
        /// </summary>
        public bool DeleteSnapshot(string snapshotName)
        {
            if (connection == null || connection.State != System.Data.ConnectionState.Open)
            {
                Console.WriteLine($"[MySQL] Cannot delete snapshot - connection not open");
                return false;
            }

            try
            {
                // Delete quest data first (if not using CASCADE)
                using var deleteQuestsCmd = new MySqlCommand(
                    "DELETE FROM test_snapshot_quests WHERE snapshot_name = @name", connection);
                deleteQuestsCmd.Parameters.AddWithValue("@name", snapshotName);
                deleteQuestsCmd.ExecuteNonQuery();

                // Delete main snapshot
                using var deleteCmd = new MySqlCommand(
                    "DELETE FROM test_snapshots WHERE name = @name", connection);
                deleteCmd.Parameters.AddWithValue("@name", snapshotName);
                var rowsAffected = deleteCmd.ExecuteNonQuery();

                if (rowsAffected > 0)
                {
                    Console.WriteLine($"[MySQL] Deleted snapshot '{snapshotName}'");
                    return true;
                }
                else
                {
                    Console.WriteLine($"[MySQL] Snapshot '{snapshotName}' not found");
                    return false;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[MySQL] Failed to delete snapshot: {ex.Message}");
                return false;
            }
        }

        #endregion

        public void Dispose()
        {
            connection?.Close();
            connection?.Dispose();
        }
    }
}
