using System;
using System.Collections.Generic;
using UnityEngine;

namespace Game.Cards {

    /// <summary>Which slice of the catalog a conjure draws from. Any = no
    /// restriction, so it's the zero value and therefore the Unity default.</summary>
    public enum ConjureKind {
        Any,
        Guy,
        Spell,
    }

    /// <summary>How a conjure treats the Rarity field beside it.</summary>
    public enum ConjureRarityFilter {
        Any,        // ignore Rarity entirely (default)
        Exactly,    // must be this rarity
        AtLeast,    // this rarity or better
        AtMost,     // this rarity or worse
    }

    /// <summary>
    /// The rules for one conjure effect: what to pull out of the catalog, and
    /// what to do to it on the way into hand.
    ///
    /// Conjured cards SPAWN in hand — created fresh from the card catalog, never
    /// drawn from and never returned to the owner's deck.
    ///
    /// Every filter is opt-in: a spec that sets none of them matches the whole
    /// catalog, and each one you do set narrows it further (they AND together).
    /// The zero value of every field means "don't filter on this", so a card
    /// that doesn't conjure at all — Count 0 — costs nothing to carry, and a new
    /// filter can be added here without touching a single existing card.
    /// </summary>
    [Serializable]
    public class ConjureSpec {

        [Tooltip("How many cards to conjure. 0 = this card doesn't conjure at all.")]
        public int Count;

        [Header("Filters — all must match; leave at defaults to not filter")]
        public ConjureKind Kind = ConjureKind.Any;

        public ConjureRarityFilter RarityFilter = ConjureRarityFilter.Any;
        [Tooltip("Only read when RarityFilter is not Any.")]
        public Rarity Rarity;

        [Tooltip("Card must have at least ONE of these archetypes. Empty = any archetype.")]
        public List<Archetype> Archetypes = new();

        [Tooltip("Card must have ALL of these tags — this is the 'tribe' filter " +
                 "(e.g. 'dragon'). Empty = any card.")]
        public List<string> RequiredTags = new();

        [Tooltip("Turn on to bound the conjured card's printed energy cost.")]
        public bool FilterByEnergyCost;
        public int MinEnergyCost;
        public int MaxEnergyCost;

        [Header("Applied to each conjured card")]
        [Tooltip("Energy knocked off the conjured card's cost, floored at 0.")]
        public int CostReduction;

        /// <summary>True if this spec actually produces anything.</summary>
        public bool Conjures => Count > 0;

        /// <summary>
        /// Whether a catalog entry is a legal thing for this spec to conjure.
        /// Unset filters pass everything, so the checks can be read as a list of
        /// narrowing conditions.
        /// </summary>
        public bool Matches(CardDefinition def) {
            if (def == null) return false;

            bool isSpell = def is SpellCardDefinition;
            if (Kind == ConjureKind.Guy && isSpell) return false;
            if (Kind == ConjureKind.Spell && !isSpell) return false;

            // Rarity is an ordered enum (Common < Rare < Epic < Legendary), so
            // AtLeast/AtMost are plain comparisons.
            switch (RarityFilter) {
                case ConjureRarityFilter.Exactly: if (def.Rarity != Rarity) return false; break;
                case ConjureRarityFilter.AtLeast: if (def.Rarity < Rarity) return false; break;
                case ConjureRarityFilter.AtMost: if (def.Rarity > Rarity) return false; break;
            }

            if (FilterByEnergyCost &&
                (def.EnergyCost < MinEnergyCost || def.EnergyCost > MaxEnergyCost)) return false;

            // Archetypes are ANY-of: a "conjure a Mage or Healer" spec should
            // accept a card that is either.
            if (Archetypes != null && Archetypes.Count > 0) {
                bool matched = false;
                foreach (var archetype in Archetypes) {
                    if (def.Archetypes != null && def.Archetypes.Contains(archetype)) { matched = true; break; }
                }
                if (!matched) return false;
            }

            // Tags are ALL-of: tags are how tribes are expressed, and "dragon
            // AND undead" is the useful reading.
            if (RequiredTags != null) {
                foreach (var tag in RequiredTags) {
                    if (!string.IsNullOrWhiteSpace(tag) && !def.HasTag(tag)) return false;
                }
            }

            return true;
        }
    }
}
