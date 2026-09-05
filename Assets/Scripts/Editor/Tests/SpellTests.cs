using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using Game.Cards;
using Game.Core.Abilities;
using Game.Core.Commands;
using Game.Core.Events;
using Game.Core.Server;
using Game.Core.State;
using UnityEngine;

namespace Game.Core.Tests
{
    /// <summary>
    /// Spells: consumed rather than placed, castable only on a spell turn, and
    /// validated before any energy is spent.
    /// </summary>
    public class SpellTests
    {
        [TearDown]
        public void ResetCatalog() => CardCatalogRuntime.Configure(null);

        // ---------- helpers ----------

        private static SpellCardDefinition Spell(
            string id, int cost, SpellTarget target,
            int damage = 0, int heal = 0, int buffAttack = 0, int buffHealth = 0, int draw = 0,
            string grantAbilityId = null, int grantAbilityX = 1)
        {
            var def = ScriptableObject.CreateInstance<SpellCardDefinition>();
            def.CardId = id;
            def.DisplayName = id;
            def.EnergyCost = cost;
            def.Target = target;
            def.DamageAmount = damage;
            def.HealAmount = heal;
            def.BuffAttack = buffAttack;
            def.BuffHealth = buffHealth;
            def.DrawCount = draw;
            def.GrantAbilityId = grantAbilityId;
            def.GrantAbilityX = grantAbilityX;
            return def;
        }

        private static GuyCardDefinition Guy(string id, int attack = 2, int health = 2)
        {
            var def = ScriptableObject.CreateInstance<GuyCardDefinition>();
            def.CardId = id;
            def.DisplayName = id;
            def.EnergyCost = 0;
            def.BaseAttack = attack;
            def.BaseHealth = health;
            return def;
        }

        /// A catalog of plain guys plus the one spell under test.
        private static void Configure(SpellCardDefinition spell)
        {
            var defs = new List<CardDefinition> { spell };
            for (int i = 0; i < 10; i++) defs.Add(Guy($"guy_{i}"));
            CardCatalogRuntime.Configure(defs);
        }

        /// <summary>
        /// A match parked on P0's SPELL turn (their second action slot this
        /// stretch) with the given spell in hand and plenty of energy. Lane
        /// types are cleared so a random lane can't perturb the measurement.
        /// </summary>
        private static GameState NewGameOnSpellTurn(SpellCardDefinition spellDef, out CardInstance spell)
        {
            Configure(spellDef);
            var state = new GameState(42);
            CommandResolver.Resolve(state, new StartGameCommand(0));
            foreach (var lane in state.Lanes) lane.LaneTypeId = null;

            // Rotation: slot 0 = P0 main, slot 1 = P1 main, slot 2 = P1 spell,
            // slot 3 = P0 spell. Walk to slot 3.
            CommandResolver.Resolve(state, new EndPhaseCommand(0));
            CommandResolver.Resolve(state, new EndPhaseCommand(1));
            CommandResolver.Resolve(state, new EndPhaseCommand(1));

            Assert.AreEqual(0, state.ActivePlayerId, "expected P0's slot");
            Assert.IsFalse(state.IsMainActionSlot, "expected a spell turn");

            var p0 = state.Players[0];
            p0.CurrentEnergy = 9;
            spell = state.Players[0].cardsInHand.FirstOrDefault(c => c.DefinitionId == spellDef.CardId);
            if (spell == null)
            {
                spell = CardFactory.Create(state, spellDef, 0);
                p0.cardsInHand.Add(spell);
            }
            return state;
        }

        private static List<GameEvent> Submit(GameState state, Command cmd)
            => CommandResolver.Resolve(state, cmd);

        private static CardInstance Deploy(GameState state, int playerId, int lane, int slot,
                                           int attack = 2, int health = 5)
        {
            var card = new CardInstance
            {
                InstanceId = state.NextCardInstanceId++,
                DefinitionId = "guy_0",
                OwnerId = playerId,
                CurrentAttack = attack,
                CurrentHealth = health,
                MaxHealth = health,
            };
            state.Lanes[lane].SublaneOf(playerId).Place(card, slot);
            return card;
        }

        private static void AssertRejected(IEnumerable<GameEvent> events) =>
            Assert.IsTrue(events.OfType<CommandRejectedEvent>().Any(), "expected a rejection");

        private static void AssertNoRejections(IEnumerable<GameEvent> events) =>
            Assert.IsFalse(events.OfType<CommandRejectedEvent>().Any(), "expected no rejection");

        // ---------- slot gating ----------

        [Test]
        public void Spell_RejectedOnMainSlot()
        {
            var def = Spell("bolt", 1, SpellTarget.None, draw: 1);
            Configure(def);
            var state = new GameState(42);
            CommandResolver.Resolve(state, new StartGameCommand(0));

            var p0 = state.Players[0];
            p0.CurrentEnergy = 9;
            var spell = CardFactory.Create(state, def, 0);
            p0.cardsInHand.Add(spell);

            Assert.IsTrue(state.IsMainActionSlot, "slot 0 is P0's main slot");
            var events = Submit(state, new PlayCardCommand(0, spell.InstanceId, LaneIndex: -1));

            AssertRejected(events);
            Assert.IsTrue(p0.cardsInHand.Contains(spell), "rejected spell stays in hand");
            Assert.AreEqual(9, p0.CurrentEnergy, "and costs nothing");
        }

        [Test]
        public void Guy_RejectedOnSpellTurn()
        {
            var def = Spell("bolt", 1, SpellTarget.None, draw: 1);
            var state = NewGameOnSpellTurn(def, out _);
            var p0 = state.Players[0];

            var guy = p0.cardsInHand.FirstOrDefault(c => c.DefinitionId != "bolt");
            Assert.IsNotNull(guy, "expected a guy in hand");

            var events = Submit(state, new PlayCardCommand(0, guy.InstanceId, LaneIndex: 0));

            AssertRejected(events);
            Assert.IsTrue(p0.cardsInHand.Contains(guy));
        }

        // ---------- the five cards ----------

        [Test]
        public void Zap_Deals2ToAGuy()
        {
            var def = Spell("zap", 1, SpellTarget.AnyCharacter, damage: 2);
            var state = NewGameOnSpellTurn(def, out var spell);
            var victim = Deploy(state, playerId: 1, lane: 0, slot: 0, health: 5);

            var events = Submit(state, new PlayCardCommand(
                0, spell.InstanceId, LaneIndex: -1, SlotIndex: -1,
                TargetCardInstanceId: victim.InstanceId));

            AssertNoRejections(events);
            Assert.AreEqual(3, victim.CurrentHealth);
            Assert.IsFalse(state.Players[0].cardsInHand.Contains(spell), "spell is consumed");
        }

        // ---------- grant ability ----------

        [Test]
        public void Grant_GivesTargetGuyTheAbility()
        {
            var def = Spell("bloodlust", 1, SpellTarget.FriendlyGuy,
                            grantAbilityId: "lifesteal", grantAbilityX: 1);
            var state = NewGameOnSpellTurn(def, out var spell);
            var ally = Deploy(state, playerId: 0, lane: 0, slot: 0, health: 5);

            Assert.IsFalse(ally.Abilities.Any(a => a.Id == "lifesteal"),
                           "guy starts without the granted keyword");

            var events = Submit(state, new PlayCardCommand(
                0, spell.InstanceId, LaneIndex: -1, SlotIndex: -1,
                TargetCardInstanceId: ally.InstanceId));

            AssertNoRejections(events);
            Assert.IsTrue(events.OfType<CardGainedAbilityEvent>().Any(e =>
                e.CardInstanceId == ally.InstanceId && e.AbilityId == "lifesteal" && e.X == 1),
                "a grant event is emitted");
            Assert.IsTrue(ally.Abilities.Any(a => a.Id == "lifesteal" && a.X == 1),
                          "the guy now carries the granted keyword at the granted magnitude");
            Assert.IsFalse(state.Players[0].cardsInHand.Contains(spell), "spell is consumed");
        }

        [Test]
        public void Grant_MergesInsteadOfDuplicating()
        {
            // A guy that already has the keyword: granting the same or weaker X is
            // a no-op; a stronger X upgrades in place — never a second copy.
            var def = Spell("bloodlust", 1, SpellTarget.FriendlyGuy,
                            grantAbilityId: "lifesteal", grantAbilityX: 1);
            var state = NewGameOnSpellTurn(def, out var spell);
            var ally = Deploy(state, playerId: 0, lane: 0, slot: 0, health: 5);
            ally.Abilities.Add(new AbilityRef { Id = "lifesteal", X = 2 });

            Submit(state, new PlayCardCommand(
                0, spell.InstanceId, LaneIndex: -1, SlotIndex: -1,
                TargetCardInstanceId: ally.InstanceId));

            Assert.AreEqual(1, ally.Abilities.Count(a => a.Id == "lifesteal"), "no duplicate entry");
            Assert.AreEqual(2, ally.Abilities.First(a => a.Id == "lifesteal").X, "kept the stronger X");
        }

        [Test]
        public void Zap_CanHitAHeroBecauseItTargetsAnything()
        {
            var def = Spell("zap", 1, SpellTarget.AnyCharacter, damage: 2);
            var state = NewGameOnSpellTurn(def, out var spell);
            int before = state.Players[1].Health;

            var events = Submit(state, new PlayCardCommand(
                0, spell.InstanceId, LaneIndex: -1, SlotIndex: -1, TargetPlayerId: 1));

            AssertNoRejections(events);
            Assert.AreEqual(before - 2, state.Players[1].Health);
        }

        [Test]
        public void Incinerate_Deals4ToAGuy_ButCannotHitAHero()
        {
            var def = Spell("incinerate", 2, SpellTarget.AnyGuy, damage: 4);
            var state = NewGameOnSpellTurn(def, out var spell);
            int heroBefore = state.Players[1].Health;

            var rejected = Submit(state, new PlayCardCommand(
                0, spell.InstanceId, LaneIndex: -1, SlotIndex: -1, TargetPlayerId: 1));

            AssertRejected(rejected);
            Assert.AreEqual(heroBefore, state.Players[1].Health, "hero untouched");
            Assert.IsTrue(state.Players[0].cardsInHand.Contains(spell), "and the spell wasn't spent");

            var victim = Deploy(state, playerId: 1, lane: 0, slot: 0, health: 5);
            var events = Submit(state, new PlayCardCommand(
                0, spell.InstanceId, LaneIndex: -1, SlotIndex: -1,
                TargetCardInstanceId: victim.InstanceId));

            AssertNoRejections(events);
            Assert.AreEqual(1, victim.CurrentHealth);
        }

        [Test]
        public void Research_Draws2AndNeedsNoTarget()
        {
            var def = Spell("research", 2, SpellTarget.None, draw: 2);
            var state = NewGameOnSpellTurn(def, out var spell);
            var p0 = state.Players[0];
            int deckBefore = p0.Deck.Count;

            var events = Submit(state, new PlayCardCommand(0, spell.InstanceId, LaneIndex: -1));

            AssertNoRejections(events);
            Assert.AreEqual(deckBefore - 2, p0.Deck.Count);
            Assert.AreEqual(2, events.OfType<CardDrawnEvent>().Count());
        }

        [Test]
        public void FieldStudy_Draws2AndHealsAGuyBy4()
        {
            var def = Spell("field_study", 3, SpellTarget.AnyGuy, heal: 4, draw: 2);
            var state = NewGameOnSpellTurn(def, out var spell);
            var p0 = state.Players[0];

            var wounded = Deploy(state, playerId: 0, lane: 0, slot: 0, health: 10);
            wounded.CurrentHealth = 3;   // 3/10 — room for the full heal
            int deckBefore = p0.Deck.Count;

            var events = Submit(state, new PlayCardCommand(
                0, spell.InstanceId, LaneIndex: -1, SlotIndex: -1,
                TargetCardInstanceId: wounded.InstanceId));

            AssertNoRejections(events);
            Assert.AreEqual(7, wounded.CurrentHealth, "healed 4");
            Assert.AreEqual(deckBefore - 2, p0.Deck.Count, "and drew 2");
        }

        [Test]
        public void Empower_Buffs2And2_OnlyOnYourOwnGuy()
        {
            var def = Spell("empower", 2, SpellTarget.FriendlyGuy, buffAttack: 2, buffHealth: 2);
            var state = NewGameOnSpellTurn(def, out var spell);

            var enemy = Deploy(state, playerId: 1, lane: 0, slot: 0);
            var rejected = Submit(state, new PlayCardCommand(
                0, spell.InstanceId, LaneIndex: -1, SlotIndex: -1,
                TargetCardInstanceId: enemy.InstanceId));

            AssertRejected(rejected);
            Assert.AreEqual(2, enemy.CurrentAttack, "enemy guy untouched");

            var mine = Deploy(state, playerId: 0, lane: 1, slot: 0, attack: 2, health: 2);
            var events = Submit(state, new PlayCardCommand(
                0, spell.InstanceId, LaneIndex: -1, SlotIndex: -1,
                TargetCardInstanceId: mine.InstanceId));

            AssertNoRejections(events);
            Assert.AreEqual(4, mine.CurrentAttack);
            Assert.AreEqual(4, mine.CurrentHealth);
            Assert.AreEqual(4, mine.MaxHealth, "buff raises the healing ceiling too");
        }

        // ---------- rules that hold across spells ----------

        [Test]
        public void Spell_NeverTakesALaneSlot()
        {
            var def = Spell("zap", 1, SpellTarget.AnyCharacter, damage: 2);
            var state = NewGameOnSpellTurn(def, out var spell);

            Submit(state, new PlayCardCommand(
                0, spell.InstanceId, LaneIndex: -1, SlotIndex: -1, TargetPlayerId: 1));

            foreach (var lane in state.Lanes)
                foreach (var slot in lane.SublaneOf(0).Slots)
                    Assert.AreNotEqual(spell.InstanceId, slot?.InstanceId,
                        "a spell resolves and is consumed — it never stands on the board");
        }

        [Test]
        public void SpellKill_PaysNoBounty_AndSweepsTheBoard()
        {
            var def = Spell("incinerate", 2, SpellTarget.AnyGuy, damage: 4);
            var state = NewGameOnSpellTurn(def, out var spell);

            var victim = Deploy(state, playerId: 1, lane: 0, slot: 0, health: 3);
            victim.KillRewardGold = 50;
            int casterGoldBefore = state.Players[0].Gold;

            var events = Submit(state, new PlayCardCommand(
                0, spell.InstanceId, LaneIndex: -1, SlotIndex: -1,
                TargetCardInstanceId: victim.InstanceId));

            AssertNoRejections(events);
            Assert.IsNull(state.Lanes[0].SublaneOf(1).Slots[0], "corpse swept immediately");
            Assert.AreEqual(casterGoldBefore, state.Players[0].Gold,
                "gold is for a guy killing a guy — spells pay nothing");
        }

        [Test]
        public void TargetedSpell_RejectedWithNoTarget_AndCostsNothing()
        {
            var def = Spell("incinerate", 2, SpellTarget.AnyGuy, damage: 4);
            var state = NewGameOnSpellTurn(def, out var spell);
            var p0 = state.Players[0];
            int energyBefore = p0.CurrentEnergy;

            var events = Submit(state, new PlayCardCommand(0, spell.InstanceId, LaneIndex: -1));

            AssertRejected(events);
            Assert.AreEqual(energyBefore, p0.CurrentEnergy, "validated before anything is paid");
            Assert.IsTrue(p0.cardsInHand.Contains(spell));
        }

        [Test]
        public void TargetedSpell_RejectedWhenTargetIsAlreadyDead()
        {
            var def = Spell("incinerate", 2, SpellTarget.AnyGuy, damage: 4);
            var state = NewGameOnSpellTurn(def, out var spell);
            var corpse = Deploy(state, playerId: 1, lane: 0, slot: 0, health: 5);
            corpse.CurrentHealth = 0;

            var events = Submit(state, new PlayCardCommand(
                0, spell.InstanceId, LaneIndex: -1, SlotIndex: -1,
                TargetCardInstanceId: corpse.InstanceId));

            AssertRejected(events);
        }

        [Test]
        public void StarterDeck_ContainsEverySpellInTheCatalog()
        {
            var def = Spell("zap", 1, SpellTarget.AnyCharacter, damage: 2);
            Configure(def);

            var state = new GameState(3);
            CommandResolver.Resolve(state, new StartGameCommand(0));

            foreach (var player in state.Players)
            {
                var all = player.Deck.Concat(player.cardsInHand).Select(c => c.DefinitionId);
                CollectionAssert.Contains(all.ToList(), "zap",
                    $"P{player.Id} must start with the spell (testing guarantee)");
            }
        }
    }
}
