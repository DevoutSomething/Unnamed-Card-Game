using System.Collections.Generic;
using Game.Core.Abilities;
using Game.Core.Events;
using Game.Core.State;

namespace Game.Core.Server
{
    public static class CombatResolver
    {
        public static void Resolve(GameState state, List<GameEvent> events)
        {
            for (int laneIndex = 0; laneIndex < state.Lanes.Length; laneIndex++)
            {
                Lane lane = state.Lanes[laneIndex];

                // Start-of-combat abilities (growth, heals, guy/hero damage)
                // fire before anyone swings, then their casualties are cleared.
                StartOfCombatPhase(state, lane, events);
                ProcessDeaths(state, lane, events);
                if (CheckGameEnd(state, events)) return;

                var (p1Front, p1Back) = GetFrontAndBack(lane.P1);
                var (p2Front, p2Back) = GetFrontAndBack(lane.P2);

                ResolveFrontPhase(state, lane, p1Front, p2Front, events);
                ProcessDeaths(state, lane, events);
                ResolveBackPhase(state, lane, p1Back, p2Back, events);
                ProcessDeaths(state, lane, events);
                if (CheckGameEnd(state, events)) return;
            }
        }

        // ------------------------------------------------------------------
        // Start-of-combat abilities
        // ------------------------------------------------------------------

        /// <summary>
        /// Fires StartOfCombat abilities for every living card in the lane, in
        /// deterministic order (P1 side then P2 side, front to back) and in three
        /// waves: permanent buffs, then heals, then damage — so a lane's healer
        /// always patches allies before enemy mages burn them down.
        /// </summary>
        private static void StartOfCombatPhase(GameState state, Lane lane, List<GameEvent> events)
        {
            foreach (var card in LivingLaneCards(lane))
            {
                int growth = AbilityRuntime.Sum(
                    card, AbilityTrigger.StartOfCombat, AbilityEffect.BuffStats, AbilityTarget.Self);
                MutationHelper.BuffStats(card, growth, events);
            }

            foreach (var card in LivingLaneCards(lane))
            {
                int heal = AbilityRuntime.Sum(
                    card, AbilityTrigger.StartOfCombat, AbilityEffect.Heal, AbilityTarget.AlliesInLane);
                if (heal <= 0) continue;

                foreach (var ally in lane.SublaneOf(card.OwnerId).Cards)
                {
                    if (ally.CurrentHealth > 0)
                        MutationHelper.HealCard(ally, heal, events);
                }
            }

            foreach (var card in LivingLaneCards(lane))
            {
                if (card.CurrentHealth <= 0) continue;   // may have died to an earlier caster

                // Guy damage: hits a random living enemy guy anywhere on the board.
                // Dealt as direct (spell-like) damage: no thorns, no kill gold.
                int guyDamage = AbilityRuntime.Sum(
                    card, AbilityTrigger.StartOfCombat, AbilityEffect.DealDamage, AbilityTarget.RandomEnemy);
                if (guyDamage > 0)
                {
                    var target = PickRandomLivingEnemy(state, card.OwnerId);
                    if (target != null)
                        MutationHelper.DealDirectDamage(target, guyDamage, card.InstanceId, events);
                }

                int heroDamage = AbilityRuntime.Sum(
                    card, AbilityTrigger.StartOfCombat, AbilityEffect.DealDamage, AbilityTarget.EnemyPlayer);
                if (heroDamage > 0)
                {
                    MutationHelper.DealCombatDamageToPlayer(
                        state.Players[1 - card.OwnerId], heroDamage, events);
                }
            }
        }

        private static IEnumerable<CardInstance> LivingLaneCards(Lane lane)
        {
            foreach (var card in lane.P1.Slots)
                if (card != null && card.CurrentHealth > 0) yield return card;
            foreach (var card in lane.P2.Slots)
                if (card != null && card.CurrentHealth > 0) yield return card;
        }

        private static CardInstance PickRandomLivingEnemy(GameState state, int casterOwnerId)
        {
            var candidates = new List<CardInstance>();
            foreach (var lane in state.Lanes)
            {
                var enemySide = lane.OpposingSide(casterOwnerId);
                foreach (var card in enemySide.Slots)
                    if (card != null && card.CurrentHealth > 0)
                        candidates.Add(card);
            }
            return candidates.Count == 0 ? null : state.Rng.Pick(candidates);
        }

        // ------------------------------------------------------------------
        // Attack phases
        // ------------------------------------------------------------------

        /// <summary>
        /// Returns the front-most card (first non-null slot) and the back card
        /// (next non-null slot after the front). Either may be null.
        /// </summary>
        private static (CardInstance front, CardInstance back) GetFrontAndBack(Sublane sublane)
        {
            CardInstance front = null;
            CardInstance back = null;

            for (int i = 0; i < sublane.Slots.Length; i++)
            {
                if (sublane.Slots[i] == null) continue;
                if (front == null) { front = sublane.Slots[i]; continue; }
                back = sublane.Slots[i];
                break;
            }

            return (front, back);
        }

        private static void ResolveFrontPhase(
            GameState state,
            Lane lane,
            CardInstance p1Front,
            CardInstance p2Front,
            List<GameEvent> events)
        {
            // Front swings are simultaneous: both fire even if the first one kills.
            if (p1Front != null) ResolveAttack(state, p1Front, lane.P2, events);
            if (p2Front != null) ResolveAttack(state, p2Front, lane.P1, events);
        }

        private static void ResolveBackPhase(
            GameState state,
            Lane lane,
            CardInstance p1Back,
            CardInstance p2Back,
            List<GameEvent> events)
        {
            if (p1Back != null && p1Back.CurrentHealth > 0) ResolveAttack(state, p1Back, lane.P2, events);
            if (p2Back != null && p2Back.CurrentHealth > 0) ResolveAttack(state, p2Back, lane.P1, events);
        }

        /// <summary>
        /// One card takes its attack turn: 1 + ExtraAttack swings. Precision
        /// swings go straight at the enemy player; pierce swings hit every living
        /// enemy guy in the lane; otherwise the front-most guy takes it, with
        /// overkill overflow spilling onto the player. Rob steals gold scaled by
        /// any damage this attack deals to the player. Extra swings stop if the
        /// attacker died (e.g. to thorns) — only the first swing is owed by the
        /// simultaneity rule.
        /// </summary>
        private static void ResolveAttack(
            GameState state,
            CardInstance attacker,
            Sublane opposingSublane,
            List<GameEvent> events)
        {
            int swings = 1 + AbilityRuntime.Sum(
                attacker, AbilityTrigger.Passive, AbilityEffect.ExtraAttack, AbilityTarget.Self);
            bool targetsPlayer = AbilityRuntime.Sum(
                attacker, AbilityTrigger.Passive, AbilityEffect.TargetPlayer, AbilityTarget.Self) > 0;
            bool hitsWholeLane = AbilityRuntime.Sum(
                attacker, AbilityTrigger.OnAttack, AbilityEffect.HitAllInLane, AbilityTarget.EnemiesInLane) > 0;
            int robPerDamage = AbilityRuntime.Sum(
                attacker, AbilityTrigger.OnAttack, AbilityEffect.StealGold, AbilityTarget.Owner);
            bool overkill = AbilityRuntime.Sum(
                attacker, AbilityTrigger.OnAttack, AbilityEffect.Overkill, AbilityTarget.EnemyPlayer) > 0;

            Player opposingPlayer = state.Players[opposingSublane.PlayerId];
            Player owner = state.Players[attacker.OwnerId];

            // Every point of combat damage this attack puts on the player also
            // triggers rob: steal damage * X gold (capped by the victim's purse).
            void HitPlayer(int damage)
            {
                MutationHelper.DealCombatDamageToPlayer(opposingPlayer, damage, events);
                if (robPerDamage > 0 && damage > 0)
                {
                    MutationHelper.StealGold(owner, opposingPlayer, damage * robPerDamage, events);
                }
            }

            for (int swing = 0; swing < swings; swing++)
            {
                if (swing > 0 && attacker.CurrentHealth <= 0) break;

                // Precision: ignore blockers, hit the hero.
                if (targetsPlayer)
                {
                    HitPlayer(attacker.CurrentAttack);
                    continue;
                }

                // Pierce: the swing hits every living enemy guy in the lane at
                // once (snapshot first — thorns/deaths mutate the slots).
                if (hitsWholeLane)
                {
                    var laneTargets = new List<CardInstance>();
                    foreach (var card in opposingSublane.Slots)
                        if (card != null && card.CurrentHealth > 0)
                            laneTargets.Add(card);

                    if (laneTargets.Count > 0)
                    {
                        foreach (var laneTarget in laneTargets)
                            MutationHelper.DealCombatDamage(laneTarget, attacker, events);
                        continue;
                    }
                    // empty lane: fall through to the normal face hit
                }

                CardInstance target = FindFrontMostCard(opposingSublane);
                if (target != null)
                {
                    int overflow = MutationHelper.DealCombatDamage(target, attacker, events);
                    if (overkill && overflow > 0)
                    {
                        HitPlayer(overflow);
                    }
                }
                else
                {
                    HitPlayer(attacker.CurrentAttack);
                }
            }
        }

        private static CardInstance FindFrontMostCard(Sublane sublane)
        {
            for (int i = 0; i < sublane.Slots.Length; i++)
            {
                var card = sublane.Slots[i];
                if (card != null && card.CurrentHealth > 0) return card;
            }
            return null;
        }

        // ------------------------------------------------------------------
        // Deaths & game end
        // ------------------------------------------------------------------

        private static void ProcessDeaths(GameState state, Lane lane, List<GameEvent> events)
        {
            // Rewards first for every death on both sides, while all dead cards
            // are still in their slots — in a mutual kill each killer must still
            // be findable for its bounty, no matter which sublane is swept first.
            AwardDeathRewards(state, lane.P1, events);
            AwardDeathRewards(state, lane.P2, events);
            RemoveDeadCards(lane, lane.P1, events);
            RemoveDeadCards(lane, lane.P2, events);
        }

        private static void AwardDeathRewards(GameState state, Sublane sublane, List<GameEvent> events)
        {
            foreach (CardInstance card in sublane.Slots)
            {
                if (card == null) continue;
                if (card.CurrentHealth > 0) continue;
                if (!card.LastDamageWasCombatDamage) continue;

                int opposingPlayerId = 1 - card.OwnerId;
                MutationHelper.GiveGold(state.Players[opposingPlayerId], card.KillRewardGold, events);

                // Bounty: the killer's own "gain gold on kill" ability.
                var killer = state.FindCardInLanes(card.LastDamagerCardId);
                if (killer != null)
                {
                    int bounty = AbilityRuntime.Sum(
                        killer,
                        AbilityTrigger.OnKill,
                        AbilityEffect.GainGold,
                        AbilityTarget.Owner);
                    MutationHelper.GiveGold(state.Players[killer.OwnerId], bounty, events);
                }
            }
        }

        private static void RemoveDeadCards(Lane lane, Sublane sublane, List<GameEvent> events)
        {
            for (int i = 0; i < sublane.Slots.Length; i++)
            {
                CardInstance card = sublane.Slots[i];
                if (card == null) continue;
                if (card.CurrentHealth > 0) continue;

                events.Add(new CardDiedEvent(
                    CardInstanceId: card.InstanceId,
                    LaneIndex: lane.Position,
                    SlotIndex: i,
                    KillerId: card.LastDamagerCardId));

                sublane.Slots[i] = null;
            }
        }

        /// <summary>
        /// Returns true if either player is at 0 health. Emits GameEndedEvent (once) with the winner.
        /// Simultaneous 0-0 reports winner -1 (a draw), though lane-by-lane resolution
        /// means lane order usually decides first.
        /// </summary>
        private static bool CheckGameEnd(GameState state, List<GameEvent> events)
        {
            bool p0Dead = state.Players[0].Health <= 0;
            bool p1Dead = state.Players[1].Health <= 0;

            if (!p0Dead && !p1Dead) return false;

            int winnerId = (p0Dead && p1Dead) ? -1 : (p0Dead ? 1 : 0);
            events.Add(new GameEndedEvent(winnerId));
            return true;
        }
    }
}
