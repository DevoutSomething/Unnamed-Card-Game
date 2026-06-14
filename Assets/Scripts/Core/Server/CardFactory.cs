using Game.Cards;
using Game.Core.State;

namespace Game.Core.Server
{
    public static class CardFactory
    {
        public static CardInstance CreateInstance(int instanceId, GuyCardDefinition def, int ownerId)
        {
            return new CardInstance
            {
                InstanceId = instanceId,
                DefinitionId = def.CardId,
                OwnerId = ownerId,
                CurrentAttack = def.BaseAttack,
                CurrentHealth = def.BaseHealth,
                CurrentCost = def.EnergyCost,
                KillRewardGold = def.KillRewardGold,
            };
        }
    }
}