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
    /// One test per ability keyword in abilities.json, driven through the real
    /// resolvers (no mocks): place guys, run combat, assert the outcome.
    /// </summary>
    public class AbilityEngineTests
    {
        [SetUp]
        public void LoadRealVocabulary()
        {
            AbilityRuntime.Configure(AbilityLoader.Parse(
                System.IO.File.ReadAllText("Assets/GameData/abilities.json")));
        }

        [TearDown]
        public void Reset()
        {
            AbilityRuntime.Configure(null);
            CardCatalogRuntime.Configure(null);
        }

        // ---------- helpers ----------

        /// <summary>Place a guy directly on the board with stats + abilities.</summary>
        private static CardInstance PlaceGuy(GameState state, int playerId, int lane, int slot,
                                             int attack, int health, int killGold = 0,
                                             params (string id, int x)[] abilities)
        {
            var card = new CardInstance
            {
                InstanceId = state.NextCardInstanceId++,
                DefinitionId = "test",
                OwnerId = playerId,
                CurrentAttack = attack,
                CurrentHealth = health,
                MaxHealth = health,
                KillRewardGold = killGold,
            };
            foreach (var (id, x) in abilities)
                card.Abilities.Add(new AbilityRef { Id = id, X = x });
            state.Lanes[lane].SublaneOf(playerId).Slots[slot] = card;
            return card;
        }

        private static List<GameEvent> RunCombat(GameState state)
        {
            var events = new List<GameEvent>();
            CombatResolver.Resolve(state, events);
            return events;
        }

        // ---------- pierce ----------

        [Test]
        public void Pierce_HitsEveryEnemyGuyInLane()
        {
            var state = new GameState(seed: 1);
            PlaceGuy(state, 0, 0, 0, attack: 2, health: 10, abilities: ("pierce", 1));
            var front = PlaceGuy(state, 1, 0, 0, attack: 0, health: 5);
            var back = PlaceGuy(state, 1, 0, 1, attack: 0, health: 5);

            RunCombat(state);

            Assert.AreEqual(3, front.CurrentHealth, "front hit for 2");
            Assert.AreEqual(3, back.CurrentHealth, "back hit for the same 2 — pierce cleaves the lane");
        }

        [Test]
        public void Pierce_HitsFaceWhenLaneIsEmpty()
        {
            var state = new GameState(seed: 1);
            int faceStart = state.Players[1].Health;
            PlaceGuy(state, 0, 0, 0, attack: 2, health: 10, abilities: ("pierce", 1));

            RunCombat(state);

            Assert.AreEqual(faceStart - 2, state.Players[1].Health, "no guys to cleave -> normal face hit");
        }

        // ---------- overkill ----------

        [Test]
        public void Overkill_ExcessDamageHitsEnemyPlayer()
        {
            var state = new GameState(seed: 1);
            int faceStart = state.Players[1].Health;
            PlaceGuy(state, 0, 0, 0, attack: 5, health: 10, abilities: ("overkill", 1));
            PlaceGuy(state, 1, 0, 0, attack: 0, health: 1);

            RunCombat(state);

            Assert.IsNull(state.Lanes[0].P2.Slots[0], "1-hp blocker dies");
            Assert.AreEqual(faceStart - 4, state.Players[1].Health, "5 attack - 1 hp = 4 overflow to face");
        }

        [Test]
        public void NoOverkill_ExcessDamageIsLost()
        {
            var state = new GameState(seed: 1);
            int faceStart = state.Players[1].Health;
            PlaceGuy(state, 0, 0, 0, attack: 5, health: 10);
            PlaceGuy(state, 1, 0, 0, attack: 0, health: 1);

            RunCombat(state);

            Assert.AreEqual(faceStart, state.Players[1].Health, "no overkill keyword -> no overflow");
        }

        // ---------- double tap ----------

        [Test]
        public void DoubleTap_AttacksTwice()
        {
            var state = new GameState(seed: 1);
            PlaceGuy(state, 0, 0, 0, attack: 2, health: 10, abilities: ("doubletap", 1));
            var wall = PlaceGuy(state, 1, 0, 0, attack: 0, health: 10);

            RunCombat(state);

            Assert.AreEqual(6, wall.CurrentHealth, "two swings of 2 = 4 damage");
        }

        [Test]
        public void DoubleTap_SecondSwingRetargetsPastCorpses()
        {
            var state = new GameState(seed: 1);
            PlaceGuy(state, 0, 0, 0, attack: 3, health: 10, abilities: ("doubletap", 1));
            PlaceGuy(state, 1, 0, 0, attack: 0, health: 3);   // dies to swing 1
            var back = PlaceGuy(state, 1, 0, 1, attack: 0, health: 5);

            RunCombat(state);

            Assert.IsNull(state.Lanes[0].P2.Slots[0], "front died to the first swing");
            Assert.AreEqual(2, back.CurrentHealth, "second swing must hit the living back card, not the corpse");
        }

        // ---------- precision ----------

        [Test]
        public void Precision_AttacksPlayerIgnoringBlockers()
        {
            var state = new GameState(seed: 1);
            int faceStart = state.Players[1].Health;
            PlaceGuy(state, 0, 0, 0, attack: 3, health: 10, abilities: ("precision", 1));
            var blocker = PlaceGuy(state, 1, 0, 0, attack: 0, health: 10);

            RunCombat(state);

            Assert.AreEqual(faceStart - 3, state.Players[1].Health, "precision goes straight at the hero");
            Assert.AreEqual(10, blocker.CurrentHealth, "the blocker is ignored entirely");
        }

        // ---------- growth ----------

        [Test]
        public void Growth_PermanentlyBuffsAtStartOfCombat()
        {
            var state = new GameState(seed: 1);
            int faceStart = state.Players[1].Health;
            var monk = PlaceGuy(state, 0, 0, 0, attack: 1, health: 3, abilities: ("growth", 1));

            RunCombat(state);

            Assert.AreEqual(2, monk.CurrentAttack, "1 attack + growth 1");
            Assert.AreEqual(4, monk.CurrentHealth, "3 health + growth 1");
            Assert.AreEqual(4, monk.MaxHealth, "growth raises the heal cap too");
            Assert.AreEqual(faceStart - 2, state.Players[1].Health, "attacks face with the buffed attack");
        }

        // ---------- heal + mending ----------

        [Test]
        public void Heal_RestoresLaneAlliesCappedAtMaxHealth_AndTriggersMending()
        {
            var state = new GameState(seed: 1);
            var nurse = PlaceGuy(state, 0, 0, 0, attack: 1, health: 2, abilities: ("heal", 2));
            var patient = PlaceGuy(state, 0, 0, 1, attack: 2, health: 3, abilities: ("mending", 1));
            patient.CurrentHealth = 2;   // damaged: heal 2 must only restore 1

            RunCombat(state);

            // heal wave: patient 2 -> 3 (capped), then mending: +1/+1 -> 4/4, attack 3
            Assert.AreEqual(3, patient.CurrentAttack, "mending buffed attack");
            Assert.AreEqual(4, patient.CurrentHealth, "healed to cap, then +1 max from mending");
            Assert.AreEqual(4, patient.MaxHealth);
            Assert.AreEqual(2, nurse.CurrentHealth, "nurse was full: no self-overheal");
        }

        [Test]
        public void Heal_AtFullHealth_DoesNothing()
        {
            var state = new GameState(seed: 1);
            var patient = PlaceGuy(state, 0, 0, 1, attack: 2, health: 3, abilities: ("mending", 1));
            PlaceGuy(state, 0, 0, 0, attack: 1, health: 2, abilities: ("heal", 2));

            RunCombat(state);

            Assert.AreEqual(2, patient.CurrentAttack, "no actual healing -> mending must not fire");
            Assert.AreEqual(3, patient.MaxHealth);
        }

        // ---------- rob ----------

        [Test]
        public void Rob_StealsDamageTimesXWhenHittingThePlayer()
        {
            var state = new GameState(seed: 1);
            state.Players[1].Gold = 10;
            PlaceGuy(state, 0, 0, 0, attack: 2, health: 10, abilities: ("rob", 2));
            // no blockers: the attack hits the player for 2 -> steals 2 * 2 = 4

            RunCombat(state);

            Assert.AreEqual(4, state.Players[0].Gold, "stole damage (2) * X (2)");
            Assert.AreEqual(6, state.Players[1].Gold);
        }

        [Test]
        public void Rob_DoesNotStealWhenBlocked()
        {
            var state = new GameState(seed: 1);
            state.Players[1].Gold = 10;
            PlaceGuy(state, 0, 0, 0, attack: 2, health: 10, abilities: ("rob", 2));
            PlaceGuy(state, 1, 0, 0, attack: 0, health: 10);   // soaks the hit: no player damage

            RunCombat(state);

            Assert.AreEqual(0, state.Players[0].Gold, "rob fires only on player damage");
            Assert.AreEqual(10, state.Players[1].Gold);
        }

        [Test]
        public void Rob_IsCappedByVictimsGold()
        {
            var state = new GameState(seed: 1);
            state.Players[1].Gold = 2;
            PlaceGuy(state, 0, 0, 0, attack: 3, health: 10, abilities: ("rob", 3));
            // face hit for 3 -> wants 9, victim only has 2

            RunCombat(state);

            Assert.AreEqual(2, state.Players[0].Gold, "can only steal what exists");
            Assert.AreEqual(0, state.Players[1].Gold);
        }

        // ---------- regen (start of the owner's turn) ----------

        [Test]
        public void Regen_HealsGuyAtStartOfOwnersTurn()
        {
            var state = new GameState(seed: 42);
            CommandResolver.Resolve(state, new StartGameCommand(0));

            var guy = PlaceGuy(state, 1, 0, 0, attack: 1, health: 5, abilities: ("regen", 2));
            guy.CurrentHealth = 1;   // damaged: two turns of regen 2 can't overheal past 5

            CommandResolver.Resolve(state, new EndPhaseCommand(0));   // -> P1's turn begins
            Assert.AreEqual(3, guy.CurrentHealth, "regen 2 fired for the owner's turn");

            CommandResolver.Resolve(state, new EndPhaseCommand(1));   // -> P1's second slot
            Assert.AreEqual(5, guy.CurrentHealth, "each action slot is a turn");

            CommandResolver.Resolve(state, new EndPhaseCommand(1));   // -> P0's turn
            Assert.AreEqual(5, guy.CurrentHealth, "capped at MaxHealth, and P0's turn heals nothing of P1's");
        }

        [Test]
        public void HeroRegen_HealsOwnerAtStartOfTurn_CappedAtMax()
        {
            var state = new GameState(seed: 42);
            CommandResolver.Resolve(state, new StartGameCommand(0));

            state.Players[1].Health = 90;
            PlaceGuy(state, 1, 0, 0, attack: 1, health: 5, abilities: ("heroregen", 3));

            CommandResolver.Resolve(state, new EndPhaseCommand(0));   // -> P1's turn
            Assert.AreEqual(93, state.Players[1].Health, "hero regen 3 healed the owner");
            Assert.AreEqual(100, state.Players[0].Health, "opponent untouched");

            state.Players[1].Health = 99;
            CommandResolver.Resolve(state, new EndPhaseCommand(1));   // -> P1 again
            Assert.AreEqual(100, state.Players[1].Health, "capped at MaxHealth");
        }

        // ---------- gold generation / stealing (start of turn) ----------

        [Test]
        public void GoldGen_GainsGoldAtStartOfOwnersTurn()
        {
            var state = new GameState(seed: 42);
            CommandResolver.Resolve(state, new StartGameCommand(0));
            PlaceGuy(state, 1, 0, 0, attack: 2, health: 2, abilities: ("goldgen", 10));

            CommandResolver.Resolve(state, new EndPhaseCommand(0));   // -> P1's turn

            Assert.AreEqual(10, state.Players[1].Gold, "investor income fired");
            Assert.AreEqual(0, state.Players[0].Gold);
        }

        [Test]
        public void GoldSteal_TakesFromOpponentAtStartOfTurn()
        {
            var state = new GameState(seed: 42);
            CommandResolver.Resolve(state, new StartGameCommand(0));
            state.Players[0].Gold = 20;
            PlaceGuy(state, 1, 0, 0, attack: 5, health: 4, abilities: ("goldsteal", 15));

            CommandResolver.Resolve(state, new EndPhaseCommand(0));   // -> P1's turn

            Assert.AreEqual(15, state.Players[1].Gold, "landlord stole");
            Assert.AreEqual(5, state.Players[0].Gold, "victim paid, capped by purse");
        }

        // ---------- mutual kill ----------

        [Test]
        public void Bounty_MutualKillPaysBothKillersRegardlessOfSeat()
        {
            var state = new GameState(seed: 1);
            PlaceGuy(state, 0, 0, 0, attack: 3, health: 3, killGold: 1, abilities: ("bounty", 2));
            PlaceGuy(state, 1, 0, 0, attack: 3, health: 3, killGold: 1, abilities: ("bounty", 2));

            RunCombat(state);

            // Each side collects the other's killRewardGold (1) plus its own bounty (2).
            Assert.AreEqual(3, state.Players[0].Gold, "P0's killer died in the trade but still earns its bounty");
            Assert.AreEqual(3, state.Players[1].Gold, "same trade, same gold — no seat advantage");
        }

        [Test]
        public void Thorns_PostMortemRetaliationDoesNotEraseKillCredit()
        {
            var state = new GameState(seed: 1);
            // Killer swings first and combat-kills the victim; the victim's dead
            // counterswing then takes thorns back — that post-mortem direct hit
            // must not overwrite who combat-killed it.
            PlaceGuy(state, 0, 0, 0, attack: 5, health: 10,
                     abilities: new[] { ("thorns", 1), ("bounty", 2) });
            PlaceGuy(state, 1, 0, 0, attack: 2, health: 3, killGold: 4);

            RunCombat(state);

            Assert.AreEqual(6, state.Players[0].Gold, "kill gold (4) + bounty (2) survive the corpse's thorns hit");
        }

        // ---------- hero damage ----------

        [Test]
        public void HeroDamage_ChipsEnemyPlayerEvenWhenBlocked()
        {
            var state = new GameState(seed: 1);
            int faceStart = state.Players[1].Health;
            PlaceGuy(state, 0, 0, 0, attack: 2, health: 10, abilities: ("herodamage", 1));
            PlaceGuy(state, 1, 0, 0, attack: 0, health: 10);   // blocker soaks the attack

            RunCombat(state);

            Assert.AreEqual(faceStart - 1, state.Players[1].Health,
                "start-of-combat hero damage bypasses the blocker (attack itself was soaked)");
        }

        // ---------- guy damage ----------

        [Test]
        public void GuyDamage_KillsWithoutAwardingKillGold()
        {
            var state = new GameState(seed: 1);
            PlaceGuy(state, 0, 0, 0, attack: 0, health: 10, abilities: ("guydamage", 2));
            var victim = PlaceGuy(state, 1, 3, 0, attack: 0, health: 2, killGold: 9);  // only enemy on board

            var events = RunCombat(state);

            Assert.IsNull(state.Lanes[3].P2.Slots[0], "victim died to guy damage");
            Assert.AreEqual(0, state.Players[0].Gold, "spell-like kill must award no gold");
            Assert.IsTrue(events.OfType<CardDiedEvent>().Any(e => e.CardInstanceId == victim.InstanceId));
        }

        // ---------- draw on play ----------

        [Test]
        public void DrawKeyword_DrawsOnPlay()
        {
            var state = new GameState(seed: 42);
            CommandResolver.Resolve(state, new StartGameCommand(0));
            var p0 = state.Players[0];

            var def = ScriptableObject.CreateInstance<GuyCardDefinition>();
            def.CardId = "initiate_test";
            def.DisplayName = "Initiate";
            def.EnergyCost = 1;
            def.BaseAttack = 1;
            def.BaseHealth = 1;
            def.Abilities.Add(new AbilityRef { Id = "draw", X = 1 });

            var card = CardFactory.Create(state, def, 0);
            p0.cardsInHand.Add(card);
            int handBefore = p0.cardsInHand.Count;
            int deckBefore = p0.Deck.Count;

            var events = CommandResolver.Resolve(state, new PlayCardCommand(0, card.InstanceId, LaneIndex: 0));

            Assert.IsFalse(events.OfType<CommandRejectedEvent>().Any());
            Assert.AreEqual(1, events.OfType<CardDrawnEvent>().Count(), "played card drew 1");
            Assert.AreEqual(deckBefore - 1, p0.Deck.Count);
            Assert.AreEqual(handBefore - 1 + 1, p0.cardsInHand.Count, "one played out, one drawn in");
        }

        // ---------- starter decks from the catalog ----------

        [Test]
        public void StarterDecks_AreBuiltFromConfiguredCatalog()
        {
            var pool = new List<CardDefinition>();
            for (int i = 0; i < 12; i++)
            {
                var def = ScriptableObject.CreateInstance<GuyCardDefinition>();
                def.CardId = $"pool_{i}";
                def.DisplayName = $"Pool {i}";
                def.EnergyCost = 1;
                def.BaseAttack = 1;
                def.BaseHealth = 2;
                pool.Add(def);
            }
            CardCatalogRuntime.Configure(pool);

            var state = new GameState(seed: 7);
            CommandResolver.Resolve(state, new StartGameCommand(0));

            foreach (var p in state.Players)
            {
                Assert.AreEqual(10, p.Deck.Count + p.cardsInHand.Count, $"P{p.Id} got a 10-card deck");
                foreach (var c in p.Deck.Concat(p.cardsInHand))
                {
                    StringAssert.StartsWith("pool_", c.DefinitionId, "cards must come from the catalog");
                    Assert.AreEqual(2, c.MaxHealth, "MaxHealth seeded from the definition");
                }
            }
        }

        [Test]
        public void StarterDecks_SameSeedSameDecks_WithCatalog()
        {
            var pool = new List<CardDefinition>();
            for (int i = 0; i < 15; i++)
            {
                var def = ScriptableObject.CreateInstance<GuyCardDefinition>();
                def.CardId = $"pool_{i}";
                def.EnergyCost = 1;
                def.BaseAttack = 1;
                def.BaseHealth = 1;
                pool.Add(def);
            }
            CardCatalogRuntime.Configure(pool);

            var a = new GameState(seed: 9);
            CommandResolver.Resolve(a, new StartGameCommand(0));
            var b = new GameState(seed: 9);
            CommandResolver.Resolve(b, new StartGameCommand(0));

            CollectionAssert.AreEqual(
                a.Players[0].Deck.Select(c => c.DefinitionId).ToList(),
                b.Players[0].Deck.Select(c => c.DefinitionId).ToList(),
                "same seed must produce identical decks");
        }

        // ---------- bloodprice ----------

        [Test]
        public void BloodPrice_OwnerLosesHealthDirectly_NotAsDamage()
        {
            var state = new GameState(seed: 1);
            var player = state.Players[0];
            player.CurrentEnergy = 1;
            int startHealth = player.Health;

            // DefinitionId is in no catalog, so IsGuyCard treats it as a guy and
            // it plays through the guy path where OnPlay abilities fire. Slot 0 is
            // P0's main action slot (Rotation.IsMainActionSlot[0]), so a guy is legal.
            var guy = new CardInstance
            {
                InstanceId = state.NextCardInstanceId++,
                DefinitionId = "bloodprice_test",
                OwnerId = 0,
                CurrentAttack = 1,
                CurrentHealth = 1,
                MaxHealth = 1,
                CurrentCost = 0,
            };
            guy.Abilities.Add(new AbilityRef { Id = "bloodprice", X = 3 });
            player.cardsInHand.Add(guy);

            var events = CommandResolver.Resolve(
                state, new PlayCardCommand(0, guy.InstanceId, LaneIndex: 0));

            Assert.IsFalse(events.OfType<CommandRejectedEvent>().Any(), "play should be accepted");
            Assert.AreEqual(startHealth - 3, player.Health, "owner pays 3 health on play");
            Assert.AreEqual(1, events.OfType<PlayerLostHealthEvent>().Count(),
                "emits exactly one PlayerLostHealthEvent");
            Assert.IsFalse(events.OfType<PlayerDamagedEvent>().Any(),
                "blood price is a cost, not damage — no PlayerDamagedEvent");
        }

        // ---------- lifesteal ----------

        [Test]
        public void Lifesteal_HealsAttackerForCombatDamageDealt()
        {
            var state = new GameState(seed: 1);
            var attacker = PlaceGuy(state, 0, 0, 0, attack: 3, health: 10, abilities: ("lifesteal", 1));
            attacker.CurrentHealth = 5;   // damaged so the heal is visible under the cap
            var blocker = PlaceGuy(state, 1, 0, 0, attack: 0, health: 10);

            RunCombat(state);

            Assert.AreEqual(7, blocker.CurrentHealth, "blocker took the 3 attack");
            Assert.AreEqual(8, attacker.CurrentHealth, "healed for damage dealt (3) * X (1): 5 -> 8");
        }

        [Test]
        public void Lifesteal_HealIsCappedAtMaxHealthAndScalesWithX()
        {
            var state = new GameState(seed: 1);
            var attacker = PlaceGuy(state, 0, 0, 0, attack: 3, health: 10, abilities: ("lifesteal", 2));
            attacker.CurrentHealth = 9;   // wants 3 * X(2) = 6, only 1 fits under MaxHealth
            PlaceGuy(state, 1, 0, 0, attack: 0, health: 10);

            RunCombat(state);

            Assert.AreEqual(10, attacker.CurrentHealth, "lifesteal heal is capped at MaxHealth");
        }

        [Test]
        public void Lifesteal_DoesNotReviveAnAttackerKilledByThorns()
        {
            var state = new GameState(seed: 1);
            // Attacker (2 hp) deals 3 to the blocker but takes 5 thorns back and dies.
            PlaceGuy(state, 0, 0, 0, attack: 3, health: 2, abilities: ("lifesteal", 5));
            PlaceGuy(state, 1, 0, 0, attack: 0, health: 5, abilities: ("thorns", 5));

            RunCombat(state);

            Assert.IsNull(state.Lanes[0].P1.Slots[0],
                "attacker died to thorns mid-attack; lifesteal must not heal a corpse back to life");
        }
    }
}
