using System.Collections.Generic;
using Game.Cards;
using Newtonsoft.Json;

namespace Game.Core.State {
    public class Player {
        public int Id { get; }
        public int Health;
        public int MaxHealth;
        public int Gold;
        public int CurrentEnergy;
        public int EnergyPerTurn;

        public int TurnNumber;
        public int SlotIndex;

        public Slot CurrentSlot => Rotation.Slots[SlotIndex];
        public int ActivePlayerId => CurrentSlot.PlayerId;
        public SlotType CurrentSlotType => CurrentSlot.Type;


        //card back is stored in player object, as cardback will be the same across all cards in a deck
        //regardless of card rarity or art/border customizations so as not to give away info about the players hand
        // [JsonIgnore]: a ScriptableObject asset reference can't cross the wire —
        // see Game.Net.NetworkHostServer/NetworkClientServer.
        [JsonIgnore]
        public CardBack CardBack;
        public List<CardInstance> cardsInHand = new List<CardInstance>();
        public List<CardInstance> Deck = new List<CardInstance>();

        // Cards currently offered to this player in the shop phase.
        public List<CardInstance> ShopOffers = new List<CardInstance>();

        // Shop-visit-scoped state, reset every time a Shop slot begins (see
        // CommandResolver.EnterShop). ShopReady latches once the player has
        // signaled they're done shopping; ShopRemovalsThisVisit drives the
        // removal cost's scaling (5, 10, 15, ...); the free reroll is a
        // one-shot per visit.
        public bool ShopReady;
        public int ShopRemovalsThisVisit;
        public bool HasUsedFreeDeckRerollThisVisit;

        public Player(int id) {
            Id = id;
            MaxHealth = 30;
            Health = MaxHealth;
            Gold = 50;
        }

        public List<Archetype> Archetypes = new List<Archetype>();
    }
}