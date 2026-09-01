using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using Game.Cards;
using Game.Core.Commands;
using Game.Core.Events;
using Game.Core.Lanes;
using Game.Core.Server;
using Game.Core.State;
using UnityEngine;

namespace Game.Core.Tests
{
    /// <summary>
    /// Lane effects ("locations"): they belong to the battlefield, so every one
    /// of them hits BOTH players' guys, and none of them is placed by a card.
    /// </summary>
    public class LaneEffectTests
    {
        [TearDown]
        public void ResetCatalog() => CardCatalogRuntime.Configure(null);

        // ---------- helpers ----------

        /// A catalog of plain 2/2 guys with no abilities, so a test measures the
        /// lane's effect and nothing else.
        private static void ConfigureVanillaGuys(int count = 12)
        {
            var defs = new List<CardDefinition>();
            for (int i = 0; i < count; i++)
            {
                var def = ScriptableObject.CreateInstance<GuyCardDefinition>();
                def.CardId = $"lane_guy_{i}";
                def.EnergyCost = 0;
                def.BaseAttack = 2;
                def.BaseHealth = 2;
                defs.Add(def);
            }
            CardCatalogRuntime.Configure(defs);
        }

        /// <summary>
        /// A started match with every lane wiped back to plain, then exactly one
        /// lane given the type under test. StartGame deals lane types at random,
        /// so a test that didn't clear them would be testing the seed.
        /// </summary>
        private static GameState NewGameWithLane(LaneDefinition def, int laneIndex = 0, int seed = 42)
        {
            var state = new GameState(seed);
            CommandResolver.Resolve(state, new StartGameCommand(0));

            foreach (var lane in state.Lanes) lane.LaneTypeId = null;
            if (def != null) state.Lanes[laneIndex].LaneTypeId = def.Id;

            return state;
        }

        private static List<GameEvent> Submit(GameState state, Command cmd)
            => CommandResolver.Resolve(state, cmd);

        /// Puts a guy straight into a lane slot, bypassing commands/energy.
        private static CardInstance Deploy(GameState state, int playerId, int laneIndex, int slot,
                                           int attack = 2, int health = 2)
        {
            var card = new CardInstance
            {
                InstanceId = state.NextCardInstanceId++,
                DefinitionId = "lane_guy_0",
                OwnerId = playerId,
                CurrentAttack = attack,
                CurrentHealth = health,
                MaxHealth = health,
            };
            state.Lanes[laneIndex].SublaneOf(playerId).Place(card, slot);
            return card;
        }

        private static CardInstance PlayableGuy(GameState state, Player player)
        {
            var card = player.cardsInHand.FirstOrDefault(c => c.CurrentCost <= player.CurrentEnergy);
            Assert.IsNotNull(card, "expected an affordable card in hand");
            return card;
        }

        // ---------- stat modifier lanes ----------

        [Test]
        public void WitheringLane_DebuffsGuyPlayedThere()
        {
            ConfigureVanillaGuys();
            var state = NewGameWithLane(LaneCatalog.Withering);
            var p0 = state.Players[0];
            var card = PlayableGuy(state, p0);

            Submit(state, new PlayCardCommand(0, card.InstanceId, LaneIndex: 0));

            Assert.AreEqual(1, card.CurrentAttack, "2/2 entering a -1/-1 lane");
            Assert.AreEqual(1, card.CurrentHealth);
            Assert.AreEqual(1, card.MaxHealth, "MaxHealth drops too, so the debuff can't be healed off");
        }

        [Test]
        public void BulwarkLane_BuffsHealthOnlyOfGuyPlayedThere()
        {
            ConfigureVanillaGuys();
            var state = NewGameWithLane(LaneCatalog.Bulwark);
            var p0 = state.Players[0];
            var card = PlayableGuy(state, p0);

            Submit(state, new PlayCardCommand(0, card.InstanceId, LaneIndex: 0));

            Assert.AreEqual(2, card.CurrentAttack, "+0 attack: unchanged");
            Assert.AreEqual(4, card.CurrentHealth, "+2 health");
            Assert.AreEqual(4, card.MaxHealth);
        }

        [Test]
        public void StatLane_LeavesGuysInOtherLanesAlone()
        {
            ConfigureVanillaGuys();
            var state = NewGameWithLane(LaneCatalog.Withering, laneIndex: 0);
            var p0 = state.Players[0];
            var card = PlayableGuy(state, p0);

            Submit(state, new PlayCardCommand(0, card.InstanceId, LaneIndex: 3));

            Assert.AreEqual(2, card.CurrentAttack, "lane 3 is plain");
            Assert.AreEqual(2, card.CurrentHealth);
        }

        [Test]
        public void StatLane_AppliesToGuysAlreadyStandingThere_WhenLaneGainsItsEffect()
        {
            // The event-card path: a lane gains an effect with guys already on it.
            ConfigureVanillaGuys();
            var state = NewGameWithLane(null);
            var mine = Deploy(state, playerId: 0, laneIndex: 1, slot: 0);
            var theirs = Deploy(state, playerId: 1, laneIndex: 1, slot: 0);

            var events = new List<GameEvent>();
            CommandResolver.ApplyLaneTypeTo(state.Lanes[1], LaneCatalog.Bulwark, events);

            Assert.AreEqual(4, mine.CurrentHealth, "affects the owner's guy");
            Assert.AreEqual(4, theirs.CurrentHealth, "and the opponent's — lanes are neutral ground");
        }

        [Test]
        public void WitheringLane_KillsAOneHealthGuyOnArrival_WithoutPayingKillGold()
        {
            ConfigureVanillaGuys();
            var state = NewGameWithLane(null);
            var frail = Deploy(state, playerId: 0, laneIndex: 1, slot: 0, attack: 1, health: 1);
            int opponentGoldBefore = state.Players[1].Gold;

            var events = new List<GameEvent>();
            CommandResolver.ApplyLaneTypeTo(state.Lanes[1], LaneCatalog.Withering, events);

            Assert.IsNull(state.Lanes[1].SublaneOf(0).Slots[0], "swept off the board, not left at 0 health");
            Assert.AreEqual(opponentGoldBefore, state.Players[1].Gold,
                "a lane killing a guy is not a combat kill — no bounty");
            Assert.IsTrue(events.OfType<CardDiedEvent>().Any(e => e.CardInstanceId == frail.InstanceId));
        }

        // ---------- draw-on-play lane ----------

        [Test]
        public void LibraryLane_DrawsACardWhenAGuyIsPlayedThere()
        {
            ConfigureVanillaGuys();
            var state = NewGameWithLane(LaneCatalog.Library);
            var p0 = state.Players[0];
            var card = PlayableGuy(state, p0);
            int handBefore = p0.cardsInHand.Count;
            int deckBefore = p0.Deck.Count;

            Submit(state, new PlayCardCommand(0, card.InstanceId, LaneIndex: 0));

            // -1 for the card played, +1 for the lane's draw.
            Assert.AreEqual(handBefore, p0.cardsInHand.Count);
            Assert.AreEqual(deckBefore - 1, p0.Deck.Count, "the lane drew one");
        }

        [Test]
        public void LibraryLane_DoesNotDrawForAGuyPlayedElsewhere()
        {
            ConfigureVanillaGuys();
            var state = NewGameWithLane(LaneCatalog.Library, laneIndex: 0);
            var p0 = state.Players[0];
            var card = PlayableGuy(state, p0);
            int deckBefore = p0.Deck.Count;

            Submit(state, new PlayCardCommand(0, card.InstanceId, LaneIndex: 2));

            Assert.AreEqual(deckBefore, p0.Deck.Count, "lane 2 is plain — no draw");
        }

        // ---------- energy-reset hazard lane ----------

        [Test]
        public void VolcanicLane_DamagesBothPlayersGuysAfterCombat()
        {
            ConfigureVanillaGuys();
            var state = NewGameWithLane(LaneCatalog.Volcanic, laneIndex: 1);

            // Beefy enough to survive both combat and the hazard, and placed in
            // the back slot so they never trade with each other.
            var mine = Deploy(state, playerId: 0, laneIndex: 1, slot: 1, attack: 0, health: 10);
            var theirs = Deploy(state, playerId: 1, laneIndex: 1, slot: 1, attack: 0, health: 10);

            // Walk to the first Combat slot: 0(P0) 1(P1) 2(P1) 3(P0) -> Combat.
            Submit(state, new EndPhaseCommand(0));
            Submit(state, new EndPhaseCommand(1));
            Submit(state, new EndPhaseCommand(1));
            Submit(state, new EndPhaseCommand(0));

            Assert.AreEqual(8, mine.CurrentHealth, "2 hazard damage after combat");
            Assert.AreEqual(8, theirs.CurrentHealth, "the hazard is neutral — it hits both sides");
        }

        [Test]
        public void VolcanicLane_LeavesOtherLanesUntouched()
        {
            ConfigureVanillaGuys();
            var state = NewGameWithLane(LaneCatalog.Volcanic, laneIndex: 1);
            var safe = Deploy(state, playerId: 0, laneIndex: 4, slot: 1, attack: 0, health: 10);

            Submit(state, new EndPhaseCommand(0));
            Submit(state, new EndPhaseCommand(1));
            Submit(state, new EndPhaseCommand(1));
            Submit(state, new EndPhaseCommand(0));

            Assert.AreEqual(10, safe.CurrentHealth, "lane 4 is plain");
        }

        // ---------- assignment ----------

        [Test]
        public void StartGame_DealsDistinctLaneTypes_AndSameSeedDealsTheSameBoard()
        {
            ConfigureVanillaGuys();

            var a = new GameState(7);
            CommandResolver.Resolve(a, new StartGameCommand(0));
            var b = new GameState(7);
            CommandResolver.Resolve(b, new StartGameCommand(0));

            var aTypes = a.Lanes.Select(l => l.LaneTypeId).ToList();
            CollectionAssert.AreEqual(aTypes, b.Lanes.Select(l => l.LaneTypeId).ToList(),
                "same seed must deal the same lanes");

            var assigned = aTypes.Where(id => !string.IsNullOrEmpty(id)).ToList();
            Assert.AreEqual(2, assigned.Count, "SpecialLanesAtStart lanes get an effect");
            CollectionAssert.AllItemsAreUnique(assigned, "lane types dealt at start are distinct");
            foreach (var id in assigned)
                Assert.IsNotNull(LaneCatalog.Get(id), $"'{id}' must be a real lane type");
        }
    }
}
