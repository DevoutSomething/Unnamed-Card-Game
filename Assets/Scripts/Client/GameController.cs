using System.Collections.Generic;
using UnityEngine;
using Game.Core.Commands;
using Game.Core.Events;
using Game.Core.State;

namespace Game.Client
{
    /// <summary>
    /// The Unity-side entry point. One instance, on one GameObject, in the
    /// main scene. Owns the LocalGameServer and is the ONLY class that talks
    /// to it. All UI scripts (card buttons, lane click zones, end-phase
    /// button) route their input through this class rather than holding
    /// their own server reference — one door in, one door out.
    ///
    /// DATA FLOW (the loop you'll live in from now on)
    /// -----------------------------------------------
    ///   player clicks something
    ///     -> GameController.Submit-ish method (PlaySelectedCard / EndPhase)
    ///       -> _server.Submit(command)
    ///         -> CommandResolver validates + mutates GameState + returns events
    ///       -> HandleEvents(batch) fires synchronously
    ///         -> RedrawEverything() rebuilds visuals from _server.State
    ///
    /// The controller has NO game rules in it. If you ever find yourself
    /// writing "if energy >= cost" in this file, stop — that check belongs
    /// in CommandResolver, and the UI's job is to submit the command and
    /// display the rejection if one comes back. (Later you can ALSO grey out
    /// unaffordable cards as a courtesy, but the resolver stays the referee.)
    /// </summary>
    public class GameController : MonoBehaviour
    {
        [Header("Config")]
        [SerializeField] private int seed = 42;   // fixed seed = reproducible games while testing

        // TODO(scoot): UI references get added here as you build the canvas, e.g.
        // [SerializeField] private Transform p0HandStrip;
        // [SerializeField] private Transform p1HandStrip;
        // [SerializeField] private Transform[] laneColumns;
        // [SerializeField] private CardView cardPrefab;
        // [SerializeField] private TMPro.TMP_Text statusLabel;
        // [SerializeField] private TMPro.TMP_Text p0EnergyLabel;
        // [SerializeField] private TMPro.TMP_Text p1EnergyLabel;

        private LocalGameServer _server;

        /// <summary>
        /// The card the active player has clicked in their hand and not yet
        /// placed. Purely a UI concept — the game state has no notion of
        /// "selected", which is exactly why it lives here and not in Core.
        /// </summary>
        private CardInstance _selectedCard;

        private void Start()
        {
            _server = new LocalGameServer();
            _server.OnEvents += HandleEvents;   // subscribe BEFORE StartNewGame
            _server.StartNewGame(seed);         // -> StartGame batch -> HandleEvents fires
        }

        // ---------------------------------------------------------------
        // INPUT (called by UI scripts / temporary keyboard hooks)
        // ---------------------------------------------------------------

        /// <summary>
        /// Hot-seat trick: commands are always sent as whoever the rotation
        /// says is active. That single line is what makes this two-player
        /// at one keyboard with zero extra code.
        /// </summary>
        private int ActivePlayer => _server.State.ActivePlayerId;

        /// <summary>Card button clicked. (Wire your card prefab's Button to this.)</summary>
        public void SelectCard(CardInstance card)
        {
            _selectedCard = card;
            // TODO(scoot): visual feedback — highlight the selected card button.
        }

        /// <summary>Lane clicked. Sends the play command if a card is selected.</summary>
        public void ClickLane(int laneIndex)
        {
            if (_selectedCard == null) return;

            _server.Submit(new PlayCardCommand(
                ActivePlayer,
                _selectedCard.InstanceId,
                laneIndex));            // SlotIndex omitted -> -1 -> auto-place

            _selectedCard = null;       // clear regardless; a rejection just logs
        }

        /// <summary>End Phase button clicked.</summary>
        public void EndPhase()
        {
            _selectedCard = null;
            _server.Submit(new EndPhaseCommand(ActivePlayer));
        }

        /// <summary>
        /// TEMPORARY keyboard driver so the whole pipeline is testable before
        /// any canvas exists. Delete once real UI input works.
        ///   Space  = end phase (as active player)
        ///   P      = play first affordable card in active player's hand to lane 0
        /// </summary>
        private void Update()
        {
            if (Input.GetKeyDown(KeyCode.Space))
                EndPhase();

            if (Input.GetKeyDown(KeyCode.P))
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
        /// Receives every event batch. For the prototype this does two things:
        /// surface rejections, then brute-force redraw. Per-event handling
        /// (animations, sounds) replaces the redraw later — the batch is
        /// already ordered correctly for an animation queue when that day comes.
        /// </summary>
        private void HandleEvents(List<GameEvent> events)
        {
            foreach (var e in events)
            {
                switch (e)
                {
                    case CommandRejectedEvent r:
                        Debug.LogWarning($"REJECTED: {r.Reason}");
                        // TODO(scoot): later, flash this on-screen instead.
                        break;

                    case GameStartedEvent:
                        Debug.Log("game started");
                        break;

                    // Individual events are logged for visibility while the
                    // console is your only 'screen'. They become animation
                    // triggers later; the redraw below handles correctness.
                    case SlotChangedEvent sc:
                        Debug.Log($"slot -> {sc}");
                        break;
                    case CardDrawnEvent cd:
                        Debug.Log($"draw -> {cd}");
                        break;
                    case CardPlayedEvent cp:
                        Debug.Log($"played -> {cp}");
                        break;
                }
            }

            RedrawEverything();
        }

        /// <summary>
        /// The ONLY method allowed to touch UI objects. Reads _server.State
        /// and rebuilds all visuals from scratch: destroy old card buttons,
        /// instantiate current hands and board, set labels. Wasteful and
        /// correct — desync between state and screen is impossible, which is
        /// worth far more than performance while the design is in flux.
        ///
        /// TODO(scoot): this is your UI milestone. Console version first:
        /// </summary>
        private void RedrawEverything()
        {
            var s = _server.State;

            // --- console 'rendering' (replace with canvas code) ---
            Debug.Log(
                $"[{s.CurrentSlotType} | P{s.ActivePlayerId} active | rot {s.RotationIndex} slot {s.SlotIndex}] " +
                $"P0: {s.Players[0].Health}hp {s.Players[0].CurrentEnergy}/{s.Players[0].EnergyPerTurn}e " +
                $"{s.Players[0].cardsInHand.Count}h/{s.Players[0].Deck.Count}d | " +
                $"P1: {s.Players[1].Health}hp {s.Players[1].CurrentEnergy}/{s.Players[1].EnergyPerTurn}e " +
                $"{s.Players[1].cardsInHand.Count}h/{s.Players[1].Deck.Count}d");

            // TODO(scoot): canvas version, in this order:
            // 1. statusLabel / energy labels          (just SetText from state)
            // 2. hand strips: clear children, instantiate cardPrefab per card
            //    in cardsInHand, wire each button -> SelectCard(card)
            // 3. lane grid: per lane, per sublane, per slot, show the card or empty
            // 4. win screen: if (s.IsGameOver) show which player hit 0 health

            if (s.IsGameOver)
                Debug.Log("GAME OVER");
        }
    }
}