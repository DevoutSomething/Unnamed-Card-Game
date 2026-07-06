using System;

namespace Game.Core.Abilities
{
    /// <summary>When an ability fires.</summary>
    public enum AbilityTrigger
    {
        Passive,        // always on (checked when relevant, e.g. damage reduction)
        OnPlay,         // when the card enters a lane
        OnDamaged,      // when this card takes combat damage
        OnAttack,       // when this card deals combat damage
        OnKill,         // when this card kills another card
        OnDeath,        // when this card dies
        StartOfCombat,
        EndOfTurn,
    }

    /// <summary>What an ability does. Magnitude comes from AbilityRef.X.</summary>
    public enum AbilityEffect
    {
        ReduceDamage,   // take X less damage
        DealDamage,     // deal X damage to the target
        Heal,
        BuffAttack,
        BuffHealth,
        GainGold,
        DrawCard,
        ApplyStatus,    // add StatusId to the target's StatusEffects
    }

    /// <summary>Who an ability affects.</summary>
    public enum AbilityTarget
    {
        Self,
        Attacker,       // the card that damaged this one
        Killer,
        Owner,          // this card's player
        EnemyPlayer,
        AlliesInLane,
        EnemiesInLane,
        RandomEnemy,
    }

    /// <summary>
    /// The rules for one ability keyword (e.g. "armored"). Authored in
    /// Assets/GameData/abilities.json — a plain class (no UnityEngine) so the
    /// server can load the same JSON without Unity. Cards attach abilities via
    /// AbilityRef, which adds the magnitude X.
    /// </summary>
    public class AbilityDefinition
    {
        public string AbilityId;
        public string DisplayName;
        public string DescriptionTemplate;   // "{X}" is replaced with the magnitude
        public AbilityTrigger Trigger;
        public AbilityEffect Effect;
        public AbilityTarget Target;
        public string StatusId;              // only for Effect == ApplyStatus

        public string Describe(int x) =>
            string.IsNullOrEmpty(DescriptionTemplate) ? DisplayName
                : DescriptionTemplate.Replace("{X}", x.ToString());
    }

    /// <summary>
    /// One ability attached to a card: which keyword + how big. Serializable so
    /// it appears in the Unity Inspector and in card JSON as {"id": "...", "x": n}.
    /// </summary>
    [Serializable]
    public class AbilityRef
    {
        public string Id;
        public int X = 1;

        public AbilityRef Clone() => new AbilityRef { Id = Id, X = X };
    }
}
