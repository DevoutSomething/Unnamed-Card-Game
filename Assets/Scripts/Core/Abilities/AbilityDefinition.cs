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
        StartOfCombat,  // when combat phase beings, prior to attacks
        StartOfTurn,    // when the owner's action slot begins (regen keywords)
        EndOfTurn, // when the owner's action slot ends
    }

    /// <summary>What an ability does. Magnitude comes from AbilityRef.X.</summary>
    public enum AbilityEffect
    {
        ReduceDamage,   // take X less damage
        DealDamage,     // deal X damage to the target
        Heal,          // restore X health to the target
        LifeSteal,      // attacker heals for X per point of combat damage it deals (lifesteal)
        LoseHealth,    // lose X player health (blood price)
        BuffAttack,    //increase targets attack by X
        BuffHealth,    //increase targets health by X
        BuffStats,      // +X attack AND +X health (growth)
        GainGold,       // gain X gold
        StealGold,      // steal gold from the enemy player, scaled by damage dealt to them (rob)
        DrawCard,      // draw x cards from owners deck to hand
        HitAllInLane,   // this card's attacks hit every enemy guy in the lane (pierce)
        Overkill,       // excess attack damage carries over to the enemy player
        ExtraAttack,    // this card attacks X extra times (double tap)
        TargetPlayer,   // this card attacks the enemy player directly, ignoring blockers (precision)
        ApplyStatus,    // add StatusId to the target's StatusEffects
        Instakill,      // any nonzero combat damage this card deals is lethal (instakill)
        GainEnergy,     // raise the owner's energy-per-turn cap by X (augments)
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
        OwnedGuys,      // every guy the owner controls, anywhere on the board —
                        // including ones played later (augments)
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
