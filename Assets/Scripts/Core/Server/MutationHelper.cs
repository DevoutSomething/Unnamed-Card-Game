using System;
using System.Collections.Generic;
using Game.Core.Abilities;
using Game.Core.Events;
using Game.Core.State;

namespace Game.Core.Server
{
    public static class MutationHelper
    {
        /// <summary>
        /// An attacker hits a card. Applies defend/armor reduction and thorns
        /// retaliation. Returns the overkill overflow — damage beyond what the
        /// target's remaining health absorbed (0 unless the hit was lethal);
        /// the caller decides what to do with it.
        /// </summary>
        public static int DealCombatDamage(
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
            incomingDamage = Math.Max(0, incomingDamage);

            if (incomingDamage == 0)
            {
                return 0;
            }

            int healthBefore = damagedCard.CurrentHealth;
            damagedCard.CurrentHealth = Math.Max(0, healthBefore - incomingDamage);
            damagedCard.LastDamageWasCombatDamage = true;
            damagedCard.LastDamagerCardId = attackingCard.InstanceId;

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

            return Math.Max(0, incomingDamage - healthBefore);
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

            target.CurrentHealth = Math.Max(0, target.CurrentHealth - amount);
            target.LastDamageWasCombatDamage = false;
            target.LastDamagerCardId = sourceInstanceId;

            events.Add(new CardDamagedEvent(target.InstanceId, amount, target.CurrentHealth));
        }

        /// <summary>
        /// Restore health, capped at MaxHealth. Emits CardHealedEvent only when
        /// health actually changes, then fires the card's OnHealed abilities
        /// (mending). Returns the amount actually healed.
        /// </summary>
        public static int HealCard(CardInstance card, int amount, List<GameEvent> events)
        {
            if (card == null || amount <= 0) return 0;

            int healed = Math.Min(amount, card.MaxHealth - card.CurrentHealth);
            if (healed <= 0) return 0;

            card.CurrentHealth += healed;
            events.Add(new CardHealedEvent(card.InstanceId, healed, card.CurrentHealth));

            int mend = AbilityRuntime.Sum(
                card, AbilityTrigger.OnHealed, AbilityEffect.BuffStats, AbilityTarget.Self);
            if (mend > 0)
            {
                BuffStats(card, mend, events);
            }

            return healed;
        }

        /// <summary>
        /// Permanent +X/+X. Raises MaxHealth alongside CurrentHealth so the buff
        /// also grows the healable room.
        /// </summary>
        public static void BuffStats(CardInstance card, int amount, List<GameEvent> events)
        {
            if (card == null || amount <= 0) return;

            card.CurrentAttack += amount;
            card.MaxHealth += amount;
            card.CurrentHealth += amount;

            events.Add(new CardBuffedEvent(card.InstanceId, amount, amount));
        }

        /// <summary>Move gold from victim to thief, capped by what the victim has.</summary>
        public static void StealGold(Player thief, Player victim, int amount, List<GameEvent> events)
        {
            if (amount <= 0) return;

            int stolen = Math.Min(amount, victim.Gold);
            if (stolen <= 0) return;

            victim.Gold -= stolen;
            thief.Gold += stolen;

            events.Add(new GoldLostEvent(victim.Id, stolen));
            events.Add(new GoldGainedEvent(thief.Id, stolen));
        }

        /// <summary>Restore player health, capped at MaxHealth. Emits only on change.</summary>
        public static void HealPlayer(Player target, int amount, List<GameEvent> events)
        {
            if (amount <= 0) return;

            int healed = Math.Min(amount, target.MaxHealth - target.Health);
            if (healed <= 0) return;

            target.Health += healed;
            events.Add(new PlayerHealedEvent(target.Id, healed, target.Health));
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

            events.Add(new GoldGainedEvent(player.Id, amount));
        }
    }
}
