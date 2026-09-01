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
