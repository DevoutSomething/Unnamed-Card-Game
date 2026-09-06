using System;
using System.Collections.Generic;
using Game.Cards;
using Game.Core.Augments;
using Game.Core.Abilities;
using Game.Core.Commands;
using Game.Core.Events;
using Game.Core.Heroes;
using Game.Core.Lanes;
using Game.Core.State;

namespace Game.Core.Server
{
    public static class CommandResolver
    {
        private const int StartingHandSize = 4;
        private const int StartingEnergyCap = 1;
        private const int EnergyCapGrowthPerBlock = 1;
        private const int CardsDrawnPerBlock = 1;
        private const int DeckSize = 10;
        private const int MaxHandSize = 10;
        private const int ShopOfferCount = 10;
        private const int ShopRemoveBaseCost = 5;
        private const int ShopRemoveCostIncrement = 5;
        private const int ShopTimeLimitSeconds = 45; // easily changeable

        /// <summary>How many lanes start the match with a lane effect. The rest
        /// stay plain. Bump this to see more lane types per game.</summary>
        private const int SpecialLanesAtStart = 2;


        public static List<GameEvent> Resolve(GameState state, Command cmd)
        {
            var events = new List<GameEvent>();

            if (state.IsGameOver)
            {
                events.Add(new CommandRejectedEvent(cmd, "game is over"));
                return events;
            }

            switch (cmd)
            {
                case StartGameCommand s: HandleStartGame(state, s, events); break;
                case PlayCardCommand p:  HandlePlayCard(state, p, events);  break;
                case EndPhaseCommand e:  HandleEndPhase(state, e, events);  break;
                case BuyCardCommand buy:            HandleBuyCard(state, buy, events);            break;
                case RemoveCardFromDeckCommand rem: HandleRemoveCardFromDeck(state, rem, events);  break;
                case EndShopCommand es:             HandleEndShop(state, es, events);              break;
                case ForceEndShopCommand fe:        HandleForceEndShop(state, fe, events);         break;
                case SelectAugmentCommand sa:       HandleSelectAugment(state, sa, events);        break;
                case ForceEndAugmentCommand fa:     HandleForceEndAugment(state, fa, events);      break;
                default:
                    events.Add(new CommandRejectedEvent(cmd, "unknown command type"));
                    break;
            }

            return events;
        }

        private static void HandleStartGame(GameState state, StartGameCommand cmd, List<GameEvent> events)
        {
            events.Add(new GameStartedEvent(state.Seed));

            foreach (var player in state.Players)
            {
                if (cmd.HeroIds != null && player.Id >= 0 && player.Id < cmd.HeroIds.Length)
                    player.HeroId = cmd.HeroIds[player.Id];

                // Snapshot the hero's class onto the player so the shop (and anything
                // else) reads a local field, not the hero registry. Empty when no
                // hero was picked or the hero has no archetypes authored.
                player.Archetypes.Clear();
                if (!string.IsNullOrEmpty(player.HeroId) &&
                    HeroRuntime.Database.TryGet(player.HeroId, out var heroClass) &&
                    heroClass.Archetypes != null)
                    player.Archetypes.AddRange(heroClass.Archetypes);

                BuildStarterDeck(state, player);
                state.Rng.Shuffle(player.Deck);

                player.EnergyPerTurn = StartingEnergyCap;
                player.CurrentEnergy = player.EnergyPerTurn;
                events.Add(new EnergyChangedEvent(player.Id, player.CurrentEnergy, player.EnergyPerTurn));

                for (int i = 0; i < StartingHandSize; i++)
                {
                    DrawCard(state, player, events);
                }
            }

            AssignRandomLaneTypes(state, events);

            state.SlotIndex = 0;
            state.RotationIndex = 0;
            EmitSlotChanged(state, events);
            ApplyStartOfTurn(state, events);   // P0's first turn begins here
        }

        /// <summary>
        /// Deals lane effects onto random lanes at match start — distinct types,
        /// drawn from state.Rng so one seed reproduces the whole board.
        ///
        /// Runs AFTER decks are built and dealt deliberately: consuming rng
        /// earlier would shift every existing seed's shuffle.
        /// </summary>
        private static void AssignRandomLaneTypes(GameState state, List<GameEvent> events)
        {
            if (state.Lanes.Length == 0) return;

            var laneIndices = new List<int>();
            for (int i = 0; i < state.Lanes.Length; i++) laneIndices.Add(i);

            var chosenLanes = state.Rng.PickN(laneIndices, SpecialLanesAtStart);
            var definitions = LaneCatalog.PickDistinct(state.Rng, chosenLanes.Count);

            for (int i = 0; i < chosenLanes.Count && i < definitions.Count; i++)
            {
                ApplyLaneTypeTo(state.Lanes[chosenLanes[i]], definitions[i], events);
            }
        }

        /// <summary>
        /// Gives a lane its effect, and applies any stat modifier to the guys
        /// already standing there. That last part is a no-op on the empty
        /// opening board, but it's what makes this correct for the eventual
        /// event-card path — public precisely because that's the entry point an
        /// event card (or a guy that terraforms a lane) will call.
        /// </summary>
        public static void ApplyLaneTypeTo(Lane lane, LaneDefinition def, List<GameEvent> events)
        {
            if (lane == null || def == null) return;

            lane.LaneTypeId = def.Id;
            events.Add(new LaneAssignedEvent(lane.Position, def.Id));

            if (!def.HasStatModifier) return;

            foreach (var sublane in new[] { lane.P1, lane.P2 })
                foreach (var card in sublane.Slots)
                    if (card != null && card.CurrentHealth > 0)
                        MutationHelper.ApplyStatModifier(card, def.AttackModifier, def.HealthModifier, events);

            CombatResolver.ClearDeadInLane(lane, events);
        }

        /// <summary>
        /// DeckSize random guys from the configured card pool (seeded via
        /// state.Rng, so the same seed always deals the same decks). Without a
        /// configured catalog — logic tests, headless tools — falls back to the
        /// stat-line-only vanilla deck below.
        /// </summary>
        private static void BuildStarterDeck(GameState state, Player player)
        {
            // A picked hero with an authored base deck deals exactly that deck
            // (see HeroDefinition.BaseDeck). Falls through to the old behavior
            // for players who picked no hero or a hero whose deck is unauthored.
            if (!string.IsNullOrEmpty(player.HeroId) &&
                HeroRuntime.Database.TryGet(player.HeroId, out var hero) &&
                hero.BaseDeck != null && hero.BaseDeck.Count > 0)
            {
                BuildHeroBaseDeck(state, player, hero);
                return;
            }

            // No configured catalog (logic tests, headless tools): the vanilla
            // stat-line deck, as this method's summary has always claimed.
            // Without this the pool is empty and every player starts with no
            // deck and no hand at all.
            if (!CardCatalogRuntime.IsConfigured)
            {
                BuildTestDeck(state, player);
                return;
            }

            // Starter decks deal "basic commons" only (game_plan) — rarer cards
            // will come from the shop/rewards once those exist.
            var guys = new List<CardDefinition>();
            var spells = new List<CardDefinition>();
            foreach (var def in CardCatalogRuntime.Pool)
                (def is SpellCardDefinition ? spells : guys).Add(def);

            // TESTING: every spell in the catalog is guaranteed into the opening
            // deck, so spell turns always have something to cast. Drop this back
            // to a plain random draw once spells are earned from the shop.
            foreach (var def in spells)
            {
                if (CardFactory.TryCreate(state, def, player.Id, out var spell, out _))
                    player.Deck.Add(spell);
            }

            int remaining = Math.Max(0, DeckSize - player.Deck.Count);
            foreach (var def in state.Rng.PickN(guys, remaining))
            {
                if (CardFactory.TryCreate(state, def, player.Id, out var card, out _))
                    player.Deck.Add(card);
            }
        }

        /// <summary>
        /// Builds a deck from a hero's authored base deck: each (cardId, quantity)
        /// entry becomes that many fresh instances. Card ids missing from the
        /// catalog are skipped rather than crashing the match.
        /// </summary>
        private static void BuildHeroBaseDeck(GameState state, Player player, HeroDefinition hero)
        {
            foreach (var (cardId, quantity) in hero.BaseDeck)
            {
                var def = FindDefinition(cardId);
                if (def == null) continue;
                for (int i = 0; i < quantity; i++)
                    if (CardFactory.TryCreate(state, def, player.Id, out var card, out _))
                        player.Deck.Add(card);
            }
        }

        private static void BuildTestDeck(GameState state, Player player)
        {
            for (int i = 0; i < DeckSize; i++)
            {
                int cost = (i % 3) + 1;
                player.Deck.Add(new CardInstance
                {
                    InstanceId = state.NextCardInstanceId++,
                    DefinitionId = $"vanilla_{cost}",
                    OwnerId = player.Id,
                    CurrentAttack = cost + 1,
                    CurrentHealth = cost + 1,
                    MaxHealth = cost + 1,
                    CurrentCost = cost,
                    KillRewardGold = 1,
                });
            }
        }

        private static void HandlePlayCard(GameState state, PlayCardCommand cmd, List<GameEvent> events)
        {
            if (state.CurrentSlotType != SlotType.Action || state.ActivePlayerId != cmd.PlayerId)
            {
                events.Add(new CommandRejectedEvent(cmd, "not your action slot"));
                return;
            }

            var player = state.Players[cmd.PlayerId];
            var card = player.cardsInHand.Find(c => c.InstanceId == cmd.CardInstanceId);
            if (card == null)
            {
                events.Add(new CommandRejectedEvent(cmd, "card not in your hand"));
                return;
            }

            // Each action slot takes exactly one kind of card: your main slot is
            // for guys, your bonus ("spell") slot is for spells. Never both.
            bool isGuy = IsGuyCard(card);
            if (isGuy && !state.IsMainActionSlot)
            {
                events.Add(new CommandRejectedEvent(cmd, "guys can only be played on your first action slot each cycle — this is a spell turn"));
                return;
            }
            if (!isGuy && state.IsMainActionSlot)
            {
                events.Add(new CommandRejectedEvent(cmd, "spells can only be cast on your spell turn — this is your main slot, for guys"));
                return;
            }

            if (card.CurrentCost > player.CurrentEnergy)
            {
                events.Add(new CommandRejectedEvent(cmd, $"not enough energy (have {player.CurrentEnergy}, need {card.CurrentCost})"));
                return;
            }

            if (!isGuy)
            {
                CastSpell(state, cmd, player, card, events);
                return;
            }

            // CardZones owns placement (lane bounds, slot resolution, the
            // CardPlayedEvent). Buffer its events so the energy payment is
            // still emitted before CardPlayed.
            var placement = new List<GameEvent>();
            if (!CardZones.TryPlaceInLane(state, card, cmd.LaneIndex, cmd.SlotIndex, placement, out string error))
            {
                events.Add(new CommandRejectedEvent(cmd, error));
                return;
            }

            player.CurrentEnergy -= card.CurrentCost;
            events.Add(new EnergyChangedEvent(player.Id, player.CurrentEnergy, player.EnergyPerTurn));

            player.cardsInHand.Remove(card);
            events.AddRange(placement);

            // "Your guys get +X/+X" has to reach guys played AFTER the augment
            // was taken, not just the ones standing when it was picked.
            if (isGuy)
            {
                int augmentBuff = AugmentRuntime.Sum(
                    player, AbilityTrigger.Passive, AbilityEffect.BuffStats, AbilityTarget.OwnedGuys);
                if (augmentBuff > 0)
                {
                    MutationHelper.ApplyStatModifier(card, augmentBuff, augmentBuff, events);
                }
            }

            // Lane effects hit the guy that just arrived. Guys only: a spell has
            // no stat line to modify, and "play a guy here" means a guy.
            var laneDef = LaneCatalog.Get(state.Lanes[cmd.LaneIndex].LaneTypeId);
            if (laneDef != null && isGuy && laneDef.HasStatModifier)
            {
                MutationHelper.ApplyStatModifier(
                    card, laneDef.AttackModifier, laneDef.HealthModifier, events);
                // A 1-health guy walking into a -1/-1 lane dies on arrival
                // rather than loitering at 0 health until the next combat.
                CombatResolver.ClearDeadInLane(state.Lanes[cmd.LaneIndex], events);
            }

            // OnPlay abilities. Only card draw exists so far (Initiate).
            int draws = AbilityRuntime.Sum(
                card, AbilityTrigger.OnPlay, AbilityEffect.DrawCard, AbilityTarget.Owner);
            for (int i = 0; i < draws; i++)
            {
                DrawCard(state, player, events);
            }

            // Blood Price: an additional cost paid on play — the owner loses X
            // health DIRECTLY (not as damage), so it never trips a "when the
            // player takes damage" reaction. See MutationHelper.LosePlayerHealth.
            int bloodPrice = AbilityRuntime.Sum(
                card, AbilityTrigger.OnPlay, AbilityEffect.LoseHealth, AbilityTarget.Owner);
            if (bloodPrice > 0)
            {
                MutationHelper.LosePlayerHealth(player, bloodPrice, events);
            }

            if (laneDef != null && isGuy && laneDef.DrawOnGuyPlayed > 0)
            {
                events.Add(new LaneEffectTriggeredEvent(cmd.LaneIndex, laneDef.Id));
                for (int i = 0; i < laneDef.DrawOnGuyPlayed; i++)
                {
                    DrawCard(state, player, events);
                }
            }

            ApplyConjure(state, player, FindDefinition(card.DefinitionId), events);
        }

        // ---------------------------------------------------------------
        // Spells
        // ---------------------------------------------------------------

        /// <summary>
        /// Resolves a spell and consumes it: unlike a guy it never takes a lane
        /// slot, so there's no placement step and nothing is left on the board.
        /// The target is validated BEFORE any energy is spent, so a mis-aimed
        /// spell costs nothing and stays in hand.
        /// </summary>
        private static void CastSpell(
            GameState state, PlayCardCommand cmd, Player player, CardInstance card, List<GameEvent> events)
        {
            if (!(FindDefinition(card.DefinitionId) is SpellCardDefinition def))
            {
                events.Add(new CommandRejectedEvent(cmd, "that card has no spell definition"));
                return;
            }

            if (!TryResolveSpellTarget(state, cmd, player, def,
                                       out var targetCard, out var targetPlayer, out string targetError))
            {
                events.Add(new CommandRejectedEvent(cmd, targetError));
                return;
            }

            player.CurrentEnergy -= card.CurrentCost;
            events.Add(new EnergyChangedEvent(player.Id, player.CurrentEnergy, player.EnergyPerTurn));

            player.cardsInHand.Remove(card);
            events.Add(new SpellCastEvent(
                player.Id, card.InstanceId, cmd.TargetCardInstanceId, cmd.TargetPlayerId));

            if (def.DamageAmount > 0)
            {
                if (targetCard != null)
                {
                    // Direct, not combat, damage: a spell kill pays no bounty
                    // (game_plan: gold is for a guy killing a guy) and provokes
                    // no thorns.
                    MutationHelper.DealDirectDamage(targetCard, def.DamageAmount, card.InstanceId, events);
                }
                else if (targetPlayer != null)
                {
                    MutationHelper.DealCombatDamageToPlayer(targetPlayer, def.DamageAmount, events);
                }
            }

            if (def.HealAmount > 0 && targetCard != null)
            {
                MutationHelper.HealCard(targetCard, def.HealAmount, events);
            }

            if ((def.BuffAttack != 0 || def.BuffHealth != 0) && targetCard != null)
            {
                MutationHelper.ApplyStatModifier(targetCard, def.BuffAttack, def.BuffHealth, events);
            }

            if (def.GrantsAbility && targetCard != null)
            {
                MutationHelper.GrantAbility(targetCard, def.GrantAbilityId, def.GrantAbilityX, events);
            }

            for (int i = 0; i < def.DrawCount; i++)
            {
                DrawCard(state, player, events);
            }

            ApplyConjure(state, player, def, events);

            // A spell can kill a guy outright, so sweep before anyone reads the
            // board again. Awards no kill gold, by ClearDeadInLane's contract.
            foreach (var lane in state.Lanes)
            {
                CombatResolver.ClearDeadInLane(lane, events);
            }

            CombatResolver.CheckGameEnd(state, events);   // burn to the face can win
        }

        /// <summary>
        /// Turns the command's raw target ids into the actual thing being hit,
        /// rejecting anything the spell isn't allowed to point at. Out params are
        /// mutually exclusive: at most one of them is non-null.
        /// </summary>
        private static bool TryResolveSpellTarget(
            GameState state, PlayCardCommand cmd, Player caster, SpellCardDefinition def,
            out CardInstance targetCard, out Player targetPlayer, out string error)
        {
            targetCard = null;
            targetPlayer = null;
            error = null;

            if (!def.NeedsTarget) return true;

            if (cmd.TargetPlayerId >= 0)
            {
                if (def.Target != SpellTarget.AnyCharacter)
                {
                    error = "this spell has to target a guy, not a hero";
                    return false;
                }
                if (cmd.TargetPlayerId >= state.Players.Length)
                {
                    error = $"no such player ({cmd.TargetPlayerId})";
                    return false;
                }
                targetPlayer = state.Players[cmd.TargetPlayerId];
                return true;
            }

            if (cmd.TargetCardInstanceId < 0)
            {
                error = "this spell needs a target";
                return false;
            }

            targetCard = state.FindCardInLanes(cmd.TargetCardInstanceId);
            if (targetCard == null || targetCard.CurrentHealth <= 0)
            {
                error = "target isn't a living guy on the board";
                return false;
            }

            if (def.Target == SpellTarget.FriendlyGuy && targetCard.OwnerId != caster.Id)
            {
                error = "this spell can only target your own guys";
                targetCard = null;
                return false;
            }

            return true;
        }

        private static void HandleEndPhase(GameState state, EndPhaseCommand cmd, List<GameEvent> events)
        {
            if (state.CurrentSlotType != SlotType.Action || state.ActivePlayerId != cmd.PlayerId)
            {
                events.Add(new CommandRejectedEvent(cmd, "not your action slot"));
                return;
            }

            AdvanceSlot(state, events);
        }

        // ---------------------------------------------------------------
        // Shop
        // ---------------------------------------------------------------

        /// <summary>
        /// Shared guard for every shop command: must be the Shop slot, and the
        /// player must not have already signaled EndShop this visit. Unlike
        /// HandlePlayCard/HandleEndPhase there's no single "active player" to
        /// check — Shop has no turn structure, both players act freely.
        /// </summary>
        private static bool TryShopGuard(GameState state, Command cmd, List<GameEvent> events, out Player player)
        {
            player = null;
            if (state.CurrentSlotType != SlotType.Shop)
            {
                events.Add(new CommandRejectedEvent(cmd, "not the shop phase"));
                return false;
            }

            player = state.Players[cmd.PlayerId];
            if (player.ShopReady)
            {
                events.Add(new CommandRejectedEvent(cmd, "already done shopping this visit"));
                return false;
            }

            return true;
        }

        private static int NextRemoveCost(Player player) =>
            ShopRemoveBaseCost + ShopRemoveCostIncrement * player.ShopRemovalsThisVisit;

        private static void HandleBuyCard(GameState state, BuyCardCommand cmd, List<GameEvent> events)
        {
            if (!TryShopGuard(state, cmd, events, out var player)) return;

            var offer = player.ShopOffers.Find(c => c.InstanceId == cmd.ShopCardInstanceId);
            if (offer == null)
            {
                events.Add(new CommandRejectedEvent(cmd, "that card isn't in your shop offers"));
                return;
            }

            int cost = FindDefinition(offer.DefinitionId)?.GoldCost ?? 0;
            if (!MutationHelper.TrySpendGold(player, cost, events))
            {
                events.Add(new CommandRejectedEvent(cmd, $"not enough gold (have {player.Gold}, need {cost})"));
                return;
            }

            player.ShopOffers.Remove(offer);
            CardZones.TryAdd(state, offer, CardZone.Deck, events, out _);
            events.Add(new CardBoughtEvent(player.Id, offer.InstanceId, cost));
        }

        /// <summary>
        /// Deletes a card from the player's DRAW PILE. Cards in hand and guys
        /// already deployed are out of reach — the shop edits the pile you draw
        /// from, nothing else. Membership is checked before any gold moves, so a
        /// bad id never charges.
        /// </summary>
        private static void HandleRemoveCardFromDeck(GameState state, RemoveCardFromDeckCommand cmd, List<GameEvent> events)
        {
            if (!TryShopGuard(state, cmd, events, out var player)) return;

            var card = player.Deck.Find(c => c.InstanceId == cmd.DeckCardInstanceId);
            if (card == null)
            {
                events.Add(new CommandRejectedEvent(cmd, "that card isn't in your draw pile"));
                return;
            }

            int cost = NextRemoveCost(player);
            if (!MutationHelper.TrySpendGold(player, cost, events))
            {
                events.Add(new CommandRejectedEvent(cmd, $"not enough gold (have {player.Gold}, need {cost})"));
                return;
            }

            player.Deck.Remove(card);
            player.ShopRemovalsThisVisit++;
            events.Add(new CardRemovedFromDeckEvent(player.Id, card.InstanceId, cost));
        }

        // ---------------------------------------------------------------
        // Augments
        // ---------------------------------------------------------------

        /// <summary>How many augments each player chooses between.</summary>
        private const int AugmentOfferCount = 3;

        /// <summary>How long the pick window stays open before one is chosen for
        /// you. Easily changeable.</summary>
        private const int AugmentTimeLimitSeconds = 30;

        /// <summary>
        /// A rotation ends in EITHER an augment pick or a shop visit, never both.
        /// Both slots sit in the rotation table; whichever one isn't this
        /// rotation's turn passes straight through, the same way an Event slot
        /// does. Even rotations augment, odd rotations shop — so the very first
        /// interlude of a match is an augment pick.
        /// </summary>
        private static bool IsAugmentRotation(GameState state) => state.RotationIndex % 2 == 0;

        /// <summary>
        /// Rotation lands on Augment: deal each player their own set of choices
        /// from the augments they don't already own. A player with nothing left
        /// to be offered is auto-marked picked, so a fully-augmented match walks
        /// straight through the phase instead of deadlocking on it.
        /// </summary>
        private static void EnterAugmentPhase(GameState state, List<GameEvent> events)
        {
            if (!IsAugmentRotation(state))
            {
                AdvanceSlot(state, events);   // this rotation's interlude is the shop
                return;
            }

            state.AugmentDeadlineUtc = DateTime.UtcNow.AddSeconds(AugmentTimeLimitSeconds);

            foreach (var player in state.Players)
            {
                player.AugmentPicked = false;
                player.AugmentOffers.Clear();

                var candidates = new List<AugmentDefinition>();
                foreach (var def in AugmentCatalogRuntime.Pool)
                    if (!player.Augments.Contains(def.AugmentId)) candidates.Add(def);

                foreach (var def in state.Rng.PickN(candidates, AugmentOfferCount))
                    player.AugmentOffers.Add(def.AugmentId);

                if (player.AugmentOffers.Count == 0)
                {
                    player.AugmentPicked = true;   // nothing left to take
                    continue;
                }

                events.Add(new AugmentOfferedEvent(player.Id, player.AugmentOffers.ToArray()));
            }

            // Neither player had anything to choose: don't strand the rotation.
            if (state.Players[0].AugmentPicked && state.Players[1].AugmentPicked)
            {
                LeaveAugmentPhase(state);
                AdvanceSlot(state, events);
            }
        }

        private static void HandleSelectAugment(GameState state, SelectAugmentCommand cmd, List<GameEvent> events)
        {
            if (state.CurrentSlotType != SlotType.Augment)
            {
                events.Add(new CommandRejectedEvent(cmd, "not the augment phase"));
                return;
            }

            var player = state.Players[cmd.PlayerId];
            if (player.AugmentPicked)
            {
                events.Add(new CommandRejectedEvent(cmd, "you've already taken an augment this phase"));
                return;
            }

            if (!player.AugmentOffers.Contains(cmd.AugmentId))
            {
                events.Add(new CommandRejectedEvent(cmd, "that augment isn't one of your options"));
                return;
            }

            var def = AugmentCatalogRuntime.Get(cmd.AugmentId);
            if (def == null)
            {
                events.Add(new CommandRejectedEvent(cmd, $"unknown augment '{cmd.AugmentId}'"));
                return;
            }

            TakeAugment(state, player, def, events);

            if (state.Players[0].AugmentPicked && state.Players[1].AugmentPicked)
            {
                LeaveAugmentPhase(state);
                AdvanceSlot(state, events);
            }
        }

        /// <summary>
        /// Polled by clients while the augment timer runs; a no-op unless the
        /// window has actually closed by the resolver's OWN clock — safe to
        /// submit speculatively, repeatedly, from any client.
        ///
        /// On expiry it picks for whoever hasn't chosen, at random from their own
        /// offers, so an idle player still gets an augment rather than being
        /// skipped and falling behind.
        /// </summary>
        private static void HandleForceEndAugment(GameState state, ForceEndAugmentCommand cmd, List<GameEvent> events)
        {
            if (state.CurrentSlotType != SlotType.Augment) return;
            if (!state.AugmentDeadlineUtc.HasValue || DateTime.UtcNow < state.AugmentDeadlineUtc.Value) return;

            foreach (var player in state.Players)
            {
                if (player.AugmentPicked) continue;
                if (player.AugmentOffers.Count == 0) { player.AugmentPicked = true; continue; }

                var def = AugmentCatalogRuntime.Get(state.Rng.Pick(player.AugmentOffers));
                if (def == null) { player.AugmentPicked = true; continue; }

                TakeAugment(state, player, def, events);
            }

            LeaveAugmentPhase(state);
            AdvanceSlot(state, events);
        }

        /// <summary>Grants an augment and latches the player as done — shared by
        /// the deliberate pick and the timer's auto-pick so both take exactly the
        /// same path.</summary>
        private static void TakeAugment(
            GameState state, Player player, AugmentDefinition def, List<GameEvent> events)
        {
            player.Augments.Add(def.AugmentId);
            player.AugmentPicked = true;
            player.AugmentOffers.Clear();
            events.Add(new AugmentSelectedEvent(player.Id, def.AugmentId));

            ApplyAugmentOnTake(state, player, def, events);
        }

        private static void LeaveAugmentPhase(GameState state)
        {
            state.AugmentDeadlineUtc = null;
            foreach (var player in state.Players) player.AugmentOffers.Clear();
        }

        /// <summary>
        /// The one-shot half of an augment, applied the moment it's taken. The
        /// ongoing half (per-turn draws and gold) is read live by the hooks in
        /// ApplyStartOfTurn, and the guy buff is re-applied to each guy played
        /// later — see AugmentRuntime's summary.
        /// </summary>
        private static void ApplyAugmentOnTake(
            GameState state, Player player, AugmentDefinition def, List<GameEvent> events)
        {
            // Extra energy is a permanent cap bump, so the player simply has a
            // bigger battery from now on — 4/4 where they had 3/3. Nothing in
            // the refill path has to know augments exist.
            int bonusEnergy = AugmentRuntime.SumOf(
                def, AbilityTrigger.Passive, AbilityEffect.GainEnergy, AbilityTarget.Owner);
            if (bonusEnergy > 0)
            {
                player.EnergyPerTurn += bonusEnergy;
                player.CurrentEnergy += bonusEnergy;   // usable immediately, not next refill
                events.Add(new EnergyChangedEvent(player.Id, player.CurrentEnergy, player.EnergyPerTurn));
            }

            // "Your guys get +X/+X" reaches the ones already deployed; guys
            // played later pick it up in HandlePlayCard.
            int guyBuff = AugmentRuntime.SumOf(
                def, AbilityTrigger.Passive, AbilityEffect.BuffStats, AbilityTarget.OwnedGuys);
            if (guyBuff > 0)
            {
                foreach (var lane in state.Lanes)
                    foreach (var card in lane.SublaneOf(player.Id).Cards)
                        if (card.CurrentHealth > 0)
                            MutationHelper.ApplyStatModifier(card, guyBuff, guyBuff, events);
            }
        }

        private static void HandleEndShop(GameState state, EndShopCommand cmd, List<GameEvent> events)
        {
            if (!TryShopGuard(state, cmd, events, out var player)) return;

            player.ShopReady = true;

            if (state.Players[0].ShopReady && state.Players[1].ShopReady)
            {
                LeaveShop(state);
                AdvanceSlot(state, events);
            }
        }

        /// <summary>
        /// Polled by clients every frame while the shop's timer is running
        /// (see GameController.Update); a no-op unless the deadline has
        /// actually passed by the resolver's OWN clock — safe to submit
        /// speculatively, repeatedly, from any/every client.
        /// </summary>
        private static void HandleForceEndShop(GameState state, ForceEndShopCommand cmd, List<GameEvent> events)
        {
            if (state.CurrentSlotType != SlotType.Shop) return;
            if (!state.ShopDeadlineUtc.HasValue || DateTime.UtcNow < state.ShopDeadlineUtc.Value) return;

            foreach (var p in state.Players) p.ShopReady = true;
            LeaveShop(state);
            AdvanceSlot(state, events);
        }

        private static void LeaveShop(GameState state)
        {
            state.ShopDeadlineUtc = null;
            foreach (var p in state.Players) p.ShopOffers.Clear();
        }

        /// Advances the rotation pointer by one slot, auto-resolving any system slots
        /// (Combat, Event) it lands on, until it settles on a slot requiring player input.
        private static void AdvanceSlot(GameState state, List<GameEvent> events)
        {
            state.SlotIndex++;
            if (state.SlotIndex >= Rotation.Length)
            {
                state.SlotIndex = 0;
                state.RotationIndex++;
            }

            EmitSlotChanged(state, events);

            switch (state.CurrentSlotType)
            {
                case SlotType.Combat:
                    CombatResolver.Resolve(state, events);
                    if (state.IsGameOver) return;       //stop end game
                    StartNewEnergyBlock(state, events); // combat ends a block: refill + grow cap + draws
                    AdvanceSlot(state, events);
                    break;

                case SlotType.Event:
                    // TODO: EventResolver. For now events are a no-op pass-through.
                    AdvanceSlot(state, events);
                    break;

                case SlotType.Shop:
                    EnterShop(state, events);
                    break; // awaits BuyCard/RemoveCardFromDeck/EndShop commands from both players
                case SlotType.Augment:
                    EnterAugmentPhase(state, events);
                    break; // awaits a SelectAugmentCommand from both players

                case SlotType.Action:
                    // A new turn begins: settle here, fire turn keywords.
                    ApplyStartOfTurn(state, events);
                    break;
            }
        }

        /// <summary>
        /// Everything that happens when a player's action slot begins (each
        /// action slot in the rotation counts as one turn): the board's
        /// StartOfTurn keywords — regen (guy heals itself), heroregen (heals
        /// its owner), goldgen (owner gains gold) and goldsteal (taken from the
        /// opponent). Card draw is NOT here — see StartNewEnergyBlock.
        /// </summary>
        private static void ApplyStartOfTurn(GameState state, List<GameEvent> events)
        {
            if (state.CurrentSlotType != SlotType.Action) return;

            var player = state.Players[state.ActivePlayerId];
            var opponent = state.Players[1 - player.Id];

            // Augments fire on the same beat a guy's StartOfTurn keywords do —
            // this is the codebase's definition of a turn (one action slot).
            int augmentDraws = AugmentRuntime.Sum(
                player, AbilityTrigger.StartOfTurn, AbilityEffect.DrawCard, AbilityTarget.Owner);
            for (int i = 0; i < augmentDraws; i++)
            {
                DrawCard(state, player, events);
            }

            int augmentGold = AugmentRuntime.Sum(
                player, AbilityTrigger.StartOfTurn, AbilityEffect.GainGold, AbilityTarget.Owner);
            if (augmentGold > 0)
            {
                MutationHelper.GiveGold(player, augmentGold, events);
            }

            foreach (var lane in state.Lanes)
            {
                foreach (var card in lane.SublaneOf(player.Id).Cards)
                {
                    if (card.CurrentHealth <= 0) continue;

                    int selfHeal = AbilityRuntime.Sum(
                        card, AbilityTrigger.StartOfTurn, AbilityEffect.Heal, AbilityTarget.Self);
                    if (selfHeal > 0)
                    {
                        MutationHelper.HealCard(card, selfHeal, events);
                    }

                    int heroHeal = AbilityRuntime.Sum(
                        card, AbilityTrigger.StartOfTurn, AbilityEffect.Heal, AbilityTarget.Owner);
                    if (heroHeal > 0)
                    {
                        MutationHelper.HealPlayer(player, heroHeal, events);
                    }

                    int gold = AbilityRuntime.Sum(
                        card, AbilityTrigger.StartOfTurn, AbilityEffect.GainGold, AbilityTarget.Owner);
                    if (gold > 0)
                    {
                        MutationHelper.GiveGold(player, gold, events);
                    }

                    int steal = AbilityRuntime.Sum(
                        card, AbilityTrigger.StartOfTurn, AbilityEffect.StealGold, AbilityTarget.Owner);
                    if (steal > 0)
                    {
                        MutationHelper.StealGold(player, opponent, steal, events);
                    }
                }
            }
        }

        /// <summary>
        /// Combat ends a block: both players draw together here (not per-turn —
        /// this is the only place cards are drawn during a match, aside from
        /// the opening deal) and energy caps grow and refill.
        /// </summary>
        private static void StartNewEnergyBlock(GameState state, List<GameEvent> events)
        {
            foreach (var player in state.Players)
            {
                player.EnergyPerTurn += EnergyCapGrowthPerBlock;
                player.CurrentEnergy = player.EnergyPerTurn;
                events.Add(new EnergyChangedEvent(player.Id, player.CurrentEnergy, player.EnergyPerTurn));

                for (int i = 0; i < CardsDrawnPerBlock; i++)
                {
                    DrawCard(state, player, events);
                }
            }

            ApplyLaneEnergyResetEffects(state, events);
        }

        /// <summary>
        /// Every lane effect that fires when an energy block begins (i.e. just
        /// after each combat) — twice per rotation, for both players at once,
        /// which is what makes these the lane's "upkeep" rather than a per-turn
        /// trigger:
        ///
        ///   - mining: each player is paid per living guy they hold in the lane
        ///   - hazards: damage to every guy in the lane, both sides
        ///
        /// Hazard damage is DIRECT damage, so it provokes no thorns and pays out
        /// no kill gold — the lane isn't a guy attacking.
        /// </summary>
        private static void ApplyLaneEnergyResetEffects(GameState state, List<GameEvent> events)
        {
            foreach (var lane in state.Lanes)
            {
                var def = LaneCatalog.Get(lane.LaneTypeId);
                if (def == null) continue;
                if (def.GoldGeneration <= 0 && def.DamageAllOnEnergyReset <= 0) continue;

                events.Add(new LaneEffectTriggeredEvent(lane.Position, def.Id));

                // Mining pays out first, so a guy standing in a lane that both
                // mines AND burns still earns for the turn it dies on.
                if (def.GoldGeneration > 0)
                {
                    foreach (var player in state.Players)
                    {
                        int miners = 0;
                        foreach (var card in lane.SublaneOf(player.Id).Cards)
                            if (card.CurrentHealth > 0) miners++;

                        if (miners > 0)
                            MutationHelper.GiveGold(player, def.GoldGeneration * miners, events);
                    }
                }

                if (def.DamageAllOnEnergyReset > 0)
                {
                    foreach (var sublane in new[] { lane.P1, lane.P2 })
                        foreach (var card in sublane.Slots)
                            if (card != null && card.CurrentHealth > 0)
                                MutationHelper.DealDirectDamage(
                                    card, def.DamageAllOnEnergyReset, sourceInstanceId: -1, events);

                    CombatResolver.ClearDeadInLane(lane, events);
                }
            }
        }

        /// <summary>
        /// Rotation lands on Shop: reset each player's per-visit shop state and
        /// deal them a fresh set of offers. Doesn't recurse into AdvanceSlot —
        /// Shop parks here awaiting both players' EndShopCommand.
        /// </summary>
        private static void EnterShop(GameState state, List<GameEvent> events)
        {
            if (IsAugmentRotation(state))
            {
                AdvanceSlot(state, events);   // this rotation's interlude was the augment pick
                return;
            }

            state.ShopDeadlineUtc = DateTime.UtcNow.AddSeconds(ShopTimeLimitSeconds);

            foreach (var player in state.Players)
            {
                player.ShopReady = false;
                player.ShopRemovalsThisVisit = 0;
                GenerateShopOffers(state, player, events);
            }
        }

        /// <summary>
        /// 10 random offers drawn from the cards that fit this player's hero: its
        /// signed cards, cards sharing one of its archetypes, plus neutral cards
        /// (no hero and no class) which every hero can buy. See <see cref="ShopOffersCard"/>.
        /// Rarity is not a filter — neutral commons are meant to show.
        /// </summary>
        private static void GenerateShopOffers(GameState state, Player player, List<GameEvent> events)
        {
            player.ShopOffers.Clear();

            var pool = new List<CardDefinition>();
            foreach (var def in CardCatalogRuntime.Pool)
                if (ShopOffersCard(def, player))
                    pool.Add(def);

            // Drawn independently (with replacement) rather than via Rng.PickN:
            // PickN only ever returns DISTINCT entries, and there are currently
            // fewer distinct non-Commons authored than there are shop slots, so
            // PickN would silently under-fill the shop. Duplicate offers are
            // normal for a shop anyway, and this keeps every slot filled however
            // small (or large) the pool grows.
            for (int i = 0; i < ShopOfferCount && pool.Count > 0; i++)
            {
                var def = state.Rng.Pick(pool);
                if (CardFactory.TryCreate(state, def, player.Id, out var card, out _))
                    CardZones.TryAdd(state, card, CardZone.Shop, events, out _);
            }

            events.Add(new ShopRefreshedEvent(player.Id));
        }

        /// <summary>
        /// Whether a card belongs in this player's shop. Three ways in:
        ///   * neutral — no hero and no class (Colorless-only counts as no class),
        ///     so it shows in every hero's shop;
        ///   * signed — the card lists this player's hero;
        ///   * class match — the card shares a real (non-Colorless) archetype with
        ///     the hero (player.Archetypes, snapshotted at StartGame).
        /// </summary>
        private static bool ShopOffersCard(CardDefinition def, Player player)
        {
            if (def == null) return false;

            bool hasHero = def.Heroes != null && def.Heroes.Count > 0;
            bool hasClass = HasNonColorlessArchetype(def);

            // Neutral cards are available to everyone.
            if (!hasHero && !hasClass) return true;

            // Cards signed to this specific hero.
            if (hasHero && !string.IsNullOrEmpty(player.HeroId) && def.Heroes.Contains(player.HeroId))
                return true;

            // Cards that share one of the hero's archetypes (Colorless never matches).
            if (player.Archetypes != null && def.Archetypes != null)
                foreach (var archetype in player.Archetypes)
                    if (archetype != Archetype.Colorless && def.Archetypes.Contains(archetype))
                        return true;

            return false;
        }

        /// <summary>True if the card has any archetype other than Colorless. Colorless
        /// means "no class", so a Colorless-only (or empty) card is treated as neutral.</summary>
        private static bool HasNonColorlessArchetype(CardDefinition def)
        {
            if (def.Archetypes == null) return false;
            foreach (var archetype in def.Archetypes)
                if (archetype != Archetype.Colorless) return true;
            return false;
        }

        /// <summary>
        /// Spawns this card's conjures into the player's hand. Conjured cards
        /// are built fresh from the catalog and never touch the deck — they
        /// aren't drawn, and playing or discarding one doesn't put it back.
        ///
        /// Drawn WITH replacement: "conjure 3 random spells" can legitimately
        /// roll the same spell twice.
        /// </summary>
        private static void ApplyConjure(
            GameState state, Player player, CardDefinition source, List<GameEvent> events)
        {
            if (source == null || !source.Conjures) return;
            ConjureFromSpec(state, player, source.Conjure, events);
        }

        /// <summary>
        /// The spec-driven half of conjuring, so anything that needs to spawn a
        /// filtered card can reuse it — a card's own Conjure block, or the
        /// empty-draw-pile rule.
        /// </summary>
        private static void ConjureFromSpec(
            GameState state, Player player, ConjureSpec spec, List<GameEvent> events)
        {
            if (spec == null || !spec.Conjures) return;

            var candidates = new List<CardDefinition>();
            foreach (var candidate in CardCatalogRuntime.Pool)
                if (spec.Matches(candidate)) candidates.Add(candidate);

            // A filter nothing in the catalog satisfies conjures nothing, rather
            // than falling back to a random card the author didn't ask for.
            if (candidates.Count == 0) return;

            for (int i = 0; i < spec.Count; i++)
            {
                if (player.cardsInHand.Count >= MaxHandSize) return;

                var chosen = state.Rng.Pick(candidates);
                if (!CardFactory.TryCreate(state, chosen, player.Id, out var card, out _)) continue;

                if (spec.CostReduction > 0)
                {
                    card.CurrentCost = Math.Max(0, card.CurrentCost - spec.CostReduction);                    
                }

                if (spec.AttackBonus > 0)
                {
                    card.CurrentAttack = Math.Max(0, card.CurrentAttack + spec.AttackBonus);
                }

                if (spec.HealthBonus > 0)
                {
                    // Both, or the conjured guy arrives pre-damaged: MaxHealth
                    // alone would give a 1/1 a 1/3 stat line at 1 health.
                    card.MaxHealth += spec.HealthBonus;
                    card.CurrentHealth += spec.HealthBonus;
                }

                player.cardsInHand.Add(card);
                events.Add(new CardConjuredEvent(
                    player.Id, card.InstanceId, card.DefinitionId, card.CurrentCost));
            }
        }

        private static CardDefinition FindDefinition(string cardId)
        {
            foreach (var def in CardCatalogRuntime.Pool)
                if (def.CardId == cardId)
                    return def;
            return null;
        }

        /// <summary>
        /// Guy cards are only playable on a player's main action slot each
        /// cycle (their other slots are spell-only) — see Rotation.IsMainActionSlot.
        /// Determined by the card's CardDefinition, looked up by DefinitionId;
        /// an unconfigured catalog (headless logic tests) treats every card as
        /// a guy, matching the vanilla test deck's guy-shaped stat lines.
        /// </summary>
        private static bool IsGuyCard(CardInstance card)
        {
            var def = FindDefinition(card.DefinitionId);
            return def == null || def is GuyCardDefinition;
        }

        /// <summary>
        /// What an empty draw pile produces instead of a card. There's no
        /// reshuffle and no fatigue in this game: run dry and you keep drawing
        /// forever, just off the bottom of the barrel.
        /// </summary>
        private static readonly ConjureSpec EmptyDeckDraw = new ConjureSpec
        {
            Count = 1,
            RarityFilter = ConjureRarityFilter.Exactly,
            Rarity = Rarity.Common,
        };

        /// <summary>
        /// Takes the top card of the draw pile, or — when the pile is empty —
        /// conjures a random common instead. A conjured card emits
        /// CardConjuredEvent rather than CardDrawnEvent, because it came from
        /// the catalog and not from anything the player built.
        /// </summary>
        private static void DrawCard(GameState state, Player player, List<GameEvent> events)
        {
            // Checked first: a full hand stops a draw whether or not the pile
            // has anything left, so an empty pile can't conjure past the cap.
            if (player.cardsInHand.Count >= MaxHandSize) return;

            if (player.Deck.Count == 0)
            {
                ConjureFromSpec(state, player, EmptyDeckDraw, events);
                return;
            }

            var card = player.Deck[0];
            player.Deck.RemoveAt(0);
            player.cardsInHand.Add(card);
            events.Add(new CardDrawnEvent(player.Id, card.InstanceId));
        }

        private static void EmitSlotChanged(GameState state, List<GameEvent> events)
        {
            events.Add(new SlotChangedEvent(
                state.SlotIndex,
                state.CurrentSlotType.ToString(),
                state.ActivePlayerId));
        }
    }
}
