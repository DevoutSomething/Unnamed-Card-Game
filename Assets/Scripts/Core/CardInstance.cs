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

        public List<string> StatusEffects  = new();   
    }
}   