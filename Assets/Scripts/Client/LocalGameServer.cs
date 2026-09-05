using System;
using System.Collections.Generic;
using Game.Core.Commands;
using Game.Core.Events;
using Game.Core.Server;
using Game.Core.State;

namespace Game.Client
{

    public class LocalGameServer : IGameServer
    {
        /// <summary>
        /// The authoritative game state.
        ///
        /// UI never directly modifies this

        /// </summary>
        public GameState State { get; private set; }

        /// <summary>
        /// Fired after every Submit with the full ordered event batch the
        /// command produced. One Submit = one invocation = one batch,
        /// even if the batch is just a single CommandRejectedEvent.
        /// </summary>
        public event Action<List<GameEvent>> OnEvents;

        /// <summary>
        /// Creates a fresh match and runs game setup (decks, hands, energy,
        /// initial slot). Subscribers receive the StartGame event batch,
        /// so subscribe to OnEvents BEFORE calling this.
        /// </summary>
        public void StartNewGame(int seed, string[] heroIds = null)
        {
            State = new GameState(seed);
            Submit(new StartGameCommand(0, heroIds));
        }

        /// <summary>
        /// The single entry point for anything wanting to change the game.
        /// Synchronous here: by the time this returns, State is already
        /// mutated and subscribers have already handled the events. Not so
        /// for Game.Net.NetworkClientServer's Submit — it just sends the
        /// command to the host and returns, with State/OnEvents only updating
        /// once the host's reply arrives — so don't write UI code against
        /// IGameServer that depends on Submit finishing "instantly" (e.g.
        /// reading State on the line after Submit instead of reacting to
        /// OnEvents); it happens to be true here but isn't part of the contract.
        /// </summary>
        public void Submit(Command cmd)
        {
            if (State == null)
                throw new InvalidOperationException("StartNewGame must be called before submitting commands.");

            List<GameEvent> events = CommandResolver.Resolve(State, cmd);
            OnEvents?.Invoke(events);
        }
    }
}