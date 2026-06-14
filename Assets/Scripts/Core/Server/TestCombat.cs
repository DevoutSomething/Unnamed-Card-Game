using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Game.Cards;
using Game.Core.Events;
using Game.Core.State;

namespace Game.Core.Server
{
    public static class CombatTests
    {
        // ===== Setup helpers =====

        /// Create a clean GameState with empty lanes.
        public static GameState MakeTestState(int laneCount = 4, int slotsPerSide = 2)
        {
            return new GameState(seed: 42, laneCount: laneCount, slotsPerSide: slotsPerSide);
        }

        /// Place a Guy directly onto the board with given stats. No CardDefinition needed.
        public static CardInstance PlaceGuy(GameState state, int playerId, int laneIndex, int slot,
            int attack, int health, int killRewardGold = 1, string definitionId = "test")
        {
            var card = new CardInstance
            {
                InstanceId = state.NextCardInstanceId++,
                DefinitionId = definitionId,
                OwnerId = playerId,
                CurrentAttack = attack,
                CurrentHealth = health,
                CurrentCost = 0,
                KillRewardGold = killRewardGold,
            };
            state.Lanes[laneIndex].SublaneOf(playerId).Slots[slot] = card;
            return card;
        }

        // ===== Assertion helper =====

        private static int _passed, _failed;

        private static void Assert(bool condition, string description)
        {
            if (condition) { _passed++; Debug.Log($"  PASS: {description}"); }
            else { _failed++; Debug.LogError($"  FAIL: {description}"); }
        }

        // ===== Run all =====

        public static void RunAll()
        {
            _passed = 0; _failed = 0;
            Test_TwoEqualCards_BothDie();
            // append more tests as you write them
            Debug.Log($"\n=== Tests: {_passed} passed, {_failed} failed ===");
        }

        // ===== Tests =====

        public static void Test_TwoEqualCards_BothDie()
        {
            Debug.Log("Test_TwoEqualCards_BothDie:");
            var state = MakeTestState();
            PlaceGuy(state, playerId: 0, laneIndex: 0, slot: 0, attack: 3, health: 3);
            PlaceGuy(state, playerId: 1, laneIndex: 0, slot: 0, attack: 3, health: 3);

            var events = new List<GameEvent>();
            CombatResolver.Resolve(state, events);

            Assert(state.Lanes[0].P1.Slots[0] == null, "P1 slot 0 should be empty");
            Assert(state.Lanes[0].P2.Slots[0] == null, "P2 slot 0 should be empty");
            Assert(events.OfType<CardDiedEvent>().Count() == 2, "should emit 2 CardDiedEvents");
        }
    }
}