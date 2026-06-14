using System.Collections.Generic;
using UnityEngine;

namespace Game.Cards {

    /// <summary>
    /// Central catalog of every card's data/rules. Resolves a card's id string
    /// (e.g. the value stored in CardInstance.DefinitionId) back to its
    /// CardDefinition. This is the gameplay-side counterpart to CardSkinLibrary,
    /// which catalogs a card's cosmetics instead of its rules.
    /// </summary>
    [CreateAssetMenu(menuName = "Cards/Card Database")]
    public class CardDatabase : ScriptableObject {

        [Tooltip("Every card asset in the game. Holds Guy and Spell cards side by side.")]
        public List<CardDefinition> Cards = new();

        // Built lazily from Cards the first time a lookup happens. Not serialized:
        // it's a runtime index, rebuilt on demand (see RebuildIndex).
        Dictionary<string, CardDefinition> _byId;

        /// <summary>Read-only view of every registered card.</summary>
        public IReadOnlyList<CardDefinition> All => Cards;

        /// <summary>The card with this id, or null if none is registered.</summary>
        public CardDefinition Get(string cardId) {
            if (string.IsNullOrEmpty(cardId)) return null;
            if (_byId == null) RebuildIndex();
            return _byId.TryGetValue(cardId, out var card) ? card : null;
        }

        /// <summary>Try to get the card with this id; false if not found.</summary>
        public bool TryGet(string cardId, out CardDefinition card) {
            card = Get(cardId);
            return card != null;
        }

        /// <summary>
        /// Rebuild the id lookup from the Cards list. Call this if Cards is changed
        /// at runtime (it's built automatically on the first Get/TryGet).
        /// </summary>
        public void RebuildIndex() {
            _byId = new Dictionary<string, CardDefinition>();
            foreach (var card in Cards) {
                if (card == null || string.IsNullOrEmpty(card.CardId)) continue;
                if (_byId.ContainsKey(card.CardId)) {
                    Debug.LogWarning($"CardDatabase: duplicate CardId '{card.CardId}' on '{card.name}'. Keeping the first one.");
                    continue;
                }
                _byId[card.CardId] = card;
            }
        }
    }
}
