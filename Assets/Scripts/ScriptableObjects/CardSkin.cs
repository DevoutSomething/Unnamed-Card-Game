using UnityEngine;

namespace Game.Cards {

    /// <summary>
    /// Where a cosmetic part (art / border / layout) comes from. Lets the game
    /// decide what a player has access to without hardcoding it per part.
    /// </summary>
    public enum UnlockSource {
        Default,      // always available (e.g. base art)
        Rarity,       // granted by the card's rarity
        Purchase,     // bought (e.g. a skin in a store)
        Achievement,  // earned
        Event,        // limited-time / promo
    }

    /// <summary>
    /// A composed skin: one art + one border + one layout. Combos are assembled
    /// from the modular parts (CardArt / CardBorder / CardLayout — each in its
    /// own file, as Unity requires for ScriptableObjects) rather than authored
    /// exhaustively. Validate() enforces the parts' compatibility rules.
    /// </summary>
    [System.Serializable]
    public class CardSkin {
        public CardArt Art;
        public CardBorder Border;
        public CardLayout Layout;

        public bool IsValid(out string error) {
            error = null;
            if (Border != null && !Border.IsCompatibleWith(Art)) {
                error = $"Border '{Border.BorderId}' is not compatible with art '{Art?.ArtId}'.";
                return false;
            }
            if (Layout != null && !Layout.IsCompatibleWith(Art)) {
                error = $"Layout '{Layout.LayoutId}' is not compatible with art '{Art?.ArtId}'.";
                return false;
            }
            return true;
        }

        /// <summary>
        /// Full validation against a specific card: the part-to-part rules of
        /// IsValid, plus that the art actually belongs to this card and that the
        /// border/layout accept the card's meta tags (CardDefinition.Tags).
        /// </summary>
        public bool IsValidFor(CardDefinition card, out string error) {
            if (!IsValid(out error)) return false;
            if (card == null) return true;   // no card context -> part checks only

            if (Art != null && Art.CardId != card.CardId) {
                error = $"Art '{Art.ArtId}' belongs to card '{Art.CardId}', not '{card.CardId}'.";
                return false;
            }
            if (Border != null && !Border.IsCompatibleWith(card)) {
                error = $"Border '{Border.BorderId}' requires card meta tags [{string.Join(", ", Border.RequiredCardTags)}] that '{card.CardId}' lacks.";
                return false;
            }
            if (Layout != null && !Layout.IsCompatibleWith(card)) {
                error = $"Layout '{Layout.LayoutId}' requires card meta tags [{string.Join(", ", Layout.RequiredCardTags)}] that '{card.CardId}' lacks.";
                return false;
            }
            return true;
        }
    }
}
