using Client;
using Client.AI.Tasks;
using Client.UI;
using Client.World;
using Client.World.Entities;
using System;

namespace BotFarm.AI.Tasks
{
    public class MoveToLocationTask : BaseTask
    {
        private readonly Position destination;
        private readonly float arrivalThreshold;
        private bool moveStarted = false;
        private int movementActionId = -1;
        
        public override string Name => $"MoveToLocation({destination.X:F1}, {destination.Y:F1}, {destination.Z:F1})";
        
        public MoveToLocationTask(float x, float y, float z, int mapId, float arrivalThreshold = 3.0f)
        {
            destination = new Position(x, y, z, 0.0f, mapId);
            this.arrivalThreshold = arrivalThreshold;
        }
        
        public MoveToLocationTask(Position destination, float arrivalThreshold = 3.0f)
        {
            this.destination = destination;
            this.arrivalThreshold = arrivalThreshold;
        }
        
        public override bool Start(AutomatedGame game)
        {
            if (!base.Start(game))
                return false;
            
            var botGame = game as BotFarm.BotGame;
            if (botGame == null)
            {
                game.Log($"MoveToLocationTask: Game is not a BotGame instance", LogLevel.Error);
                return false;
            }
            
            if (game.Player.MapID != destination.MapID)
            {
                game.Log($"MoveToLocationTask: Player is on map {game.Player.MapID}, destination is on map {destination.MapID}", LogLevel.Error);
                return false;
            }
            
            return true;
        }
        
        protected override TaskResult UpdateTask(AutomatedGame game)
        {
            var botGame = game as BotFarm.BotGame;
            if (botGame == null)
                return TaskResult.Failed;
            
            if (!game.Player.IsAlive)
            {
                ErrorMessage = "Player is dead";
                game.Log($"MoveToLocationTask: {ErrorMessage}", LogLevel.Warning);
                return TaskResult.Failed;
            }
            
            float distance = (destination.GetPosition() - game.Player).Length;
            
            if (distance <= arrivalThreshold)
            {
                game.Log($"MoveToLocationTask: Arrived at destination (distance: {distance:F2}m)", LogLevel.Debug);
                return TaskResult.Success;
            }
            
            if (!moveStarted)
            {
                game.Log($"MoveToLocationTask: Starting move to destination (distance: {distance:F2}m)", LogLevel.Debug);
                game.CancelActionsByFlag(ActionFlag.Movement);
                botGame.MoveTo(destination);
                moveStarted = true;
            }
            
            return TaskResult.Running;
        }
        
        public override void Cleanup(AutomatedGame game)
        {
            if (movementActionId >= 0)
            {
                game.CancelAction(movementActionId);
            }
        }
    }
}
