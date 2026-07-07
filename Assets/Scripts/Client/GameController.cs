using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Game.Cards;
using Game.Client.View;
using Game.Core.Commands;
using Game.Core.Events;
using Game.Core.Server;
using Game.Core.State;

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
    ///       -> HandleEvents(batch) fires synchronously
    ///         -> BoardView.Redraw() rebuilds visuals from _server.State
    ///
    /// The controller has NO game rules in it: it submits commands and displays
    /// whatever comes back. Rejections are shown, not prevented — the resolver
    /// stays the referee.
    ///
    /// HOT-SEAT: commands are always sent as whoever the rotation says is
    /// active, and the hand strip always shows that player's hand. Two players,
    /// one keyboard, zero extra code.
    /// </summary>
    public class GameController : MonoBehaviour
    {
        [Header("Config")]
        [Tooltip("0 = random seed every match. Set non-zero to reproduce a game exactly.")]
        [SerializeField] private int seed = 0;

        private LocalGameServer _server;
        private BoardView _board;
        private CardDatabase _db;
        private CardSkinLibrary _skins;

        /// <summary>
        /// The card the active player has clicked in their hand and not yet
        /// placed. Purely a UI concept, which is why it lives here and not in Core.
        /// </summary>
        private CardInstance _selectedCard;

        private int ActivePlayer => _server.State.ActivePlayerId;

        private void Start()
        {
            // Bootstrap: abilities + card pool (both discovered from imported
            // assets), skins for rendering, then the board and the match itself.
            AbilityLoader.Bootstrap();
            _db = new CardDatabase();
            _skins = new CardSkinLibrary();
            CardCatalogRuntime.Configure(_db.All);
            if (_db.All.Count == 0)
                Debug.LogWarning("GameController: no cards found — run Cards > Pipeline > Import All. " +
                                 "Falling back to vanilla test decks.");

            _board = new BoardView();
            _board.Build(this, _db, _skins, laneCount: 5, slotsPerSide: 2);

            _server = new LocalGameServer();
            _server.OnEvents += HandleEvents;   // subscribe BEFORE StartNewGame
            StartMatch();
        }

        private void StartMatch()
        {
            _selectedCard = null;
            int matchSeed = seed != 0 ? seed : new System.Random().Next(1, int.MaxValue);
            Debug.Log($"starting match with seed {matchSeed}");
            _server.StartNewGame(matchSeed);    // -> StartGame batch -> HandleEvents -> Redraw
        }

        // ---------------------------------------------------------------
        // INPUT (wired to BoardView's buttons; keyboard fallback below)
        // ---------------------------------------------------------------

        /// <summary>Hand card clicked: select it (click again to deselect).</summary>
        public void SelectCard(CardInstance card)
        {
            _selectedCard = _selectedCard == card ? null : card;
            _board.ShowMessage(_selectedCard != null
                ? $"Click a lane to play it (cost {_selectedCard.CurrentCost})."
                : "");
            Redraw();
        }

        /// <summary>Lane clicked. Sends the play command if a card is selected.</summary>
        public void ClickLane(int laneIndex)
        {
            if (_server.State.IsGameOver) return;
            if (_selectedCard == null)
            {
                _board.ShowMessage("Select a card from your hand first.");
                return;
            }

            _server.Submit(new PlayCardCommand(
                ActivePlayer,
                _selectedCard.InstanceId,
                laneIndex));            // SlotIndex omitted -> -1 -> auto-place

            _selectedCard = null;       // clear regardless; a rejection just shows
            Redraw();
        }

        /// <summary>End Phase button clicked.</summary>
        public void EndPhase()
        {
            if (_server.State.IsGameOver) return;
            _selectedCard = null;
            _board.ShowMessage("");
            _server.Submit(new EndPhaseCommand(ActivePlayer));
        }

        /// <summary>Play Again button on the game-over overlay.</summary>
        public void Restart() => StartMatch();

        /// <summary>
        /// Keyboard fallback (handy while testing):
        ///   Space  = end phase (as active player)
        ///   P      = play first affordable card in active player's hand to lane 0
        /// Compiled against whichever input backend the project has active.
        /// </summary>
        private void Update()
        {
            if (_server == null || _server.State == null || _server.State.IsGameOver) return;

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
                var player = _server.State.Players[ActivePlayer];
                var card = player.cardsInHand.Find(c => c.CurrentCost <= player.CurrentEnergy);
                if (card != null)
                {
                    _selectedCard = card;
                    ClickLane(0);
                }
                else Debug.Log("no affordable card in hand");
            }
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

        private void Redraw() => _board.Redraw(_server.State, _selectedCard);
    }
}
