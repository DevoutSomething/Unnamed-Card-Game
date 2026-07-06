using System.Collections.Generic;
using Game.Cards;
using Game.Core.Events;
using Game.Core.Server;
using Game.Core.State;
using UnityEngine;

namespace Game.Client
{
    /// <summary>
    /// One-call convenience for the string-id world: resolve a cardId through the
    /// CardDatabase, create an instance, and drop it in a zone. Failures are
    /// logged and return null — use CardFactory/CardZones directly when you need
    /// to handle errors yourself (e.g. server-side command validation).
    /// </summary>
    public static class CardSpawner
    {
        /// <example>
        /// CardSpawner.Give(state, db, "goblin_01", playerId: 0, CardZone.Deck);
        /// </example>
        public static CardInstance Give(GameState state, CardDatabase db, string cardId, int playerId,
                                        CardZone zone, List<GameEvent> events = null, string artId = null)
        {
            if (db == null)
            {
                Debug.LogError("CardSpawner: CardDatabase is null");
                return null;
            }
            if (!db.TryGet(cardId, out var def))
            {
                Debug.LogError($"CardSpawner: no card with id '{cardId}' in the database " +
                               "(is its .asset in Assets/Resources/Cards? Run Cards > Pipeline > Import All)");
                return null;
            }
            if (!CardFactory.TryCreate(state, def, playerId, out var card, out var error, artId))
            {
                Debug.LogError($"CardSpawner: could not create '{cardId}': {error}");
                return null;
            }
            if (!CardZones.TryAdd(state, card, zone, events, out error))
            {
                Debug.LogError($"CardSpawner: could not add '{cardId}' to {zone}: {error}");
                return null;
            }
            return card;
        }

        /// <summary>Create a card and place it straight into a lane (guys only in practice).</summary>
        public static CardInstance GiveInLane(GameState state, CardDatabase db, string cardId, int playerId,
                                              int laneIndex, int slotIndex = -1,
                                              List<GameEvent> events = null, string artId = null)
        {
            if (db == null || !db.TryGet(cardId, out var def))
            {
                Debug.LogError($"CardSpawner: no card with id '{cardId}' in the database");
                return null;
            }
            if (!CardFactory.TryCreate(state, def, playerId, out var card, out var error, artId))
            {
                Debug.LogError($"CardSpawner: could not create '{cardId}': {error}");
                return null;
            }
            if (!CardZones.TryPlaceInLane(state, card, laneIndex, slotIndex, events, out error))
            {
                Debug.LogError($"CardSpawner: could not place '{cardId}' in lane {laneIndex}: {error}");
                return null;
            }
            return card;
        }
    }
}
