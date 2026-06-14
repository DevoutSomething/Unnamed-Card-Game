using System.Collections.Generic;
using UnityEngine;

namespace Game.Cards {

    /// <summary>
    /// Base data shared by every card, authored as a ScriptableObject asset.
    /// This type is abstract: you never create a plain "card", only a concrete
    /// card type (Guy, Spell, ...). Add new card types by extending this class.
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


    }

    /// <summary>
    /// A creature ("guy") that occupies a lane slot and fights in combat.
    /// </summary>
    [CreateAssetMenu(menuName = "Cards/Guy Card")]
    public class GuyCardDefinition : CardDefinition {

        [Header("Guy Stats")]
        public int BaseAttack;
        public int BaseHealth;
        public List<string> Abilities = new();

        [Tooltip("Gold awarded to the opponent when this guy is killed (seeds CardInstance.KillRewardGold).")]
        public int KillRewardGold;
    }

    /// <summary>
    /// A one-shot spell. Spell-specific fields go here as the design grows.
    /// </summary>
    [CreateAssetMenu(menuName = "Cards/Spell Card")]
    public class SpellCardDefinition : CardDefinition {
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
