namespace Game.Core.Lanes
{
    /// <summary>
    /// The rules for one lane type ("location"). A lane effect is a property of
    /// the battlefield, not of either side, so every effect here applies to
    /// BOTH players' guys in that lane. There are no cards that place them: for
    /// now they're dealt randomly at match start (CommandResolver.AssignRandomLaneTypes),
    /// and later they'll also arrive from event cards and from specific guys.
    ///
    /// Effects are plain typed fields rather than a general effect engine —
    /// a handful of lane types doesn't justify one, and every field is
    /// additive, so a new lane type is usually just new numbers in LaneCatalog.
    /// </summary>
    public class LaneDefinition
    {
        public string Id;
        public string DisplayName;
        public string Description;

        /// <summary>Applied once to each guy as it ENTERS this lane, so guys
        /// played here later are affected too. Negative values are a debuff.</summary>
        public int AttackModifier;
        public int HealthModifier;

        /// <summary>Direct damage dealt to every guy here (both sides) each time
        /// an energy block begins — i.e. after each combat.</summary>
        public int DamageAllOnEnergyReset;

        /// <summary>Cards the controller draws when they play a guy here.</summary>
        public int DrawOnGuyPlayed;

        /// <summary>Gold paid to a player per living guy they hold in this lane,
        /// on the same beat as DamageAllOnEnergyReset — once per energy block
        /// (twice a rotation), not once per action slot.</summary>
        public int GoldGeneration;

        public bool HasStatModifier => AttackModifier != 0 || HealthModifier != 0;
    }
}
