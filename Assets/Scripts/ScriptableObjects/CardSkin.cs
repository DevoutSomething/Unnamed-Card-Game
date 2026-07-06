using System.Collections.Generic;
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
    /// A single artwork option for a specific card. A card can have many arts
    /// (base art, alternate arts, full-art versions, ...). Tags let borders and
    /// layouts declare which arts they are allowed to combine with.
    /// </summary>
    [CreateAssetMenu(menuName = "Cards/Skins/Art")]
    public class CardArt : ScriptableObject {
        public string ArtId;
        public string CardId;                 // the card this art belongs to
        public Sprite Image;
        public UnlockSource Unlock = UnlockSource.Default;

        [Tooltip("Free-form tags (e.g. 'holo', 'fullart'). Borders/layouts can require these.")]
        public List<string> Tags = new();

        public bool HasTag(string tag) => Tags != null && Tags.Contains(tag);
    }

    /// <summary>
    /// A frame/border, reusable across many cards. Can be gated by rarity or any
    /// other unlock source, and can restrict which arts it pairs with.
    /// </summary>
    [CreateAssetMenu(menuName = "Cards/Skins/Border")]
    public class CardBorder : ScriptableObject {
        public string BorderId;
        public Sprite Frame;
        public UnlockSource Unlock = UnlockSource.Default;

        [Tooltip("When Unlock == Rarity, the border used as the default for this rarity.")]
        public Rarity RarityForDefault;

        [Tooltip("If non-empty, this border is only valid with arts that have ALL of these tags.")]
        public List<string> RequiredArtTags = new();

        [Tooltip("If non-empty, this border is only valid on cards that have ALL of these meta tags (CardDefinition.Tags).")]
        public List<string> RequiredCardTags = new();

        public bool IsCompatibleWith(CardArt art) => HasAllTags(art, RequiredArtTags);
        public bool IsCompatibleWith(CardDefinition card) => HasAllCardTags(card, RequiredCardTags);

        internal static bool HasAllTags(CardArt art, List<string> required) {
            if (required == null || required.Count == 0) return true;
            if (art == null) return false;
            foreach (var t in required)
                if (!art.HasTag(t)) return false;
            return true;
        }

        internal static bool HasAllCardTags(CardDefinition card, List<string> required) {
            if (required == null || required.Count == 0) return true;
            if (card == null) return false;
            foreach (var t in required)
                if (!card.HasTag(t)) return false;
            return true;
        }
    }

    /// <summary>
    /// A card template that determines where art / name / costs / text / abilities
    /// are rendered. Most cards share one default layout; special skins swap it for
    /// a different template prefab. Can also be gated by art tags.
    /// </summary>
    [CreateAssetMenu(menuName = "Cards/Skins/Layout")]
    public class CardLayout : ScriptableObject {
        public string LayoutId;

        [Tooltip("Prefab that defines where each element renders for this layout.")]
        public GameObject Template;

        [Tooltip("If non-empty, this layout is only valid with arts that have ALL of these tags.")]
        public List<string> RequiredArtTags = new();

        [Tooltip("If non-empty, this layout is only valid on cards that have ALL of these meta tags (CardDefinition.Tags).")]
        public List<string> RequiredCardTags = new();

        public bool IsCompatibleWith(CardArt art) => CardBorder.HasAllTags(art, RequiredArtTags);
        public bool IsCompatibleWith(CardDefinition card) => CardBorder.HasAllCardTags(card, RequiredCardTags);
    }
    /// <summary>
    /// A card back: Like border, but is instead the art on the back of a card
    /// and is only visible when the card is face down. Is the same for all cards in a deck
    /// so will not be stored in card skin but instead in deck or player profile
    /// </summary>
    [CreateAssetMenu(menuName = "Cards/Skins/Back")]
    public class CardBack : ScriptableObject {
        public string BackId;
        public Sprite Image;
        public UnlockSource Unlock = UnlockSource.Default;

        [Tooltip("When Unlock == Rarity, the back used as the default for this rarity.")]
        public Rarity RarityForDefault;
    }



    /// <summary>
    /// A composed skin: one art + one border + one layout. Combos are assembled
    /// from the modular parts above rather than authored exhaustively. Validate()
    /// enforces the parts' compatibility rules.
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
