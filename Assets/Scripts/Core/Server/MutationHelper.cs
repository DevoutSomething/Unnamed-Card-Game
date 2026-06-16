using System.Collections.Generic;
using Game.Core.Events;
using Game.Core.State;
using Unity.Mathematics;

namespace Game.Core.Server
{
    public static class MutationHelper
    {
        public static void DealCombatDamage(
            CardInstance damagedCard,
            CardInstance attackingCard,
            List<GameEvent> events)
        {
            int incomingDamage = attackingCard.CurrentAttack;

            if (damagedCard.StatusEffects.Contains("Armored"))
            {
                incomingDamage -= 1;
            }
            incomingDamage = math.max(0, incomingDamage);

            if (incomingDamage == 0)
            {
                return;
            }

            damagedCard.CurrentHealth -= incomingDamage;
            damagedCard.LastDamageWasCombatDamage = true;
            damagedCard.LastDamagerCardId = attackingCard.InstanceId;

            if (damagedCard.CurrentHealth < 0)
            {
                damagedCard.CurrentHealth = 0;
            }

            events.Add(new CardDamagedEvent(
                damagedCard.InstanceId,
                incomingDamage,
                damagedCard.CurrentHealth));
        }

       public static void DealCombatDamageToPlayer(
            Player target,
            int amount,
            List<GameEvent> events)
        {
            if (amount <= 0) return;

            target.Health -= amount;
            if (target.Health < 0)
            {
                target.Health = 0;
            }

            events.Add(new PlayerDamagedEvent(target.Id, amount, target.Health));
        }
        public static void GiveGold(Player player, int amount, List<GameEvent> events)
        {
            if (amount <= 0) return;

            player.Gold += amount;

            events.Add(new GoldGainedEvent(amount, player.Id));
        }

}
    }
