using System.Collections.Generic;
using NUnit.Framework;
using Game.Cards;
using Game.Core.Abilities;
using Game.Core.Events;
using Game.Core.Server;
using Game.Core.State;
using UnityEngine;

namespace Game.Core.Tests
{
    public class CardSystemTests
    {
        [SetUp]
        public void RegisterTestAbilities()
        {
            // Load the real vocabulary so tests exercise the shipped data too.
            AbilityRuntime.Configure(AbilityLoader.Parse(
                System.IO.File.ReadAllText("Assets/GameData/abilities.json")));
        }

        [TearDown]
        public void ResetAbilities() => AbilityRuntime.Configure(null);

        // ---------- helpers ----------

        private static GuyCardDefinition Guy(string id = "test_guy", int attack = 2, int health = 3,
                                             int cost = 1, int killGold = 5)
        {
            var def = ScriptableObject.CreateInstance<GuyCardDefinition>();
            def.CardId = id;
            def.DisplayName = "Test Guy";
            def.EnergyCost = cost;
            def.BaseAttack = attack;
            def.BaseHealth = health;
            def.KillRewardGold = killGold;
            return def;
        }

        private static SpellCardDefinition Spell(string id = "test_spell", int cost = 2)
        {
            var def = ScriptableObject.CreateInstance<SpellCardDefinition>();
            def.CardId = id;
            def.DisplayName = "Test Spell";
            def.EnergyCost = cost;
            return def;
        }

        // ---------- CardFactory ----------

        [Test]
        public void TryCreate_SeedsGuyStatsFromDefinition()
        {
            var state = new GameState(seed: 1);

            bool ok = CardFactory.TryCreate(state, Guy(), ownerId: 0, out var card, out string error);

            Assert.IsTrue(ok, error);
            Assert.AreEqual("test_guy", card.DefinitionId);
            Assert.AreEqual(0, card.OwnerId);
            Assert.AreEqual(2, card.CurrentAttack);
            Assert.AreEqual(3, card.CurrentHealth);
            Assert.AreEqual(1, card.CurrentCost);
            Assert.AreEqual(5, card.KillRewardGold);
            Assert.IsNull(card.ArtId, "no artId given -> default art");
        }

        [Test]
        public void TryCreate_AllocatesUniqueInstanceIds()
        {
            var state = new GameState(seed: 1);
            var def = Guy();

            CardFactory.TryCreate(state, def, 0, out var a, out _);
            CardFactory.TryCreate(state, def, 1, out var b, out _);

            Assert.AreNotEqual(a.InstanceId, b.InstanceId);
        }

        [Test]
        public void TryCreate_SpellHasNoCombatStats()
        {
            var state = new GameState(seed: 1);

            bool ok = CardFactory.TryCreate(state, Spell(), 0, out var card, out string error);

            Assert.IsTrue(ok, error);
            Assert.AreEqual(0, card.CurrentAttack);
            Assert.AreEqual(0, card.CurrentHealth);
            Assert.AreEqual(2, card.CurrentCost);
        }

        [Test]
        public void TryCreate_RejectsBadInput()
        {
            var state = new GameState(seed: 1);

            Assert.IsFalse(CardFactory.TryCreate(state, null, 0, out _, out string e1));
            StringAssert.Contains("null", e1);

            Assert.IsFalse(CardFactory.TryCreate(state, Guy(), ownerId: 7, out _, out string e2));
            StringAssert.Contains("ownerId", e2);

            var blankId = Guy(id: "");
            Assert.IsFalse(CardFactory.TryCreate(state, blankId, 0, out _, out string e3));
            StringAssert.Contains("CardId", e3);
        }

        [Test]
        public void TryCreate_StoresRequestedArtId()
        {
            var state = new GameState(seed: 1);

            CardFactory.TryCreate(state, Guy(), 0, out var card, out _, artId: "holo");

            Assert.AreEqual("holo", card.ArtId);
        }

        // ---------- Abilities ----------

        [Test]
        public void TryCreate_DeepCopiesAbilitiesFromDefinition()
        {
            var state = new GameState(seed: 1);
            var def = Guy();
            def.Abilities.Add(new AbilityRef { Id = "defend", X = 2 });

            var card = CardFactory.Create(state, def, 0);

            Assert.AreEqual(1, card.Abilities.Count);
            Assert.AreEqual("defend", card.Abilities[0].Id);
            Assert.AreEqual(2, card.Abilities[0].X);

            card.Abilities[0].X = 99; // instance upgrade must not touch the asset
            Assert.AreEqual(2, def.Abilities[0].X);
        }

        [Test]
        public void Defend_ReducesCombatDamage()
        {
            var state = new GameState(seed: 1);
            var attacker = CardFactory.Create(state, Guy(attack: 3), 0);
            var defender = CardFactory.Create(state, Guy(health: 5), 1);
            defender.Abilities.Add(new AbilityRef { Id = "defend", X = 2 });

            MutationHelper.DealCombatDamage(defender, attacker, new List<GameEvent>());

            Assert.AreEqual(4, defender.CurrentHealth, "3 damage - 2 defend = 1");
        }

        [Test]
        public void Thorns_ReflectsDamageWithoutAwardingKillGold()
        {
            var state = new GameState(seed: 1);
            var attacker = CardFactory.Create(state, Guy(attack: 1, health: 2, killGold: 9), 0);
            var hedgehog = CardFactory.Create(state, Guy(health: 5), 1);
            hedgehog.Abilities.Add(new AbilityRef { Id = "thorns", X = 2 });

            MutationHelper.DealCombatDamage(hedgehog, attacker, new List<GameEvent>());

            Assert.AreEqual(4, hedgehog.CurrentHealth, "took the hit");
            Assert.AreEqual(0, attacker.CurrentHealth, "thorns killed the attacker");
            Assert.IsFalse(attacker.LastDamageWasCombatDamage, "thorns is direct damage -> no kill gold");
        }

        [Test]
        public void Bounty_GrantsGoldToKillersOwnerOnCombatKill()
        {
            var state = new GameState(seed: 1);
            var events = new List<GameEvent>();
            var killer = CardFactory.Create(state, Guy(attack: 5), 0);
            killer.Abilities.Add(new AbilityRef { Id = "bounty", X = 2 });
            var victim = CardFactory.Create(state, Guy(health: 1, killGold: 3), 1);

            CardZones.TryPlaceInLane(state, killer, 0, -1, events, out _);
            CardZones.TryPlaceInLane(state, victim, 0, -1, events, out _);
            CombatResolver.Resolve(state, events);

            // killRewardGold (3) + bounty (2)
            Assert.AreEqual(5, state.Players[0].Gold);
        }

        // ---------- Card meta tags & skin validity ----------

        [Test]
        public void IsValidFor_EnforcesCardMetaTagsAndArtOwnership()
        {
            var card = Guy(id: "dragon_01");
            card.Tags.Add("dragon");

            var art = ScriptableObject.CreateInstance<CardArt>();
            art.ArtId = "base";
            art.CardId = "dragon_01";

            var border = ScriptableObject.CreateInstance<CardBorder>();
            border.BorderId = "dragon_frame";
            border.RequiredCardTags.Add("dragon");

            var skin = new CardSkin { Art = art, Border = border };
            Assert.IsTrue(skin.IsValidFor(card, out string error), error);

            card.Tags.Clear();
            Assert.IsFalse(skin.IsValidFor(card, out _), "border requires the 'dragon' meta tag");

            var foreignArt = ScriptableObject.CreateInstance<CardArt>();
            foreignArt.ArtId = "base";
            foreignArt.CardId = "someone_else";
            var stolen = new CardSkin { Art = foreignArt };
            Assert.IsFalse(stolen.IsValidFor(card, out _), "art belongs to a different card");
        }

        [Test]
        public void IsValidFor_PassesWithNoTagRequirements()
        {
            var card = Guy();
            var border = ScriptableObject.CreateInstance<CardBorder>();
            border.BorderId = "plain";

            var skin = new CardSkin { Border = border };
            Assert.IsTrue(skin.IsValidFor(card, out string error), error);
        }

        // ---------- CardZones ----------

        [Test]
        public void TryAdd_PutsCardInEachZoneAndEmitsEvent()
        {
            var state = new GameState(seed: 1);
            var events = new List<GameEvent>();
            var player = state.Players[0];

            var deckCard = CardFactory.Create(state, Guy(), 0);
            var handCard = CardFactory.Create(state, Guy(), 0);
            var shopCard = CardFactory.Create(state, Guy(), 0);

            Assert.IsTrue(CardZones.TryAdd(state, deckCard, CardZone.Deck, events, out _));
            Assert.IsTrue(CardZones.TryAdd(state, handCard, CardZone.Hand, events, out _));
            Assert.IsTrue(CardZones.TryAdd(state, shopCard, CardZone.Shop, events, out _));

            Assert.Contains(deckCard, player.Deck);
            Assert.Contains(handCard, player.cardsInHand);
            Assert.Contains(shopCard, player.ShopOffers);
            Assert.AreEqual(3, events.Count);
            Assert.IsInstanceOf<CardGrantedEvent>(events[0]);
        }

        [Test]
        public void TryPlaceInLane_UsesFirstEmptySlotAndRejectsWhenFull()
        {
            var state = new GameState(seed: 1); // 5 lanes, 2 slots per side
            var events = new List<GameEvent>();
            var def = Guy();

            var first = CardFactory.Create(state, def, 0);
            var second = CardFactory.Create(state, def, 0);
            var third = CardFactory.Create(state, def, 0);

            Assert.IsTrue(CardZones.TryPlaceInLane(state, first, 0, -1, events, out _));
            Assert.IsTrue(CardZones.TryPlaceInLane(state, second, 0, -1, events, out _));
            Assert.AreSame(first, state.Lanes[0].P1.Slots[0]);
            Assert.AreSame(second, state.Lanes[0].P1.Slots[1]);

            Assert.IsFalse(CardZones.TryPlaceInLane(state, third, 0, -1, events, out string error));
            StringAssert.Contains("full", error);
        }

        [Test]
        public void TryPlaceInLane_RejectsTakenOrInvalidSlots()
        {
            var state = new GameState(seed: 1);
            var def = Guy();
            var a = CardFactory.Create(state, def, 1);
            var b = CardFactory.Create(state, def, 1);

            Assert.IsTrue(CardZones.TryPlaceInLane(state, a, 2, 0, null, out _));
            Assert.AreSame(a, state.Lanes[2].P2.Slots[0], "owner 1 goes to the P2 side");

            Assert.IsFalse(CardZones.TryPlaceInLane(state, b, 2, 0, null, out _), "slot taken");
            Assert.IsFalse(CardZones.TryPlaceInLane(state, b, 99, 0, null, out _), "lane out of range");
        }
    }
}
