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
        private const int CardsDrawnPerTurn = 1;
        private const int DeckSize = 10;


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
                if (def is GuyCardDefinition && def.Rarity == Rarity.Common)
                    guys.Add(def);

            if (guys.Count == 0)
            {
                BuildTestDeck(state, player);
                return;
            }

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
                    //todo shop
                    //we need to await actions
                    break;
                case SlotType.Augment:
                    // TODO: these need player input eventually. Skipped until built.
                    AdvanceSlot(state, events);
                    break;

                case SlotType.Action:
                    // A new turn begins: settle here, draw, fire turn keywords.
                    ApplyStartOfTurn(state, events);
                    break;
            }
        }

        /// <summary>
        /// Everything that happens when a player's action slot begins (each
        /// action slot in the rotation counts as one turn): the turn draw, then
        /// the board's StartOfTurn keywords — regen (guy heals itself),
        /// heroregen (heals its owner), goldgen (owner gains gold) and
        /// goldsteal (taken from the opponent).
        /// </summary>
        private static void ApplyStartOfTurn(GameState state, List<GameEvent> events)
        {
            if (state.CurrentSlotType != SlotType.Action) return;

            var player = state.Players[state.ActivePlayerId];
            var opponent = state.Players[1 - player.Id];

            for (int i = 0; i < CardsDrawnPerTurn; i++)
            {
                DrawCard(state, player, events);
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
        /// Combat ends a block: energy caps grow and refill. Card draw is NOT
        /// tied to blocks — players draw at the start of each of their turns
        /// (ApplyStartOfTurn), per the "draw one per turn" design.
        /// </summary>
        private static void StartNewEnergyBlock(GameState state, List<GameEvent> events)
        {
            foreach (var player in state.Players)
            {
                player.EnergyPerTurn += EnergyCapGrowthPerBlock;
                player.CurrentEnergy = player.EnergyPerTurn;
                events.Add(new EnergyChangedEvent(player.Id, player.CurrentEnergy, player.EnergyPerTurn));
            }
        }

        private static void DrawCard(GameState state, Player player, List<GameEvent> events)
        {
            if (player.Deck.Count == 0) return; //shuffle TODO

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