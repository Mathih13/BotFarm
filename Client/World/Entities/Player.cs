using Client.World.Definitions;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Client.World.Entities
{
    public class Player : Unit
    {
        public bool IsGhost
        {
            get
            {
                return HasFlag(PlayerFlags.PLAYER_FLAGS_GHOST);
            }
        }

        public bool IsAlive
        {
            get
            {
                return this[UnitField.UNIT_FIELD_HEALTH] > 0 && !IsGhost;
            }
        }

        public bool HasFlag(PlayerFlags flag)
        {
            return (this[PlayerField.PLAYER_FLAGS] & (uint)flag) != 0;
        }

        public Position CorpsePosition
        {
            get;
            set;
        }

        #region Combat Properties
        /// <summary>
        /// Current health value
        /// </summary>
        public uint Health => this[UnitField.UNIT_FIELD_HEALTH];

        /// <summary>
        /// Maximum health value
        /// </summary>
        public uint MaxHealth => this[UnitField.UNIT_FIELD_MAXHEALTH];

        /// <summary>
        /// Health as a percentage (0-100)
        /// </summary>
        public float HealthPercent => MaxHealth > 0 ? (Health * 100f / MaxHealth) : 0;

        /// <summary>
        /// Current mana (POWER1)
        /// </summary>
        public uint Mana => this[UnitField.UNIT_FIELD_POWER1];

        /// <summary>
        /// Maximum mana
        /// </summary>
        public uint MaxMana => this[UnitField.UNIT_FIELD_MAXPOWER1];

        /// <summary>
        /// Mana as a percentage (0-100)
        /// </summary>
        public float ManaPercent => MaxMana > 0 ? (Mana * 100f / MaxMana) : 100;

        /// <summary>
        /// Current rage (POWER2) - stored as rage * 10
        /// </summary>
        public uint Rage => this[UnitField.UNIT_FIELD_POWER2] / 10;

        /// <summary>
        /// Current energy (POWER4)
        /// </summary>
        public uint Energy => this[UnitField.UNIT_FIELD_POWER4];

        /// <summary>
        /// Player level
        /// </summary>
        public uint Level => this[UnitField.UNIT_FIELD_LEVEL];

        /// <summary>
        /// Current target GUID (low part)
        /// </summary>
        public ulong TargetGuid
        {
            get
            {
                uint low = this[UnitField.UNIT_FIELD_TARGET];
                uint high = this[(int)UnitField.UNIT_FIELD_TARGET + 1];
                return ((ulong)high << 32) | low;
            }
        }

        /// <summary>
        /// Returns true if player has a target
        /// </summary>
        public bool HasTarget => TargetGuid != 0;
        #endregion
    }
}
