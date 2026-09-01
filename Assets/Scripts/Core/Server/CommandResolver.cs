using System;
using System.Collections.Generic;
using Game.Cards;
using Game.Core.Abilities;
using Game.Core.Commands;
using Game.Core.Events;
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
        private const int MaxHandSize = 7;
        private const int ShopOfferCount = 10;
        private const int ShopRemoveBaseCost = 5;
        private const int ShopRemoveCostIncrement = 5;
        private const int ShopTimeLimitSeconds = 45; // easily changeable

        // TEMP: there isn't enough non-Common card content yet to fill a real
        // random pool (design_plan wants 10; only a handful exist so far), so
        // for now every shop offer is just this one card — guarantees there's
        // always something clickable to buy while more content gets authored.
        // Remove this override (see GenerateShopOffers) once the pool's grown.
        private const string TempShopPlaceholderCardId = "giantape_01"; // "Great Ape"


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
                case RerollDeckCommand rr:          HandleRerollDeck(state, rr, events);           break;
                case EndShopCommand es:             HandleEndShop(state, es, events);              break;
                case ForceEndShopCommand fe:        HandleForceEndShop(state, fe, events);         break;
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

            state.SlotIndex = 0;
            state.RotationIndex = 0;
            EmitSlotChanged(state, events);
            ApplyStartOfTurn(state, events);   // P0's first turn begins here
        }

        /// <summary>
        /// DeckSize random guys from the configured card pool (seeded via
        /// state.Rng, so the same seed always deals the same decks). Without a
        /// configured catalog — logic tests, headless tools — falls back to the
        /// stat-line-only vanilla deck below.
        /// </summary>
        private static void BuildStarterDeck(GameState state, Player player)
        {
            // Starter decks deal "basic commons" only (game_plan) — rarer cards
            // will come from the shop/rewards once those exist.
            var guys = new List<CardDefinition>();
            foreach (var def in CardCatalogRuntime.Pool)
                guys.Add(def);

            foreach (var def in state.Rng.PickN(guys, DeckSize))
            {
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

            if (!state.IsMainActionSlot && IsGuyCard(card))
            {
                events.Add(new CommandRejectedEvent(cmd, "guys can only be played on your first action slot each cycle — this is a bonus, spell-only slot"));
                return;
            }

            if (card.CurrentCost > player.CurrentEnergy)
            {
                events.Add(new CommandRejectedEvent(cmd, $"not enough energy (have {player.CurrentEnergy}, need {card.CurrentCost})"));
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

            // OnPlay abilities. Only card draw exists so far (Initiate).
            int draws = AbilityRuntime.Sum(
                card, AbilityTrigger.OnPlay, AbilityEffect.DrawCard, AbilityTarget.Owner);
            for (int i = 0; i < draws; i++)
            {
                DrawCard(state, player, events);
            }
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
        /// Deletes a card from the player's collection — the undrawn deck, their
        /// hand, or a guy already deployed in a lane (see CardZones.OwnedCards).
        /// Ownership is checked before any gold moves, so a bad id never charges.
        /// </summary>
        private static void HandleRemoveCardFromDeck(GameState state, RemoveCardFromDeckCommand cmd, List<GameEvent> events)
        {
            if (!TryShopGuard(state, cmd, events, out var player)) return;

            if (!CardZones.Owns(state, player, cmd.DeckCardInstanceId))
            {
                events.Add(new CommandRejectedEvent(cmd, "that card isn't one of yours"));
                return;
            }

            int cost = NextRemoveCost(player);
            if (!MutationHelper.TrySpendGold(player, cost, events))
            {
                events.Add(new CommandRejectedEvent(cmd, $"not enough gold (have {player.Gold}, need {cost})"));
                return;
            }

            CardZones.TryRemoveOwned(state, player, cmd.DeckCardInstanceId);
            player.ShopRemovalsThisVisit++;
            events.Add(new CardRemovedFromDeckEvent(player.Id, cmd.DeckCardInstanceId, cost));
        }

        /// <summary>Once per shop visit, free: discards the whole deck (not hand)
        /// and deals a fresh one, same as the opening starter deck.</summary>
        private static void HandleRerollDeck(GameState state, RerollDeckCommand cmd, List<GameEvent> events)
        {
            if (!TryShopGuard(state, cmd, events, out var player)) return;

            if (player.HasUsedFreeDeckRerollThisVisit)
            {
                events.Add(new CommandRejectedEvent(cmd, "already used your free deck reroll this shop visit"));
                return;
            }

            player.Deck.Clear();
            BuildStarterDeck(state, player);
            state.Rng.Shuffle(player.Deck);
            player.HasUsedFreeDeckRerollThisVisit = true;
            events.Add(new DeckRerolledEvent(player.Id));
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
                    break; // awaits BuyCard/RemoveCardFromDeck/RerollDeck/EndShop commands from both players
                case SlotType.Augment:
                    // TODO: these need player input eventually. Skipped until built.
                    AdvanceSlot(state, events);
                    break;

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
        }

        /// <summary>
        /// Rotation lands on Shop: reset each player's per-visit shop state and
        /// deal them a fresh set of offers. Doesn't recurse into AdvanceSlot —
        /// Shop parks here awaiting both players' EndShopCommand.
        /// </summary>
        private static void EnterShop(GameState state, List<GameEvent> events)
        {
            state.ShopDeadlineUtc = DateTime.UtcNow.AddSeconds(ShopTimeLimitSeconds);

            foreach (var player in state.Players)
            {
                player.ShopReady = false;
                player.ShopRemovalsThisVisit = 0;
                player.HasUsedFreeDeckRerollThisVisit = false;
                GenerateShopOffers(state, player, events);
            }
        }

        /// <summary>
        /// 10 random offers from non-Common cards (design_plan: "10 cards
        /// available from a random pool of non common cards" — later filtered
        /// further by the player's character, for now the whole non-Common pool).
        /// </summary>
        private static void GenerateShopOffers(GameState state, Player player, List<GameEvent> events)
        {
            player.ShopOffers.Clear();

            // TEMP placeholder (see TempShopPlaceholderCardId) while the non-Common
            // pool is still too small to fill 10 real random offers.
            var placeholder = FindDefinition(TempShopPlaceholderCardId);
            if (placeholder != null)
            {
                for (int i = 0; i < ShopOfferCount; i++)
                    if (CardFactory.TryCreate(state, placeholder, player.Id, out var card, out _))
                        CardZones.TryAdd(state, card, CardZone.Shop, events, out _);
            }
            else
            {
                var pool = new List<CardDefinition>();
                foreach (var def in CardCatalogRuntime.Pool)
                    if (def.Rarity != Rarity.Common)
                        pool.Add(def);

                foreach (var def in state.Rng.PickN(pool, ShopOfferCount))
                    if (CardFactory.TryCreate(state, def, player.Id, out var card, out _))
                        CardZones.TryAdd(state, card, CardZone.Shop, events, out _);
            }

            events.Add(new ShopRefreshedEvent(player.Id));
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

        private static void DrawCard(GameState state, Player player, List<GameEvent> events)
        {
            if (player.Deck.Count == 0) return; //shuffle TODO
            if (player.cardsInHand.Count >= MaxHandSize) return;

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