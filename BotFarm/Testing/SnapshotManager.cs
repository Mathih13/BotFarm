using System;

namespace BotFarm.Testing
{
    /// <summary>
    /// Manages character snapshots for test setup and restoration.
    /// Wraps DatabaseAccess snapshot methods with additional orchestration.
    /// </summary>
    public class SnapshotManager
    {
        private readonly DatabaseAccess database;
        private bool tablesInitialized = false;

        public SnapshotManager(DatabaseAccess database)
        {
            this.database = database;
        }

        /// <summary>
        /// Ensure snapshot tables exist in the database
        /// </summary>
        public void EnsureTablesExist()
        {
            if (tablesInitialized) return;

            if (database == null)
            {
                Console.WriteLine("[Snapshot] Cannot initialize - no database connection");
                return;
            }

            database.CreateSnapshotTables();
            tablesInitialized = true;
        }

        /// <summary>
        /// Check if the snapshot manager is available (database connected)
        /// </summary>
        public bool IsAvailable => database != null;

        /// <summary>
        /// Save a character's current state to a named snapshot
        /// </summary>
        /// <param name="snapshotName">Name for the snapshot (must be unique)</param>
        /// <param name="characterName">Character to snapshot</param>
        /// <returns>True if snapshot was saved successfully</returns>
        public bool SaveSnapshot(string snapshotName, string characterName)
        {
            if (database == null)
            {
                Console.WriteLine("[Snapshot] Cannot save - no database connection");
                return false;
            }

            EnsureTablesExist();
            return database.SaveSnapshot(snapshotName, characterName);
        }

        /// <summary>
        /// Restore a character's state from a named snapshot
        /// </summary>
        /// <param name="snapshotName">Name of the snapshot to restore</param>
        /// <param name="characterName">Character to restore to</param>
        /// <returns>True if snapshot was restored successfully</returns>
        public bool RestoreSnapshot(string snapshotName, string characterName)
        {
            if (database == null)
            {
                Console.WriteLine("[Snapshot] Cannot restore - no database connection");
                return false;
            }

            EnsureTablesExist();
            return database.RestoreSnapshot(snapshotName, characterName);
        }

        /// <summary>
        /// Check if a snapshot exists
        /// </summary>
        public bool SnapshotExists(string snapshotName)
        {
            if (database == null) return false;

            EnsureTablesExist();
            return database.SnapshotExists(snapshotName);
        }

        /// <summary>
        /// Delete a snapshot
        /// </summary>
        public bool DeleteSnapshot(string snapshotName)
        {
            if (database == null)
            {
                Console.WriteLine("[Snapshot] Cannot delete - no database connection");
                return false;
            }

            EnsureTablesExist();
            return database.DeleteSnapshot(snapshotName);
        }
    }
}
