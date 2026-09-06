using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using Game.Cards;
using Game.Core.Abilities;
using Game.Core.Augments;
using Game.Core.Commands;
using Game.Core.Events;
using Game.Core.Server;
using Game.Core.State;
using UnityEngine;

namespace Game.Core.Tests
{
    /// <summary>
    /// Augments: a permanent, once-per-rotation pick whose entire behaviour comes
    /// from the shared ability keywords, exactly like a card's.
    /// </summary>
    public class AugmentTests
    {
        [TearDown]
        public void Reset()
        {
            CardCatalogRuntime.Configure(null);
            AugmentCatalogRuntime.Configure(null);
            AbilityRuntime.Configure(null);
        }

        // ---------- setup ----------

        /// The four real augment keywords, registered the way abilities.json does.
        private static void ConfigureAbilities()
        {
            var db = new AbilityDatabase();
            db.Register(new AbilityDefinition {
                AbilityId = "turndraw", Trigger = AbilityTrigger.StartOfTurn,
                Effect = AbilityEffect.DrawCard, Target = AbilityTarget.Owner });
            db.Register(new AbilityDefinition {
                AbilityId = "goldgen", Trigger = AbilityTrigger.StartOfTurn,
                Effect = AbilityEffect.GainGold, Target = AbilityTarget.Owner });
            db.Register(new AbilityDefinition {
                AbilityId = "energyboost", Trigger = AbilityTrigger.Passive,
                Effect = AbilityEffect.GainEnergy, Target = AbilityTarget.Owner });
            db.Register(new AbilityDefinition {
                AbilityId = "armyboost", Trigger = AbilityTrigger.Passive,
                Effect = AbilityEffect.BuffStats, Target = AbilityTarget.OwnedGuys });
            AbilityRuntime.Configure(db);
        }

        private static AugmentDefinition Augment(string id, string abilityId, int x)
        {
            return new AugmentDefinition {
                AugmentId = id,
                DisplayName = id,
                Description = id,
                Abilities = new List<AbilityRef> { new AbilityRef { Id = abilityId, X = x } },
            };
        }

        /// <summary>The four shipped augments, plus a plain guy catalog so decks deal.</summary>
        private static void ConfigureAll(params AugmentDefinition[] augments)
        {
            ConfigureAbilities();

            var db = new AugmentDatabase();
            foreach (var augment in augments) db.Register(augment);
            AugmentCatalogRuntime.Configure(db);

            var cards = new List<CardDefinition>();
            for (int i = 0; i < 10; i++)
            {
                var guy = ScriptableObject.CreateInstance<GuyCardDefinition>();
                guy.CardId = $"guy_{i}";
                guy.DisplayName = $"guy_{i}";
                guy.EnergyCost = 0;
                guy.BaseAttack = 2;
                guy.BaseHealth = 2;
                cards.Add(guy);
            }
            CardCatalogRuntime.Configure(cards);
        }

        private static AugmentDefinition[] FourAugments() => new[]
        {
            Augment("study_habit", "turndraw", 1),
            Augment("dividends", "goldgen", 30),
            Augment("overcharge", "energyboost", 1),
            Augment("drill_sergeant", "armyboost", 1),
        };

        private static List<GameEvent> Submit(GameState state, Command cmd)
            => CommandResolver.Resolve(state, cmd);

        /// <summary>A started match walked to the Augment slot (rotation index 10).</summary>
        private static GameState NewGameAtAugment(int seed = 42)
        {
            var state = new GameState(seed);
            CommandResolver.Resolve(state, new StartGameCommand(0));
            foreach (var lane in state.Lanes) lane.LaneTypeId = null;

            foreach (int p in new[] { 0, 1, 1, 0, 1, 0, 0, 1 })
                CommandResolver.Resolve(state, new EndPhaseCommand(p));

            return state;
        }

        private static CardInstance Deploy(GameState state, int playerId, int lane, int slot)
        {
            var card = new CardInstance
            {
                InstanceId = state.NextCardInstanceId++,
                DefinitionId = "guy_0",
                OwnerId = playerId,
                CurrentAttack = 2,
                CurrentHealth = 2,
                MaxHealth = 2,
            };
            state.Lanes[lane].SublaneOf(playerId).Place(card, slot);
            return card;
        }

        private static void AssertRejected(IEnumerable<GameEvent> events) =>
            Assert.IsTrue(events.OfType<CommandRejectedEvent>().Any(), "expected a rejection");

        private static void AssertNoRejections(IEnumerable<GameEvent> events) =>
            Assert.IsFalse(events.OfType<CommandRejectedEvent>().Any(), "expected no rejection");

        // ---------- the phase ----------

        [Test]
        public void FirstRotation_EndsInAnAugmentAndSkipsTheShop()
        {
            ConfigureAll(FourAugments());
            var state = NewGameAtAugment();

            Assert.AreEqual(SlotType.Augment, state.CurrentSlotType, "rotation 0 is an augment rotation");
            Assert.AreEqual(0, state.RotationIndex);

            Submit(state, new SelectAugmentCommand(0, state.Players[0].AugmentOffers[0]));
            Submit(state, new SelectAugmentCommand(1, state.Players[1].AugmentOffers[0]));

            Assert.AreEqual(SlotType.Action, state.CurrentSlotType,
                "one interlude per rotation — the shop is skipped entirely");
            Assert.AreEqual(1, state.RotationIndex, "straight into the next rotation");
        }

        [Test]
        public void SecondRotation_EndsInAShopAndSkipsTheAugment()
        {
            ConfigureAll(FourAugments());
            var state = NewGameAtAugment();

            // Clear rotation 0's augment, then walk rotation 1 to its interlude.
            Submit(state, new SelectAugmentCommand(0, state.Players[0].AugmentOffers[0]));
            Submit(state, new SelectAugmentCommand(1, state.Players[1].AugmentOffers[0]));
            foreach (int p in new[] { 0, 1, 1, 0, 1, 0, 0, 1 })
                Submit(state, new EndPhaseCommand(p));

            Assert.AreEqual(1, state.RotationIndex);
            Assert.AreEqual(SlotType.Shop, state.CurrentSlotType,
                "rotation 1 is a shop rotation — no augment offered");
            foreach (var player in state.Players)
                CollectionAssert.IsEmpty(player.AugmentOffers, "and nothing was put on offer");
        }

        [Test]
        public void EnterAugmentPhase_OffersThreeToEachPlayer()
        {
            ConfigureAll(FourAugments());
            var state = NewGameAtAugment();

            foreach (var player in state.Players)
            {
                Assert.AreEqual(3, player.AugmentOffers.Count, $"P{player.Id} options");
                CollectionAssert.AllItemsAreUnique(player.AugmentOffers, "options are distinct");
                Assert.IsFalse(player.AugmentPicked);
            }
        }

        [Test]
        public void SelectAugment_WaitsForBothPlayers()
        {
            ConfigureAll(FourAugments());
            var state = NewGameAtAugment();

            var events = Submit(state, new SelectAugmentCommand(0, state.Players[0].AugmentOffers[0]));

            AssertNoRejections(events);
            Assert.AreEqual(SlotType.Augment, state.CurrentSlotType, "still waiting on P1");
            Assert.IsTrue(state.Players[0].AugmentPicked);

            Submit(state, new SelectAugmentCommand(1, state.Players[1].AugmentOffers[0]));
            Assert.AreEqual(SlotType.Action, state.CurrentSlotType, "both picked -> advance");
        }

        [Test]
        public void SelectAugment_RejectsAnOptionYouWereNotOffered()
        {
            ConfigureAll(FourAugments());
            var state = NewGameAtAugment();
            var p0 = state.Players[0];

            string notOffered = new[] { "study_habit", "dividends", "overcharge", "drill_sergeant" }
                .First(id => !p0.AugmentOffers.Contains(id));

            var events = Submit(state, new SelectAugmentCommand(0, notOffered));

            AssertRejected(events);
            CollectionAssert.IsEmpty(p0.Augments);
        }

        [Test]
        public void SelectAugment_RejectsASecondPickInTheSamePhase()
        {
            ConfigureAll(FourAugments());
            var state = NewGameAtAugment();
            var p0 = state.Players[0];
            var offers = new List<string>(p0.AugmentOffers);

            Submit(state, new SelectAugmentCommand(0, offers[0]));
            var events = Submit(state, new SelectAugmentCommand(0, offers[1]));

            AssertRejected(events);
            Assert.AreEqual(1, p0.Augments.Count, "one augment per phase");
        }

        [Test]
        public void SelectAugment_RejectedOutsideTheAugmentPhase()
        {
            ConfigureAll(FourAugments());
            var state = new GameState(42);
            CommandResolver.Resolve(state, new StartGameCommand(0));

            AssertRejected(Submit(state, new SelectAugmentCommand(0, "study_habit")));
        }

        [Test]
        public void AugmentPhase_SkipsItselfWhenThereIsNothingLeftToOffer()
        {
            // Only one augment exists and both players already hold it.
            ConfigureAll(Augment("study_habit", "turndraw", 1));
            var state = new GameState(42);
            CommandResolver.Resolve(state, new StartGameCommand(0));
            foreach (var player in state.Players) player.Augments.Add("study_habit");

            foreach (int p in new[] { 0, 1, 1, 0, 1, 0, 0, 1 })
                CommandResolver.Resolve(state, new EndPhaseCommand(p));

            Assert.AreEqual(SlotType.Action, state.CurrentSlotType,
                "an augment phase with no options must not strand the rotation");
            Assert.AreEqual(1, state.RotationIndex);
        }

        [Test]
        public void Offers_ExcludeAugmentsAlreadyTaken()
        {
            ConfigureAll(FourAugments());
            var state = NewGameAtAugment();
            var p0 = state.Players[0];

            string taken = p0.AugmentOffers[0];
            Submit(state, new SelectAugmentCommand(0, taken));
            Submit(state, new SelectAugmentCommand(1, state.Players[1].AugmentOffers[0]));

            // Walk a whole rotation back around to the next augment phase.
            while (state.CurrentSlotType != SlotType.Augment)
            {
                if (state.CurrentSlotType == SlotType.Shop)
                {
                    Submit(state, new EndShopCommand(0));
                    Submit(state, new EndShopCommand(1));
                }
                else
                {
                    Submit(state, new EndPhaseCommand(state.ActivePlayerId));
                }
            }

            CollectionAssert.DoesNotContain(p0.AugmentOffers, taken,
                "an augment you already own is never offered again");
        }

        // ---------- the four effects ----------

        [Test]
        public void Overcharge_RaisesTheEnergyCapPermanently()
        {
            ConfigureAll(Augment("overcharge", "energyboost", 1));
            var state = NewGameAtAugment();
            var p0 = state.Players[0];
            int capBefore = p0.EnergyPerTurn;

            Submit(state, new SelectAugmentCommand(0, "overcharge"));

            Assert.AreEqual(capBefore + 1, p0.EnergyPerTurn,
                "the player simply has a bigger battery — 4/4 where they had 3/3");
        }

        [Test]
        public void DrillSergeant_BuffsGuysAlreadyDeployed()
        {
            ConfigureAll(Augment("drill_sergeant", "armyboost", 1));
            var state = NewGameAtAugment();
            var mine = Deploy(state, playerId: 0, lane: 0, slot: 0);
            var theirs = Deploy(state, playerId: 1, lane: 0, slot: 0);

            Submit(state, new SelectAugmentCommand(0, "drill_sergeant"));

            Assert.AreEqual(3, mine.CurrentAttack, "+1/+1 to the taker's guy");
            Assert.AreEqual(3, mine.CurrentHealth);
            Assert.AreEqual(2, theirs.CurrentAttack, "the opponent's guys are untouched");
        }

        [Test]
        public void DrillSergeant_AlsoBuffsGuysPlayedAfterwards()
        {
            ConfigureAll(Augment("drill_sergeant", "armyboost", 1));
            var state = NewGameAtAugment();

            // Both picking ends the rotation, landing straight on P0's next
            // main action slot.
            Submit(state, new SelectAugmentCommand(0, "drill_sergeant"));
            Submit(state, new SelectAugmentCommand(1, "drill_sergeant"));
            Assert.AreEqual(SlotType.Action, state.CurrentSlotType);

            var p0 = state.Players[0];
            p0.CurrentEnergy = 9;
            var guy = p0.cardsInHand.First();
            Submit(state, new PlayCardCommand(0, guy.InstanceId, LaneIndex: 0));

            Assert.AreEqual(3, guy.CurrentAttack, "a guy played later still gets the augment");
            Assert.AreEqual(3, guy.CurrentHealth);
        }

        [Test]
        public void StudyHabit_DrawsAnExtraCardEachTurn()
        {
            ConfigureAll(Augment("study_habit", "turndraw", 1));
            var state = NewGameAtAugment();

            Submit(state, new SelectAugmentCommand(0, "study_habit"));
            // The second pick ends the rotation and starts P0's turn in the same
            // batch, so the augment's draw lands here.
            var events = Submit(state, new SelectAugmentCommand(1, "study_habit"));

            Assert.AreEqual(0, state.ActivePlayerId);
            Assert.IsTrue(events.OfType<CardDrawnEvent>().Any(e => e.PlayerId == 0),
                "the augment draws on the owner's turn");
        }

        [Test]
        public void Dividends_PaysGoldEachTurn()
        {
            ConfigureAll(Augment("dividends", "goldgen", 30));
            var state = NewGameAtAugment();
            var p0 = state.Players[0];

            Submit(state, new SelectAugmentCommand(0, "dividends"));

            int before = p0.Gold;
            Submit(state, new SelectAugmentCommand(1, "dividends"));   // ends the rotation -> P0's turn

            Assert.AreEqual(0, state.ActivePlayerId);
            Assert.AreEqual(before + 30, p0.Gold, "30 gold at the start of the owner's turn");
        }

        [Test]
        public void Augments_OnlyAffectTheirOwner()
        {
            ConfigureAll(Augment("dividends", "goldgen", 30));
            var state = NewGameAtAugment();
            var p1 = state.Players[1];

            Submit(state, new SelectAugmentCommand(0, "dividends"));

            int p1Before = p1.Gold;
            Submit(state, new SelectAugmentCommand(1, "dividends"));   // P0's turn begins, not P1's

            Assert.AreEqual(0, state.ActivePlayerId);
            Assert.AreEqual(p1Before, p1.Gold, "P1's augment doesn't pay on P0's turn");
        }
    }
}
