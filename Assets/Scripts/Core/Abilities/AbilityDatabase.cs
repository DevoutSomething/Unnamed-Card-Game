using System.Collections.Generic;
using Game.Core.State;

namespace Game.Core.Abilities
{
    /// <summary>
    /// Registry of ability rules: AbilityId -> AbilityDefinition. Pure C# (no
    /// UnityEngine) so a headless server can hold one too. In Unity it is filled
    /// from Resources by AbilityLoader; a server fills it from the same JSON file.
    /// </summary>
    public class AbilityDatabase
    {
        readonly Dictionary<string, AbilityDefinition> _byId = new();

        public IReadOnlyCollection<AbilityDefinition> All => _byId.Values;
        public int Count => _byId.Count;

        /// <summary>Adds or replaces a definition. Returns false for null/blank ids.</summary>
        public bool Register(AbilityDefinition def)
        {
            if (def == null || string.IsNullOrWhiteSpace(def.AbilityId)) return false;
            _byId[def.AbilityId] = def;
            return true;
        }

        public AbilityDefinition Get(string abilityId) =>
            !string.IsNullOrEmpty(abilityId) && _byId.TryGetValue(abilityId, out var def) ? def : null;

        public bool TryGet(string abilityId, out AbilityDefinition def)
        {
            def = Get(abilityId);
            return def != null;
        }
    }

    /// <summary>
    /// Process-wide access point the resolvers query, so combat code doesn't have
    /// to thread a database through every call. Configure(...) once at bootstrap
    /// (game client, server, or test setup). Unconfigured = no abilities resolve,
    /// which keeps ability-free tests and tools working.
    /// </summary>
    public static class AbilityRuntime
    {
        public static AbilityDatabase Database { get; private set; } = new AbilityDatabase();

        public static void Configure(AbilityDatabase database) =>
            Database = database ?? new AbilityDatabase();

        /// <summary>
        /// Total magnitude of a card's abilities matching trigger+effect+target.
        /// Unknown ability ids are skipped (validators flag them at author time).
        /// </summary>
        public static int Sum(CardInstance card, AbilityTrigger trigger, AbilityEffect effect, AbilityTarget target)
        {
            if (card?.Abilities == null) return 0;
            int total = 0;
            foreach (var ability in card.Abilities)
            {
                if (ability == null || !Database.TryGet(ability.Id, out var def)) continue;
                if (def.Trigger == trigger && def.Effect == effect && def.Target == target)
                    total += ability.X;
            }
            return total;
        }
    }
}
