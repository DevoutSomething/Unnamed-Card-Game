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

        public bool IsCompatibleWith(CardArt art) => HasAllTags(art, RequiredArtTags);

        internal static bool HasAllTags(CardArt art, List<string> required) {
            if (required == null || required.Count == 0) return true;
            if (art == null) return false;
            foreach (var t in required)
                if (!art.HasTag(t)) return false;
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

        public bool IsCompatibleWith(CardArt art) => CardBorder.HasAllTags(art, RequiredArtTags);
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
    }
}
