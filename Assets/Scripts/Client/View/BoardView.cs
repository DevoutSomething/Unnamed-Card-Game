using System.Collections.Generic;
using Game.Cards;
using Game.Core.State;
using UnityEngine;
using UnityEngine.UI;

namespace Game.Client.View
{
    /// <summary>
    /// The whole match screen, built from code so no scene/prefab hand-wiring is
    /// needed: top stat bar, phase bar, 5 lane columns, the viewer's hand strip,
    /// an End Phase button, and a game-over overlay.
    ///
    /// Viewer-relative, not absolute-player-relative: whichever player is
    /// looking at this particular BoardView (viewerPlayerId, passed into every
    /// Redraw) always sees their own side near the bottom — closest to their
    /// hand, like they're "P1" — and the opponent's side far/top, regardless
    /// of which of the two actual player ids they are. Two online clients each
    /// get their own BoardView instance, so this is what makes both players
    /// see a normal, non-mirrored-feeling board instead of one of them staring
    /// at their own side upside-down at the top. In hot-seat, "you" is always
    /// whoever's turn it currently is, so the banner only ever says YOUR TURN.
    ///
    /// Pure view: it renders GameState and forwards clicks to GameController.
    /// Redraw() rebuilds the dynamic parts from scratch every event batch —
    /// wasteful and desync-proof, per the prototype philosophy.
    /// </summary>
    public class BoardView
    {
        const float CardW = 240f, CardH = 336f;      // CardView prefab native size
        const float SlotScale = 0.5f;                 // board cards: 120x168
        const float HandScale = 0.7f;                 // hand cards: 168x235

        static readonly Color PanelColor = new Color(0f, 0f, 0f, 0.35f);
        static readonly Color ActiveColor = new Color(1f, 0.85f, 0.3f);

        // Side highlighting: cool/blue = your side, warm/red = opponent's side,
        // brighter whichever side is actually mid-turn right now.
        static readonly Color YourSideColor = new Color(0.25f, 0.55f, 0.95f, 0.18f);
        static readonly Color YourSideActiveColor = new Color(0.35f, 0.7f, 1f, 0.40f);
        static readonly Color OpponentSideColor = new Color(0.85f, 0.35f, 0.30f, 0.18f);
        static readonly Color OpponentSideActiveColor = new Color(0.95f, 0.4f, 0.35f, 0.40f);

        // Phase bar background: green whenever it's your turn (main or spell),
        // grey whenever it's the opponent's.
        static readonly Color PhaseYourTurnColor = new Color(0.20f, 0.6f, 0.32f, 0.95f);
        static readonly Color PhaseOpponentTurnColor = new Color(0.45f, 0.45f, 0.47f, 0.95f);
        static readonly Color PhaseNeutralColor = new Color(0.5f, 0.42f, 0.18f, 0.95f);

        // Drop preview: the one slot a dragged hand card would land in if released now.
        static readonly Color DropPreviewColor = new Color(1f, 0.95f, 0.4f, 0.55f);

        GameController _controller;
        CardDatabase _db;
        CardSkinLibrary _skins;

        Image _phaseBarImage;
        Text _phaseLabel;
        Text _messageLabel;
        readonly Text[] _playerLabels = new Text[2];   // [0] = you, [1] = opponent
        Button _endPhaseButton;

        // [lane, screenRow, slot] -> the slot's own rect (background tint lives
        // here) and its two children: _cardHolders is what RedrawSublane
        // actually clears/spawns the CardView into (so that doesn't clobber
        // the drop-preview overlay, a sibling rather than something nested
        // under it). screenRow 0 = near/bottom (the viewer's own side), 1 =
        // far/top (opponent's) — which absolute player fills which row is
        // decided fresh every Redraw.
        RectTransform[,,] _slotContainers;
        RectTransform[,,] _cardHolders;
        Image[,,] _dropPreviewOverlays;
        Image _activeDropPreview;
        GameState _lastState;
        int _lastViewerPlayerId;
        RectTransform _handRoot;
        readonly List<int> _handOrder = new List<int>();   // CardInstance ids, the player's own drag-reorder preference
        bool _handDragInProgress;
        GameObject _gameOverOverlay;
        Text _gameOverLabel;
        GameObject _canvasRoot;

        // ------------------------------------------------------------------
        // Build (once)
        // ------------------------------------------------------------------

        public void Build(GameController controller, CardDatabase db, CardSkinLibrary skins,
                          int laneCount, int slotsPerSide)
        {
            _controller = controller;
            _db = db;
            _skins = skins;
            _slotContainers = new RectTransform[laneCount, 2, slotsPerSide];
            _cardHolders = new RectTransform[laneCount, 2, slotsPerSide];
            _dropPreviewOverlays = new Image[laneCount, 2, slotsPerSide];

            var canvasGo = new GameObject("BoardCanvas",
                typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            _canvasRoot = canvasGo;
            var canvas = canvasGo.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            var scaler = canvasGo.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);
            scaler.matchWidthOrHeight = 0.5f;

            EnsureEventSystem();

            var bg = AddRect(canvasGo.transform, "Background", Vector2.zero, Vector2.one);
            bg.gameObject.AddComponent<Image>().color = new Color(0.09f, 0.11f, 0.13f);

            BuildPhaseBar(canvasGo.transform);
            BuildTopBar(canvasGo.transform);
            BuildLanes(canvasGo.transform, laneCount, slotsPerSide);
            BuildHandStrip(canvasGo.transform);
            BuildEndPhaseButton(canvasGo.transform);
            BuildGameOverOverlay(canvasGo.transform);   // last = drawn on top
        }

        static void EnsureEventSystem()
        {
            if (Object.FindAnyObjectByType<UnityEngine.EventSystems.EventSystem>() != null) return;
#if ENABLE_INPUT_SYSTEM
            // Project uses the new Input System: the legacy StandaloneInputModule
            // would refuse to run (and buttons would ignore every click).
            new GameObject("EventSystem",
                typeof(UnityEngine.EventSystems.EventSystem),
                typeof(UnityEngine.InputSystem.UI.InputSystemUIInputModule));
#else
            new GameObject("EventSystem",
                typeof(UnityEngine.EventSystems.EventSystem),
                typeof(UnityEngine.EventSystems.StandaloneInputModule));
#endif
        }

        void BuildTopBar(Transform canvas)
        {
            var bar = AddRect(canvas, "TopBar", new Vector2(0, 1), new Vector2(1, 1));
            bar.pivot = new Vector2(0.5f, 1);
            bar.anchoredPosition = new Vector2(0, -40);   // sits directly under the 40px PhaseBar
            bar.sizeDelta = new Vector2(0, 64);
            bar.gameObject.AddComponent<Image>().color = PanelColor;

            _playerLabels[0] = AddLabel(bar, "YouStats", new Vector2(0, 0), new Vector2(0.5f, 1),
                                        20, TextAnchor.MiddleLeft);
            ((RectTransform)_playerLabels[0].transform).offsetMin = new Vector2(20, 0);

            _playerLabels[1] = AddLabel(bar, "OpponentStats", new Vector2(0.5f, 0), new Vector2(1, 1),
                                        20, TextAnchor.MiddleRight);
            ((RectTransform)_playerLabels[1].transform).offsetMax = new Vector2(-20, 0);

            _messageLabel = AddLabel(canvas, "Message", new Vector2(0.2f, 1), new Vector2(0.8f, 1),
                                     18, TextAnchor.MiddleCenter);
            var msgRect = (RectTransform)_messageLabel.transform;
            msgRect.pivot = new Vector2(0.5f, 1);
            msgRect.anchoredPosition = new Vector2(0, -112);
            msgRect.sizeDelta = new Vector2(0, 32);
            _messageLabel.color = new Color(1f, 0.55f, 0.5f);
        }

        /// <summary>A dedicated, color-coded banner flush against the very top
        /// of the screen (above the stat bar): whose turn it is, from this
        /// viewer's own perspective — the clearest single thing a networked
        /// player needs at a glance.</summary>
        void BuildPhaseBar(Transform canvas)
        {
            var bar = AddRect(canvas, "PhaseBar", new Vector2(0, 1), new Vector2(1, 1));
            bar.pivot = new Vector2(0.5f, 1);
            bar.sizeDelta = new Vector2(0, 40);
            _phaseBarImage = bar.gameObject.AddComponent<Image>();

            _phaseLabel = AddLabel(bar, "PhaseText", Vector2.zero, Vector2.one, 22, TextAnchor.MiddleCenter);
            _phaseLabel.fontStyle = FontStyle.Bold;
            _phaseLabel.color = Color.black;
        }

        void BuildLanes(Transform canvas, int laneCount, int slotsPerSide)
        {
            float slotW = CardW * SlotScale, slotH = CardH * SlotScale;   // 120 x 168
            float colW = slotW + 30f;
            float colH = slotsPerSide * 2 * slotH + 8f + 6f * (slotsPerSide * 2) + 12f;

            var lanesRoot = AddRect(canvas, "Lanes", new Vector2(0.5f, 0.58f), new Vector2(0.5f, 0.58f));
            lanesRoot.sizeDelta = new Vector2(laneCount * colW + (laneCount - 1) * 18f, colH);

            var row = lanesRoot.gameObject.AddComponent<HorizontalLayoutGroup>();
            row.spacing = 18f;
            row.childAlignment = TextAnchor.MiddleCenter;
            row.childControlWidth = false;
            row.childControlHeight = false;
            row.childForceExpandWidth = false;
            row.childForceExpandHeight = false;

            for (int lane = 0; lane < laneCount; lane++)
            {
                var col = new GameObject($"Lane{lane}", typeof(RectTransform), typeof(Image), typeof(LaneDropTarget));
                var colRect = (RectTransform)col.transform;
                colRect.SetParent(lanesRoot, false);
                colRect.sizeDelta = new Vector2(colW, colH);
                col.GetComponent<Image>().color = PanelColor;
                col.GetComponent<LaneDropTarget>().LaneIndex = lane;

                var stack = col.AddComponent<VerticalLayoutGroup>();
                stack.padding = new RectOffset(0, 0, 6, 6);
                stack.spacing = 6f;
                stack.childAlignment = TextAnchor.MiddleCenter;
                stack.childControlWidth = false;
                stack.childControlHeight = false;
                stack.childForceExpandWidth = false;
                stack.childForceExpandHeight = false;

                // Far row (top of screen, opponent's side once Redraw assigns
                // it): back slot first so its front slot faces the middle.
                for (int slot = slotsPerSide - 1; slot >= 0; slot--)
                    _slotContainers[lane, 1, slot] = AddSlot(colRect, $"Far_Slot{slot}", slotW, slotH, lane, slot,
                        out _cardHolders[lane, 1, slot], out _dropPreviewOverlays[lane, 1, slot]);

                var divider = AddRect(colRect, "Divider", Vector2.zero, Vector2.zero);
                divider.sizeDelta = new Vector2(colW - 16f, 8f);
                divider.gameObject.AddComponent<Image>().color = new Color(1f, 1f, 1f, 0.25f);
                divider.gameObject.AddComponent<LayoutElement>().preferredHeight = 8f;

                // Near row (bottom of screen, the viewer's own side): front
                // slot faces the middle.
                for (int slot = 0; slot < slotsPerSide; slot++)
                    _slotContainers[lane, 0, slot] = AddSlot(colRect, $"Near_Slot{slot}", slotW, slotH, lane, slot,
                        out _cardHolders[lane, 0, slot], out _dropPreviewOverlays[lane, 0, slot]);
            }
        }

        /// <summary>
        /// A slot is three layers: the slot rect itself (background tint +
        /// SlotDropTarget for precise front/back drop targeting), a
        /// "CardHolder" child that RedrawSublane clears and spawns the
        /// CardView into, and a "DropPreview" overlay sibling toggled during
        /// drag. CardHolder exists so clearing/rebuilding it each redraw never
        /// touches the overlay next to it.
        /// </summary>
        RectTransform AddSlot(Transform parent, string name, float w, float h, int laneIndex, int slotIndex,
                              out RectTransform cardHolder, out Image dropPreviewOverlay)
        {
            var slot = AddRect(parent, name, Vector2.zero, Vector2.zero);
            slot.sizeDelta = new Vector2(w, h);
            var img = slot.gameObject.AddComponent<Image>();
            img.raycastTarget = true;   // the precise drop target itself now — see SlotDropTarget
            var le = slot.gameObject.AddComponent<LayoutElement>();
            le.preferredWidth = w;
            le.preferredHeight = h;

            var dropTarget = slot.gameObject.AddComponent<SlotDropTarget>();
            dropTarget.LaneIndex = laneIndex;
            dropTarget.SlotIndex = slotIndex;

            cardHolder = AddRect(slot, "CardHolder", Vector2.zero, Vector2.one);

            var overlayRect = AddRect(slot, "DropPreview", Vector2.zero, Vector2.one);
            dropPreviewOverlay = overlayRect.gameObject.AddComponent<Image>();
            dropPreviewOverlay.color = DropPreviewColor;
            dropPreviewOverlay.raycastTarget = false;
            overlayRect.gameObject.SetActive(false);

            return slot;
        }

        void BuildHandStrip(Transform canvas)
        {
            _handRoot = AddRect(canvas, "Hand", new Vector2(0.5f, 0), new Vector2(0.5f, 0));
            _handRoot.pivot = new Vector2(0.5f, 0);
            _handRoot.anchoredPosition = new Vector2(0, 10);
            _handRoot.sizeDelta = new Vector2(1500, CardH * HandScale + 14);

            var row = _handRoot.gameObject.AddComponent<HorizontalLayoutGroup>();
            row.spacing = 10f;
            row.childAlignment = TextAnchor.LowerCenter;
            row.childControlWidth = false;
            row.childControlHeight = false;
            row.childForceExpandWidth = false;
            row.childForceExpandHeight = false;
        }

        void BuildEndPhaseButton(Transform canvas)
        {
            var rect = AddRect(canvas, "EndPhase", new Vector2(1, 0), new Vector2(1, 0));
            rect.pivot = new Vector2(1, 0);
            rect.anchoredPosition = new Vector2(-24, 24);
            rect.sizeDelta = new Vector2(190, 64);

            var image = rect.gameObject.AddComponent<Image>();
            image.color = new Color(0.25f, 0.45f, 0.75f);
            _endPhaseButton = rect.gameObject.AddComponent<Button>();
            _endPhaseButton.onClick.AddListener(() => _controller.EndPhase());

            var label = AddLabel(rect, "Label", Vector2.zero, Vector2.one, 24, TextAnchor.MiddleCenter);
            label.text = "End Phase";
            label.fontStyle = FontStyle.Bold;
        }

        void BuildGameOverOverlay(Transform canvas)
        {
            _gameOverOverlay = new GameObject("GameOver", typeof(RectTransform), typeof(Image));
            var rect = (RectTransform)_gameOverOverlay.transform;
            rect.SetParent(canvas, false);
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
            _gameOverOverlay.GetComponent<Image>().color = new Color(0, 0, 0, 0.78f);  // blocks clicks

            _gameOverLabel = AddLabel(rect, "Result", new Vector2(0, 0.5f), new Vector2(1, 0.75f),
                                      64, TextAnchor.MiddleCenter);
            _gameOverLabel.fontStyle = FontStyle.Bold;

            // The one and only option here: back to the main menu. No in-place
            // rematch, so there's nothing else this overlay needs to offer.
            var btnRect = AddRect(rect, "MainMenuButton", new Vector2(0.5f, 0.38f), new Vector2(0.5f, 0.38f));
            btnRect.sizeDelta = new Vector2(280, 72);
            btnRect.gameObject.AddComponent<Image>().color = new Color(0.25f, 0.6f, 0.3f);
            btnRect.gameObject.AddComponent<Button>().onClick.AddListener(() => _controller.ReturnToMainMenu());
            var btnLabel = AddLabel(btnRect, "Label", Vector2.zero, Vector2.one, 26, TextAnchor.MiddleCenter);
            btnLabel.text = "Main Menu";
            btnLabel.fontStyle = FontStyle.Bold;

            _gameOverOverlay.SetActive(false);
        }

        /// <summary>Shows/hides the whole board canvas — used when backing out
        /// of an online match to the main menu, where the board would
        /// otherwise still be sitting behind the lobby screen.</summary>
        public void SetVisible(bool visible) => _canvasRoot.SetActive(visible);

        // ------------------------------------------------------------------
        // Redraw (every event batch)
        // ------------------------------------------------------------------

        /// <param name="viewerPlayerId">Whose eyes this BoardView is rendering
        /// for: the local hot-seat active player (so "you" always means
        /// whoever's turn it is — the banner will say YOUR TURN for them, never
        /// OPPONENT'S TURN, since hot-seat has no separate fixed viewer), or
        /// (online) this client's fixed player id. Drives which hand is shown,
        /// which side of the board is "near" (yours) vs "far" (opponent's),
        /// the you/opponent labeling, the phase banner, and the End Phase
        /// button's enabled state.</param>
        public void Redraw(GameState state, int viewerPlayerId)
        {
            _lastState = state;
            _lastViewerPlayerId = viewerPlayerId;
            ClearDropPreview();   // slots are about to be rebuilt/retinted from scratch

            int opponentId = 1 - viewerPlayerId;
            bool viewerActive = state.CurrentSlotType == SlotType.Action && state.ActivePlayerId == viewerPlayerId;
            bool opponentActive = state.CurrentSlotType == SlotType.Action && state.ActivePlayerId == opponentId;

            Color yourTint = viewerActive ? YourSideActiveColor : YourSideColor;
            Color opponentTint = opponentActive ? OpponentSideActiveColor : OpponentSideColor;

            for (int lane = 0; lane < state.Lanes.Length; lane++)
            {
                RedrawSublane(state.Lanes[lane].SublaneOf(viewerPlayerId), lane, screenRow: 0, yourTint);
                RedrawSublane(state.Lanes[lane].SublaneOf(opponentId), lane, screenRow: 1, opponentTint);
            }

            RedrawHand(state, viewerPlayerId);
            RedrawLabels(state, viewerPlayerId, viewerActive, opponentActive);
            RedrawGameOver(state, viewerPlayerId);

            _endPhaseButton.interactable = viewerActive;
        }

        /// <summary>
        /// Called every frame a hand card is dragged over a lane, previewing
        /// exactly where PlayCard(forPlayerId's card, laneIndex, slotIndex)
        /// would land: requestedSlot's own front(0)/back(1) slot if it's
        /// empty, or (requestedSlot -1, meaning "somewhere in this lane, not a
        /// specific tile") the closest empty one — the same resolution
        /// Sublane.ResolveSlot/CardZones.TryPlaceInLane use server-side, so
        /// the preview can never show something the drop won't actually do.
        /// Always forPlayerId's own row, never the opponent's, regardless of
        /// whether the pointer is over the near (yours) or far (opponent's)
        /// half of the lane column, since that's the only place it could land.
        /// </summary>
        public void ShowDropPreview(int laneIndex, int requestedSlot, int forPlayerId)
        {
            ClearDropPreview();
            if (_lastState == null || laneIndex < 0 || laneIndex >= _lastState.Lanes.Length) return;

            var sublane = _lastState.Lanes[laneIndex].SublaneOf(forPlayerId);
            int slot = sublane.ResolveSlot(requestedSlot);
            if (slot < 0) return;   // requested slot taken, or the lane's full — nowhere to preview

            int screenRow = forPlayerId == _lastViewerPlayerId ? 0 : 1;
            var overlay = _dropPreviewOverlays[laneIndex, screenRow, slot];
            if (overlay == null) return;

            overlay.gameObject.SetActive(true);
            _activeDropPreview = overlay;
        }

        public void ClearDropPreview()
        {
            if (_activeDropPreview != null) _activeDropPreview.gameObject.SetActive(false);
            _activeDropPreview = null;
        }

        void RedrawSublane(Sublane sublane, int lane, int screenRow, Color tint)
        {
            for (int slot = 0; slot < sublane.Slots.Length; slot++)
            {
                var background = _slotContainers[lane, screenRow, slot].GetComponent<Image>();
                if (background != null) background.color = tint;

                var cardHolder = _cardHolders[lane, screenRow, slot];
                ClearChildren(cardHolder);

                var card = sublane.Slots[slot];
                if (card == null) continue;

                var view = CardViewFactory.Spawn(card, cardHolder, _db, _skins);
                if (view == null) continue;
                PlaceCardView(view, SlotScale);
            }
        }

        void RedrawHand(GameState state, int viewerPlayerId)
        {
            // A hand-reorder drag keeps its wrapper parented under _handRoot
            // for the whole gesture (see DraggableHandCard) — rebuilding out
            // from under it would destroy the very object Unity's EventSystem
            // is still calling OnDrag/OnEndDrag on. Online, this can genuinely
            // happen: the opponent's rejected action still broadcasts a state
            // update to you. Skip the rebuild until the drag settles; nothing
            // about your own hand can legitimately change while you're the one
            // holding the mouse down on it.
            if (_handDragInProgress) return;

            ClearChildren(_handRoot);
            var player = state.Players[viewerPlayerId];

            foreach (var card in OrderedHand(player.cardsInHand))
            {
                var wrapper = new GameObject("HandCard",
                    typeof(RectTransform), typeof(Image), typeof(DraggableHandCard), typeof(LayoutElement));
                var rect = (RectTransform)wrapper.transform;
                rect.SetParent(_handRoot, false);
                float w = CardW * HandScale + 8, h = CardH * HandScale + 8;
                rect.sizeDelta = new Vector2(w, h);
                var le = wrapper.GetComponent<LayoutElement>();
                le.preferredWidth = w;
                le.preferredHeight = h;

                wrapper.GetComponent<Image>().color = new Color(0, 0, 0, 0.25f);

                var drag = wrapper.GetComponent<DraggableHandCard>();
                drag.Card = card;
                drag.Controller = _controller;
                drag.HandRoot = _handRoot;

                var view = CardViewFactory.Spawn(card, rect, _db, _skins);
                if (view != null) PlaceCardView(view, HandScale);
            }
        }

        /// <summary>
        /// RedrawHand rebuilds the whole strip from scratch every event batch,
        /// so a purely visual drag-reorder would snap back on the very next
        /// redraw. _handOrder is what actually survives: the player's own
        /// left-to-right preference (by instance id), reconciled against the
        /// server's real hand contents — drop ids that got played, append ids
        /// that just got drawn (which naturally land at the end, undisturbed).
        /// </summary>
        List<CardInstance> OrderedHand(List<CardInstance> hand)
        {
            _handOrder.RemoveAll(id => !hand.Exists(c => c.InstanceId == id));
            foreach (var card in hand)
                if (!_handOrder.Contains(card.InstanceId))
                    _handOrder.Add(card.InstanceId);

            var ordered = new List<CardInstance>(_handOrder.Count);
            foreach (var id in _handOrder)
                ordered.Add(hand.Find(c => c.InstanceId == id));
            return ordered;
        }

        /// <summary>Called once a hand-reorder drag settles (see
        /// DraggableHandCard): reads the hand strip's current child order —
        /// already live-updated during the drag — back into _handOrder so it
        /// survives the next RedrawHand.</summary>
        public void CommitHandOrder()
        {
            _handOrder.Clear();
            for (int i = 0; i < _handRoot.childCount; i++)
            {
                var draggable = _handRoot.GetChild(i).GetComponent<DraggableHandCard>();
                if (draggable != null) _handOrder.Add(draggable.Card.InstanceId);
            }
        }

        public void SetHandDragInProgress(bool inProgress) => _handDragInProgress = inProgress;

        /// <summary>Center a spawned CardView in its container at the given scale.</summary>
        static void PlaceCardView(CardView view, float scale)
        {
            var rect = (RectTransform)view.transform;
            rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = Vector2.zero;
            rect.sizeDelta = new Vector2(CardW, CardH);
            rect.localScale = new Vector3(scale, scale, 1f);

            // Clicks must reach the wrapper button (hand) or lane button (board).
            var rootImage = view.GetComponent<Image>();
            if (rootImage != null) rootImage.raycastTarget = false;
        }

        void RedrawLabels(GameState state, int viewerPlayerId, bool viewerActive, bool opponentActive)
        {
            int opponentId = 1 - viewerPlayerId;

            _playerLabels[0].text = FormatPlayerStats("YOU", viewerPlayerId, state.Players[viewerPlayerId]);
            _playerLabels[0].color = viewerActive ? ActiveColor : Color.white;

            _playerLabels[1].text = FormatPlayerStats("OPPONENT", opponentId, state.Players[opponentId]);
            _playerLabels[1].color = opponentActive ? ActiveColor : Color.white;

            // Same ordering as the rotation table always had — this only changes
            // the words: "spell turn" is a player's bonus, guy-free action slot
            // (GameState.IsMainActionSlot false), same green/grey as their main turn.
            if (viewerActive)
            {
                _phaseLabel.text = state.IsMainActionSlot ? "YOUR TURN" : "YOUR SPELL TURN";
                _phaseBarImage.color = PhaseYourTurnColor;
            }
            else if (opponentActive)
            {
                _phaseLabel.text = state.IsMainActionSlot ? "OPPONENT'S TURN" : "OPPONENT'S SPELL TURN";
                _phaseBarImage.color = PhaseOpponentTurnColor;
            }
            else
            {
                _phaseLabel.text = $"{state.CurrentSlotType}...";
                _phaseBarImage.color = PhaseNeutralColor;
            }
        }

        /// <param name="playerId">Absolute 0/1 player id, shown 1-indexed (P1/P2) — plainer for
        /// players than the internal 0-indexed id, and unambiguous alongside "YOU"/"OPPONENT".</param>
        static string FormatPlayerStats(string label, int playerId, Player p) =>
            $"{label} (P{playerId + 1})   HP {p.Health}/{p.MaxHealth}   EN {p.CurrentEnergy}/{p.EnergyPerTurn}   " +
            $"GOLD {p.Gold}   HAND {p.cardsInHand.Count}   DECK {p.Deck.Count}";

        void RedrawGameOver(GameState state, int viewerPlayerId)
        {
            if (!state.IsGameOver)
            {
                _gameOverOverlay.SetActive(false);
                return;
            }

            bool viewerDead = state.Players[viewerPlayerId].Health <= 0;
            bool opponentDead = state.Players[1 - viewerPlayerId].Health <= 0;
            _gameOverLabel.text = (viewerDead && opponentDead) ? "Draw!"
                : viewerDead ? "You lose!" : "You win!";
            _gameOverOverlay.SetActive(true);
        }

        public void ShowMessage(string message) => _messageLabel.text = message ?? "";

        // ------------------------------------------------------------------
        // helpers
        // ------------------------------------------------------------------

        static void ClearChildren(Transform parent)
        {
            for (int i = parent.childCount - 1; i >= 0; i--)
            {
                var child = parent.GetChild(i).gameObject;
                child.SetActive(false);        // hide immediately (Destroy is end-of-frame)
                Object.Destroy(child);
            }
        }

        static RectTransform AddRect(Transform parent, string name, Vector2 anchorMin, Vector2 anchorMax)
        {
            var go = new GameObject(name, typeof(RectTransform));
            var rect = (RectTransform)go.transform;
            rect.SetParent(parent, false);
            rect.anchorMin = anchorMin;
            rect.anchorMax = anchorMax;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
            return rect;
        }

        static Text AddLabel(Component parent, string name, Vector2 anchorMin, Vector2 anchorMax,
                             int size, TextAnchor align)
            => AddLabel(parent.transform, name, anchorMin, anchorMax, size, align);

        static Text AddLabel(Transform parent, string name, Vector2 anchorMin, Vector2 anchorMax,
                             int size, TextAnchor align)
        {
            var rect = AddRect(parent, name, anchorMin, anchorMax);
            var text = rect.gameObject.AddComponent<Text>();
            text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            text.fontSize = size;
            text.alignment = align;
            text.color = Color.white;
            text.horizontalOverflow = HorizontalWrapMode.Overflow;
            text.verticalOverflow = VerticalWrapMode.Overflow;
            text.raycastTarget = false;
            return text;
        }
    }
}
