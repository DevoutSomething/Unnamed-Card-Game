using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using Game.Cards;
using Game.Core.Commands;
using Game.Core.Events;
using Game.Core.Server;
using Game.Core.State;
using UnityEngine;

namespace Game.Core.Tests
{
    public class CommandResolverTests
    {
        [TearDown]
        public void ResetCatalog() => CardCatalogRuntime.Configure(null);

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

            // Card draw only happens at StartNewEnergyBlock (both players
            // together, when a Combat slot resolves) — the opening deal is the
            // only thing in either hand until the first combat.
            Assert.AreEqual(4, state.Players[0].cardsInHand.Count, "P0 hand: dealt only");
            Assert.AreEqual(6, state.Players[0].Deck.Count, "P0 deck: 10 - 4");
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
            Assert.AreEqual(8, events.OfType<CardDrawnEvent>().Count(), "4 per player, opening deal only");
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
            Assert.AreEqual(4, p0.cardsInHand.Count, "hand untouched after rejection (dealt only)");
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

        // ---------- PlayCard: guy/spell slot gating ----------

        [Test]
        public void PlayCard_GuyRejectedOnBonusSlot_SpellStillAllowed()
        {
            var guyDef = ScriptableObject.CreateInstance<GuyCardDefinition>();
            guyDef.CardId = "test_guy";
            guyDef.EnergyCost = 0;
            guyDef.BaseAttack = 1;
            guyDef.BaseHealth = 1;

            var spellDef = ScriptableObject.CreateInstance<SpellCardDefinition>();
            spellDef.CardId = "test_spell";
            spellDef.EnergyCost = 0;

            CardCatalogRuntime.Configure(new CardDefinition[] { guyDef, spellDef });

            var state = NewGame();
            var p1 = state.Players[1];
            var guy = new CardInstance { InstanceId = 9001, DefinitionId = "test_guy", OwnerId = 1 };
            var spell = new CardInstance { InstanceId = 9002, DefinitionId = "test_spell", OwnerId = 1 };
            p1.cardsInHand.Add(guy);
            p1.cardsInHand.Add(spell);

            Submit(state, new EndPhaseCommand(0));   // slot 0 (P0 main) -> slot 1 (P1 main)
            Submit(state, new EndPhaseCommand(1));   // slot 1 -> slot 2 (P1's bonus slot)
            Assert.AreEqual(1, state.ActivePlayerId);
            Assert.IsFalse(state.IsMainActionSlot, "slot 2 is P1's second slot this stretch");

            var guyEvents = Submit(state, new PlayCardCommand(1, guy.InstanceId, LaneIndex: 0));
            AssertRejected(guyEvents);
            Assert.IsTrue(p1.cardsInHand.Contains(guy), "guy stays in hand: bonus slots are spell-only");

            var spellEvents = Submit(state, new PlayCardCommand(1, spell.InstanceId, LaneIndex: 0));
            AssertNoRejections(spellEvents);
            Assert.IsFalse(p1.cardsInHand.Contains(spell), "spells are playable on a bonus slot");
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
        public void EndPhase_MidBlockAdvance_DrawsNothing()
        {
            var state = NewGame();
            int p1Before = state.Players[1].cardsInHand.Count;

            var events = Submit(state, new EndPhaseCommand(0)); // -> slot 1, P1's turn begins

            Assert.AreEqual(0, events.OfType<CardDrawnEvent>().Count(), "draw only happens at block boundaries, not per turn");
            Assert.AreEqual(p1Before, state.Players[1].cardsInHand.Count, "hand unchanged mid-block");
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

            // energy block: cap grew 1 -> 2 and refilled. Both players draw
            // together at the block boundary, regardless of whose turn is next.
            foreach (var p in state.Players)
            {
                Assert.AreEqual(2, p.EnergyPerTurn, $"P{p.Id} cap grew");
                Assert.AreEqual(2, p.CurrentEnergy, $"P{p.Id} refilled");
            }
            Assert.AreEqual(p0Hand + 1, state.Players[0].cardsInHand.Count, "P0 drew at the block");
            Assert.AreEqual(p1Hand + 1, state.Players[1].cardsInHand.Count, "P1 drew at the block");

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
            // rotation: P0 P1 P1 P0 | Combat | P1 P0 P0 P1 | Combat Shop
            int[] expectedActive = { 1, 1, 0, /*combat*/ 1, 0, 0, 1 };

            foreach (int expected in expectedActive)
            {
                var events = Submit(state, new EndPhaseCommand(state.ActivePlayerId));
                AssertNoRejections(events);
                Assert.AreEqual(SlotType.Action, state.CurrentSlotType, "must always settle on an Action slot");
                Assert.AreEqual(expected, state.ActivePlayerId);
            }

            // the last EndPhase resolves the second combat and settles on Shop —
            // it awaits both players' EndShopCommand rather than auto-wrapping.
            var shopEvents = Submit(state, new EndPhaseCommand(state.ActivePlayerId));
            AssertNoRejections(shopEvents);
            Assert.AreEqual(SlotType.Shop, state.CurrentSlotType, "settles on the shop slot after the second combat");
            Assert.AreEqual(0, state.RotationIndex, "still mid-rotation — the shop hasn't wrapped yet");

            Submit(state, new EndShopCommand(0));
            var wrapEvents = Submit(state, new EndShopCommand(1));
            AssertNoRejections(wrapEvents);

            Assert.AreEqual(1, state.RotationIndex, "one full rotation completed");
            Assert.AreEqual(0, state.SlotIndex, "wrapped back to slot 0");
            Assert.AreEqual(SlotType.Action, state.CurrentSlotType);
            Assert.AreEqual(0, state.ActivePlayerId);

            // two combats happened -> two energy blocks: cap 1 -> 3
            foreach (var p in state.Players)
                Assert.AreEqual(3, p.EnergyPerTurn, $"P{p.Id} cap after two combats");
        }

        // ---------- Shop ----------

        /// Walks EndPhase from a fresh NewGame() to the Shop slot (through both combats).
        private static void AdvanceToShop(GameState state)
        {
            while (state.CurrentSlotType != SlotType.Shop)
                Submit(state, new EndPhaseCommand(state.ActivePlayerId));
        }

        /// A minimal non-Common catalog so shop offer generation has something to pick from.
        private static void ConfigureShopCatalog(int count = 12)
        {
            var defs = new List<CardDefinition>();
            for (int i = 0; i < count; i++)
            {
                var def = ScriptableObject.CreateInstance<GuyCardDefinition>();
                def.CardId = $"shop_guy_{i}";
                def.EnergyCost = 1;
                def.GoldCost = 10;
                def.BaseAttack = 1;
                def.BaseHealth = 1;
                def.Rarity = Rarity.Rare;
                defs.Add(def);
            }
            CardCatalogRuntime.Configure(defs);
        }

        [Test]
        public void Shop_EnteringPhase_PopulatesTenOffersPerPlayer()
        {
            ConfigureShopCatalog();
            var state = NewGame();
            AdvanceToShop(state);

            foreach (var p in state.Players)
                Assert.AreEqual(10, p.ShopOffers.Count, $"P{p.Id} shop offers");
        }

        [Test]
        public void Shop_Offers_ExcludeCommonRarity()
        {
            var defs = new List<CardDefinition>();
            var common = ScriptableObject.CreateInstance<GuyCardDefinition>();
            common.CardId = "common_guy";
            common.Rarity = Rarity.Common;
            defs.Add(common);
            for (int i = 0; i < 10; i++)
            {
                var rare = ScriptableObject.CreateInstance<GuyCardDefinition>();
                rare.CardId = $"rare_guy_{i}";
                rare.Rarity = Rarity.Rare;
                defs.Add(rare);
            }
            CardCatalogRuntime.Configure(defs);

            var state = NewGame();
            AdvanceToShop(state);

            foreach (var p in state.Players)
                CollectionAssert.DoesNotContain(
                    p.ShopOffers.ConvertAll(c => c.DefinitionId), "common_guy",
                    $"P{p.Id} shop offers must exclude commons");
        }

        [Test]
        public void BuyCard_MovesOfferIntoDeckAndDeductsGold()
        {
            ConfigureShopCatalog();
            var state = NewGame();
            AdvanceToShop(state);
            var p0 = state.Players[0];
            p0.Gold = 999;
            var offer = p0.ShopOffers[0];
            int deckBefore = p0.Deck.Count;

            var events = Submit(state, new BuyCardCommand(0, offer.InstanceId));

            AssertNoRejections(events);
            Assert.IsTrue(p0.Deck.Contains(offer), "bought card lands in the deck");
            Assert.IsFalse(p0.ShopOffers.Contains(offer), "no longer offered");
            Assert.AreEqual(deckBefore + 1, p0.Deck.Count);
            Assert.AreEqual(999 - 10, p0.Gold, "GoldCost deducted");
        }

        [Test]
        public void BuyCard_RejectedWhenTooExpensive()
        {
            ConfigureShopCatalog();
            var state = NewGame();
            AdvanceToShop(state);
            var p0 = state.Players[0];
            p0.Gold = 0;
            var offer = p0.ShopOffers[0];

            var events = Submit(state, new BuyCardCommand(0, offer.InstanceId));

            AssertRejected(events);
            Assert.IsTrue(p0.ShopOffers.Contains(offer), "offer untouched after rejection");
        }

        [Test]
        public void RemoveCard_CostsScaleByFiveEachTimeThisVisit()
        {
            ConfigureShopCatalog();
            var state = NewGame();
            AdvanceToShop(state);
            var p0 = state.Players[0];
            p0.Gold = 999;
            Assert.GreaterOrEqual(p0.Deck.Count, 2, "need at least 2 deck cards for this test");

            var first = p0.Deck[0];
            Submit(state, new RemoveCardFromDeckCommand(0, first.InstanceId));
            Assert.AreEqual(999 - 5, p0.Gold, "first removal costs 5");

            var second = p0.Deck[0];
            Submit(state, new RemoveCardFromDeckCommand(0, second.InstanceId));
            Assert.AreEqual(999 - 5 - 10, p0.Gold, "second removal costs 10");
        }

        [Test]
        public void RemoveCard_RejectedWhenCardNotOwned()
        {
            ConfigureShopCatalog();
            var state = NewGame();
            AdvanceToShop(state);

            var events = Submit(state, new RemoveCardFromDeckCommand(0, DeckCardInstanceId: 99999));
            AssertRejected(events);
        }

        [Test]
        public void RemoveCard_RejectedForOpponentsCard_AndChargesNoGold()
        {
            ConfigureShopCatalog();
            var state = NewGame();
            AdvanceToShop(state);
            var p0 = state.Players[0];
            p0.Gold = 999;
            var opponentCard = state.Players[1].Deck[0];

            var events = Submit(state, new RemoveCardFromDeckCommand(0, opponentCard.InstanceId));

            AssertRejected(events);
            Assert.AreEqual(999, p0.Gold, "a bad id must never charge gold");
            Assert.IsTrue(state.Players[1].Deck.Contains(opponentCard), "opponent's deck untouched");
        }

        [Test]
        public void RemoveCard_CanRemoveFromHand()
        {
            ConfigureShopCatalog();
            var state = NewGame();
            AdvanceToShop(state);
            var p0 = state.Players[0];
            p0.Gold = 999;
            Assert.IsNotEmpty(p0.cardsInHand, "expected cards in hand at the shop");
            var inHand = p0.cardsInHand[0];

            var events = Submit(state, new RemoveCardFromDeckCommand(0, inHand.InstanceId));

            AssertNoRejections(events);
            Assert.IsFalse(p0.cardsInHand.Contains(inHand), "hand card removed");
            Assert.AreEqual(999 - 5, p0.Gold);
        }

        [Test]
        public void RemoveCard_CanRemoveDeployedGuyFromBoard()
        {
            ConfigureShopCatalog();
            var state = NewGame();
            AdvanceToShop(state);
            var p0 = state.Players[0];
            p0.Gold = 999;

            // Deploy a guy directly (test setup, not via commands) so there's
            // something on the board to delete.
            var sublane = state.Lanes[0].SublaneOf(0);
            var deployed = new CardInstance { InstanceId = 8100, DefinitionId = "shop_guy_0", OwnerId = 0 };
            sublane.Place(deployed, 0);

            var events = Submit(state, new RemoveCardFromDeckCommand(0, deployed.InstanceId));

            AssertNoRejections(events);
            Assert.IsNull(sublane.Slots[0], "lane slot freed by the removal");
            Assert.AreEqual(999 - 5, p0.Gold);
        }

        [Test]
        public void OwnedCards_SpansDeckHandAndBoard()
        {
            ConfigureShopCatalog();
            var state = NewGame();
            AdvanceToShop(state);
            var p0 = state.Players[0];

            var deployed = new CardInstance { InstanceId = 8200, DefinitionId = "shop_guy_0", OwnerId = 0 };
            state.Lanes[2].SublaneOf(0).Place(deployed, 0);

            var owned = CardZones.OwnedCards(state, p0);

            Assert.AreEqual(p0.Deck.Count + p0.cardsInHand.Count + 1, owned.Count,
                "collection = draw pile + hand + deployed guys");
            CollectionAssert.Contains(owned.ConvertAll(o => o.Card), deployed);
        }

        [Test]
        public void RerollDeck_IsFreeOnceThenRejected()
        {
            ConfigureShopCatalog();
            var state = NewGame();
            AdvanceToShop(state);
            var p0 = state.Players[0];
            int goldBefore = p0.Gold;

            var events = Submit(state, new RerollDeckCommand(0));
            AssertNoRejections(events);
            Assert.AreEqual(goldBefore, p0.Gold, "reroll is free");
            Assert.AreEqual(10, p0.Deck.Count, "fresh full deck");

            var again = Submit(state, new RerollDeckCommand(0));
            AssertRejected(again);
        }

        [Test]
        public void EndShop_RequiresBothPlayersReady_ThenAdvancesAndClearsOffers()
        {
            ConfigureShopCatalog();
            var state = NewGame();
            AdvanceToShop(state);

            var events0 = Submit(state, new EndShopCommand(0));
            AssertNoRejections(events0);
            Assert.AreEqual(SlotType.Shop, state.CurrentSlotType, "still waiting on P1");

            var events1 = Submit(state, new EndShopCommand(1));
            AssertNoRejections(events1);
            Assert.AreEqual(SlotType.Action, state.CurrentSlotType, "both ready -> rotation advances");
            foreach (var p in state.Players)
                Assert.AreEqual(0, p.ShopOffers.Count, "offers cleared on leaving the shop");
        }

        [Test]
        public void ShopCommands_RejectedOutsideShopPhase()
        {
            var state = NewGame(); // still in the opening Action slot

            AssertRejected(Submit(state, new BuyCardCommand(0, ShopCardInstanceId: 1)));
            AssertRejected(Submit(state, new RemoveCardFromDeckCommand(0, DeckCardInstanceId: 1)));
            AssertRejected(Submit(state, new RerollDeckCommand(0)));
            AssertRejected(Submit(state, new EndShopCommand(0)));
        }

        [Test]
        public void Shop_EnteringPhase_SetsA45SecondDeadline()
        {
            ConfigureShopCatalog();
            var state = NewGame();
            var before = System.DateTime.UtcNow;
            AdvanceToShop(state);

            Assert.IsTrue(state.ShopDeadlineUtc.HasValue);
            var remaining = state.ShopDeadlineUtc.Value - before;
            Assert.Greater(remaining.TotalSeconds, 44, "~45s window (allowing for test execution time)");
            Assert.LessOrEqual(remaining.TotalSeconds, 45.5);
        }

        [Test]
        public void ForceEndShop_BeforeDeadline_IsANoOp()
        {
            ConfigureShopCatalog();
            var state = NewGame();
            AdvanceToShop(state);

            var events = Submit(state, new ForceEndShopCommand());

            AssertNoRejections(events);
            Assert.AreEqual(SlotType.Shop, state.CurrentSlotType, "deadline hasn't passed yet");
            Assert.IsFalse(state.Players[0].ShopReady);
        }

        [Test]
        public void ForceEndShop_AfterDeadline_AdvancesEvenIfNeitherPlayerIsReady()
        {
            ConfigureShopCatalog();
            var state = NewGame();
            AdvanceToShop(state);
            state.ShopDeadlineUtc = System.DateTime.UtcNow.AddSeconds(-1); // simulate time running out

            var events = Submit(state, new ForceEndShopCommand());

            AssertNoRejections(events);
            Assert.AreEqual(SlotType.Action, state.CurrentSlotType, "timeout advances regardless of ShopReady");
            Assert.IsNull(state.ShopDeadlineUtc, "cleared on leaving the shop");
            foreach (var p in state.Players)
                Assert.AreEqual(0, p.ShopOffers.Count);
        }

        [Test]
        public void ShopCommands_RejectedOnceAlreadyReady()
        {
            ConfigureShopCatalog();
            var state = NewGame();
            AdvanceToShop(state);
            var p0 = state.Players[0];

            Submit(state, new EndShopCommand(0));
            var offer = p0.ShopOffers[0];
            var events = Submit(state, new BuyCardCommand(0, offer.InstanceId));

            AssertRejected(events);
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
            Assert.AreEqual(4, state.Players[0].cardsInHand.Count, "hand unchanged (dealt only)");
        }
    }
}