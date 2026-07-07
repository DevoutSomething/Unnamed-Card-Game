using System.Collections.Generic;
using UnityEngine;

namespace Game.Cards {

    /// <summary>
    /// A frame/border, reusable across many cards. Can be gated by rarity or any
    /// other unlock source, and can restrict which arts/cards it pairs with.
    /// File name must match the class name — Unity binds .asset files by it.
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
}
