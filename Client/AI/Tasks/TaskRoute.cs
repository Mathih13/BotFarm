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
