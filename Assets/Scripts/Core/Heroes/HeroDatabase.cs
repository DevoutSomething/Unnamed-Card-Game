using System.Collections.Generic;

namespace Game.Core.Heroes
{
    /// <summary>
    /// Registry of playable heroes: HeroId -> HeroDefinition. Pure C# (no
    /// UnityEngine) so a headless server can hold one too, mirroring
    /// <see cref="Game.Core.Abilities.AbilityDatabase"/>. In Unity it is filled
    /// from Resources by HeroLoader; a server fills it from the same JSON.
    /// </summary>
    public class HeroDatabase
    {
        readonly Dictionary<string, HeroDefinition> _byId = new();

        public IReadOnlyCollection<HeroDefinition> All => _byId.Values;
        public int Count => _byId.Count;

        /// <summary>Adds or replaces a definition. Returns false for null/blank ids.</summary>
        public bool Register(HeroDefinition def)
        {
            if (def == null || string.IsNullOrWhiteSpace(def.HeroId)) return false;
            _byId[def.HeroId] = def;
            return true;
        }

        public HeroDefinition Get(string heroId) =>
            !string.IsNullOrEmpty(heroId) && _byId.TryGetValue(heroId, out var def) ? def : null;

        public bool TryGet(string heroId, out HeroDefinition def)
        {
            def = Get(heroId);
            return def != null;
        }

        public bool Contains(string heroId) =>
            !string.IsNullOrEmpty(heroId) && _byId.ContainsKey(heroId);
    }

    /// <summary>
    /// Process-wide access point for the hero roster, mirroring
    /// AbilityRuntime/CardCatalogRuntime. Configure(...) once at bootstrap (game
    /// client, server, or test setup). Left unconfigured it is simply empty, so
    /// hero-free logic tests and tools keep working.
    /// </summary>
    public static class HeroRuntime
    {
        public static HeroDatabase Database { get; private set; } = new HeroDatabase();

        public static bool IsConfigured => Database.Count > 0;

        public static void Configure(HeroDatabase database) =>
            Database = database ?? new HeroDatabase();
    }
}
