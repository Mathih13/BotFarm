namespace Client.World.Definitions
{
    /// <summary>
    /// Represents information about an active aura/buff on a unit.
    /// Populated from SMSG_AURA_UPDATE and SMSG_AURA_UPDATE_ALL packets.
    /// </summary>
    public class AuraInfo
    {
        /// <summary>
        /// The spell ID of this aura
        /// </summary>
        public uint SpellId { get; set; }

        /// <summary>
        /// Aura flags (determines which fields are present in the packet)
        /// </summary>
        public byte Flags { get; set; }

        /// <summary>
        /// Caster level of the aura
        /// </summary>
        public byte Level { get; set; }

        /// <summary>
        /// Stack count for stackable auras
        /// </summary>
        public byte StackCount { get; set; }

        /// <summary>
        /// Remaining duration in milliseconds (-1 = infinite)
        /// </summary>
        public int Duration { get; set; }

        /// <summary>
        /// Maximum duration in milliseconds
        /// </summary>
        public int MaxDuration { get; set; }

        /// <summary>
        /// GUID of the unit that cast this aura
        /// </summary>
        public ulong CasterGuid { get; set; }

        /// <summary>
        /// The aura slot index (0-255)
        /// </summary>
        public byte Slot { get; set; }

        // Aura flag constants (from WoW 3.3.5a)
        public const byte AFLAG_EFFECT_1 = 0x01;
        public const byte AFLAG_EFFECT_2 = 0x02;
        public const byte AFLAG_EFFECT_3 = 0x04;
        public const byte AFLAG_NOT_CASTER = 0x08;
        public const byte AFLAG_POSITIVE = 0x10;
        public const byte AFLAG_DURATION = 0x20;
        public const byte AFLAG_ANY_EFFECT_AMOUNT_SENT = AFLAG_EFFECT_1 | AFLAG_EFFECT_2 | AFLAG_EFFECT_3;

        public bool HasCaster => (Flags & AFLAG_NOT_CASTER) == 0;
        public bool HasDuration => (Flags & AFLAG_DURATION) != 0;
        public bool IsPositive => (Flags & AFLAG_POSITIVE) != 0;
    }
}
