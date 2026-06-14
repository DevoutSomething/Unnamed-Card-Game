using System.Collections.Generic;
using UnityEngine;

namespace Game.Cards {

    /// <summary>
    /// Central catalog of every cosmetic part. Resolves the default skin for a
    /// card (from its id + rarity) and assembles arbitrary combos with validation,
    /// so combos never have to be hand-authored one by one.
    /// </summary>
    [CreateAssetMenu(menuName = "Cards/Skins/Skin Library")]
    public class CardSkinLibrary : ScriptableObject {

        public List<CardArt> Arts = new();
        public List<CardBorder> Borders = new();
        public List<CardLayout> Layouts = new();

        [Tooltip("Fallback layout used when no rarity/card-specific layout applies.")]
        public CardLayout DefaultLayout;

        /// <summary>All arts authored for a given card.</summary>
        public List<CardArt> ArtsFor(string cardId) {
            var result = new List<CardArt>();
            foreach (var art in Arts)
                if (art != null && art.CardId == cardId)
                    result.Add(art);
            return result;
        }

        /// <summary>
        /// The default skin for a card: its base art, the border tied to its
        /// rarity, and the default layout. Returns a best-effort skin even if some
        /// parts are missing (renderer can decide how to handle nulls).
        /// </summary>
        public CardSkin ResolveDefault(CardDefinition card) {
            if (card == null) return new CardSkin();

            CardArt baseArt = DefaultArtFor(card.CardId);
            return new CardSkin {
                Art = baseArt,
                Border = DefaultBorderFor(card.Rarity),
                Layout = DefaultLayout,
            };
        }

        /// <summary>
        /// Build a specific combo and report whether it is valid. Picks an art/
        /// border/layout by id (null id = leave that slot empty).
        /// </summary>
        public bool TryCompose(string artId, string borderId, string layoutId,
                               out CardSkin skin, out string error) {
            skin = new CardSkin {
                Art = FindById(Arts, a => a.ArtId, artId),
                Border = FindById(Borders, b => b.BorderId, borderId),
                Layout = FindById(Layouts, l => l.LayoutId, layoutId),
            };
            return skin.IsValid(out error);
        }

        /// <summary>Every border that can legally pair with the given art.</summary>
        public List<CardBorder> CompatibleBorders(CardArt art) {
            var result = new List<CardBorder>();
            foreach (var b in Borders)
                if (b != null && b.IsCompatibleWith(art))
                    result.Add(b);
            return result;
        }

        CardArt DefaultArtFor(string cardId) {
            CardArt fallback = null;
            foreach (var art in Arts) {
                if (art == null || art.CardId != cardId) continue;
                if (art.Unlock == UnlockSource.Default) return art;
                fallback ??= art;   // remember the first art for this card
            }
            return fallback;
        }

        CardBorder DefaultBorderFor(Rarity rarity) {
            foreach (var b in Borders)
                if (b != null && b.Unlock == UnlockSource.Rarity && b.RarityForDefault == rarity)
                    return b;
            return null;
        }

        static T FindById<T>(List<T> list, System.Func<T, string> idOf, string id) where T : Object {
            if (string.IsNullOrEmpty(id)) return null;
            foreach (var item in list)
                if (item != null && idOf(item) == id)
                    return item;
            return null;
        }
    }
}
