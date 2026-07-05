using System.Collections.Generic;
using UnityEngine;

namespace Game.Cards {

    /// <summary>
    /// Runtime registry of every card's data/rules. Resolves a card's id string
    /// (e.g. the value stored in CardInstance.DefinitionId) back to its
    /// CardDefinition. This is the gameplay-side counterpart to CardSkinLibrary,
    /// which catalogs a card's cosmetics instead of its rules.
    ///
    /// Cards are NOT hand-registered. Every CardDefinition asset under a
    /// Resources/<see cref="ResourcesPath"/> folder is discovered automatically,
    /// so the assets themselves are the single source of truth — dropping a new
    /// card asset in that folder is all it takes to make it live.
    /// </summary>
    public class CardDatabase {

        /// <summary>
        /// Subfolder (under any "Resources" folder) scanned for card assets.
        /// Acts as the "which cards are live" guardrail: only assets here load.
        /// </summary>
        public const string ResourcesPath = "Cards";

        // The id -> card index. This IS the storage now (no separate serialized
        // list): Unity can't serialize a Dictionary, but we no longer need to —
        // the list of cards lives on disk as individual assets, discovered on Load.
        readonly Dictionary<string, CardDefinition> _byId = new();
        bool _loaded;

        /// <summary>Read-only view of every registered card.</summary>
        public IReadOnlyCollection<CardDefinition> All {
            get {
                if (!_loaded) Load();
                return _byId.Values;
            }
        }

        /// <summary>The card with this id, or null if none is registered.</summary>
        public CardDefinition Get(string cardId) {
            if (string.IsNullOrEmpty(cardId)) return null;
            if (!_loaded) Load();
            return _byId.TryGetValue(cardId, out var card) ? card : null;
        }

        /// <summary>Try to get the card with this id; false if not found.</summary>
        public bool TryGet(string cardId, out CardDefinition card) {
            card = Get(cardId);
            return card != null;
        }

        /// <summary>
        /// Discover every CardDefinition asset under Resources/<see cref="ResourcesPath"/>
        /// and (re)build the id index. Called automatically on the first lookup;
        /// call it explicitly from a bootstrap to control when the load hitch happens,
        /// or to pick up assets added at runtime.
        /// </summary>
        public void Load() {
            _byId.Clear();
            foreach (var card in Resources.LoadAll<CardDefinition>(ResourcesPath)) {
                if (card == null || string.IsNullOrEmpty(card.CardId)) continue;
                if (_byId.ContainsKey(card.CardId)) {
                    Debug.LogWarning($"CardDatabase: duplicate CardId '{card.CardId}' on '{card.name}'. Keeping the first one.");
                    continue;
                }
                _byId[card.CardId] = card;
            }
            _loaded = true;
        }
    }
}
