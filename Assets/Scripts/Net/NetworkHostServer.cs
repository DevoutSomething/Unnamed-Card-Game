using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using Game.Client;
using Game.Core.Commands;
using Game.Core.Events;
using Game.Core.Server;
using Game.Core.State;

namespace Game.Net
{
    /// <summary>
    /// The authoritative side of a networked match. The host's own client
    /// plays as player 0 and owns the real GameState + CommandResolver, same
    /// as LocalGameServer, but with a second, remote player 1 connected over
    /// TCP. Every command — from either side — is resolved here, then the
    /// resulting state+events are broadcast to the remote peer.
    ///
    /// NOT hidden-information-safe: the full GameState (both hands and decks)
    /// is sent to the remote client exactly as the host sees it locally. Fine
    /// for playing with a trusted friend; a modified client could read the
    /// opponent's hand off the wire. Hardening that would mean sending each
    /// side a redacted view instead of the raw state.
    /// </summary>
    public class NetworkHostServer : IGameServer, IDisposable
    {
        public GameState State { get; private set; }
        public event Action<List<GameEvent>> OnEvents;

        /// <summary>Main-thread: fired once the opponent completes the join handshake.</summary>
        public event Action<string> OnOpponentJoined;
        /// <summary>Main-thread: fired if the connected opponent drops, before or during the match.</summary>
        public event Action<string> OnOpponentDisconnected;

        public bool OpponentConnected => _channel != null;
        public int Port { get; }
        public string HostName { get; }
        public string OpponentName { get; private set; }

        private readonly TcpListener _listener;
        private readonly Thread _acceptThread;
        private readonly ConcurrentQueue<TcpClient> _pendingConnections = new ConcurrentQueue<TcpClient>();
        private volatile bool _disposed;
        private NetChannel _channel;

        public NetworkHostServer(int port, string hostName)
        {
            Port = port;
            HostName = string.IsNullOrWhiteSpace(hostName) ? "Host" : hostName;

            _listener = new TcpListener(IPAddress.Any, port);
            _listener.Start();

            _acceptThread = new Thread(AcceptLoop) { IsBackground = true, Name = "NetworkHostServer-Accept" };
            _acceptThread.Start();
        }

        /// <summary>Best-effort local IPv4 addresses to show the host — e.g. so a LAN friend knows what to type.</summary>
        public static List<string> GetLocalAddresses()
        {
            var result = new List<string>();
            try
            {
                foreach (var ip in Dns.GetHostAddresses(Dns.GetHostName()))
                    if (ip.AddressFamily == AddressFamily.InterNetwork)
                        result.Add(ip.ToString());
            }
            catch { /* best-effort only */ }
            if (result.Count == 0) result.Add("127.0.0.1");
            return result;
        }

        private void AcceptLoop()
        {
            try
            {
                while (!_disposed)
                {
                    var client = _listener.AcceptTcpClient();
                    _pendingConnections.Enqueue(client);
                }
            }
            catch { /* listener stopped/disposed — exit quietly */ }
        }

        /// <summary>Call once per frame from the main thread.</summary>
        public void Pump()
        {
            while (_pendingConnections.TryDequeue(out var client))
                HandleNewConnection(client);

            _channel?.Pump();
        }

        private void HandleNewConnection(TcpClient client)
        {
            if (_channel != null)
            {
                // Lobby's a strict 2 players — refuse politely rather than silently drop.
                var reject = new NetChannel(client);
                reject.Send(new JoinRejectedMessage { Reason = "Lobby is full." });
                reject.Dispose();
                return;
            }

            _channel = new NetChannel(client);
            _channel.OnMessage += HandleMessage;
            _channel.OnDisconnected += HandleOpponentDisconnected;
        }

        private void HandleMessage(NetMessage msg)
        {
            switch (msg)
            {
                case JoinRequestMessage join:
                    OpponentName = string.IsNullOrWhiteSpace(join.PlayerName) ? "Opponent" : join.PlayerName;
                    _channel.Send(new WelcomeMessage { YourPlayerId = 1, HostName = HostName });
                    OnOpponentJoined?.Invoke(OpponentName);
                    break;

                case SubmitCommandMessage submit:
                    // The remote peer is always player 1 in this 2-player design —
                    // ignore anything claiming to act as player 0 (the host itself).
                    if (submit.Command.PlayerId != 1) return;
                    ResolveAndBroadcast(submit.Command);
                    break;
            }
        }

        private void HandleOpponentDisconnected(string reason)
        {
            _channel = null;
            OpponentName = null;
            OnOpponentDisconnected?.Invoke(reason);
        }

        /// <summary>Host-only: begins the match once an opponent is present.</summary>
        public void StartMatch(int seed)
        {
            State = new GameState(seed);
            ResolveAndBroadcast(new StartGameCommand(seed));
        }

        /// <summary>IGameServer.Submit — the host's own local player's actions.</summary>
        public void Submit(Command cmd) => ResolveAndBroadcast(cmd);

        private void ResolveAndBroadcast(Command cmd)
        {
            List<GameEvent> events = CommandResolver.Resolve(State, cmd);
            OnEvents?.Invoke(events);
            _channel?.Send(new StateUpdateMessage { State = State, Events = events });
        }

        public void Dispose()
        {
            _disposed = true;
            try { _listener?.Stop(); } catch { /* already gone */ }
            _channel?.Dispose();
        }
    }
}
