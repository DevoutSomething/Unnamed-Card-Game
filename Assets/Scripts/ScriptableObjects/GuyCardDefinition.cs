using System.Collections.Generic;
using Game.Core.Abilities;
using UnityEngine;

namespace Game.Cards {

    /// <summary>
    /// A creature ("guy") that occupies a lane slot and fights in combat.
    /// File name must match the class name — Unity binds .asset files by it.
    /// </summary>
    [CreateAssetMenu(menuName = "Cards/Guy Card")]
    public class GuyCardDefinition : CardDefinition {

        [Header("Guy Stats")]
        public int BaseAttack;
        public int BaseHealth;

        [Tooltip("Ability keywords + magnitudes (defined in Assets/GameData/abilities.json).")]
        public List<AbilityRef> Abilities = new();

        [Tooltip("Gold awarded to the opponent when this guy is killed (seeds CardInstance.KillRewardGold).")]
        public int KillRewardGold;
    }
}
