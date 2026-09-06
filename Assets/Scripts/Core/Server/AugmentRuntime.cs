using System.Collections.Generic;
using Game.Core.Abilities;
using Game.Core.Augments;
using Game.Core.State;

namespace Game.Core.Server
{
    /// <summary>
    /// Process-wide access point for augment rules, mirroring HeroRuntime and
    /// AbilityRuntime: Configure(...) once at bootstrap (AugmentLoader does it
    /// in Unity; a headless server parses the same JSON). Left unconfigured, no
    /// augments are ever offered, which keeps augment-free logic tests working.
    /// </summary>
    public static class AugmentCatalogRuntime
    {
        public static AugmentDatabase Database { get; private set; } = new AugmentDatabase();

        public static void Configure(AugmentDatabase database) =>
            Database = database ?? new AugmentDatabase();

        public static IReadOnlyCollection<AugmentDefinition> Pool => Database.All;
        public static bool IsConfigured => Database.Count > 0;

        public static AugmentDefinition Get(string augmentId) => Database.Get(augmentId);
    }

    /// <summary>
    /// Reads the augments a player has taken, the same way AbilityRuntime reads
    /// the abilities on a card: sum every matching keyword's magnitude.
    ///
    /// WHICH TRIGGERS ACTUALLY FIRE for an augment (each is a hook in
    /// CommandResolver, not something this class schedules):
    ///   StartOfTurn — read in ApplyStartOfTurn, once per action slot, the same
    ///                 beat a guy's regen/goldgen keywords use.
    ///   Passive     — read where the effect makes sense: GainEnergy is applied
    ///                 once when the augment is taken (a permanent cap bump, so
    ///                 every later refill is bigger), and BuffStats/OwnedGuys is
    ///                 applied both to the guys already deployed and to each guy
    ///                 played afterwards.
    /// Anything else on an augment is simply never read yet — adding a trigger
    /// means adding its hook, not changing this class.
    /// </summary>
    public static class AugmentRuntime
    {
        /// <summary>Total magnitude across this player's augments for one
        /// trigger+effect+target. Unknown ability ids are skipped, the same as
        /// on cards.</summary>
        public static int Sum(Player player, AbilityTrigger trigger, AbilityEffect effect, AbilityTarget target)
        {
            if (player?.Augments == null) return 0;

            int total = 0;
            foreach (var augmentId in player.Augments)
            {
                var augment = AugmentCatalogRuntime.Get(augmentId);
                if (augment?.Abilities == null) continue;

                foreach (var ability in augment.Abilities)
                {
                    if (ability == null || !AbilityRuntime.Database.TryGet(ability.Id, out var def)) continue;
                    if (def.Trigger == trigger && def.Effect == effect && def.Target == target)
                        total += ability.X;
                }
            }
            return total;
        }

        /// <summary>Magnitude of a single augment definition's matching keywords —
        /// used when an augment is first taken, before it's in Player.Augments.</summary>
        public static int SumOf(AugmentDefinition augment, AbilityTrigger trigger, AbilityEffect effect, AbilityTarget target)
        {
            if (augment?.Abilities == null) return 0;

            int total = 0;
            foreach (var ability in augment.Abilities)
            {
                if (ability == null || !AbilityRuntime.Database.TryGet(ability.Id, out var def)) continue;
                if (def.Trigger == trigger && def.Effect == effect && def.Target == target)
                    total += ability.X;
            }
            return total;
        }
    }
}
