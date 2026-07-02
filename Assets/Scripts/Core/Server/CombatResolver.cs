using System.Collections.Generic;
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
                Dictionary<int, int> attacks = SnapshotAttacks(lane);

                var (p1Front, p1Back) = GetFrontAndBack(lane.P1);
                var (p2Front, p2Back) = GetFrontAndBack(lane.P2);

                ResolveFrontPhase(state, lane, p1Front, p2Front, events);
                ProcessDeaths(state, lane, events);
                ResolveBackPhase(state, lane, p1Back, p2Back, events);
                ProcessDeaths(state, lane, events);
                if (CheckGameEnd(state, events)) return;
            }
        }

        private static Dictionary<int, int> SnapshotAttacks(Lane lane)
        {
            Dictionary<int, int> attacks = new Dictionary<int, int>();

            foreach (var card in lane.P1.Slots)
            {
                if (card != null) attacks[card.InstanceId] = card.CurrentAttack;
            }
            foreach (var card in lane.P2.Slots)
            {
                if (card != null) attacks[card.InstanceId] = card.CurrentAttack;
            }

            return attacks;
        }

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

        private static void ResolveAttack(
            GameState state,
            CardInstance attacker,
            Sublane opposingSublane,
            List<GameEvent> events)
        {
            CardInstance target = FindFrontMostCard(opposingSublane);

            if (target != null)
            {
                MutationHelper.DealCombatDamage(target, attacker, events);
            }
            else
            {
                Player opposingPlayer = state.Players[opposingSublane.PlayerId];
                MutationHelper.DealCombatDamageToPlayer(opposingPlayer, attacker.CurrentAttack, events);
            }
        }

        private static CardInstance FindFrontMostCard(Sublane sublane)
        {
            for (int i = 0; i < sublane.Slots.Length; i++)
            {
                if (sublane.Slots[i] != null) return sublane.Slots[i];
            }
            return null;
        }

        private static void ProcessDeaths(GameState state, Lane lane, List<GameEvent> events)
        {
            ProcessDeathsInSublane(state, lane, lane.P1, events);
            ProcessDeathsInSublane(state, lane, lane.P2, events);
        }

        private static void ProcessDeathsInSublane(
            GameState state,
            Lane lane,
            Sublane sublane,
            List<GameEvent> events)
        {
            for (int i = 0; i < sublane.Slots.Length; i++)
            {
                CardInstance card = sublane.Slots[i];
                if (card == null) continue;
                if (card.CurrentHealth > 0) continue;

                if (card.LastDamageWasCombatDamage)
                {
                    int opposingPlayerId = 1 - card.OwnerId;
                    MutationHelper.GiveGold(state.Players[opposingPlayerId], card.KillRewardGold, events);
                }

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
        /// A player wins if their opponent hits 0. (Simultaneous 0-0 is currently decided in P0's favor —
        /// see note below.)
        /// </summary>
        private static bool CheckGameEnd(GameState state, List<GameEvent> events)
        {
            bool p0Dead = state.Players[0].Health <= 0;
            bool p1Dead = state.Players[1].Health <= 0;

            if (!p0Dead && !p1Dead) return false;

            int winnerId = (p0Dead && p1Dead) ? -1 : (p0Dead ? 1 : 0);            events.Add(new GameEndedEvent(winnerId));
            return true;
        }
    }
}