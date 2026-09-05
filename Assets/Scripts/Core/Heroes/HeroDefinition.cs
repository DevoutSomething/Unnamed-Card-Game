using System.Collections.Generic;
using Game.Cards;

namespace Game.Core.Heroes
{
    /// <summary>
    /// One playable hero. For now a hero is purely a way to organize a set of
    /// cards: the identity a player picks before a match, which decides the fixed
    /// cards they always start with and (via the cards themselves) the pool they
    /// draw from. Authored in Assets/GameData/heroes.json — a plain class (no
    /// UnityEngine) so a headless server can load the same JSON without Unity,
    /// exactly like <see cref="Game.Core.Abilities.AbilityDefinition"/>.
    ///
    /// A card belongs to a hero when the card's own Heroes list contains this
    /// HeroId; the hero does NOT enumerate its pool. That one direction
    /// (card -> hero) is the whole mapping, so adding a card to a hero is a
    /// one-line edit on the card. The pool is resolved fast at runtime from a
    /// reverse index built once when the card catalog is configured.
    ///
    /// Art lives elsewhere on purpose: like CardDefinition has no art (CardArt +
    /// CardSkinLibrary own that), a hero's portrait/skins will be a parallel
    /// keyed cosmetic system, so this stays purely organizational.
    /// </summary>
    public class HeroDefinition
    {
        public string HeroId;
        public string DisplayName;

        /// <summary>This hero's class identity: the archetypes its shop draws from.
        /// A card is offered when it shares any of these (Colorless aside — that
        /// means "no class"). Typically two archetypes; empty for a hero whose
        /// class hasn't been authored yet.</summary>
        public List<Archetype> Archetypes = new();

        /// <summary>The fixed cards this hero always opens with, as
        /// (card id, quantity) pairs — e.g. ("brawler_01", 2) means two copies.
        /// Empty for a hero whose base deck hasn't been authored yet.</summary>
        public List<(string CardId, int Quantity)> BaseDeck = new();
    }
}
