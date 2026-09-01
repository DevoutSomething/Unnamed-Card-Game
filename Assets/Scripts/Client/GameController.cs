using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Game.Cards;
using Game.Client.View;
using Game.Core.Commands;
using Game.Core.Events;
using Game.Core.Server;
using Game.Core.State;
using Game.Net;

namespace Game.Client
{
    /// <summary>
    /// The Unity-side entry point. One instance, on one GameObject, in the main
    /// scene — it builds the whole board UI from code on Start, so an empty
    /// scene plus this component is a playable game.
    ///
    /// DATA FLOW
    /// ---------
    ///   player clicks something (hand card / lane / End Phase)
    ///     -> GameController input method
    ///       -> _server.Submit(command)
    ///         -> CommandResolver validates + mutates GameState + returns events
    ///           (locally for LocalGameServer/NetworkHostServer; on the host,
    ///           for NetworkClientServer — see Game.Net)
    ///       -> HandleEvents(batch) fires synchronously
    ///         -> BoardView.Redraw() rebuilds visuals from _server.State
    ///
    /// The controller has NO game rules in it: it submits commands and displays
    /// whatever comes back. Rejections are shown, not prevented — the resolver
    /// stays the referee.
    ///
    /// MATCH KINDS
    /// -----------
    ///   Local (hot-seat): commands are always sent as whoever the rotation
    ///   says is active, and the hand strip always shows that player's hand.
    ///   Two players, one keyboard, zero extra code.
    ///
    ///   Online (host/join): LobbyView gathers a host/join choice first; the
    ///   host is always player 0 and the joining client always player 1 (see
    ///   Game.Net.NetworkHostServer/NetworkClientServer). Each side only ever
    ///   acts as its own fixed player id and only ever sees its own hand.
    /// </summary>
    public class GameController : MonoBehaviour
    {
        [Header("Config")]
        [Tooltip("0 = random seed every match. Set non-zero to reproduce a game exactly.")]
        [SerializeField] private int seed = 0;
        [Tooltip("TCP port used for both hosting and joining.")]
        [SerializeField] private int port = 7777;

        private IGameServer _server;
        private NetworkHostServer _hostServer;
        private NetworkClientServer _clientServer;

        /// <summary>null = hot-seat (acting player follows the turn rotation);
        /// otherwise the fixed player id this client acts as (0 = host, 1 = joined).</summary>
        private int? _actingPlayerId;

        /// <summary>Hot-seat only: which player is currently browsing the shop
        /// (see ShopViewerId below). Defaults to P1; irrelevant online.</summary>
        private int _hotSeatShopperId = 0;

        private BoardView _board;
        private ShopView _shop;
        private LobbyView _lobby;
        private CardDatabase _db;
        private CardSkinLibrary _skins;

        /// <summary>
        /// Online, always the fixed assigned id. Hot-seat, normally whoever's
        /// turn it is — except State.ActivePlayerId is -1 (a system slot's
        /// "no owning player") right when a match ends: the game can only end
        /// during Combat, and CommandResolver deliberately stops advancing the
        /// instant IsGameOver becomes true, leaving SlotIndex parked on that
        /// Combat slot. Redraw() still needs a valid 0/1 to render the
        /// game-over board with (Players[-1] would throw), so this falls back
        /// to player 0 rather than propagating the sentinel into the view.
        /// </summary>
        private int ActingPlayerId
        {
            get
            {
                if (_actingPlayerId.HasValue) return _actingPlayerId.Value;
                int active = _server.State.ActivePlayerId;
                return active >= 0 ? active : 0;
            }
        }

        /// <summary>True for a hot-seat match (no fixed acting player) — ShopView
        /// uses this to decide whether to show its P1/P2 toggle at all.</summary>
        public bool IsHotSeat => !_actingPlayerId.HasValue;

        /// <summary>
        /// Who's acting in the shop right now. Online, always the fixed acting
        /// id (no ambiguity there). Hot-seat, ActingPlayerId's -1 fallback lands
        /// on player 0 during Shop (ActivePlayerId is -1 there too — it's a
        /// system slot with no single active player) — fine for the board, but
        /// the shop needs BOTH hot-seat players able to act, so it uses this
        /// locally toggled selection instead (see SetHotSeatShopper).
        /// </summary>
        public int ShopViewerId => _actingPlayerId ?? _hotSeatShopperId;

        /// <summary>Hot-seat P1/P2 toggle clicked in ShopView.</summary>
        public void SetHotSeatShopper(int playerId)
        {
            _hotSeatShopperId = playerId;
            Redraw();
        }

        private void Start()
        {
            // Bootstrap: abilities + card pool (both discovered from imported
            // assets), skins for rendering, then the pre-match menu.
            AbilityLoader.Bootstrap();
            _db = new CardDatabase();
            _skins = new CardSkinLibrary();
            CardCatalogRuntime.Configure(_db.All);
            if (_db.All.Count == 0)
                Debug.LogWarning("GameController: no cards found — run Cards > Pipeline > Import All. " +
                                 "Falling back to vanilla test decks.");

            _lobby = new LobbyView();
            _lobby.Build(this);
        }

        // ---------------------------------------------------------------
        // LOBBY (wired to LobbyView's buttons)
        // ---------------------------------------------------------------

        public void OnLocalMatchClicked()
        {
            _actingPlayerId = null;
            _server = new LocalGameServer();
            _server.OnEvents += HandleEvents;   // subscribe BEFORE StartNewGame
            BeginMatchView();
            StartLocalMatch();
        }

        public void OnHostClicked(string playerName)
        {
            TeardownNetworking();
            try
            {
                _hostServer = new NetworkHostServer(port, playerName);
            }
            catch (Exception e)
            {
                _lobby.ShowMainMenu($"Couldn't start hosting on port {port}: {e.Message}");
                return;
            }

            _hostServer.OnOpponentJoined += name => _lobby.SetHostOpponentStatus(true, name);
            _hostServer.OnOpponentDisconnected += reason =>
            {
                _lobby.SetHostOpponentStatus(false, null);
                // Mid-match this reaches an already-hidden lobby label — surface it on the board too.
                if (_actingPlayerId.HasValue) _board?.ShowMessage($"Opponent disconnected: {reason}");
            };
            _lobby.ShowHostLobby(port, NetworkHostServer.GetLocalAddresses());
        }

        public void OnJoinClicked(string playerName, string addressText)
        {
            if (!TryParseAddress(addressText, out string host, out int joinPort))
            {
                _lobby.ShowMainMenu("Enter an address like 192.168.1.12:7777");
                return;
            }

            TeardownNetworking();
            _clientServer = new NetworkClientServer(host, joinPort, playerName);
            _clientServer.OnWelcomed += _ =>
                _lobby.SetJoinStatus("Connected! Waiting for host to start the match...");
            _clientServer.OnMatchStarted += () =>
            {
                _actingPlayerId = _clientServer.LocalPlayerId;
                _server = _clientServer;
                _server.OnEvents += HandleEvents;
                BeginMatchView();
            };
            _clientServer.OnDisconnected += reason =>
            {
                TeardownNetworking();
                _lobby.ShowMainMenu($"Disconnected: {reason}");
            };

            _lobby.ShowJoinLobby($"{host}:{joinPort}");
        }

        public void OnStartMatchClicked()
        {
            if (_hostServer == null || !_hostServer.OpponentConnected) return;

            _actingPlayerId = 0;
            _server = _hostServer;
            _server.OnEvents += HandleEvents;   // subscribe BEFORE StartMatch
            BeginMatchView();

            int matchSeed = seed != 0 ? seed : new System.Random().Next(1, int.MaxValue);
            Debug.Log($"starting hosted match with seed {matchSeed}");
            _hostServer.StartMatch(matchSeed);
        }

        public void OnLobbyCancelClicked()
        {
            TeardownNetworking();
            _lobby.ShowMainMenu();
        }

        private static bool TryParseAddress(string text, out string host, out int port)
        {
            host = null;
            port = 7777;
            if (string.IsNullOrWhiteSpace(text)) return false;

            text = text.Trim();
            int splitAt = text.LastIndexOf(':');
            if (splitAt < 0)
            {
                host = text;
                return true;
            }

            host = text.Substring(0, splitAt);
            return int.TryParse(text.Substring(splitAt + 1), out port) && !string.IsNullOrEmpty(host);
        }

        private void TeardownNetworking()
        {
            _hostServer?.Dispose();
            _hostServer = null;
            _clientServer?.Dispose();
            _clientServer = null;
        }

        private void BeginMatchView()
        {
            if (_board == null)
            {
                _board = new BoardView();
                _board.Build(this, _db, _skins, laneCount: 5, slotsPerSide: 2);
            }
            if (_shop == null)
            {
                _shop = new ShopView();
                _shop.Build(this, _db, _skins);
            }
            _board.SetVisible(true);
            _lobby.Hide();
        }

        private void StartLocalMatch()
        {
            int matchSeed = seed != 0 ? seed : new System.Random().Next(1, int.MaxValue);
            Debug.Log($"starting match with seed {matchSeed}");
            ((LocalGameServer)_server).StartNewGame(matchSeed);   // -> StartGame batch -> HandleEvents -> Redraw
        }

        // ---------------------------------------------------------------
        // INPUT (wired to BoardView's drag handlers; keyboard fallback below)
        // ---------------------------------------------------------------

        /// <summary>Hand card dropped on a lane: sends the play command.
        /// slotIndex -1 (the default, used by the keyboard-fallback play) means
        /// auto-place to the closest empty slot; a dragged card instead names
        /// the exact front(0)/back(1) slot it was dropped on.</summary>
        public void PlayCard(CardInstance card, int laneIndex, int slotIndex = -1)
        {
            if (_server.State.IsGameOver) return;

            _server.Submit(new PlayCardCommand(ActingPlayerId, card.InstanceId, laneIndex, slotIndex));

            Redraw();
        }

        /// <summary>
        /// Casts a spell at whatever it was dropped on. Both target ids may be
        /// -1 (an untargeted spell like Research); the resolver is the referee
        /// on whether this spell was allowed to point at that, and its rejection
        /// is what the player sees.
        /// </summary>
        public void CastSpell(CardInstance card, int targetCardInstanceId, int targetPlayerId)
        {
            if (_server.State.IsGameOver) return;

            _board.ShowMessage("");
            _server.Submit(new PlayCardCommand(
                ActingPlayerId, card.InstanceId,
                LaneIndex: -1, SlotIndex: -1,
                TargetCardInstanceId: targetCardInstanceId,
                TargetPlayerId: targetPlayerId));

            Redraw();
        }

        /// <summary>The instance id of the guy standing in a given slot, or -1.
        /// Lets a drag translate "where the pointer is" into "which card".</summary>
        public int CardInstanceAt(int laneIndex, int slotIndex, int ownerPlayerId)
        {
            var state = _server?.State;
            if (state == null) return -1;
            if (laneIndex < 0 || laneIndex >= state.Lanes.Length) return -1;
            if (ownerPlayerId < 0 || ownerPlayerId >= state.Players.Length) return -1;

            var slots = state.Lanes[laneIndex].SublaneOf(ownerPlayerId).Slots;
            if (slotIndex < 0 || slotIndex >= slots.Length) return -1;

            return slots[slotIndex]?.InstanceId ?? -1;
        }

        /// <summary>Dragging a hand card over a lane: preview which slot it'll land in.</summary>
        public void ShowDropPreview(int laneIndex, int slotIndex, int forPlayerId) =>
            _board.ShowDropPreview(laneIndex, slotIndex, forPlayerId);

        /// <summary>Drag ended or moved off every lane: hide the preview.</summary>
        public void ClearDropPreview() => _board.ClearDropPreview();

        /// <summary>A spell drag started: outline every legal target for it.</summary>
        public void BeginSpellTargeting(CardInstance card) => _board.ShowValidSpellTargets(card);

        /// <summary>The drag settled (played, cancelled, or dropped nowhere).</summary>
        public void EndSpellTargeting() => _board.ClearSpellTargets();

        /// <summary>A hand-reorder drag settled without playing the card: persist its new position.</summary>
        public void CommitHandOrder() => _board.CommitHandOrder();

        /// <summary>True for the duration of a hand-reorder drag, so a redraw
        /// triggered by something else mid-drag (online, the opponent's
        /// rejected action still broadcasts a state update) doesn't rebuild
        /// the hand strip out from under the card Unity is still dragging.</summary>
        public void SetHandDragInProgress(bool inProgress) => _board.SetHandDragInProgress(inProgress);

        /// <summary>End Phase button clicked.</summary>
        public void EndPhase()
        {
            if (_server.State.IsGameOver) return;
            _board.ShowMessage("");
            _server.Submit(new EndPhaseCommand(ActingPlayerId));
        }

        // ---------------------------------------------------------------
        // SHOP (wired to ShopView's buttons)
        // ---------------------------------------------------------------

        /// <summary>Shop offer card clicked: buy it into ShopViewerId's deck.</summary>
        public void BuyCard(CardInstance offer)
        {
            if (_server.State.IsGameOver) return;
            _server.Submit(new BuyCardCommand(ShopViewerId, offer.InstanceId));
            Redraw();
        }

        /// <summary>Deck card's Remove button clicked: pay this visit's scaling cost.</summary>
        public void RemoveCardFromDeck(CardInstance card)
        {
            if (_server.State.IsGameOver) return;
            _server.Submit(new RemoveCardFromDeckCommand(ShopViewerId, card.InstanceId));
            Redraw();
        }

        /// <summary>"New Deck" button clicked: the once-per-visit free full reroll.</summary>
        public void RerollDeck()
        {
            if (_server.State.IsGameOver) return;
            _server.Submit(new RerollDeckCommand(ShopViewerId));
            Redraw();
        }

        /// <summary>"Done Shopping" button clicked.</summary>
        public void EndShop()
        {
            if (_server.State.IsGameOver) return;
            _server.Submit(new EndShopCommand(ShopViewerId));
            Redraw();
        }

        /// <summary>
        /// The game-over overlay's single button, local or online alike:
        /// tears down any network connection, drops the finished match
        /// entirely, and backs out to the main menu. No in-place rematch —
        /// starting another match (local, host, or join) always goes back
        /// through the menu, so there's never a stale/diverged match left
        /// sitting on screen.
        /// </summary>
        public void ReturnToMainMenu()
        {
            TeardownNetworking();
            _server = null;
            _actingPlayerId = null;
            _board.SetVisible(false);
            _shop.SetVisible(false);
            _lobby.ShowMainMenu();
        }

        /// <summary>
        /// Keyboard fallback (handy while testing):
        ///   Space  = end phase (as the acting player)
        ///   P      = play first affordable card in the acting player's hand to lane 0
        /// Compiled against whichever input backend the project has active.
        /// </summary>
        private void Update()
        {
            _hostServer?.Pump();
            _clientServer?.Pump();

            if (_server == null || _server.State == null || _server.State.IsGameOver) return;

            if (_server.State.CurrentSlotType == SlotType.Shop)
            {
                _shop.TickCountdown(_server.State);
                var deadline = _server.State.ShopDeadlineUtc;
                if (deadline.HasValue && DateTime.UtcNow >= deadline.Value)
                    _server.Submit(new ForceEndShopCommand());
            }

#if ENABLE_INPUT_SYSTEM
            var keyboard = UnityEngine.InputSystem.Keyboard.current;
            if (keyboard == null) return;
            bool endPhasePressed = keyboard.spaceKey.wasPressedThisFrame;
            bool playFirstPressed = keyboard.pKey.wasPressedThisFrame;
#else
            bool endPhasePressed = Input.GetKeyDown(KeyCode.Space);
            bool playFirstPressed = Input.GetKeyDown(KeyCode.P);
#endif

            if (endPhasePressed)
                EndPhase();

            if (playFirstPressed)
            {
                var player = _server.State.Players[ActingPlayerId];
                bool mainSlot = _server.State.IsMainActionSlot;

                // Main slots take guys, spell turns take spells — searching for
                // the wrong kind would only ever earn a rejection.
                var card = player.cardsInHand.Find(c =>
                    c.CurrentCost <= player.CurrentEnergy &&
                    (_db?.Get(c.DefinitionId) is SpellCardDefinition) != mainSlot);

                if (card == null)
                {
                    Debug.Log(mainSlot ? "no affordable guy in hand" : "no affordable spell in hand");
                }
                else if (mainSlot)
                {
                    PlayCard(card, 0);
                }
                else if (_db?.Get(card.DefinitionId) is SpellCardDefinition spell && spell.NeedsTarget)
                {
                    Debug.Log($"'{spell.DisplayName}' needs a target — drag it onto a guy or a hero.");
                }
                else
                {
                    CastSpell(card, targetCardInstanceId: -1, targetPlayerId: -1);
                }
            }
        }

        private void OnDestroy()
        {
            TeardownNetworking();
        }

        // ---------------------------------------------------------------
        // OUTPUT (reacting to the game)
        // ---------------------------------------------------------------

        /// <summary>
        /// Receives every event batch: surface rejections, then brute-force
        /// redraw. Per-event handling (animations, sounds) replaces the redraw
        /// later — the batch is already ordered correctly for that day.
        /// </summary>
        private void HandleEvents(List<GameEvent> events)
        {
            foreach (var e in events)
            {
                switch (e)
                {
                    case CommandRejectedEvent r:
                        _board.ShowMessage(r.Reason);
                        Debug.LogWarning($"REJECTED: {r.Reason}");
                        break;
                    case GameEndedEvent g:
                        Debug.Log($"game over, winner: {g.WinnerId}");
                        break;
                }
            }

            // Combat resolves instantly inside an End Phase batch — without this
            // summary the only trace is numbers quietly changing.
            if (events.OfType<SlotChangedEvent>().Any(s => s.SlotType == nameof(SlotType.Combat)))
            {
                int deaths = events.OfType<CardDiedEvent>().Count();
                int p0Hit = events.OfType<PlayerDamagedEvent>().Where(p => p.PlayerId == 0).Sum(p => p.Amount);
                int p1Hit = events.OfType<PlayerDamagedEvent>().Where(p => p.PlayerId == 1).Sum(p => p.Amount);
                _board.ShowMessage(
                    $"COMBAT!  {deaths} guy(s) died  |  P0 hero took {p0Hit}  |  P1 hero took {p1Hit}");
            }

            Redraw();
        }

        private void Redraw()
        {
            _board.Redraw(_server.State, ActingPlayerId);

            bool inShop = _server.State.CurrentSlotType == SlotType.Shop;
            _shop.SetVisible(inShop);
            if (inShop) _shop.Redraw(_server.State, ShopViewerId);
        }
    }
}
