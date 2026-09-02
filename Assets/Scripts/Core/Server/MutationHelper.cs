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

            // Armor: the defend keyword, stacks by X.
            incomingDamage -= AbilityRuntime.Sum(
                damagedCard, AbilityTrigger.OnDamaged, AbilityEffect.ReduceDamage, AbilityTarget.Self);
            incomingDamage = Math.Max(0, incomingDamage);

            if (incomingDamage == 0)
            {
                return 0;
            }

            int healthBefore = damagedCard.CurrentHealth;
            int healthAfter = Math.Max(0, healthBefore - incomingDamage);

            // Instakill: any nonzero combat damage from this attacker is lethal,
            // regardless of how much health the target had left.
            bool instakill = AbilityRuntime.Sum(
                attackingCard, AbilityTrigger.OnAttack, AbilityEffect.Instakill, AbilityTarget.EnemiesInLane) > 0;
            if (instakill)
            {
                healthAfter = 0;
            }

            damagedCard.CurrentHealth = healthAfter;
            damagedCard.LastDamageWasCombatDamage = true;
            damagedCard.LastDamagerCardId = attackingCard.InstanceId;

            events.Add(new CardDamagedEvent(
                damagedCard.InstanceId,
                healthBefore - healthAfter,
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

            // A corpse's death attribution is frozen: a card killed in the
            // simultaneous swing still counterattacks, and the thorns it takes
            // back must not overwrite who combat-killed it (kill gold + bounty).
            if (target.CurrentHealth <= 0) return;

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

        /// <summary>
        /// A permanent, asymmetric, possibly-NEGATIVE stat change — what lane
        /// modifiers apply on entry. Differs from BuffStats in three ways that
        /// matter: attack and health move independently (+0/+2), negatives are
        /// allowed, and MaxHealth moves with CurrentHealth so a debuff can't
        /// simply be healed off.
        ///
        /// A guy reduced to 0 health is LEFT at 0 for the caller to sweep (see
        /// CombatResolver.ClearDeadInLane) rather than dying here: nothing
        /// killed it in combat, so it must not pay out kill gold.
        /// </summary>
        public static void ApplyStatModifier(
            CardInstance card, int attackDelta, int healthDelta, List<GameEvent> events)
        {
            if (card == null || (attackDelta == 0 && healthDelta == 0)) return;

            int attackBefore = card.CurrentAttack;
            int healthBefore = card.CurrentHealth;

            card.CurrentAttack = Math.Max(0, card.CurrentAttack + attackDelta);
            card.MaxHealth = Math.Max(1, card.MaxHealth + healthDelta);
            card.CurrentHealth = Math.Max(0, card.CurrentHealth + healthDelta);

            events.Add(new CardBuffedEvent(
                card.InstanceId,
                card.CurrentAttack - attackBefore,
                card.CurrentHealth - healthBefore));
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

        /// <summary>
        /// Drains player health as a cost (e.g. Blood Price), clamped at 0.
        /// Deliberately NOT routed through DealCombatDamageToPlayer: this is a
        /// life PAYMENT, not damage, so it emits PlayerLostHealthEvent and must
        /// never trip a "when the player takes damage" reaction. A lethal cost is
        /// allowed — Health can reach 0, which ends the game like any other.
        /// </summary>
        public static void LosePlayerHealth(Player target, int amount, List<GameEvent> events)
        {
            if (amount <= 0) return;

            target.Health -= amount;
            if (target.Health < 0)
            {
                target.Health = 0;
            }

            events.Add(new PlayerLostHealthEvent(target.Id, amount, target.Health));
        }

        public static void GiveGold(Player player, int amount, List<GameEvent> events)
        {
            if (amount <= 0) return;

            player.Gold += amount;

            events.Add(new GoldGainedEvent(player.Id, amount));
        }

        /// <summary>
        /// Deduct gold to pay for a shop action. Returns false (no mutation, no
        /// event) if the player can't afford it — callers reject the command
        /// instead of partially charging.
        /// </summary>
        public static bool TrySpendGold(Player player, int amount, List<GameEvent> events)
        {
            if (amount < 0) return false;
            if (player.Gold < amount) return false;
            if (amount == 0) return true;

            player.Gold -= amount;
            events.Add(new GoldLostEvent(player.Id, amount));
            return true;
        }
    }
}
