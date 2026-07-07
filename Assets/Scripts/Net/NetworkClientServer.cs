using System;
using System.Collections.Generic;
using System.Net.Sockets;
using System.Threading;
using Game.Client;
using Game.Core.Commands;
using Game.Core.Events;
using Game.Core.State;

namespace Game.Net
{
    /// <summary>
    /// The joining side of a networked match: forwards local actions to the
    /// host and displays whatever state/events the host broadcasts. Never
    /// runs CommandResolver itself — the host is the sole authority, so the
    /// two sides can't disagree about game state, at the cost of a round trip
    /// before your own action visibly updates your board (no local prediction).
    /// </summary>
    public class NetworkClientServer : IGameServer, IDisposable
    {
        public GameState State { get; private set; }
        public event Action<List<GameEvent>> OnEvents;

        /// <summary>Main-thread: fired once the host accepts the join (before the match starts).</summary>
        public event Action<int> OnWelcomed;
        /// <summary>Main-thread: fired the first time state arrives — the match has begun.</summary>
        public event Action OnMatchStarted;
        /// <summary>Main-thread: fired if the connection fails, is rejected, or later drops.</summary>
        public event Action<string> OnDisconnected;

        public int LocalPlayerId { get; private set; } = -1;

        private readonly string _playerName;
        private readonly Thread _connectThread;
        private NetChannel _channel;
        private bool _matchStarted;
        private volatile bool _disposed;
        private volatile string _pendingDisconnectReason;

        public NetworkClientServer(string address, int port, string playerName)
        {
            _playerName = string.IsNullOrWhiteSpace(playerName) ? "Player" : playerName;
            _connectThread = new Thread(() => ConnectAndHandshake(address, port))
                { IsBackground = true, Name = "NetworkClientServer-Connect" };
            _connectThread.Start();
        }

        private void ConnectAndHandshake(string address, int port)
        {
            try
            {
                var tcp = new TcpClient();
                tcp.Connect(address, port);
                if (_disposed) { tcp.Close(); return; }

                _channel = new NetChannel(tcp);
                _channel.OnMessage += HandleMessage;
                _channel.OnDisconnected += reason => _pendingDisconnectReason = _pendingDisconnectReason ?? reason;
                _channel.Send(new JoinRequestMessage { PlayerName = _playerName });
            }
            catch (Exception e)
            {
                _pendingDisconnectReason = e.Message;
            }
        }

        /// <summary>Call once per frame from the main thread.</summary>
        public void Pump()
        {
            _channel?.Pump();

            if (_pendingDisconnectReason != null)
            {
                string reason = _pendingDisconnectReason;
                _pendingDisconnectReason = null;
                OnDisconnected?.Invoke(reason);
            }
        }

        private void HandleMessage(NetMessage msg)
        {
            switch (msg)
            {
                case WelcomeMessage welcome:
                    LocalPlayerId = welcome.YourPlayerId;
                    OnWelcomed?.Invoke(LocalPlayerId);
                    break;

                case JoinRejectedMessage rejected:
                    _pendingDisconnectReason = rejected.Reason;
                    break;

                case StateUpdateMessage update:
                    State = update.State;
                    bool firstUpdate = !_matchStarted;
                    _matchStarted = true;
                    if (firstUpdate) OnMatchStarted?.Invoke();
                    OnEvents?.Invoke(update.Events);
                    break;
            }
        }

        /// <summary>
        /// IGameServer.Submit — sends the action to the host; there is no
        /// local resolution here, so State only changes once the host's
        /// echoed StateUpdateMessage arrives (see HandleMessage above).
        /// </summary>
        public void Submit(Command cmd) => _channel?.Send(new SubmitCommandMessage { Command = cmd });

        public void Dispose()
        {
            _disposed = true;
            _channel?.Dispose();
        }
    }
}
