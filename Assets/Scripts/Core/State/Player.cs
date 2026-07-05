using System.Collections.Generic;
using Game.Cards;

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
        public CardBack CardBack;
        public List<CardInstance> cardsInHand = new List<CardInstance>();
        public List<CardInstance> cardsInDeck = new List<CardInstance>();
        public List<CardInstance> Deck = new List<CardInstance>();

        public Player(int id) {
            Id = id;
            Health = 100;
            MaxHealth = 100;
            Gold = 0;
        }

        public List<Archetype> Archetypes = new List<Archetype>();
    }
}