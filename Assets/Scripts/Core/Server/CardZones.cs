using System.Collections.Generic;
using Game.Core.Events;
using Game.Core.State;

namespace Game.Core.Server
{
    /// <summary>Where a card instance can be put outside of a lane.</summary>
    public enum CardZone
    {
        Deck,
        Hand,
        Shop,
    }

    /// <summary>Where one of a player's owned cards currently sits. Unlike
    /// CardZone (which is about *placing* a card, so it has no lane member —
    /// lanes need a lane/slot index and go through TryPlaceInLane) this
    /// describes where a card was *found*, so Board is a real answer.</summary>
    public enum OwnedCardLocation
    {
        Deck,
        Hand,
        Board,
    }

    /// <summary>One card a player owns, plus where it currently lives.</summary>
    public readonly struct OwnedCard
    {
        public CardInstance Card { get; }
        public OwnedCardLocation Location { get; }

        public OwnedCard(CardInstance card, OwnedCardLocation location)
        {
            Card = card;
            Location = location;
        }
    }

    /// <summary>
    /// Puts CardInstances into zones (deck / hand / shop / lane slot) with
    /// validation, emitting events like the rest of the server mutations.
    /// All methods return false + a human-readable error instead of throwing.
    /// </summary>
    public static class CardZones
    {
        public static bool TryAdd(GameState state, CardInstance card, CardZone zone,
                                  List<GameEvent> events, out string error)
        {
            if (!ValidateCard(state, card, out error)) return false;

            var player = state.Players[card.OwnerId];
            switch (zone)
            {
                case CardZone.Deck: player.Deck.Add(card); break;
                case CardZone.Hand: player.cardsInHand.Add(card); break;
                case CardZone.Shop: player.ShopOffers.Add(card); break;
                default:
                    error = $"unknown zone '{zone}'";
                    return false;
            }

            events?.Add(new CardGrantedEvent(card.OwnerId, card.InstanceId, card.DefinitionId, zone.ToString()));
            return true;
        }

        /// <summary>
        /// Place a card on the owner's side of a lane. slotIndex -1 = first empty slot.
        /// </summary>
        public static bool TryPlaceInLane(GameState state, CardInstance card, int laneIndex, int slotIndex,
                                          List<GameEvent> events, out string error)
        {
            if (!ValidateCard(state, card, out error)) return false;

            if (laneIndex < 0 || laneIndex >= state.Lanes.Length)
            {
                error = $"lane {laneIndex} is out of range (lanes: 0..{state.Lanes.Length - 1})";
                return false;
            }

            var side = state.Lanes[laneIndex].SublaneOf(card.OwnerId);
            int slot = side.ResolveSlot(slotIndex);
            if (slot < 0)
            {
                error = slotIndex < 0
                    ? $"lane {laneIndex} is full on player {card.OwnerId}'s side"
                    : $"slot {slotIndex} in lane {laneIndex} is taken or out of range";
                return false;
            }

            side.Place(card, slot);
            events?.Add(new CardPlayedEvent(card.OwnerId, card.InstanceId, laneIndex, slot));
            return true;
        }

        /// <summary>
        /// Every card the player owns this match, wherever it currently sits:
        /// undrawn deck, then hand, then lane slots. This is the shop's notion
        /// of "your deck" — the whole collection, not just the draw pile — so
        /// the deck view and RemoveCardFromDeckCommand agree on exactly what a
        /// player can see and act on.
        /// </summary>
        public static List<OwnedCard> OwnedCards(GameState state, Player player)
        {
            var owned = new List<OwnedCard>();
            if (state == null || player == null) return owned;

            foreach (var card in player.Deck)
                if (card != null) owned.Add(new OwnedCard(card, OwnedCardLocation.Deck));

            foreach (var card in player.cardsInHand)
                if (card != null) owned.Add(new OwnedCard(card, OwnedCardLocation.Hand));

            foreach (var lane in state.Lanes)
            {
                var sublane = lane.SublaneOf(player.Id);
                foreach (var card in sublane.Slots)
                    if (card != null) owned.Add(new OwnedCard(card, OwnedCardLocation.Board));
            }

            return owned;
        }

        /// <summary>True if this instance id is one of the player's own cards
        /// (any zone) — the ownership check a shop removal validates against
        /// before charging any gold.</summary>
        public static bool Owns(GameState state, Player player, int cardInstanceId)
        {
            foreach (var owned in OwnedCards(state, player))
                if (owned.Card.InstanceId == cardInstanceId) return true;
            return false;
        }

        /// <summary>
        /// Deletes one of the player's cards from whichever zone holds it.
        /// Returns false if they don't own it. Lane removals free the slot, so
        /// a guy deleted mid-match stops fighting in the next combat.
        /// </summary>
        public static bool TryRemoveOwned(GameState state, Player player, int cardInstanceId)
        {
            if (state == null || player == null) return false;

            var inDeck = player.Deck.Find(c => c != null && c.InstanceId == cardInstanceId);
            if (inDeck != null)
            {
                player.Deck.Remove(inDeck);
                return true;
            }

            var inHand = player.cardsInHand.Find(c => c != null && c.InstanceId == cardInstanceId);
            if (inHand != null)
            {
                player.cardsInHand.Remove(inHand);
                return true;
            }

            foreach (var lane in state.Lanes)
            {
                var sublane = lane.SublaneOf(player.Id);
                for (int i = 0; i < sublane.Slots.Length; i++)
                {
                    var card = sublane.Slots[i];
                    if (card != null && card.InstanceId == cardInstanceId)
                    {
                        sublane.RemoveAt(i);
                        return true;
                    }
                }
            }

            return false;
        }

        static bool ValidateCard(GameState state, CardInstance card, out string error)
        {
            if (state == null) { error = "GameState is null"; return false; }
            if (card == null) { error = "CardInstance is null"; return false; }
            if (card.OwnerId < 0 || card.OwnerId >= state.Players.Length)
            {
                error = $"card {card.InstanceId} has invalid OwnerId {card.OwnerId}";
                return false;
            }
            error = null;
            return true;
        }
    }
}
