using System.Collections.Generic;
using UnityEngine;

namespace Game.Cards {

    /// <summary>
    /// Runtime registry of every cosmetic part. Resolves the default skin for a
    /// card (from its id + rarity) and assembles arbitrary combos with validation,
    /// so combos never have to be hand-authored one by one.
    ///
    /// Like CardDatabase, parts are NOT hand-registered: every CardArt / CardBorder
    /// / CardLayout / CardBack asset under a Resources/<see cref="ResourcesPath"/>
    /// folder is discovered automatically, so the assets themselves are the single
    /// source of truth.
    /// </summary>
    public class CardSkinLibrary {

        /// <summary>
        /// Subfolder (under any "Resources" folder) scanned for cosmetic assets.
        /// Only parts here are live.
        /// </summary>
        public const string ResourcesPath = "Skins";

        /// <summary>
        /// LayoutId of the layout used as the fallback when no rarity/card-specific
        /// layout applies. Author one CardLayout asset with this id.
        /// </summary>
        public const string DefaultLayoutId = "default";

        readonly List<CardArt> _arts = new();
        readonly List<CardBorder> _borders = new();
        readonly List<CardLayout> _layouts = new();
        readonly List<CardBack> _backs = new();
        bool _loaded;

        /// <summary>Fallback layout (the CardLayout whose id is <see cref="DefaultLayoutId"/>).</summary>
        public CardLayout DefaultLayout {
            get {
                EnsureLoaded();
                return FindById(_layouts, l => l.LayoutId, DefaultLayoutId);
            }
        }

        /// <summary>
        /// Discover every cosmetic asset under Resources/<see cref="ResourcesPath"/>
        /// and (re)build the part lists. Called automatically on first use; call it
        /// explicitly from a bootstrap to control when the load hitch happens.
        /// </summary>
        public void Load() {
            _arts.Clear();
            _borders.Clear();
            _layouts.Clear();
            _backs.Clear();
            _arts.AddRange(Resources.LoadAll<CardArt>(ResourcesPath));
            _borders.AddRange(Resources.LoadAll<CardBorder>(ResourcesPath));
            _layouts.AddRange(Resources.LoadAll<CardLayout>(ResourcesPath));
            _backs.AddRange(Resources.LoadAll<CardBack>(ResourcesPath));
            _loaded = true;
        }

        /// <summary>All arts authored for a given card.</summary>
        public List<CardArt> ArtsFor(string cardId) {
            EnsureLoaded();
            var result = new List<CardArt>();
            foreach (var art in _arts)
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
            EnsureLoaded();

            CardArt baseArt = DefaultArtFor(card.CardId);
            return new CardSkin {
                Art = baseArt,
                Border = DefaultBorderFor(card),
                Layout = DefaultLayout,
            };
        }

        /// <summary>
        /// Like TryCompose, but also enforces the card-level rules: the art must
        /// belong to the card and border/layout must accept its meta tags.
        /// Use this when composing a skin for a known card (the usual case).
        /// </summary>
        public bool TryComposeFor(CardDefinition card, string artId, string borderId, string layoutId,
                                  out CardSkin skin, out string error) {
            EnsureLoaded();
            skin = new CardSkin {
                Art = FindById(_arts, a => a.ArtId, artId),
                Border = FindById(_borders, b => b.BorderId, borderId),
                Layout = FindById(_layouts, l => l.LayoutId, layoutId),
            };
            return skin.IsValidFor(card, out error);
        }

        /// <summary>
        /// Build a specific combo and report whether it is valid. Picks an art/
        /// border/layout by id (null id = leave that slot empty).
        /// </summary>
        public bool TryCompose(string artId, string borderId, string layoutId,
                               out CardSkin skin, out string error) {
            EnsureLoaded();
            skin = new CardSkin {
                Art = FindById(_arts, a => a.ArtId, artId),
                Border = FindById(_borders, b => b.BorderId, borderId),
                Layout = FindById(_layouts, l => l.LayoutId, layoutId),
            };
            return skin.IsValid(out error);
        }

        /// <summary>Every border that can legally pair with the given art.</summary>
        public List<CardBorder> CompatibleBorders(CardArt art) {
            EnsureLoaded();
            var result = new List<CardBorder>();
            foreach (var b in _borders)
                if (b != null && b.IsCompatibleWith(art))
                    result.Add(b);
            return result;
        }

        void EnsureLoaded() {
            if (!_loaded) Load();
        }

        CardArt DefaultArtFor(string cardId) {
            CardArt fallback = null;
            foreach (var art in _arts) {
                if (art == null || art.CardId != cardId) continue;
                if (art.Unlock == UnlockSource.Default) return art;
                fallback ??= art;   // remember the first art for this card
            }
            return fallback;
        }

        CardBorder DefaultBorderFor(CardDefinition card) {
            // Most-specific wins: a border that *requires* card tags (e.g. the
            // "tank" archetype frame) beats the generic rarity frame for cards
            // that carry those tags; untagged cards still get the generic one.
            CardBorder best = null;
            int bestSpecificity = -1;
            foreach (var b in _borders) {
                if (b == null || b.Unlock != UnlockSource.Rarity || b.RarityForDefault != card.Rarity) continue;
                if (!b.IsCompatibleWith(card)) continue;
                int specificity = b.RequiredCardTags?.Count ?? 0;
                if (specificity > bestSpecificity) {
                    best = b;
                    bestSpecificity = specificity;
                }
            }
            return best;
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
