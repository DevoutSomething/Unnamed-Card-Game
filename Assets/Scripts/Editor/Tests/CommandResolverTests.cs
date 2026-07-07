using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using Game.Core.Commands;
using Game.Core.Events;
using Game.Core.Server;
using Game.Core.State;

namespace Game.Core.Tests
{
    public class CommandResolverTests
    {
        // ---------- helpers ----------

        private static GameState NewGame(int seed = 42)
        {
            var state = new GameState(seed);
            CommandResolver.Resolve(state, new StartGameCommand(0));
            return state;
        }

        private static List<GameEvent> Submit(GameState state, Command cmd)
            => CommandResolver.Resolve(state, cmd);

        /// Finds a card in the player's hand costing at most maxCost.
        /// Decks are seed-shuffled, so tests must find cards by cost, never by index.
        private static CardInstance AffordableCard(Player player, int maxCost)
        {
            var card = player.cardsInHand.FirstOrDefault(c => c.CurrentCost <= maxCost);
            Assert.IsNotNull(card, $"expected a card costing <= {maxCost} in hand (seed-dependent; try another seed)");
            return card;
        }

        private static void AssertNoRejections(IEnumerable<GameEvent> events)
        {
            Assert.IsFalse(events.OfType<CommandRejectedEvent>().Any(),
                "expected no CommandRejectedEvent in batch");
        }

        private static void AssertRejected(IEnumerable<GameEvent> events)
        {
            Assert.IsTrue(events.OfType<CommandRejectedEvent>().Any(),
                "expected a CommandRejectedEvent in batch");
        }

        // ---------- StartGame ----------

        [Test]
        public void StartGame_DealsStartingHandsAndEnergy()
        {
            var state = NewGame();

            // P0's first turn starts immediately, so P0 has the opening hand (4)
            // plus their first turn draw; P1 draws when their first slot begins.
            Assert.AreEqual(5, state.Players[0].cardsInHand.Count, "P0 hand: 4 dealt + 1 turn draw");
            Assert.AreEqual(5, state.Players[0].Deck.Count, "P0 deck: 10 - 5");
            Assert.AreEqual(4, state.Players[1].cardsInHand.Count, "P1 hand: dealt only");
            Assert.AreEqual(6, state.Players[1].Deck.Count, "P1 deck: 10 - 4");

            foreach (var p in state.Players)
            {
                Assert.AreEqual(1, p.CurrentEnergy, $"P{p.Id} energy");
                Assert.AreEqual(1, p.EnergyPerTurn, $"P{p.Id} energy per turn");
            }

            Assert.AreEqual(0, state.SlotIndex);
            Assert.AreEqual(0, state.RotationIndex);
            Assert.AreEqual(SlotType.Action, state.CurrentSlotType);
            Assert.AreEqual(0, state.ActivePlayerId, "rotation slot 0 should be P0's action");
        }

        [Test]
        public void StartGame_SameSeedProducesIdenticalDeckOrder()
        {
            var a = NewGame(seed: 7);
            var b = NewGame(seed: 7);

            CollectionAssert.AreEqual(
                a.Players[0].Deck.Select(c => c.DefinitionId).ToList(),
                b.Players[0].Deck.Select(c => c.DefinitionId).ToList(),
                "same seed must shuffle identically (determinism)");
        }

        [Test]
        public void StartGame_EmitsExpectedEventTypes()
        {
            var state = new GameState(42);
            var events = Submit(state, new StartGameCommand(0));

            Assert.AreEqual(1, events.OfType<GameStartedEvent>().Count());
            Assert.AreEqual(2, events.OfType<EnergyChangedEvent>().Count(), "one per player");
            Assert.AreEqual(9, events.OfType<CardDrawnEvent>().Count(), "4 per player + P0's first turn draw");
            Assert.AreEqual(1, events.OfType<SlotChangedEvent>().Count());
            AssertNoRejections(events);
        }

        // ---------- PlayCard: happy path ----------

        [Test]
        public void PlayCard_MovesCardAndDeductsEnergy()
        {
            var state = NewGame();
            var p0 = state.Players[0];
            var card = AffordableCard(p0, p0.CurrentEnergy);
            int cost = card.CurrentCost;
            int handBefore = p0.cardsInHand.Count;

            var events = Submit(state, new PlayCardCommand(0, card.InstanceId, LaneIndex: 0));

            AssertNoRejections(events);
            Assert.AreEqual(handBefore - 1, p0.cardsInHand.Count, "card left hand");
            Assert.IsFalse(p0.cardsInHand.Contains(card));
            Assert.AreEqual(1 - cost, p0.CurrentEnergy, "energy deducted");

            var sublane = state.Lanes[0].SublaneOf(0);
            Assert.IsTrue(sublane.Cards.Contains(card), "card is on the board");
        }

        [Test]
        public void PlayCard_EventOrder_EnergyBeforeCardPlayed()
        {
            var state = NewGame();
            var p0 = state.Players[0];
            var card = AffordableCard(p0, p0.CurrentEnergy);

            var events = Submit(state, new PlayCardCommand(0, card.InstanceId, LaneIndex: 0));

            int energyIdx = events.FindIndex(e => e is EnergyChangedEvent);
            int playedIdx = events.FindIndex(e => e is CardPlayedEvent);
            Assert.IsTrue(energyIdx >= 0 && playedIdx >= 0, "both events emitted");
            Assert.Less(energyIdx, playedIdx, "EnergyChanged must precede CardPlayed");
        }

        [Test]
        public void PlayCard_AutoPlaceFillsFirstEmptySlot()
        {
            var state = NewGame();
            var p0 = state.Players[0];
            p0.CurrentEnergy = 99; // force affordability for this placement-only test
            var card = p0.cardsInHand[0];

            Submit(state, new PlayCardCommand(0, card.InstanceId, LaneIndex: 2)); // SlotIndex defaults -1

            var sublane = state.Lanes[2].SublaneOf(0);
            Assert.AreSame(card, sublane.Slots[0], "auto-place should use slot 0 in an empty sublane");
        }

        // ---------- PlayCard: rejections (state must not change) ----------

        [Test]
        public void PlayCard_RejectedWhenNotYourSlot()
        {
            var state = NewGame();
            var p1 = state.Players[1];
            var card = p1.cardsInHand[0];

            var events = Submit(state, new PlayCardCommand(1, card.InstanceId, LaneIndex: 0));

            AssertRejected(events);
            Assert.AreEqual(4, p1.cardsInHand.Count, "hand untouched");
            Assert.AreEqual(1, p1.CurrentEnergy, "energy untouched");
        }

        [Test]
        public void PlayCard_RejectedWhenCardNotInHand()
        {
            var state = NewGame();
            var events = Submit(state, new PlayCardCommand(0, CardInstanceId: 99999, LaneIndex: 0));
            AssertRejected(events);
        }

        [Test]
        public void PlayCard_RejectedWhenTooExpensive()
        {
            var state = NewGame();
            var p0 = state.Players[0];
            var expensive = p0.cardsInHand.FirstOrDefault(c => c.CurrentCost > p0.CurrentEnergy);
            if (expensive == null) Assert.Inconclusive("seed dealt only 1-cost cards; change seed");

            var events = Submit(state, new PlayCardCommand(0, expensive.InstanceId, LaneIndex: 0));

            AssertRejected(events);
            Assert.AreEqual(1, p0.CurrentEnergy, "energy untouched after rejection");
            Assert.AreEqual(5, p0.cardsInHand.Count, "hand untouched after rejection (4 dealt + turn draw)");
        }

        [Test]
        public void PlayCard_RejectedOnInvalidLane()
        {
            var state = NewGame();
            var card = state.Players[0].cardsInHand[0];

            AssertRejected(Submit(state, new PlayCardCommand(0, card.InstanceId, LaneIndex: -1)));
            AssertRejected(Submit(state, new PlayCardCommand(0, card.InstanceId, LaneIndex: state.Lanes.Length)));
        }

        [Test]
        public void PlayCard_RejectedWhenSublaneFull()
        {
            var state = NewGame();
            var p0 = state.Players[0];
            p0.CurrentEnergy = 99;

            var sublane = state.Lanes[0].SublaneOf(0);
            // fill every slot directly (test setup, not via commands)
            for (int i = 0; i < sublane.Slots.Length; i++)
                sublane.Slots[i] = new CardInstance { InstanceId = 9000 + i, OwnerId = 0 };

            var card = p0.cardsInHand[0];
            var events = Submit(state, new PlayCardCommand(0, card.InstanceId, LaneIndex: 0));

            AssertRejected(events);
            Assert.IsTrue(p0.cardsInHand.Contains(card), "card stays in hand");
        }

        [Test]
        public void PlayCard_RejectedWhenSpecificSlotOccupied_NoFallback()
        {
            var state = NewGame();
            var p0 = state.Players[0];
            p0.CurrentEnergy = 99;

            var sublane = state.Lanes[0].SublaneOf(0);
            sublane.Slots[0] = new CardInstance { InstanceId = 9000, OwnerId = 0 };

            var card = p0.cardsInHand[0];
            var events = Submit(state, new PlayCardCommand(0, card.InstanceId, LaneIndex: 0, SlotIndex: 0));

            AssertRejected(events);
            Assert.IsNull(sublane.Slots[1], "must NOT silently fall back to another slot");
        }

        // ---------- EndPhase ----------

        [Test]
        public void EndPhase_RejectedForInactivePlayer()
        {
            var state = NewGame();
            var events = Submit(state, new EndPhaseCommand(1)); // it's P0's slot

            AssertRejected(events);
            Assert.AreEqual(0, state.SlotIndex, "slot pointer untouched");
        }

        [Test]
        public void EndPhase_MidRotation_AdvancesToNextActionSlot()
        {
            var state = NewGame();
            var events = Submit(state, new EndPhaseCommand(0)); // slot 0 -> slot 1

            AssertNoRejections(events);
            Assert.AreEqual(1, state.SlotIndex);
            Assert.AreEqual(SlotType.Action, state.CurrentSlotType);
            Assert.AreEqual(1, state.ActivePlayerId);
        }

        [Test]
        public void EndPhase_EachTurnStartDrawsOneForTheActivePlayer()
        {
            var state = NewGame();
            int p1Before = state.Players[1].cardsInHand.Count;

            var events = Submit(state, new EndPhaseCommand(0)); // -> slot 1, P1's turn begins

            Assert.AreEqual(1, events.OfType<CardDrawnEvent>().Count(), "exactly one draw per turn start");
            Assert.AreEqual(p1Before + 1, state.Players[1].cardsInHand.Count, "the new active player drew it");
        }

        [Test]
        public void EndPhase_LastActionSlot_RunsCombatBlockAndSettles()
        {
            var state = NewGame();
            // walk to the last action slot before the first combat: 0(P0) 1(P1) 2(P1) 3(P0)
            Submit(state, new EndPhaseCommand(0));
            Submit(state, new EndPhaseCommand(1));
            Submit(state, new EndPhaseCommand(1));

            int p0Hand = state.Players[0].cardsInHand.Count;
            int p1Hand = state.Players[1].cardsInHand.Count;

            var events = Submit(state, new EndPhaseCommand(0)); // triggers Combat slot

            AssertNoRejections(events);

            // energy block: cap grew 1 -> 2 and refilled. Draws are per-turn, not
            // per-block: only P1 draws here, because settling past combat starts
            // P1's turn (slot 5). P0's hand must not change.
            foreach (var p in state.Players)
            {
                Assert.AreEqual(2, p.EnergyPerTurn, $"P{p.Id} cap grew");
                Assert.AreEqual(2, p.CurrentEnergy, $"P{p.Id} refilled");
            }
            Assert.AreEqual(p0Hand, state.Players[0].cardsInHand.Count, "P0 does not draw at the block");
            Assert.AreEqual(p1Hand + 1, state.Players[1].cardsInHand.Count, "P1 drew for their new turn");

            // settled past combat onto the next action slot (slot 5, P1 first this half)
            Assert.AreEqual(SlotType.Action, state.CurrentSlotType);
            Assert.AreEqual(1, state.ActivePlayerId, "second half of rotation starts with P1");
            Assert.AreEqual(5, state.SlotIndex);
            Assert.IsFalse(state.IsGameOver, "empty-board combat must not end the game");
        }

        [Test]
        public void EndPhase_FullRotationWalk_MatchesDesignedOrder()
        {
            var state = NewGame();

            // expected ActivePlayerId after each successive EndPhase from an empty board.
            // rotation: P0 P1 P1 P0 | Combat | P1 P0 P0 P1 | Combat Event | wrap -> P0
            int[] expectedActive = { 1, 1, 0, /*combat*/ 1, 0, 0, 1, /*combat+event+wrap*/ 0 };

            foreach (int expected in expectedActive)
            {
                var events = Submit(state, new EndPhaseCommand(state.ActivePlayerId));
                AssertNoRejections(events);
                Assert.AreEqual(SlotType.Action, state.CurrentSlotType, "must always settle on an Action slot");
                Assert.AreEqual(expected, state.ActivePlayerId);
            }

            Assert.AreEqual(1, state.RotationIndex, "one full rotation completed");
            Assert.AreEqual(0, state.SlotIndex, "wrapped back to slot 0");

            // two combats happened -> two energy blocks: cap 1 -> 3
            foreach (var p in state.Players)
                Assert.AreEqual(3, p.EnergyPerTurn, $"P{p.Id} cap after two combats");
        }

        // ---------- Guards ----------

        [Test]
        public void Commands_RejectedAfterGameOver()
        {
            var state = NewGame();
            state.Players[1].Health = 0;

            AssertRejected(Submit(state, new EndPhaseCommand(0)));
            var card = state.Players[0].cardsInHand[0];
            AssertRejected(Submit(state, new PlayCardCommand(0, card.InstanceId, LaneIndex: 0)));
        }

        [Test]
        public void DrawCard_EmptyDeckIsSafeNoOp()
        {
            var state = NewGame();
            state.Players[0].Deck.Clear();
            state.Players[1].Deck.Clear();

            // walk through turns and a combat: every turn draw finds an empty deck
            Submit(state, new EndPhaseCommand(0));
            Submit(state, new EndPhaseCommand(1));
            Submit(state, new EndPhaseCommand(1));
            var events = Submit(state, new EndPhaseCommand(0));

            AssertNoRejections(events);
            Assert.AreEqual(0, events.OfType<CardDrawnEvent>().Count(), "no draws from empty decks");
            Assert.AreEqual(5, state.Players[0].cardsInHand.Count, "hand unchanged (4 dealt + first turn draw)");
        }
    }
}