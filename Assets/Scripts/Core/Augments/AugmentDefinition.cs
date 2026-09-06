using System.Collections.Generic;
using Game.Core.Abilities;

namespace Game.Core.Augments
{
    /// <summary>
    /// One augment: a PERMANENT upgrade a player picks between rotations and
    /// keeps for the rest of the match.
    ///
    /// Authored as data the same way cards and heroes are — Assets/GameData/
    /// augments.json is the source of truth, published into Resources by
    /// CardPipeline and parsed by AugmentLoader. A plain class (no UnityEngine)
    /// so a headless server can load the same file, matching HeroDefinition and
    /// AbilityDefinition.
    ///
    /// ALL of an augment's behaviour comes from Abilities — the same keyword
    /// vocabulary cards use (abilities.json). That's what keeps complexity open
    /// ended: a new augment is a new entry here plus, if it needs a genuinely
    /// new behaviour, one new keyword. Anything a card can do, an augment can.
    /// </summary>
    public class AugmentDefinition
    {
        public string AugmentId;
        public string DisplayName;
        public string Description;

        /// <summary>Reserved for weighting the offer roll once there are enough
        /// augments to bucket by power.</summary>
        public Game.Cards.Rarity Rarity;

        /// <summary>Free-form meta tags, for future filtering (e.g. only offering
        /// augments that match the player's hero).</summary>
        public List<string> Tags = new();

        /// <summary>Keywords from abilities.json. Which triggers actually fire
        /// for an augment is documented on AugmentRuntime.</summary>
        public List<AbilityRef> Abilities = new();

        public bool HasTag(string tag) => Tags != null && Tags.Contains(tag);
    }

    /// <summary>
    /// Registry of augment rules: AugmentId -> AugmentDefinition. Mirrors
    /// HeroDatabase/AbilityDatabase — pure C#, filled by AugmentLoader in Unity
    /// or from the same JSON on a headless server.
    /// </summary>
    public class AugmentDatabase
    {
        readonly Dictionary<string, AugmentDefinition> _byId = new();

        public IReadOnlyCollection<AugmentDefinition> All => _byId.Values;
        public int Count => _byId.Count;

        public bool Register(AugmentDefinition def)
        {
            if (def == null || string.IsNullOrWhiteSpace(def.AugmentId)) return false;
            _byId[def.AugmentId] = def;
            return true;
        }

        public AugmentDefinition Get(string augmentId) =>
            !string.IsNullOrEmpty(augmentId) && _byId.TryGetValue(augmentId, out var def) ? def : null;

        public bool TryGet(string augmentId, out AugmentDefinition def)
        {
            def = Get(augmentId);
            return def != null;
        }
    }
}
