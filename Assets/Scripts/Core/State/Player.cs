using System.Collections.Generic;
using Game.Cards;

namespace Game.Core.State {
    public class Player {
        public int Id { get; }
        public int Health { get; private set; }
        public int MaxHealth { get; private set; }
        public int Gold { get; private set; }

        public int TurnNumber;   
        public int SlotIndex;      

        public Slot CurrentSlot => Rotation.Sequence[SlotIndex];
        public int ActivePlayerId => CurrentSlot.PlayerId;
        public SlotType CurrentSlotType => CurrentSlot.Type;



        public List<CardInstance> cardsInHand = new List<CardInstance>();
        public List<CardInstance> cardsInDeck = new List<CardInstance>();
        public Player(int id) {
            Id = id;
            Health = 100;
            MaxHealth = 100;
            Gold = 0;
        }

        public List<Archetype> Archetypes = new List<Archetype>();
    }
}