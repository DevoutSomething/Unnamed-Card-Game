using System.Collections.Generic;
using Game.Cards;

namespace Game.Core.Server
{
    /// <summary>
    /// Process-wide card pool the server builds starter decks from, mirroring
    /// AbilityRuntime: Configure(...) once at bootstrap with the definitions the
    /// client loaded (GameController does this from CardDatabase; a headless
    /// server would load the same card JSON). Left unconfigured, CommandResolver
    /// falls back to its vanilla test deck so logic tests stay asset-free.
    /// </summary>
    public static class CardCatalogRuntime
    {
        static readonly List<CardDefinition> _pool = new();

        public static IReadOnlyList<CardDefinition> Pool => _pool;
        public static bool IsConfigured => _pool.Count > 0;

        public static void Configure(IEnumerable<CardDefinition> definitions)
        {
            _pool.Clear();
            if (definitions == null) return;
            foreach (var def in definitions)
                if (def != null && !string.IsNullOrWhiteSpace(def.CardId))
                    _pool.Add(def);
        }
    }
}
