namespace MDTPro.Data {
    /// <summary>In-game injury data for pre-filling injury reports. Source is either DamageTrackerFramework (detailed) or ped health fallback.</summary>
    public class InjuryGameData {
        /// <summary>"DamageTracker" when from DTF, "Health" when from ped health only.</summary>
        public string Source;
        /// <summary>Suggested injury type (e.g. Gunshot, Blunt trauma, Vehicle impact).</summary>
        public string InjuryType;
        /// <summary>Suggested severity: Minor, Moderate, Serious, Critical.</summary>
        public string Severity;
        /// <summary>Suggested treatment (e.g. Transported to hospital, EMS on scene).</summary>
        public string Treatment;
        /// <summary>Body region if from DTF: Head, Torso, Arms, Legs.</summary>
        public string BodyRegion;
        /// <summary>Weapon/damage group if from DTF: Bullet, Melee, Vehicle, Fall, etc.</summary>
        public string WeaponGroup;
        /// <summary>Health damage amount if from DTF.</summary>
        public int? DamageAmount;
        /// <summary>Armour damage if from DTF.</summary>
        public int? ArmourDamage;
        /// <summary>Current health when using health fallback (or after damage for DTF).</summary>
        public int? Health;
        /// <summary>Max health (typically 200).</summary>
        public int? MaxHealth;
        /// <summary>Current armour.</summary>
        public int? Armor;
        /// <summary>Whether the victim was alive at time of capture (DTF) or currently (health).</summary>
        public bool? VictimAlive;
        /// <summary>Optional free-text description from game (e.g. "Gunshot, Head, 45 damage").</summary>
        public string Description;
    }
}
