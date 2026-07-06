using System.Collections.Generic;
using Game.Core.Abilities;

namespace Game.Core.State {
    public class CardInstance {

        // Identifiers of the card instance
        public int InstanceId;
        public string DefinitionId;
        public int OwnerId;

        // Which art skin this copy uses (CardArt.ArtId). Null/empty = default art.
        // Kept as a string so this layer stays free of UnityEngine types.
        public string ArtId;


        //current card stats
        public int KillRewardGold;

        public int CurrentAttack;

        public int CurrentHealth;

        public int CurrentCost;
        public bool LastDamageWasCombatDamage;

        public int LastDamagerCardId = -1; // instance ID of the card that last damaged this card, or -1 if it was damaged by a non-card source (e.g. burn damage)

        // Permanent abilities, seeded from the definition by CardFactory (own copy,
        // so buffs can add/upgrade abilities per instance without touching the asset).
        public List<AbilityRef> Abilities = new();

        // Temporary/status markers applied during play (e.g. "Stunned"). For
        // permanent keyword abilities use Abilities above.
        public List<string> StatusEffects  = new();
    }
}   