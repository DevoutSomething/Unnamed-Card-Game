using System;
using System.Collections.Generic;
using Game.Core.Commands;
using Game.Core.Events;
using Game.Core.State;

namespace Game.Client
{
    /// <summary>
    /// Whatever GameController talks to: the local hot-seat resolver
    /// (LocalGameServer), a networked match's host (Game.Net.NetworkHostServer),
    /// or its joining client (Game.Net.NetworkClientServer). Same shape either
    /// way, so GameController and BoardView never need to know which one they
    /// have — see LocalGameServer's Submit doc for the shared contract.
    /// </summary>
    public interface IGameServer
    {
        GameState State { get; }

        event Action<List<GameEvent>> OnEvents;

        void Submit(Command cmd);
    }
}
