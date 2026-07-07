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
        public List<Archetype> Archetypes = new();

        [Header("Meta")]
        [Tooltip("Free-form meta tags (e.g. 'dragon', 'starter'). Cosmetic parts and future rules " +
                 "can require these to decide what is valid for this card.")]
        public List<string> Tags = new();

        public bool HasTag(string tag) => Tags != null && Tags.Contains(tag);
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
