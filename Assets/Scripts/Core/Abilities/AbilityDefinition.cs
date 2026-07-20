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
        OnHealed,       // when this card actually recovers health
        StartOfCombat,
        StartOfTurn,    // when the owner's action slot begins (regen keywords)
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
        BuffStats,      // +X attack AND +X health (growth, mending)
        GainGold,
        StealGold,      // steal gold from the enemy player, scaled by damage dealt to them (rob)
        DrawCard,
        HitAllInLane,   // this card's attacks hit every enemy guy in the lane (pierce)
        Overkill,       // excess attack damage carries over to the enemy player
        ExtraAttack,    // this card attacks X extra times (double tap)
        TargetPlayer,   // this card attacks the enemy player directly, ignoring blockers (precision)
        ApplyStatus,    // add StatusId to the target's StatusEffects
        Instakill,      // any nonzero combat damage this card deals is lethal (instakill)
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
