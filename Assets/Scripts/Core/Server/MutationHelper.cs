using System.Collections.Generic;
using Game.Core.Abilities;
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

            // Permanent armor (ability keyword, stacks by X) ...
            incomingDamage -= AbilityRuntime.Sum(
                damagedCard, AbilityTrigger.OnDamaged, AbilityEffect.ReduceDamage, AbilityTarget.Self);
            // ... plus the temporary "Armored" status marker.
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

            // Thorns: reflect damage at the attacker. Dealt as direct (non-combat)
            // damage so it can't trigger thorns back and loop forever, and so a
            // thorns kill grants no kill-reward gold (gold is for combat kills).
            int thorns = AbilityRuntime.Sum(
                damagedCard, AbilityTrigger.OnDamaged, AbilityEffect.DealDamage, AbilityTarget.Attacker);
            if (thorns > 0)
            {
                DealDirectDamage(attackingCard, thorns, damagedCard.InstanceId, events);
            }
        }

        /// <summary>
        /// Non-combat damage from a card source (thorns, spells, burns). Does not
        /// count as combat damage, so it never awards kill gold or triggers
        /// OnDamaged combat abilities.
        /// </summary>
        public static void DealDirectDamage(
            CardInstance target,
            int amount,
            int sourceInstanceId,
            List<GameEvent> events)
        {
            if (amount <= 0) return;

            target.CurrentHealth = math.max(0, target.CurrentHealth - amount);
            target.LastDamageWasCombatDamage = false;
            target.LastDamagerCardId = sourceInstanceId;

            events.Add(new CardDamagedEvent(target.InstanceId, amount, target.CurrentHealth));
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
