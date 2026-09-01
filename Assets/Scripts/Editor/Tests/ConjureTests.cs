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
    /// <summary>
    /// Conjuring: cards spawned into hand straight from the catalog. They are
    /// unrelated to the owner's deck in both directions — never drawn from it,
    /// never returned to it.
    /// </summary>
    public class ConjureTests
    {
        [TearDown]
        public void ResetCatalog() => CardCatalogRuntime.Configure(null);

        // ---------- builders ----------

        private static GuyCardDefinition Guy(string id, int cost = 2, Rarity rarity = Rarity.Common,
                                             Archetype archetype = Archetype.Colorless, params string[] tags)
        {
            var def = ScriptableObject.CreateInstance<GuyCardDefinition>();
            def.CardId = id;
            def.DisplayName = id;
            def.EnergyCost = cost;
            def.BaseAttack = 1;
            def.BaseHealth = 1;
            def.Rarity = rarity;
            def.Archetypes = new List<Archetype> { archetype };
            def.Tags = new List<string>(tags);
            return def;
        }

        private static SpellCardDefinition SpellDef(string id, int cost = 1, Rarity rarity = Rarity.Common)
        {
            var def = ScriptableObject.CreateInstance<SpellCardDefinition>();
            def.CardId = id;
            def.DisplayName = id;
            def.EnergyCost = cost;
            def.Rarity = rarity;
            def.Target = SpellTarget.None;
            return def;
        }

        private static List<GameEvent> Submit(GameState state, Command cmd)
            => CommandResolver.Resolve(state, cmd);

        /// <summary>A started match with the given catalog, parked on P0's main
        /// slot with the conjuring card in hand and energy to spare.</summary>
        private static GameState NewGameWith(List<CardDefinition> catalog, CardDefinition toPlay,
                                             out CardInstance inHand)
        {
            CardCatalogRuntime.Configure(catalog);
            var state = new GameState(42);
            CommandResolver.Resolve(state, new StartGameCommand(0));
            foreach (var lane in state.Lanes) lane.LaneTypeId = null;   // keep lane effects out of it

            var p0 = state.Players[0];
            p0.CurrentEnergy = 9;
            inHand = CardFactory.Create(state, toPlay, 0);
            p0.cardsInHand.Add(inHand);
            return state;
        }

        // ---------- the two new cards ----------

        [Test]
        public void Conjurer_SpawnsOneDiscountedGuyInHand()
        {
            var conjurer = Guy("conjurer", cost: 2, rarity: Rarity.Epic);
            conjurer.Conjure = new ConjureSpec { Count = 1, Kind = ConjureKind.Guy, CostReduction = 1 };

            var catalog = new List<CardDefinition> { conjurer, Guy("target_guy", cost: 4) };
            var state = NewGameWith(catalog, conjurer, out var card);
            var p0 = state.Players[0];
            int deckBefore = p0.Deck.Count;

            var events = Submit(state, new PlayCardCommand(0, card.InstanceId, LaneIndex: 0));

            var conjured = events.OfType<CardConjuredEvent>().ToList();
            Assert.AreEqual(1, conjured.Count, "one card conjured");

            // A Guy-filtered conjure can legitimately roll the Conjurer itself,
            // so assert the discount relationship rather than a fixed card.
            var rolled = catalog.Find(d => d.CardId == conjured[0].DefinitionId);
            Assert.IsNotNull(rolled, "conjured something from the catalog");
            Assert.IsFalse(rolled is SpellCardDefinition, "Kind=Guy must not roll a spell");
            Assert.AreEqual(rolled.EnergyCost - 1, conjured[0].Cost, "conjured at 1 less than printed");

            var spawned = p0.cardsInHand.Find(c => c.InstanceId == conjured[0].CardInstanceId);
            Assert.IsNotNull(spawned, "it's in hand");
            Assert.AreEqual(rolled.EnergyCost - 1, spawned.CurrentCost);
            Assert.AreEqual(deckBefore, p0.Deck.Count, "conjuring never touches the deck");
        }

        [Test]
        public void ArcaneSurge_SpawnsThreeSpells()
        {
            var surge = SpellDef("surge", cost: 3);
            surge.Conjure = new ConjureSpec { Count = 3, Kind = ConjureKind.Spell };

            var catalog = new List<CardDefinition>
            {
                surge, SpellDef("bolt"), SpellDef("mend"), Guy("a_guy"),
            };
            var state = NewGameWith(catalog, surge, out var card);
            var p0 = state.Players[0];
            int deckBefore = p0.Deck.Count;

            // Spells cast on the spell turn: slot 0(P0 main) -> 1 -> 2 -> 3(P0 spell).
            Submit(state, new EndPhaseCommand(0));
            Submit(state, new EndPhaseCommand(1));
            Submit(state, new EndPhaseCommand(1));
            p0.CurrentEnergy = 9;

            var events = Submit(state, new PlayCardCommand(0, card.InstanceId, LaneIndex: -1));

            var conjured = events.OfType<CardConjuredEvent>().ToList();
            Assert.AreEqual(3, conjured.Count, "three cards conjured");
            foreach (var e in conjured)
                Assert.AreNotEqual("a_guy", e.DefinitionId, "Kind=Spell must never roll a guy");
            Assert.AreEqual(deckBefore, p0.Deck.Count, "conjuring never touches the deck");
        }

        // ---------- filters ----------

        [Test]
        public void Conjure_KindFilter_RestrictsToGuys()
        {
            var source = Guy("source");
            source.Conjure = new ConjureSpec { Count = 6, Kind = ConjureKind.Guy };

            var catalog = new List<CardDefinition> { source, Guy("g1"), Guy("g2"), SpellDef("s1") };
            var state = NewGameWith(catalog, source, out var card);

            var events = Submit(state, new PlayCardCommand(0, card.InstanceId, LaneIndex: 0));

            foreach (var e in events.OfType<CardConjuredEvent>())
                Assert.AreNotEqual("s1", e.DefinitionId, "a Guy-filtered conjure must not roll a spell");
        }

        [Test]
        public void Conjure_RarityFilter_AtLeastExcludesLowerRarities()
        {
            var source = Guy("source");
            source.Conjure = new ConjureSpec
            {
                Count = 8,
                RarityFilter = ConjureRarityFilter.AtLeast,
                Rarity = Rarity.Epic,
            };

            var catalog = new List<CardDefinition>
            {
                source,
                Guy("common_guy", rarity: Rarity.Common),
                Guy("rare_guy", rarity: Rarity.Rare),
                Guy("epic_guy", rarity: Rarity.Epic),
                Guy("legendary_guy", rarity: Rarity.Legendary),
            };
            var state = NewGameWith(catalog, source, out var card);

            var events = Submit(state, new PlayCardCommand(0, card.InstanceId, LaneIndex: 0));
            var ids = events.OfType<CardConjuredEvent>().Select(e => e.DefinitionId).ToList();

            CollectionAssert.IsNotEmpty(ids);
            foreach (var id in ids)
                Assert.IsTrue(id == "epic_guy" || id == "legendary_guy", $"'{id}' is below Epic");
        }

        [Test]
        public void Conjure_TagFilter_IsTheTribeFilter()
        {
            var source = Guy("source");
            source.Conjure = new ConjureSpec
            {
                Count = 6,
                RequiredTags = new List<string> { "dragon" },
            };

            var catalog = new List<CardDefinition>
            {
                source,
                Guy("dragon_a", tags: "dragon"),
                Guy("dragon_b", tags: "dragon"),
                Guy("goblin", tags: "goblin"),
            };
            var state = NewGameWith(catalog, source, out var card);

            var events = Submit(state, new PlayCardCommand(0, card.InstanceId, LaneIndex: 0));
            var ids = events.OfType<CardConjuredEvent>().Select(e => e.DefinitionId).ToList();

            CollectionAssert.IsNotEmpty(ids);
            foreach (var id in ids)
                Assert.IsTrue(id.StartsWith("dragon"), $"'{id}' isn't a dragon");
        }

        [Test]
        public void Conjure_ArchetypeFilter_MatchesAnyListedArchetype()
        {
            var source = Guy("source");
            source.Conjure = new ConjureSpec
            {
                Count = 6,
                Archetypes = new List<Archetype> { Archetype.Mage, Archetype.Healer },
            };

            var catalog = new List<CardDefinition>
            {
                source,
                Guy("mage_guy", archetype: Archetype.Mage),
                Guy("healer_guy", archetype: Archetype.Healer),
                Guy("tank_guy", archetype: Archetype.Tank),
            };
            var state = NewGameWith(catalog, source, out var card);

            var events = Submit(state, new PlayCardCommand(0, card.InstanceId, LaneIndex: 0));
            var ids = events.OfType<CardConjuredEvent>().Select(e => e.DefinitionId).ToList();

            CollectionAssert.IsNotEmpty(ids);
            foreach (var id in ids)
                Assert.AreNotEqual("tank_guy", id, "Tank is not in the archetype filter");
        }

        [Test]
        public void Conjure_EnergyCostFilter_BoundsThePrintedCost()
        {
            // Cost 7 so the source itself falls outside its own filter.
            var source = Guy("source", cost: 7);
            source.Conjure = new ConjureSpec
            {
                Count = 6,
                FilterByEnergyCost = true,
                MinEnergyCost = 2,
                MaxEnergyCost = 3,
            };

            var catalog = new List<CardDefinition>
            {
                source, Guy("cheap", cost: 1), Guy("mid", cost: 3), Guy("pricey", cost: 5),
            };
            var state = NewGameWith(catalog, source, out var card);

            var events = Submit(state, new PlayCardCommand(0, card.InstanceId, LaneIndex: 0));
            var ids = events.OfType<CardConjuredEvent>().Select(e => e.DefinitionId).ToList();

            CollectionAssert.IsNotEmpty(ids);
            foreach (var id in ids)
                Assert.AreEqual("mid", id, "only the 3-cost guy is inside [2,3]");
        }

        // ---------- edges ----------

        [Test]
        public void Conjure_WithNoMatchingCards_ConjuresNothing()
        {
            var source = Guy("source");
            source.Conjure = new ConjureSpec
            {
                Count = 3,
                RequiredTags = new List<string> { "nothing_has_this_tag" },
            };

            var catalog = new List<CardDefinition> { source, Guy("g1"), Guy("g2") };
            var state = NewGameWith(catalog, source, out var card);
            int handBefore = state.Players[0].cardsInHand.Count;

            var events = Submit(state, new PlayCardCommand(0, card.InstanceId, LaneIndex: 0));

            Assert.IsEmpty(events.OfType<CardConjuredEvent>(),
                "an unsatisfiable filter conjures nothing rather than falling back to a random card");
            Assert.AreEqual(handBefore - 1, state.Players[0].cardsInHand.Count, "only the played card left hand");
        }

        [Test]
        public void Conjure_StopsAtTheHandSizeCap()
        {
            var source = Guy("source");
            source.Conjure = new ConjureSpec { Count = 20, Kind = ConjureKind.Guy };

            var catalog = new List<CardDefinition> { source, Guy("filler") };
            var state = NewGameWith(catalog, source, out var card);
            var p0 = state.Players[0];

            Submit(state, new PlayCardCommand(0, card.InstanceId, LaneIndex: 0));

            Assert.LessOrEqual(p0.cardsInHand.Count, 7, "conjuring can't overflow the hand cap");
        }

        [Test]
        public void Conjure_CostReductionFloorsAtZero()
        {
            var source = Guy("source");
            source.Conjure = new ConjureSpec { Count = 1, Kind = ConjureKind.Guy, CostReduction = 5 };

            var catalog = new List<CardDefinition> { source, Guy("cheap", cost: 1) };
            var state = NewGameWith(catalog, source, out var card);

            var events = Submit(state, new PlayCardCommand(0, card.InstanceId, LaneIndex: 0));
            var conjured = events.OfType<CardConjuredEvent>().Single();

            Assert.AreEqual(0, conjured.Cost, "a big discount can't make a card cost negative energy");
        }

        [Test]
        public void ACardThatDoesNotConjure_ConjuresNothing()
        {
            var plain = Guy("plain");   // default ConjureSpec, Count 0
            var catalog = new List<CardDefinition> { plain, Guy("other") };
            var state = NewGameWith(catalog, plain, out var card);

            var events = Submit(state, new PlayCardCommand(0, card.InstanceId, LaneIndex: 0));

            Assert.IsEmpty(events.OfType<CardConjuredEvent>());
        }
    }
}
