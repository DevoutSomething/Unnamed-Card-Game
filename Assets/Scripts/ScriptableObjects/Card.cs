using System.Collections.Generic;
using UnityEngine;

namespace Game.Cards {

    /// <summary>
    /// Base data shared by every card, authored as a ScriptableObject asset.
    /// This type is abstract: you never create a plain "card", only a concrete
    /// card type (Guy, Spell, ...). Add new card types by extending this class.
    ///
    /// NOTE: every concrete ScriptableObject class must live in a .cs file named
    /// exactly after the class (GuyCardDefinition.cs, ...). Unity binds saved
    /// .asset files to scripts by file name — a class hiding in a shared file
    /// deserializes as "Missing (Mono Script)" in the next session.
    /// </summary>
    public abstract class CardDefinition : ScriptableObject {

        [Header("Identity")]
        public string CardId;
        public string DisplayName;

        [Header("Costs")]
        public int EnergyCost;   // cost to play the card during a match
        public int GoldCost;     // cost to buy the card in the shop

        [Header("Info")]
        [TextArea] public string Description;
        public Rarity Rarity;

        [Header("Identity — who this card belongs to")]
        [Tooltip("Class identity. A hero's shop offers cards that share one of the hero's " +
                 "archetypes. Colorless counts as 'no class' — a purely-Colorless card is " +
                 "neutral and shows in every hero's shop.")]
        public List<Archetype> Archetypes = new();

        [Tooltip("Hero ids this card is signed to (e.g. 'oceanlord'). A hero's shop always " +
                 "offers its signed cards, on top of its archetype matches. Empty = not tied " +
                 "to any specific hero.")]
        public List<string> Heroes = new();

        [Tooltip("Creature/faction tribe(s), e.g. 'aquatic', 'dragon'. The tribe filter for " +
                 "conjures and future rules reads these.")]
        public List<string> Tribes = new();

        [Header("Meta")]
        [Tooltip("Free-form meta tags that aren't a class, hero, or tribe (e.g. 'classless', " +
                 "'starter'). Cosmetic parts and future rules can require these.")]
        public List<string> Tags = new();

        [Header("Conjure")]
        [Tooltip("Fires when this card is played (a guy entering a lane) or cast (a spell " +
                 "resolving). Count 0 = this card doesn't conjure.")]
        public ConjureSpec Conjure = new();

        /// <summary>True if playing/casting this card spawns cards in hand.</summary>
        public bool Conjures => Conjure != null && Conjure.Conjures;

        public bool HasTag(string tag) => Tags != null && Tags.Contains(tag);
        public bool HasTribe(string tribe) => Tribes != null && Tribes.Contains(tribe);
        public bool HasHero(string heroId) => Heroes != null && Heroes.Contains(heroId);
    }

    public enum Rarity {
        Common,
        Rare,
        Epic,
        Legendary,
    }

    public enum Archetype {
        Colorless,
        Tank,
        Bruiser,
        Assassin,
        Mage,
        Healer,
    }
}
