using UnityEngine;

namespace Game.Cards {

    /// <summary>What the caster must pick before a spell resolves.</summary>
    public enum SpellTarget {
        None,           // resolves immediately, nothing to pick
        AnyGuy,         // any living guy in a lane, either side
        FriendlyGuy,    // one of the caster's own guys
        AnyCharacter,   // any living guy, OR either player's hero
    }

    /// <summary>
    /// A one-shot spell: it resolves and is consumed, never occupying a lane
    /// slot the way a guy does, and it's only castable on a spell turn (a
    /// player's non-main action slot — see Rotation.IsMainActionSlot).
    ///
    /// Effects are additive fields rather than a scripting layer: every spell
    /// so far is some combination of "draw N / deal D / heal H / buff +A/+B",
    /// and all non-zero fields apply, so one card can do several things.
    /// File name must match the class name — Unity binds .asset files by it.
    /// </summary>
    [CreateAssetMenu(menuName = "Cards/Spell Card")]
    public class SpellCardDefinition : CardDefinition {

        [Header("Targeting")]
        public SpellTarget Target = SpellTarget.None;

        [Header("Effects — every non-zero field applies, in this order")]
        public int DamageAmount;
        public int HealAmount;
        public int BuffAttack;
        public int BuffHealth;
        public int DrawCount;

        public bool NeedsTarget => Target != SpellTarget.None;
    }
}
