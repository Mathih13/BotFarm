using Client;
using Client.AI;
using Client.World.Entities;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace BotFarm.AI
{
    class FollowGroupLeaderAI : IStrategicAI
    {
        int scheduledAction;
        AutomatedGame game;
        bool followingStarted = false;

        public bool Activate(AutomatedGame game)
        {
            this.game = game;
            followingStarted = false;
            ScheduleFollowLeader();
            return true;
        }

        void ScheduleFollowLeader()
        {
            scheduledAction = game.ScheduleAction(() =>
            {
                if (!game.Player.IsAlive)
                {
                    game.Log("FollowGroupLeaderAI: Player is dead, stopping follow", Client.UI.LogLevel.Debug);
                    game.CancelActionsByFlag(ActionFlag.Movement);
                    followingStarted = false;
                    return;
                }

                // Check if we are in a party and follow the party leader
                if (game.GroupLeaderGuid == 0)
                {
                    game.Log("FollowGroupLeaderAI: No group leader GUID set", Client.UI.LogLevel.Debug);
                    if (followingStarted)
                    {
                        game.CancelActionsByFlag(ActionFlag.Movement);
                        followingStarted = false;
                    }
                    return;
                }

                WorldObject groupLeader;
                if (game.Objects.TryGetValue(game.GroupLeaderGuid, out groupLeader))
                {
                    // Only start following once - Follow() handles continuous updates
                    if (!followingStarted)
                    {
                        game.Log($"FollowGroupLeaderAI: Starting to follow leader GUID 0x{game.GroupLeaderGuid:X}", Client.UI.LogLevel.Info);
                        game.CancelActionsByFlag(ActionFlag.Movement);
                        game.Follow(groupLeader);
                        followingStarted = true;
                    }
                }
                else
                {
                    game.Log($"FollowGroupLeaderAI: Leader GUID 0x{game.GroupLeaderGuid:X} not in Objects (out of range or not visible)", Client.UI.LogLevel.Warning);
                    if (followingStarted)
                    {
                        game.CancelActionsByFlag(ActionFlag.Movement);
                        followingStarted = false;
                    }
                }
            }, DateTime.Now.AddSeconds(1), new TimeSpan(0, 0, 30));
        }

        public bool AllowPause()
        {
            return true;
        }

        public void Deactivate()
        {
            game.CancelAction(scheduledAction);
        }

        public void Pause()
        {
            game.CancelAction(scheduledAction);
        }

        public void Resume()
        {
            ScheduleFollowLeader();
        }

        public void Update()
        {
            // Nothing to do
        }
    }
}
