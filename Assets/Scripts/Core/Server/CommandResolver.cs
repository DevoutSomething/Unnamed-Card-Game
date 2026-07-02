using System.Collections.Generic;
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
                //replace with actual deck-building once CharacterData + CardFactory wiring exists
                BuildTestDeck(state, player);
                state.Rng.Shuffle(player.Deck);

                player.EnergyCap = StartingEnergyCap;
                player.Energy = player.EnergyCap;
                events.Add(new EnergyChangedEvent(player.Id, player.Energy, player.EnergyCap));

                for (int i = 0; i < StartingHandSize; i++)
                {
                    DrawCard(state, player, events);
                }
            }

            state.SlotIndex = 0;
            state.RotationIndex = 0;
            EmitSlotChanged(state, events);
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
            var card = player.Hand.Find(c => c.InstanceId == cmd.CardInstanceId);
            if (card == null)
            {
                events.Add(new CommandRejectedEvent(cmd, "card not in your hand"));
                return;
            }

            if (card.CurrentCost > player.Energy)
            {
                events.Add(new CommandRejectedEvent(cmd, $"not enough energy (have {player.Energy}, need {card.CurrentCost})"));
                return;
            }

            if (cmd.LaneIndex < 0 || cmd.LaneIndex >= state.Lanes.Length)
            {
                events.Add(new CommandRejectedEvent(cmd, "invalid lane"));
                return;
            }

            var sublane = state.Lanes[cmd.LaneIndex].SublaneOf(cmd.PlayerId);
            int slot = sublane.ResolveSlot(cmd.SlotIndex);
            if (slot < 0)
            {
                events.Add(new CommandRejectedEvent(cmd, "no available slot in that lane"));
                return;
            }

            player.Energy -= card.CurrentCost;
            events.Add(new EnergyChangedEvent(player.Id, player.Energy, player.EnergyCap));

            player.Hand.Remove(card);
            sublane.Place(card, slot);
            events.Add(new CardPlayedEvent(cmd.PlayerId, card.InstanceId, cmd.LaneIndex, slot));
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
                    //stop advancing the slot.
                    break;
            }
        }

        private static void StartNewEnergyBlock(GameState state, List<GameEvent> events)
        {
            foreach (var player in state.Players)
            {
                player.EnergyCap += EnergyCapGrowthPerBlock;
                player.Energy = player.EnergyCap;
                events.Add(new EnergyChangedEvent(player.Id, player.Energy, player.EnergyCap));

                for (int i = 0; i < CardsDrawnPerBlock; i++)
                {
                    DrawCard(state, player, events);
                }
            }
        }

        private static void DrawCard(GameState state, Player player, List<GameEvent> events)
        {
            if (player.Deck.Count == 0) return; //shuffle TODO

            var card = player.Deck[0];
            player.Deck.RemoveAt(0);
            player.Hand.Add(card);
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