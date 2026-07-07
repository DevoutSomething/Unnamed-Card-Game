using System.Collections.Generic;
using Game.Core.Commands;
using Game.Core.Events;
using Game.Core.State;

namespace Game.Net
{
    /// <summary>
    /// Everything that can cross a NetChannel. Plain mutable-field classes
    /// (not records like Command/GameEvent) — these are wire DTOs owned by
    /// this layer, not game-rule types, so there's no reason for them to be
    /// immutable. Polymorphic dispatch (which concrete message arrived) goes
    /// through NetJson's TypeNameHandling, same as Command/GameEvent.
    /// </summary>
    public abstract class NetMessage { }

    // ---- Client -> Host ----

    /// <summary>First message a joining client sends, right after connecting.</summary>
    public class JoinRequestMessage : NetMessage
    {
        public string PlayerName;
    }

    /// <summary>A local player action, forwarded to the host for resolution.</summary>
    public class SubmitCommandMessage : NetMessage
    {
        public Command Command;
    }

    // ---- Host -> Client ----

    /// <summary>Accepts a join: assigns the client's player id (always 1 — host is 0).</summary>
    public class WelcomeMessage : NetMessage
    {
        public int YourPlayerId;
        public string HostName;
    }

    /// <summary>Only sent if a second stranger tries to connect to an already-full lobby.</summary>
    public class JoinRejectedMessage : NetMessage
    {
        public string Reason;
    }

    /// <summary>
    /// The result of resolving one command (or the very first one, from
    /// StartGameCommand): full state snapshot + the event batch it produced.
    /// The client's first StateUpdateMessage is what signals "the match has
    /// started" — there's no separate MatchStarted message.
    /// </summary>
    public class StateUpdateMessage : NetMessage
    {
        public GameState State;
        public List<GameEvent> Events;
    }
}
