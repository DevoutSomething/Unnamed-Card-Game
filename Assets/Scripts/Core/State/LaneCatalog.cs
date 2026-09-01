using System.Collections.Generic;
using Game.Core.Util;

namespace Game.Core.Lanes
{
    /// <summary>
    /// Every lane type in the game, keyed by id. Hardcoded on purpose for now:
    /// a handful of lanes doesn't warrant an authoring pipeline yet. When it
    /// does, this is the single place to swap for a JSON loader (see
    /// AbilityDatabase for that pattern) — Lane only ever stores the id string,
    /// so nothing else has to change.
    /// </summary>
    public static class LaneCatalog
    {
        public static readonly LaneDefinition Withering = new LaneDefinition
        {
            Id = "withering",
            DisplayName = "Withering Field",
            Description = "Guys here get -1/-1.",
            AttackModifier = -1,
            HealthModifier = -1,
        };

        public static readonly LaneDefinition Bulwark = new LaneDefinition
        {
            Id = "bulwark",
            DisplayName = "Bulwark",
            Description = "Guys here get +0/+2.",
            HealthModifier = 2,
        };

        public static readonly LaneDefinition Volcanic = new LaneDefinition
        {
            Id = "volcanic",
            DisplayName = "Volcanic Vent",
            Description = "After each combat, 2 damage to every guy here.",
            DamageAllOnEnergyReset = 2,
        };

        public static readonly LaneDefinition Library = new LaneDefinition
        {
            Id = "library",
            DisplayName = "Old Library",
            Description = "Play a guy here: draw a card.",
            DrawOnGuyPlayed = 1,
        };


        static readonly List<LaneDefinition> _all = new List<LaneDefinition>
        {
            Withering, Bulwark, Volcanic, Library,
        };

        public static IReadOnlyList<LaneDefinition> All => _all;

        /// <summary>The definition for a Lane.LaneTypeId, or null for a plain
        /// lane (no effect) — callers treat null as "nothing to apply".</summary>
        public static LaneDefinition Get(string id)
        {
            if (string.IsNullOrEmpty(id)) return null;
            foreach (var def in _all)
                if (def.Id == id) return def;
            return null;
        }

        /// <summary>`count` DISTINCT lane types, drawn from the seeded match rng
        /// so one seed always deals the same board.</summary>
        public static List<LaneDefinition> PickDistinct(GameRng rng, int count) => rng.PickN(_all, count);
    }
}
