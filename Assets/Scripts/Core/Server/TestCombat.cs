using System.Collections.Generic;
using System.Linq;
using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif
using Game.Cards;
using Game.Core.Events;
using Game.Core.State;

namespace Game.Core.Server
{
    public static class CombatTests
    {
        // ============================================================
        //  Setup helpers
        // ============================================================

        public static GameState MakeTestState(int laneCount = 4, int slotsPerSide = 2)
        {
            return new GameState(seed: 42, laneCount: laneCount, slotsPerSide: slotsPerSide);
        }

        /// Create a card instance without placing it on the board — for mutation helper tests.
        public static CardInstance MakeGuy(int instanceId, int ownerId, int attack, int health, int killRewardGold = 1)
        {
            return new CardInstance
            {
                InstanceId = instanceId,
                DefinitionId = "test",
                OwnerId = ownerId,
                CurrentAttack = attack,
                CurrentHealth = health,
                CurrentCost = 0,
                KillRewardGold = killRewardGold,
            };
        }

        /// Place a guy directly on the board with given stats.
        public static CardInstance PlaceGuy(GameState state, int playerId, int laneIndex, int slot,
            int attack, int health, int killRewardGold = 1)
        {
            var card = new CardInstance
            {
                InstanceId = state.NextCardInstanceId++,
                DefinitionId = "test",
                OwnerId = playerId,
                CurrentAttack = attack,
                CurrentHealth = health,
                CurrentCost = 0,
                KillRewardGold = killRewardGold,
            };
            state.Lanes[laneIndex].SublaneOf(playerId).Slots[slot] = card;
            return card;
        }

        // ============================================================
        //  Assertion infrastructure
        // ============================================================

        private static int _passed, _failed;
        private static string _currentTest = "";

        private static void BeginTest(string name)
        {
            _currentTest = name;
            Debug.Log($"<color=cyan>>>> {name}</color>");
        }

        private static void Assert(bool condition, string description)
        {
            if (condition) { _passed++; Debug.Log($"  PASS: {description}"); }
            else { _failed++; Debug.LogError($"  FAIL [{_currentTest}]: {description}"); }
        }

        // ============================================================
        //  Run all
        // ============================================================

#if UNITY_EDITOR
        [MenuItem("Game/Run Combat Tests")]
#endif
        public static void RunAll()
        {
            _passed = 0; _failed = 0;

            Debug.Log("===== SECTION 1: Mutation helper tests =====");
            Test_DealCombatDamage_ReducesHealth();
            Test_DealCombatDamage_ClampedAtZero();
            Test_DealCombatDamage_ArmorReducesByOne();
            Test_DealCombatDamage_ArmorDoesNotMakeDamageNegative();
            Test_DealCombatDamage_ZeroAttackDoesNothing();
            Test_DealCombatDamage_EmitsCardDamagedEvent();
            Test_DealCombatDamage_SetsLastDamageWasCombatDamage();
            Test_DealCombatDamageToPlayer_ReducesHealth();
            Test_DealCombatDamageToPlayer_ClampedAtZero();
            Test_DealCombatDamageToPlayer_EmitsPlayerDamagedEvent();
            Test_DealCombatDamageToPlayer_ZeroDamageEmitsNoEvent();
            Test_GiveGold_IncreasesPlayerGold();
            Test_GiveGold_EmitsEvent();
            Test_GiveGold_ZeroAmountDoesNothing();

            Debug.Log("\n===== SECTION 2: Front phase — basic combat =====");
            Test_TwoEqualCards_BothDie();
            Test_StrongerKillsWeaker_StrongerSurvives();
            Test_BothSurvive_NoDeath();
            Test_FrontDamage_IsSymmetric();
            Test_OverkillDamage_DoesNotChainToBack();

            Debug.Log("\n===== SECTION 3: Targeting — front-most card rule =====");
            Test_OffsetFronts_AttackEachOtherInFrontPhase();   // The corrected design
            Test_OnlyBackSlotOccupied_StillAttacksAsFront();
            Test_FrontEmptyBackPresent_AttackerHitsBackCard();
            Test_BothSlotsEmpty_AttackerHitsFace();
            Test_BothSidesEmpty_NoCombat();

            Debug.Log("\n===== SECTION 4: Death processing =====");
            Test_DeadCardIsRemovedFromSublane();
            Test_CreatureKill_AwardsGoldToOpposingPlayer();
            Test_DeathEmitsCardDiedEvent();
            Test_NonCombatKill_DoesNotAwardGold();
            Test_KillRewardGoldRespectsCustomValue();

            Debug.Log("\n===== SECTION 5: Edge cases =====");
            Test_ArmoredCardTakesReducedDamage();
            Test_ZeroAttackCard_NoDamageDealt();
            Test_MassiveOverkill_StopsAtTarget();
            Test_CombatRunsAcrossAllLanes();
            Test_PreExistingZeroHpCard_GetsCleanedUp();

            Debug.Log("\n===== SECTION 6: Back phase (FAIL until back phase is uncommented) =====");
            Test_BothSidesFullBoard_AllAttacks();
            Test_TwoAttackersOneDefender_BothHitDefender();   // The 2v1 scenario you asked for
            Test_BackAttacks_OpposingFrontDiedInFrontPhase();
            Test_BackOnlySide_StillAttacksInBackPhase();

            Debug.Log("\n===== SECTION 7: Multi-lane =====");
            Test_LanesAreIndependent();

            Debug.Log($"\n=== Tests: <color=green>{_passed} passed</color>, <color=red>{_failed} failed</color> ===");
        }

        // ============================================================
        //  SECTION 1: Mutation helper tests
        // ============================================================

        public static void Test_DealCombatDamage_ReducesHealth()
        {
            BeginTest(nameof(Test_DealCombatDamage_ReducesHealth));
            var attacker = MakeGuy(1, 0, attack: 3, health: 5);
            var target = MakeGuy(2, 1, attack: 2, health: 5);

            var events = new List<GameEvent>();
            MutationHelper.DealCombatDamage(target, attacker, events);

            Assert(target.CurrentHealth == 2, "target should be at 5 - 3 = 2 HP");
        }

        public static void Test_DealCombatDamage_ClampedAtZero()
        {
            BeginTest(nameof(Test_DealCombatDamage_ClampedAtZero));
            var attacker = MakeGuy(1, 0, attack: 99, health: 1);
            var target = MakeGuy(2, 1, attack: 1, health: 3);

            var events = new List<GameEvent>();
            MutationHelper.DealCombatDamage(target, attacker, events);

            Assert(target.CurrentHealth == 0, "target should clamp at 0, never go negative");
        }

        public static void Test_DealCombatDamage_ArmorReducesByOne()
        {
            BeginTest(nameof(Test_DealCombatDamage_ArmorReducesByOne));
            var attacker = MakeGuy(1, 0, attack: 3, health: 5);
            var target = MakeGuy(2, 1, attack: 2, health: 5);
            target.StatusEffects.Add("Armored");

            var events = new List<GameEvent>();
            MutationHelper.DealCombatDamage(target, attacker, events);

            Assert(target.CurrentHealth == 3, "armored target should take 3-1=2 damage, ending at 3 HP");
        }

        public static void Test_DealCombatDamage_ArmorDoesNotMakeDamageNegative()
        {
            BeginTest(nameof(Test_DealCombatDamage_ArmorDoesNotMakeDamageNegative));
            var attacker = MakeGuy(1, 0, attack: 1, health: 5);  // 1 attack vs armor = 0 effective
            var target = MakeGuy(2, 1, attack: 2, health: 5);
            target.StatusEffects.Add("Armored");

            var events = new List<GameEvent>();
            MutationHelper.DealCombatDamage(target, attacker, events);

            Assert(target.CurrentHealth == 5, "1 attack vs armor should be 0 damage, target unchanged");
        }

        public static void Test_DealCombatDamage_ZeroAttackDoesNothing()
        {
            BeginTest(nameof(Test_DealCombatDamage_ZeroAttackDoesNothing));
            var attacker = MakeGuy(1, 0, attack: 0, health: 5);
            var target = MakeGuy(2, 1, attack: 1, health: 5);

            var events = new List<GameEvent>();
            MutationHelper.DealCombatDamage(target, attacker, events);

            Assert(target.CurrentHealth == 5, "zero-attack should not reduce health");
            Assert(events.Count == 0, "zero-attack should not emit any event");
        }

        public static void Test_DealCombatDamage_EmitsCardDamagedEvent()
        {
            BeginTest(nameof(Test_DealCombatDamage_EmitsCardDamagedEvent));
            var attacker = MakeGuy(1, 0, attack: 3, health: 5);
            var target = MakeGuy(2, 1, attack: 2, health: 5);

            var events = new List<GameEvent>();
            MutationHelper.DealCombatDamage(target, attacker, events);

            var dmg = events.OfType<CardDamagedEvent>().ToList();
            Assert(dmg.Count == 1, "should emit exactly 1 CardDamagedEvent");
            Assert(dmg.Count == 1 && dmg[0].CardInstanceId == target.InstanceId, "event should reference the damaged card");
        }

        public static void Test_DealCombatDamage_SetsLastDamageWasCombatDamage()
        {
            BeginTest(nameof(Test_DealCombatDamage_SetsLastDamageWasCombatDamage));
            var attacker = MakeGuy(1, 0, attack: 3, health: 5);
            var target = MakeGuy(2, 1, attack: 2, health: 5);

            var events = new List<GameEvent>();
            MutationHelper.DealCombatDamage(target, attacker, events);

            Assert(target.LastDamageWasCombatDamage == true, "combat damage should set the flag to true");
        }

        public static void Test_DealCombatDamageToPlayer_ReducesHealth()
        {
            BeginTest(nameof(Test_DealCombatDamageToPlayer_ReducesHealth));
            var state = MakeTestState();
            int start = state.Players[1].Health;

            var events = new List<GameEvent>();
            MutationHelper.DealCombatDamageToPlayer(state.Players[1], 5, events);

            Assert(state.Players[1].Health == start - 5, $"player health should be {start - 5}");
        }

        public static void Test_DealCombatDamageToPlayer_ClampedAtZero()
        {
            BeginTest(nameof(Test_DealCombatDamageToPlayer_ClampedAtZero));
            var state = MakeTestState();

            var events = new List<GameEvent>();
            MutationHelper.DealCombatDamageToPlayer(state.Players[1], 9999, events);

            Assert(state.Players[1].Health == 0, "player health should clamp at 0");
        }

        public static void Test_DealCombatDamageToPlayer_EmitsPlayerDamagedEvent()
        {
            BeginTest(nameof(Test_DealCombatDamageToPlayer_EmitsPlayerDamagedEvent));
            var state = MakeTestState();

            var events = new List<GameEvent>();
            MutationHelper.DealCombatDamageToPlayer(state.Players[1], 5, events);

            Assert(events.OfType<PlayerDamagedEvent>().Count() == 1, "should emit 1 PlayerDamagedEvent");
        }

        public static void Test_DealCombatDamageToPlayer_ZeroDamageEmitsNoEvent()
        {
            BeginTest(nameof(Test_DealCombatDamageToPlayer_ZeroDamageEmitsNoEvent));
            var state = MakeTestState();

            var events = new List<GameEvent>();
            MutationHelper.DealCombatDamageToPlayer(state.Players[1], 0, events);

            Assert(events.Count == 0, "zero damage should not emit any event");
        }

        public static void Test_GiveGold_IncreasesPlayerGold()
        {
            BeginTest(nameof(Test_GiveGold_IncreasesPlayerGold));
            var state = MakeTestState();
            int start = state.Players[0].Gold;

            var events = new List<GameEvent>();
            MutationHelper.GiveGold(state.Players[0], 3, events);

            Assert(state.Players[0].Gold == start + 3, $"player gold should be {start + 3}");
        }

        public static void Test_GiveGold_EmitsEvent()
        {
            BeginTest(nameof(Test_GiveGold_EmitsEvent));
            var state = MakeTestState();

            var events = new List<GameEvent>();
            MutationHelper.GiveGold(state.Players[0], 3, events);

            Assert(events.Count == 1, "should emit exactly 1 event");
        }

        public static void Test_GiveGold_ZeroAmountDoesNothing()
        {
            BeginTest(nameof(Test_GiveGold_ZeroAmountDoesNothing));
            var state = MakeTestState();
            int start = state.Players[0].Gold;

            var events = new List<GameEvent>();
            MutationHelper.GiveGold(state.Players[0], 0, events);

            Assert(state.Players[0].Gold == start, "gold should not change");
            Assert(events.Count == 0, "no event should be emitted");
        }

        // ============================================================
        //  SECTION 2: Front phase basic combat
        // ============================================================

        public static void Test_TwoEqualCards_BothDie()
        {
            BeginTest(nameof(Test_TwoEqualCards_BothDie));
            var state = MakeTestState();
            PlaceGuy(state, 0, 0, 0, attack: 3, health: 3);
            PlaceGuy(state, 1, 0, 0, attack: 3, health: 3);

            var events = new List<GameEvent>();
            CombatResolver.Resolve(state, events);

            Assert(state.Lanes[0].P1.Slots[0] == null, "P1 slot 0 should be empty");
            Assert(state.Lanes[0].P2.Slots[0] == null, "P2 slot 0 should be empty");
            Assert(events.OfType<CardDiedEvent>().Count() == 2, "should emit 2 CardDiedEvents");
        }

        public static void Test_StrongerKillsWeaker_StrongerSurvives()
        {
            BeginTest(nameof(Test_StrongerKillsWeaker_StrongerSurvives));
            var state = MakeTestState();
            PlaceGuy(state, 0, 0, 0, attack: 5, health: 5);
            PlaceGuy(state, 1, 0, 0, attack: 1, health: 2);

            var events = new List<GameEvent>();
            CombatResolver.Resolve(state, events);

            var survivor = state.Lanes[0].P1.Slots[0];
            Assert(survivor != null && survivor.CurrentHealth == 4, "P1 should be at 4 HP (5 - 1)");
            Assert(state.Lanes[0].P2.Slots[0] == null, "P2 card should be dead");
        }

        public static void Test_BothSurvive_NoDeath()
        {
            BeginTest(nameof(Test_BothSurvive_NoDeath));
            var state = MakeTestState();
            PlaceGuy(state, 0, 0, 0, attack: 1, health: 5);
            PlaceGuy(state, 1, 0, 0, attack: 1, health: 5);

            var events = new List<GameEvent>();
            CombatResolver.Resolve(state, events);

            Assert(state.Lanes[0].P1.Slots[0]?.CurrentHealth == 4, "P1 should be at 4 HP");
            Assert(state.Lanes[0].P2.Slots[0]?.CurrentHealth == 4, "P2 should be at 4 HP");
            Assert(events.OfType<CardDiedEvent>().Count() == 0, "no cards should die");
        }

        public static void Test_FrontDamage_IsSymmetric()
        {
            BeginTest(nameof(Test_FrontDamage_IsSymmetric));
            var state = MakeTestState();
            PlaceGuy(state, 0, 0, 0, attack: 2, health: 10);
            PlaceGuy(state, 1, 0, 0, attack: 4, health: 10);

            var events = new List<GameEvent>();
            CombatResolver.Resolve(state, events);

            Assert(state.Lanes[0].P1.Slots[0]?.CurrentHealth == 6, "P1 should take 4 damage");
            Assert(state.Lanes[0].P2.Slots[0]?.CurrentHealth == 8, "P2 should take 2 damage");
        }

        public static void Test_OverkillDamage_DoesNotChainToBack()
        {
            BeginTest(nameof(Test_OverkillDamage_DoesNotChainToBack));
            var state = MakeTestState();
            PlaceGuy(state, 0, 0, 0, attack: 100, health: 100);
            PlaceGuy(state, 1, 0, 0, attack: 0, health: 2);
            PlaceGuy(state, 1, 0, 1, attack: 0, health: 5);

            var events = new List<GameEvent>();
            CombatResolver.Resolve(state, events);

            Assert(state.Lanes[0].P2.Slots[0] == null, "P2 front should die");
            Assert(state.Lanes[0].P2.Slots[1]?.CurrentHealth == 5, "P2 back should be untouched (no overflow)");
        }

        // ============================================================
        //  SECTION 3: Targeting — front-most card rule
        // ============================================================

        /// The corrected design: front phase fires for whoever is the front-most card on each side,
        /// regardless of slot index. P1 in slot 0 and P2 in slot 1 attack each other simultaneously.
        public static void Test_OffsetFronts_AttackEachOtherInFrontPhase()
        {
            BeginTest(nameof(Test_OffsetFronts_AttackEachOtherInFrontPhase));
            var state = MakeTestState();
            PlaceGuy(state, 0, 0, 0, attack: 3, health: 10);  // P1 only has slot 0
            PlaceGuy(state, 1, 0, 1, attack: 2, health: 10);  // P2 only has slot 1

            var events = new List<GameEvent>();
            CombatResolver.Resolve(state, events);

            Assert(state.Lanes[0].P1.Slots[0]?.CurrentHealth == 8, "P1 slot 0 should take 2 from P2 slot 1");
            Assert(state.Lanes[0].P2.Slots[1]?.CurrentHealth == 7, "P2 slot 1 should take 3 from P1 slot 0");
        }

        public static void Test_OnlyBackSlotOccupied_StillAttacksAsFront()
        {
            BeginTest(nameof(Test_OnlyBackSlotOccupied_StillAttacksAsFront));
            var state = MakeTestState();
            PlaceGuy(state, 0, 0, 1, attack: 4, health: 5);  // P1 only in slot 1
            // P2 has nothing — slot 1 attacker should still fire and hit face
            int p1FaceStart = state.Players[1].Health;

            var events = new List<GameEvent>();
            CombatResolver.Resolve(state, events);

            Assert(state.Players[1].Health == p1FaceStart - 4, "P1 slot 1 (front-most) should hit face for 4");
        }

        public static void Test_FrontEmptyBackPresent_AttackerHitsBackCard()
        {
            BeginTest(nameof(Test_FrontEmptyBackPresent_AttackerHitsBackCard));
            var state = MakeTestState();
            PlaceGuy(state, 0, 0, 0, attack: 3, health: 10);
            // P2 slot 0 empty
            PlaceGuy(state, 1, 0, 1, attack: 0, health: 10);

            var events = new List<GameEvent>();
            CombatResolver.Resolve(state, events);

            Assert(state.Lanes[0].P2.Slots[1]?.CurrentHealth == 7, "P2 slot 1 should take 3 damage (it's P2's front)");
        }

        public static void Test_BothSlotsEmpty_AttackerHitsFace()
        {
            BeginTest(nameof(Test_BothSlotsEmpty_AttackerHitsFace));
            var state = MakeTestState();
            PlaceGuy(state, 0, 0, 0, attack: 5, health: 10);
            int p1FaceStart = state.Players[1].Health;
            // P2 has nothing in lane 0

            var events = new List<GameEvent>();
            CombatResolver.Resolve(state, events);

            Assert(state.Players[1].Health == p1FaceStart - 5, "P2 face should take 5 damage");
        }

        public static void Test_BothSidesEmpty_NoCombat()
        {
            BeginTest(nameof(Test_BothSidesEmpty_NoCombat));
            var state = MakeTestState();
            int p0FaceStart = state.Players[0].Health;
            int p1FaceStart = state.Players[1].Health;

            var events = new List<GameEvent>();
            CombatResolver.Resolve(state, events);

            Assert(state.Players[0].Health == p0FaceStart, "P0 face untouched");
            Assert(state.Players[1].Health == p1FaceStart, "P1 face untouched");
            Assert(events.Count == 0, "no events should fire with no cards on the board");
        }

        // ============================================================
        //  SECTION 4: Death processing
        // ============================================================

        public static void Test_DeadCardIsRemovedFromSublane()
        {
            BeginTest(nameof(Test_DeadCardIsRemovedFromSublane));
            var state = MakeTestState();
            PlaceGuy(state, 0, 0, 0, attack: 99, health: 99);
            PlaceGuy(state, 1, 0, 0, attack: 1, health: 1);

            var events = new List<GameEvent>();
            CombatResolver.Resolve(state, events);

            Assert(state.Lanes[0].P2.Slots[0] == null, "P2 slot 0 should be null after death");
        }

        public static void Test_CreatureKill_AwardsGoldToOpposingPlayer()
        {
            BeginTest(nameof(Test_CreatureKill_AwardsGoldToOpposingPlayer));
            var state = MakeTestState();
            int p0StartGold = state.Players[0].Gold;
            PlaceGuy(state, 0, 0, 0, attack: 99, health: 99);
            PlaceGuy(state, 1, 0, 0, attack: 1, health: 1, killRewardGold: 3);

            var events = new List<GameEvent>();
            CombatResolver.Resolve(state, events);

            Assert(state.Players[0].Gold == p0StartGold + 3,
                $"P0 should gain 3 gold (start={p0StartGold}, got={state.Players[0].Gold})");
        }

        public static void Test_DeathEmitsCardDiedEvent()
        {
            BeginTest(nameof(Test_DeathEmitsCardDiedEvent));
            var state = MakeTestState();
            PlaceGuy(state, 0, 0, 0, attack: 99, health: 99);
            PlaceGuy(state, 1, 0, 0, attack: 1, health: 1);

            var events = new List<GameEvent>();
            CombatResolver.Resolve(state, events);

            Assert(events.OfType<CardDiedEvent>().Count() >= 1, "at least 1 CardDiedEvent should fire");
        }

        public static void Test_NonCombatKill_DoesNotAwardGold()
        {
            BeginTest(nameof(Test_NonCombatKill_DoesNotAwardGold));
            var state = MakeTestState();
            int p0StartGold = state.Players[0].Gold;
            var victim = PlaceGuy(state, 1, 0, 0, attack: 0, health: 1, killRewardGold: 3);
            victim.CurrentHealth = 0;
            victim.LastDamageWasCombatDamage = false;

            var events = new List<GameEvent>();
            CombatResolver.Resolve(state, events);

            Assert(state.Players[0].Gold == p0StartGold, "no gold awarded for non-combat death");
        }

        public static void Test_KillRewardGoldRespectsCustomValue()
        {
            BeginTest(nameof(Test_KillRewardGoldRespectsCustomValue));
            var state = MakeTestState();
            int p0StartGold = state.Players[0].Gold;
            PlaceGuy(state, 0, 0, 0, attack: 99, health: 99);
            PlaceGuy(state, 1, 0, 0, attack: 0, health: 1, killRewardGold: 7);

            var events = new List<GameEvent>();
            CombatResolver.Resolve(state, events);

            Assert(state.Players[0].Gold == p0StartGold + 7, "P0 should gain 7 gold for high-bounty kill");
        }

        // ============================================================
        //  SECTION 5: Edge cases
        // ============================================================

        public static void Test_ArmoredCardTakesReducedDamage()
        {
            BeginTest(nameof(Test_ArmoredCardTakesReducedDamage));
            var state = MakeTestState();
            PlaceGuy(state, 0, 0, 0, attack: 3, health: 10);
            var target = PlaceGuy(state, 1, 0, 0, attack: 0, health: 10);
            target.StatusEffects.Add("Armored");

            var events = new List<GameEvent>();
            CombatResolver.Resolve(state, events);

            Assert(target.CurrentHealth == 8, "armored target should take 3-1=2 damage");
        }

        public static void Test_ZeroAttackCard_NoDamageDealt()
        {
            BeginTest(nameof(Test_ZeroAttackCard_NoDamageDealt));
            var state = MakeTestState();
            PlaceGuy(state, 0, 0, 0, attack: 0, health: 10);
            var target = PlaceGuy(state, 1, 0, 0, attack: 0, health: 10);

            var events = new List<GameEvent>();
            CombatResolver.Resolve(state, events);

            Assert(target.CurrentHealth == 10, "0-attack should deal 0 damage");
            Assert(events.Count == 0, "no events should fire from 0-attack trades");
        }

        public static void Test_MassiveOverkill_StopsAtTarget()
        {
            BeginTest(nameof(Test_MassiveOverkill_StopsAtTarget));
            var state = MakeTestState();
            int p1FaceStart = state.Players[1].Health;
            PlaceGuy(state, 0, 0, 0, attack: 1000, health: 1000);
            PlaceGuy(state, 1, 0, 0, attack: 0, health: 1);

            var events = new List<GameEvent>();
            CombatResolver.Resolve(state, events);

            Assert(state.Lanes[0].P2.Slots[0] == null, "P2 should die");
            Assert(state.Players[1].Health == p1FaceStart, "P2 face should take no damage (no overflow)");
        }

        public static void Test_CombatRunsAcrossAllLanes()
        {
            BeginTest(nameof(Test_CombatRunsAcrossAllLanes));
            var state = MakeTestState(laneCount: 4);
            for (int i = 0; i < 4; i++)
            {
                PlaceGuy(state, 0, i, 0, attack: 3, health: 3);
                PlaceGuy(state, 1, i, 0, attack: 3, health: 3);
            }

            var events = new List<GameEvent>();
            CombatResolver.Resolve(state, events);

            Assert(events.OfType<CardDiedEvent>().Count() == 8, "all 8 cards should die (4 lanes x 2 sides)");
        }

        public static void Test_PreExistingZeroHpCard_GetsCleanedUp()
        {
            BeginTest(nameof(Test_PreExistingZeroHpCard_GetsCleanedUp));
            var state = MakeTestState();
            var corpse = PlaceGuy(state, 0, 0, 0, attack: 0, health: 1);
            corpse.CurrentHealth = 0;
            corpse.LastDamageWasCombatDamage = true;

            var events = new List<GameEvent>();
            CombatResolver.Resolve(state, events);

            Assert(state.Lanes[0].P1.Slots[0] == null, "pre-existing 0-HP card should be removed by ProcessDeaths");
        }

        // ============================================================
        //  SECTION 6: Back phase (only passes once back phase is uncommented)
        // ============================================================

        public static void Test_BothSidesFullBoard_AllAttacks()
        {
            BeginTest(nameof(Test_BothSidesFullBoard_AllAttacks));
            var state = MakeTestState();
            PlaceGuy(state, 0, 0, 0, attack: 2, health: 10);  // P1 front
            PlaceGuy(state, 0, 0, 1, attack: 1, health: 10);  // P1 back
            PlaceGuy(state, 1, 0, 0, attack: 2, health: 10);  // P2 front
            PlaceGuy(state, 1, 0, 1, attack: 1, health: 10);  // P2 back

            var events = new List<GameEvent>();
            CombatResolver.Resolve(state, events);

            // Front phase: P1f(2) hits P2f. P2f(2) hits P1f. Both go to 8.
            // Back phase: P1b(1) hits P2's current front = P2f (still alive at 8). P2f → 7.
            //             P2b(1) hits P1's current front = P1f (still alive at 8). P1f → 7.
            Assert(state.Lanes[0].P1.Slots[0]?.CurrentHealth == 7, "P1 front should be at 7 HP");
            Assert(state.Lanes[0].P2.Slots[0]?.CurrentHealth == 7, "P2 front should be at 7 HP");
            Assert(state.Lanes[0].P1.Slots[1]?.CurrentHealth == 10, "P1 back untouched");
            Assert(state.Lanes[0].P2.Slots[1]?.CurrentHealth == 10, "P2 back untouched");
        }

        /// 2 attackers on P1 side, 1 defender on P2 side. Both P1 attackers should hit the single
        /// P2 defender (the defender is the front-most card for both attacks). The defender attacks
        /// only once (in front phase) because it's their only card.
        public static void Test_TwoAttackersOneDefender_BothHitDefender()
        {
            BeginTest(nameof(Test_TwoAttackersOneDefender_BothHitDefender));
            var state = MakeTestState();
            PlaceGuy(state, 0, 0, 0, attack: 3, health: 10);   // P1 front: deals 3 in front phase
            PlaceGuy(state, 0, 0, 1, attack: 2, health: 10);   // P1 back:  deals 2 in back phase
            PlaceGuy(state, 1, 0, 0, attack: 4, health: 20);   // P2 only card: defends, attacks once

            var events = new List<GameEvent>();
            CombatResolver.Resolve(state, events);

            // P2 defender takes 3 + 2 = 5 damage. P1 front takes 4 damage. P1 back is untouched.
            Assert(state.Lanes[0].P2.Slots[0]?.CurrentHealth == 15,
                "P2 defender should take 3+2=5 damage from both P1 attackers (current 20-5=15)");
            Assert(state.Lanes[0].P1.Slots[0]?.CurrentHealth == 6,
                "P1 front should take 4 damage from the single defender's one attack");
            Assert(state.Lanes[0].P1.Slots[1]?.CurrentHealth == 10,
                "P1 back should be untouched");
        }

        public static void Test_BackAttacks_OpposingFrontDiedInFrontPhase()
        {
            BeginTest(nameof(Test_BackAttacks_OpposingFrontDiedInFrontPhase));
            var state = MakeTestState();
            PlaceGuy(state, 0, 0, 0, attack: 5, health: 10);  // P1 front kills P2 front
            PlaceGuy(state, 0, 0, 1, attack: 3, health: 10);  // P1 back attacks AFTER P2 front dies
            PlaceGuy(state, 1, 0, 0, attack: 0, health: 2);   // P2 front dies in front phase
            PlaceGuy(state, 1, 0, 1, attack: 0, health: 10);  // P2 back becomes P2 front after death

            var events = new List<GameEvent>();
            CombatResolver.Resolve(state, events);

            Assert(state.Lanes[0].P2.Slots[0] == null, "P2 front dead");
            Assert(state.Lanes[0].P2.Slots[1]?.CurrentHealth == 7,
                "P2 back should take 3 damage from P1 back (now opposing front-most)");
        }

        public static void Test_BackOnlySide_StillAttacksInBackPhase()
        {
            BeginTest(nameof(Test_BackOnlySide_StillAttacksInBackPhase));
            var state = MakeTestState();
            PlaceGuy(state, 0, 0, 0, attack: 5, health: 10);  // P1 front: kills P2's only card
            PlaceGuy(state, 0, 0, 1, attack: 4, health: 10);  // P1 back: hits face in back phase
            PlaceGuy(state, 1, 0, 0, attack: 0, health: 3);   // P2's only card, dies front phase
            int p1FaceStart = state.Players[1].Health;

            var events = new List<GameEvent>();
            CombatResolver.Resolve(state, events);

            Assert(state.Lanes[0].P2.Slots[0] == null, "P2 card dead");
            Assert(state.Players[1].Health == p1FaceStart - 4,
                "P2 face should take 4 damage from P1 back hitting empty board");
        }

        // ============================================================
        //  SECTION 7: Multi-lane
        // ============================================================

        public static void Test_LanesAreIndependent()
        {
            BeginTest(nameof(Test_LanesAreIndependent));
            var state = MakeTestState(laneCount: 4);
            // Lane 0: trade
            PlaceGuy(state, 0, 0, 0, attack: 3, health: 3);
            PlaceGuy(state, 1, 0, 0, attack: 3, health: 3);
            // Lane 2: P0 hits face uncontested
            PlaceGuy(state, 0, 2, 0, attack: 2, health: 5);
            int p1FaceStart = state.Players[1].Health;

            var events = new List<GameEvent>();
            CombatResolver.Resolve(state, events);

            Assert(state.Lanes[0].P1.Slots[0] == null && state.Lanes[0].P2.Slots[0] == null,
                "Lane 0: both cards should die");
            Assert(state.Lanes[2].P1.Slots[0]?.CurrentHealth == 5, "Lane 2: P0 card untouched");
            Assert(state.Players[1].Health == p1FaceStart - 2,
                "Lane 2's uncontested attacker should hit P1 face for 2");
        }
    }
}