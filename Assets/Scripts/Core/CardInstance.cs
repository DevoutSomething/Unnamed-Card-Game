using System.Collections.Generic;

namespace Game.Core.State {
    public class CardInstance {

        // Identifiers of the card instance
        public int InstanceId;          
        public string DefinitionId;     
        public int OwnerId;


        //current card stats
        public int KillRewardGold;

        public int CurrentAttack;

        public int CurrentHealth;

        public int CurrentCost;
        public bool LastDamageWasCombatDamage;

        public int LastDamagerCardId = -1; // instance ID of the card that last damaged this card, or -1 if it was damaged by a non-card source (e.g. burn damage)

        public List<string> StatusEffects  = new();   
    }
}   