using System;
using System.Collections.Generic;

namespace Client.AI.Tasks
{
    public class TaskRoute
    {
        public string Name { get; set; }
        public string Description { get; set; }
        public List<ITask> Tasks { get; set; }
        public bool Loop { get; set; }
        public string FilePath { get; set; }

        /// <summary>
        /// Optional harness settings for test framework - defines bot requirements
        /// </summary>
        public HarnessSettings Harness { get; set; }

        /// <summary>
        /// Returns true if this route has harness settings configured
        /// </summary>
        public bool HasHarness => Harness != null;

        public TaskRoute()
        {
            Tasks = new List<ITask>();
            Loop = false;
        }

        public TaskRoute(string name, string description = "")
        {
            Name = name;
            Description = description;
            Tasks = new List<ITask>();
            Loop = false;
        }

        public void AddTask(ITask task)
        {
            Tasks.Add(task);
        }
    }
}
