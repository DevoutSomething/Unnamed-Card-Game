using System.Collections.Generic;
using Game.Cards;
using Game.Core.Lanes;
using Game.Core.State;
using UnityEngine;
using UnityEngine.UI;

namespace Game.Client.View
{
    /// <summary>
    /// The whole match screen, built from code so no scene/prefab hand-wiring is
    /// needed: phase banner, stat bar, 5 lane columns, the viewer's hand strip,
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

        // Sized so the whole board fits between the stat bar and the hand strip
        // without anything overlapping at the 1920x1080 reference resolution.
        // Hovering enlarges anything you actually need to read closely.
        const float SlotScale = 0.43f;
        const float HandScale = 0.62f;

        const float PhaseBarH = 46f;
        const float StatBarH = 74f;
        const float LaneBannerH = 40f;

        // ---- palette -----------------------------------------------------
        // One place to retune the whole screen. Backgrounds are near-black and
        // desaturated so the cards (the only saturated thing on screen) read as
        // the foreground.
        static readonly Color BgColor = new Color(0.055f, 0.065f, 0.085f);
        static readonly Color BarColor = new Color(0.10f, 0.12f, 0.16f, 0.96f);
        static readonly Color LanePanelColor = new Color(1f, 1f, 1f, 0.035f);
        static readonly Color SlotEmptyColor = new Color(1f, 1f, 1f, 0.05f);

        static readonly Color TextBright = new Color(0.96f, 0.97f, 1f);
        static readonly Color TextDim = new Color(0.62f, 0.66f, 0.76f);

        static readonly Color AccentGold = new Color(1f, 0.82f, 0.35f);
        static readonly Color AccentRed = new Color(1f, 0.48f, 0.45f);
        static readonly Color AccentGreen = new Color(0.46f, 0.86f, 0.56f);
        static readonly Color AccentBlue = new Color(0.45f, 0.72f, 1f);

        // Side tint: cool = yours, warm = the opponent's, brighter mid-turn.
        static readonly Color YourSideColor = new Color(0.28f, 0.58f, 0.95f, 0.10f);
        static readonly Color YourSideActiveColor = new Color(0.35f, 0.70f, 1f, 0.24f);
        static readonly Color OpponentSideColor = new Color(0.88f, 0.36f, 0.32f, 0.10f);
        static readonly Color OpponentSideActiveColor = new Color(0.95f, 0.42f, 0.38f, 0.24f);

        static readonly Color PhaseYourTurnColor = new Color(0.17f, 0.52f, 0.31f, 0.98f);
        static readonly Color PhaseOpponentTurnColor = new Color(0.30f, 0.32f, 0.38f, 0.98f);
        static readonly Color PhaseNeutralColor = new Color(0.42f, 0.34f, 0.14f, 0.98f);

        static readonly Color DropPreviewColor = new Color(1f, 0.95f, 0.4f, 0.35f);
        static readonly Color ValidTargetColor = new Color(0.45f, 1f, 0.68f, 1f);

        static readonly Color LaneEffectColor = new Color(0.38f, 0.30f, 0.62f, 0.85f);
        static readonly Color LanePlainColor = new Color(1f, 1f, 1f, 0.07f);

        GameController _controller;
        CardDatabase _db;
        CardSkinLibrary _skins;

        Image _phaseBarImage;
        Text _phaseLabel;
        Text _rotationLabel;
        Text _messageLabel;
        readonly Text[] _playerLabels = new Text[2];   // [0] = you, [1] = opponent
        readonly HeroDropTarget[] _heroDropTargets = new HeroDropTarget[2];
        readonly GameObject[] _heroHighlights = new GameObject[2];
        Button _endPhaseButton;
        Image _endPhaseImage;
        Text _endPhaseLabel;

        // [lane, screenRow, slot] -> the slot's own rect (background tint lives
        // here) plus its layered children: _cardHolders is what RedrawSublane
        // clears/spawns the CardView into, so rebuilding it never clobbers the
        // drop-preview or valid-target overlays sitting alongside it.
        // screenRow 0 = near/bottom (the viewer's own side), 1 = far/top.
        RectTransform[,,] _slotContainers;
        RectTransform[,,] _cardHolders;
        Image[,,] _dropPreviewOverlays;
        GameObject[,,] _targetHighlights;

        Image[] _laneBanners;
        Text[] _laneNameLabels;
        Text[] _laneDescLabels;

        Image _activeDropPreview;
        bool _targetsShown;

        GameState _lastState;
        int _lastViewerPlayerId;
        RectTransform _handRoot;
        readonly List<int> _handOrder = new List<int>();   // CardInstance ids, the player's own drag-reorder preference
        bool _handDragInProgress;
        GameObject _gameOverOverlay;
        Text _gameOverLabel;
        GameObject _canvasRoot;
        RectTransform _canvasRect;

        // Hovering any card — in hand or deployed in a lane — floats an
        // enlarged copy plus a reticle. Same layer the shop uses.
        readonly CardPreviewLayer _preview = new CardPreviewLayer();

        // Hovering a lane's plate blows its rules text up to a readable size.
        GameObject _laneTooltip;
        RectTransform _laneTooltipRect;
        Text _laneTooltipName;
        Text _laneTooltipDesc;

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
            _targetHighlights = new GameObject[laneCount, 2, slotsPerSide];
            _laneBanners = new Image[laneCount];
            _laneNameLabels = new Text[laneCount];
            _laneDescLabels = new Text[laneCount];

            var canvasGo = new GameObject("BoardCanvas",
                typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            _canvasRoot = canvasGo;
            _canvasRect = (RectTransform)canvasGo.transform;
            var canvas = canvasGo.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            var scaler = canvasGo.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);
            scaler.matchWidthOrHeight = 0.5f;

            EnsureEventSystem();

            var bg = AddRect(canvasGo.transform, "Background", Vector2.zero, Vector2.one);
            bg.gameObject.AddComponent<Image>().color = BgColor;

            BuildPhaseBar(canvasGo.transform);
            BuildStatBar(canvasGo.transform);
            BuildLanes(canvasGo.transform, laneCount, slotsPerSide);
            BuildHandStrip(canvasGo.transform);
            BuildEndPhaseButton(canvasGo.transform);
            BuildLaneTooltip(canvasGo.transform);
            _preview.Build(canvasGo.transform, _db, _skins);   // above the board, below game-over
            BuildGameOverOverlay(canvasGo.transform);          // last = drawn on top
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

        /// <summary>The color-coded banner flush against the very top: whose
        /// turn it is, from this viewer's perspective — the single clearest
        /// thing a player needs at a glance.</summary>
        void BuildPhaseBar(Transform canvas)
        {
            var bar = AddRect(canvas, "PhaseBar", new Vector2(0, 1), new Vector2(1, 1));
            bar.pivot = new Vector2(0.5f, 1);
            bar.sizeDelta = new Vector2(0, PhaseBarH);
            _phaseBarImage = bar.gameObject.AddComponent<Image>();

            _phaseLabel = AddLabel(bar, "PhaseText", Vector2.zero, Vector2.one, 24, TextAnchor.MiddleCenter);
            _phaseLabel.fontStyle = FontStyle.Bold;

            _rotationLabel = AddLabel(bar, "Rotation", new Vector2(1, 0), new Vector2(1, 1), 15, TextAnchor.MiddleRight);
            var rotRect = (RectTransform)_rotationLabel.transform;
            rotRect.pivot = new Vector2(1, 0.5f);
            rotRect.sizeDelta = new Vector2(220, 0);
            rotRect.anchoredPosition = new Vector2(-20, 0);
        }

        void BuildStatBar(Transform canvas)
        {
            var bar = AddRect(canvas, "StatBar", new Vector2(0, 1), new Vector2(1, 1));
            bar.pivot = new Vector2(0.5f, 1);
            bar.anchoredPosition = new Vector2(0, -PhaseBarH);
            bar.sizeDelta = new Vector2(0, StatBarH);
            bar.gameObject.AddComponent<Image>().color = BarColor;

            _playerLabels[0] = AddLabel(bar, "YouStats", new Vector2(0, 0), new Vector2(0.5f, 1),
                                        19, TextAnchor.MiddleLeft);
            ((RectTransform)_playerLabels[0].transform).offsetMin = new Vector2(26, 0);

            _playerLabels[1] = AddLabel(bar, "OpponentStats", new Vector2(0.5f, 0), new Vector2(1, 1),
                                        19, TextAnchor.MiddleRight);
            ((RectTransform)_playerLabels[1].transform).offsetMax = new Vector2(-26, 0);

            // A hairline under the bar, so the play area reads as its own region.
            var edge = AddRect(bar, "Edge", new Vector2(0, 0), new Vector2(1, 0));
            edge.pivot = new Vector2(0.5f, 1);
            edge.sizeDelta = new Vector2(0, 2f);
            edge.gameObject.AddComponent<Image>().color = new Color(1f, 1f, 1f, 0.08f);

            // Added after the labels so they sit on top in the raycast order —
            // the labels themselves don't raycast, so these catch spell drops
            // aimed at either hero.
            _heroDropTargets[0] = AddHeroDropZone(bar, "YouHeroDrop", new Vector2(0, 0), new Vector2(0.5f, 1),
                                                  out _heroHighlights[0]);
            _heroDropTargets[1] = AddHeroDropZone(bar, "OpponentHeroDrop", new Vector2(0.5f, 0), new Vector2(1, 1),
                                                  out _heroHighlights[1]);

            _messageLabel = AddLabel(canvas, "Message", new Vector2(0.15f, 1), new Vector2(0.85f, 1),
                                     18, TextAnchor.MiddleCenter);
            var msgRect = (RectTransform)_messageLabel.transform;
            msgRect.pivot = new Vector2(0.5f, 1);
            msgRect.anchoredPosition = new Vector2(0, -(PhaseBarH + StatBarH + 6f));
            msgRect.sizeDelta = new Vector2(0, 28);
            _messageLabel.color = AccentGold;
        }

        void BuildLanes(Transform canvas, int laneCount, int slotsPerSide)
        {
            float slotW = CardW * SlotScale, slotH = CardH * SlotScale;
            float colW = slotW + 64f;
            float rowSpacing = 5f;
            float colH = slotsPerSide * 2 * slotH + LaneBannerH + rowSpacing * (slotsPerSide * 2) + 12f;

            // Centred in the band left between the stat bar and the hand strip.
            float bandTop = 1080f - (PhaseBarH + StatBarH + 40f);
            float bandBottom = CardH * HandScale + 26f;
            float bandCenter = (bandTop + bandBottom) * 0.5f;

            var lanesRoot = AddRect(canvas, "Lanes",
                new Vector2(0.5f, bandCenter / 1080f), new Vector2(0.5f, bandCenter / 1080f));
            lanesRoot.sizeDelta = new Vector2(laneCount * colW + (laneCount - 1) * 16f, colH);

            var row = lanesRoot.gameObject.AddComponent<HorizontalLayoutGroup>();
            row.spacing = 16f;
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
                col.GetComponent<Image>().color = LanePanelColor;
                col.GetComponent<LaneDropTarget>().LaneIndex = lane;

                var stack = col.AddComponent<VerticalLayoutGroup>();
                stack.padding = new RectOffset(0, 0, 6, 6);
                stack.spacing = rowSpacing;
                stack.childAlignment = TextAnchor.MiddleCenter;
                stack.childControlWidth = false;
                stack.childControlHeight = false;
                stack.childForceExpandWidth = false;
                stack.childForceExpandHeight = false;

                // Far row (top of screen, opponent's side once Redraw assigns
                // it): back slot first so its front slot faces the middle.
                for (int slot = slotsPerSide - 1; slot >= 0; slot--)
                    _slotContainers[lane, 1, slot] = AddSlot(colRect, $"Far_Slot{slot}", slotW, slotH, lane, slot,
                        out _cardHolders[lane, 1, slot], out _dropPreviewOverlays[lane, 1, slot],
                        out _targetHighlights[lane, 1, slot]);

                AddLaneBanner(colRect, colW - 14f, LaneBannerH, lane);

                // Near row (bottom of screen, the viewer's own side): front
                // slot faces the middle.
                for (int slot = 0; slot < slotsPerSide; slot++)
                    _slotContainers[lane, 0, slot] = AddSlot(colRect, $"Near_Slot{slot}", slotW, slotH, lane, slot,
                        out _cardHolders[lane, 0, slot], out _dropPreviewOverlays[lane, 0, slot],
                        out _targetHighlights[lane, 0, slot]);
            }
        }

        /// <summary>A transparent but raycastable zone over one player's stat
        /// readout — where a spell aimed at a hero gets dropped, and what lights
        /// up when such a spell is being dragged.</summary>
        static HeroDropTarget AddHeroDropZone(Transform parent, string name,
                                              Vector2 anchorMin, Vector2 anchorMax, out GameObject highlight)
        {
            var rect = AddRect(parent, name, anchorMin, anchorMax);
            var image = rect.gameObject.AddComponent<Image>();
            image.color = new Color(0, 0, 0, 0f);
            image.raycastTarget = true;   // invisible, but still catches the drop

            highlight = AddOutline(rect, "ValidTarget", 3f, ValidTargetColor, inset: 6f);
            highlight.SetActive(false);

            return rect.gameObject.AddComponent<HeroDropTarget>();
        }

        /// <summary>
        /// The lane's "location" plate, dividing the two sides: effect name over
        /// its rules text. Deliberately small — hovering it pops the full text
        /// up at a readable size (see BuildLaneTooltip), so the resting state
        /// can stay quiet and out of the way.
        /// </summary>
        void AddLaneBanner(Transform column, float w, float h, int laneIndex)
        {
            var rect = AddRect(column, "LaneBanner", Vector2.zero, Vector2.zero);
            rect.sizeDelta = new Vector2(w, h);
            _laneBanners[laneIndex] = rect.gameObject.AddComponent<Image>();

            var layout = rect.gameObject.AddComponent<LayoutElement>();
            layout.preferredWidth = w;
            layout.preferredHeight = h;

            var hover = rect.gameObject.AddComponent<LaneHoverTarget>();
            hover.LaneIndex = laneIndex;
            hover.HoverEnter = ShowLaneTooltip;
            hover.HoverExit = HideLaneTooltip;

            _laneNameLabels[laneIndex] = AddLabel(rect, "LaneName",
                new Vector2(0, 0.48f), new Vector2(1, 1f), 12, TextAnchor.LowerCenter);
            _laneNameLabels[laneIndex].fontStyle = FontStyle.Bold;

            _laneDescLabels[laneIndex] = AddLabel(rect, "LaneDesc",
                new Vector2(0, 0f), new Vector2(1, 0.48f), 10, TextAnchor.UpperCenter);
            // Rules text is the one board label that must wrap rather than run
            // off the side of its lane.
            _laneDescLabels[laneIndex].horizontalOverflow = HorizontalWrapMode.Wrap;
            _laneDescLabels[laneIndex].color = TextDim;
        }

        /// <summary>
        /// A slot is layered: the slot rect itself (background tint +
        /// SlotDropTarget for precise front/back drop targeting), a "CardHolder"
        /// child that RedrawSublane clears and respawns the CardView into, and
        /// two overlay siblings — the drop preview and the valid-spell-target
        /// outline. CardHolder exists so rebuilding it each redraw never touches
        /// the overlays beside it.
        /// </summary>
        RectTransform AddSlot(Transform parent, string name, float w, float h, int laneIndex, int slotIndex,
                              out RectTransform cardHolder, out Image dropPreviewOverlay, out GameObject targetHighlight)
        {
            var slot = AddRect(parent, name, Vector2.zero, Vector2.zero);
            slot.sizeDelta = new Vector2(w, h);
            var img = slot.gameObject.AddComponent<Image>();
            img.raycastTarget = true;   // the precise drop target itself — see SlotDropTarget
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

            targetHighlight = AddOutline(slot, "ValidTarget", 3f, ValidTargetColor, inset: 2f);
            targetHighlight.SetActive(false);

            return slot;
        }

        void BuildHandStrip(Transform canvas)
        {
            float h = CardH * HandScale + 16;

            var backing = AddRect(canvas, "HandBacking", new Vector2(0, 0), new Vector2(1, 0));
            backing.pivot = new Vector2(0.5f, 0);
            backing.sizeDelta = new Vector2(0, h + 14);
            backing.gameObject.AddComponent<Image>().color = new Color(1f, 1f, 1f, 0.03f);

            _handRoot = AddRect(canvas, "Hand", new Vector2(0.5f, 0), new Vector2(0.5f, 0));
            _handRoot.pivot = new Vector2(0.5f, 0);
            _handRoot.anchoredPosition = new Vector2(0, 10);
            _handRoot.sizeDelta = new Vector2(1500, h);

            var row = _handRoot.gameObject.AddComponent<HorizontalLayoutGroup>();
            row.spacing = 12f;
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
            rect.anchoredPosition = new Vector2(-28, 28);
            rect.sizeDelta = new Vector2(200, 62);

            _endPhaseImage = rect.gameObject.AddComponent<Image>();
            _endPhaseButton = rect.gameObject.AddComponent<Button>();
            _endPhaseButton.onClick.AddListener(() => _controller.EndPhase());

            _endPhaseLabel = AddLabel(rect, "Label", Vector2.zero, Vector2.one, 22, TextAnchor.MiddleCenter);
            _endPhaseLabel.text = "END PHASE";
            _endPhaseLabel.fontStyle = FontStyle.Bold;
        }

        /// <summary>The floating panel a hovered lane plate blows up into —
        /// the same words, at a size you can actually read.</summary>
        void BuildLaneTooltip(Transform canvas)
        {
            _laneTooltip = new GameObject("LaneTooltip", typeof(RectTransform), typeof(Image));
            _laneTooltipRect = (RectTransform)_laneTooltip.transform;
            _laneTooltipRect.SetParent(canvas, false);
            _laneTooltipRect.anchorMin = _laneTooltipRect.anchorMax = new Vector2(0.5f, 0.5f);
            _laneTooltipRect.pivot = new Vector2(0.5f, 0.5f);
            _laneTooltipRect.sizeDelta = new Vector2(340, 132);
            _laneTooltip.GetComponent<Image>().color = new Color(0.07f, 0.08f, 0.12f, 0.98f);
            _laneTooltip.GetComponent<Image>().raycastTarget = false;

            AddOutline(_laneTooltipRect, "Edge", 2f, LaneEffectColor, inset: 0f);

            _laneTooltipName = AddLabel(_laneTooltipRect, "Name",
                new Vector2(0, 0.58f), new Vector2(1, 1f), 24, TextAnchor.MiddleCenter);
            _laneTooltipName.fontStyle = FontStyle.Bold;
            _laneTooltipName.color = AccentGold;

            _laneTooltipDesc = AddLabel(_laneTooltipRect, "Desc",
                new Vector2(0.06f, 0f), new Vector2(0.94f, 0.58f), 17, TextAnchor.UpperCenter);
            _laneTooltipDesc.horizontalOverflow = HorizontalWrapMode.Wrap;

            _laneTooltip.SetActive(false);
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
            _gameOverOverlay.GetComponent<Image>().color = new Color(0.02f, 0.03f, 0.05f, 0.86f);  // blocks clicks

            _gameOverLabel = AddLabel(rect, "Result", new Vector2(0, 0.5f), new Vector2(1, 0.75f),
                                      68, TextAnchor.MiddleCenter);
            _gameOverLabel.fontStyle = FontStyle.Bold;

            // The one and only option here: back to the main menu. No in-place
            // rematch, so there's nothing else this overlay needs to offer.
            var btnRect = AddRect(rect, "MainMenuButton", new Vector2(0.5f, 0.38f), new Vector2(0.5f, 0.38f));
            btnRect.sizeDelta = new Vector2(280, 72);
            btnRect.gameObject.AddComponent<Image>().color = new Color(0.20f, 0.50f, 0.28f);
            btnRect.gameObject.AddComponent<Button>().onClick.AddListener(() => _controller.ReturnToMainMenu());
            var btnLabel = AddLabel(btnRect, "Label", Vector2.zero, Vector2.one, 26, TextAnchor.MiddleCenter);
            btnLabel.text = "Main Menu";
            btnLabel.fontStyle = FontStyle.Bold;

            _gameOverOverlay.SetActive(false);
        }

        /// <summary>Shows/hides the whole board canvas — used when backing out
        /// of an online match to the main menu, where the board would
        /// otherwise still be sitting behind the lobby screen.</summary>
        public void SetVisible(bool visible)
        {
            _canvasRoot.SetActive(visible);
            if (!visible)
            {
                _preview.Hide();
                HideLaneTooltip();
                ClearSpellTargets();
            }
        }

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
            _preview.Hide();      // whatever it was anchored to is about to change

            // Mid-drag the highlights describe the card still in the player's
            // hand, so they must survive a redraw the opponent triggered.
            if (!_handDragInProgress) ClearSpellTargets();

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

            RedrawLaneBanners(state);
            RedrawHand(state, viewerPlayerId);
            RedrawLabels(state, viewerPlayerId, viewerActive, opponentActive);
            RedrawGameOver(state, viewerPlayerId);

            _endPhaseButton.interactable = viewerActive;
            _endPhaseImage.color = viewerActive
                ? new Color(0.22f, 0.45f, 0.78f)
                : new Color(0.18f, 0.20f, 0.25f);
            _endPhaseLabel.color = viewerActive ? TextBright : TextDim;
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

        // ------------------------------------------------------------------
        // Spell targeting
        // ------------------------------------------------------------------

        /// <summary>
        /// Outlines everything a spell being dragged is actually allowed to hit,
        /// so "deal 2 damage to anything" shows its legal targets instead of
        /// making the player guess and eat a rejection. Mirrors the resolver's
        /// own rules (CommandResolver.TryResolveSpellTarget); the server still
        /// re-validates, this only saves the player a wasted drag.
        /// </summary>
        public void ShowValidSpellTargets(CardInstance spellCard)
        {
            ClearSpellTargets();
            if (_lastState == null || spellCard == null) return;
            if (!(_db?.Get(spellCard.DefinitionId) is SpellCardDefinition def) || !def.NeedsTarget) return;

            int casterId = spellCard.OwnerId;

            for (int lane = 0; lane < _lastState.Lanes.Length && lane < _targetHighlights.GetLength(0); lane++)
            {
                for (int screenRow = 0; screenRow < 2; screenRow++)
                {
                    int ownerId = screenRow == 0 ? _lastViewerPlayerId : 1 - _lastViewerPlayerId;
                    var sublane = _lastState.Lanes[lane].SublaneOf(ownerId);

                    for (int slot = 0; slot < sublane.Slots.Length && slot < _targetHighlights.GetLength(2); slot++)
                    {
                        var card = sublane.Slots[slot];
                        if (card == null || card.CurrentHealth <= 0) continue;
                        if (def.Target == SpellTarget.FriendlyGuy && card.OwnerId != casterId) continue;

                        var highlight = _targetHighlights[lane, screenRow, slot];
                        if (highlight != null) highlight.SetActive(true);
                    }
                }
            }

            // Only "anything" reaches past the guys to the heroes themselves.
            if (def.Target == SpellTarget.AnyCharacter)
            {
                foreach (var highlight in _heroHighlights)
                    if (highlight != null) highlight.SetActive(true);
            }

            _targetsShown = true;
        }

        public void ClearSpellTargets()
        {
            if (!_targetsShown) return;

            foreach (var highlight in _targetHighlights)
                if (highlight != null) highlight.SetActive(false);
            foreach (var highlight in _heroHighlights)
                if (highlight != null) highlight.SetActive(false);

            _targetsShown = false;
        }

        // ------------------------------------------------------------------
        // Lane tooltip
        // ------------------------------------------------------------------

        void ShowLaneTooltip(int laneIndex, RectTransform source)
        {
            if (_lastState == null || laneIndex < 0 || laneIndex >= _lastState.Lanes.Length) return;

            var def = LaneCatalog.Get(_lastState.Lanes[laneIndex].LaneTypeId);
            if (def == null) return;   // a plain lane has nothing to enlarge

            _laneTooltipName.text = def.DisplayName.ToUpperInvariant();
            _laneTooltipDesc.text = def.Description;
            _laneTooltipRect.anchoredPosition = ClampedCanvasPosition(source, _laneTooltipRect.sizeDelta, 0f);
            _laneTooltip.SetActive(true);
        }

        void HideLaneTooltip()
        {
            if (_laneTooltip != null) _laneTooltip.SetActive(false);
        }

        /// <summary>Canvas-local position centred on a hovered rect, clamped so
        /// the panel stays fully on screen.</summary>
        Vector2 ClampedCanvasPosition(RectTransform source, Vector2 size, float yNudge)
        {
            Vector2 local = Vector2.zero;
            if (source != null)
            {
                Vector2 screenPoint = RectTransformUtility.WorldToScreenPoint(null, source.position);
                RectTransformUtility.ScreenPointToLocalPointInRectangle(_canvasRect, screenPoint, null, out local);
            }
            local.y += yNudge;

            Vector2 canvasSize = _canvasRect.rect.size;
            float maxX = Mathf.Max(0f, (canvasSize.x - size.x) * 0.5f);
            float maxY = Mathf.Max(0f, (canvasSize.y - size.y) * 0.5f);
            local.x = Mathf.Clamp(local.x, -maxX, maxX);
            local.y = Mathf.Clamp(local.y, -maxY, maxY);
            return local;
        }

        // ------------------------------------------------------------------
        // Redraw parts
        // ------------------------------------------------------------------

        void RedrawSublane(Sublane sublane, int lane, int screenRow, Color tint)
        {
            for (int slot = 0; slot < sublane.Slots.Length; slot++)
            {
                var card = sublane.Slots[slot];

                var background = _slotContainers[lane, screenRow, slot].GetComponent<Image>();
                // An occupied slot gets the side tint; an empty one stays a
                // near-neutral well, so "where can I still play" reads instantly.
                if (background != null) background.color = card != null ? tint : SlotEmptyColor;

                // Whose row this is, for spell targeting (see SlotDropTarget).
                var dropTarget = _slotContainers[lane, screenRow, slot].GetComponent<SlotDropTarget>();
                if (dropTarget != null) dropTarget.OwnerPlayerId = sublane.PlayerId;

                var cardHolder = _cardHolders[lane, screenRow, slot];
                ClearChildren(cardHolder);

                // Slot containers persist across redraws, so this just points
                // the existing hover target at whatever occupies the slot now —
                // null for an empty slot, which previews nothing.
                _preview.Attach(_slotContainers[lane, screenRow, slot].gameObject, card);

                if (card == null) continue;

                var view = CardViewFactory.Spawn(card, cardHolder, _db, _skins);
                if (view == null) continue;
                PlaceCardView(view, SlotScale);
            }
        }

        /// <summary>Lane effects are read straight off replicated state
        /// (Lane.LaneTypeId), so both players always see the same board.</summary>
        void RedrawLaneBanners(GameState state)
        {
            for (int lane = 0; lane < _laneBanners.Length && lane < state.Lanes.Length; lane++)
            {
                var def = LaneCatalog.Get(state.Lanes[lane].LaneTypeId);

                _laneBanners[lane].color = def != null ? LaneEffectColor : LanePlainColor;
                _laneNameLabels[lane].text = def != null ? def.DisplayName.ToUpperInvariant() : "";
                _laneDescLabels[lane].text = def != null ? def.Description : "";
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

            bool spellTurn = state.CurrentSlotType == SlotType.Action
                             && state.ActivePlayerId == viewerPlayerId
                             && !state.IsMainActionSlot;
            bool mainTurn = state.CurrentSlotType == SlotType.Action
                            && state.ActivePlayerId == viewerPlayerId
                            && state.IsMainActionSlot;

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

                bool isSpell = _db?.Get(card.DefinitionId) is SpellCardDefinition;

                // Dim whatever this slot type can't play, so the hand shows what
                // you can actually do right now instead of a wall of cards.
                bool playableNow = (isSpell && spellTurn) || (!isSpell && mainTurn);
                bool affordable = card.CurrentCost <= player.CurrentEnergy;
                wrapper.GetComponent<Image>().color = playableNow && affordable
                    ? new Color(0.45f, 0.8f, 1f, 0.22f)
                    : new Color(0f, 0f, 0f, 0.28f);

                var drag = wrapper.GetComponent<DraggableHandCard>();
                drag.Card = card;
                drag.Controller = _controller;
                drag.HandRoot = _handRoot;
                // A spell is aimed at a target, not placed in a slot — the view
                // knows the definition, so the drag doesn't have to look it up.
                drag.IsSpell = isSpell;

                _preview.Attach(wrapper, card);

                var view = CardViewFactory.Spawn(card, rect, _db, _skins);
                if (view != null)
                {
                    PlaceCardView(view, HandScale);
                    var group = view.gameObject.AddComponent<CanvasGroup>();
                    group.alpha = playableNow && affordable ? 1f : 0.55f;
                }
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

        public void SetHandDragInProgress(bool inProgress)
        {
            _handDragInProgress = inProgress;
            // A giant preview pinned under the cursor is only ever in the way
            // while you're aiming a card.
            _preview.SetSuppressed(inProgress);
            if (inProgress) HideLaneTooltip();
        }

        /// <summary>Center a spawned CardView in its container at the given scale.</summary>
        static void PlaceCardView(CardView view, float scale)
        {
            var rect = (RectTransform)view.transform;
            rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = Vector2.zero;
            rect.sizeDelta = new Vector2(CardW, CardH);
            rect.localScale = new Vector3(scale, scale, 1f);

            // Clicks must reach the wrapper (hand) or the slot (board).
            var rootImage = view.GetComponent<Image>();
            if (rootImage != null) rootImage.raycastTarget = false;
        }

        void RedrawLabels(GameState state, int viewerPlayerId, bool viewerActive, bool opponentActive)
        {
            int opponentId = 1 - viewerPlayerId;

            _playerLabels[0].text = FormatPlayerStats("YOU", viewerPlayerId, state.Players[viewerPlayerId], viewerActive);
            _playerLabels[1].text = FormatPlayerStats("OPPONENT", opponentId, state.Players[opponentId], opponentActive);

            _heroDropTargets[0].PlayerId = viewerPlayerId;
            _heroDropTargets[1].PlayerId = opponentId;

            _rotationLabel.text = $"ROTATION {state.RotationIndex + 1}";
            _rotationLabel.color = new Color(0f, 0f, 0f, 0.45f);

            // "Spell turn" is a player's bonus, guy-free action slot
            // (GameState.IsMainActionSlot false) — same green/grey as their main
            // turn, but named so the slot rule is never a surprise.
            if (viewerActive)
            {
                _phaseLabel.text = state.IsMainActionSlot ? "YOUR TURN — PLAY A GUY" : "YOUR SPELL TURN — CAST A SPELL";
                _phaseBarImage.color = PhaseYourTurnColor;
                _phaseLabel.color = TextBright;
            }
            else if (opponentActive)
            {
                _phaseLabel.text = state.IsMainActionSlot ? "OPPONENT'S TURN" : "OPPONENT'S SPELL TURN";
                _phaseBarImage.color = PhaseOpponentTurnColor;
                _phaseLabel.color = TextBright;
            }
            else
            {
                _phaseLabel.text = state.CurrentSlotType.ToString().ToUpperInvariant() + "...";
                _phaseBarImage.color = PhaseNeutralColor;
                _phaseLabel.color = TextBright;
            }
        }

        /// <param name="playerId">Absolute 0/1 player id, shown 1-indexed (P1/P2) — plainer for
        /// players than the internal 0-indexed id, and unambiguous alongside "YOU"/"OPPONENT".</param>
        static string FormatPlayerStats(string label, int playerId, Player p, bool active)
        {
            string name = active
                ? $"<b><color=#{Hex(AccentGold)}>{label} (P{playerId + 1})</color></b>"
                : $"<b><color=#{Hex(TextBright)}>{label} (P{playerId + 1})</color></b>";

            return name + "   "
                 + Stat("HP", $"{p.Health}/{p.MaxHealth}", p.Health <= 10 ? AccentRed : AccentGreen) + "  "
                 + Stat("EN", $"{p.CurrentEnergy}/{p.EnergyPerTurn}", AccentBlue) + "  "
                 + Stat("GOLD", p.Gold, AccentGold) + "  "
                 + Stat("HAND", p.cardsInHand.Count, TextBright) + "  "
                 + Stat("DECK", p.Deck.Count, TextBright);
        }

        static string Stat(string label, object value, Color valueColor) =>
            $"<color=#{Hex(TextDim)}><size=13>{label}</size></color> " +
            $"<color=#{Hex(valueColor)}><b>{value}</b></color>";

        static string Hex(Color c) => ColorUtility.ToHtmlStringRGB(c);

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
            _gameOverLabel.color = (viewerDead && opponentDead) ? TextBright
                : viewerDead ? AccentRed : AccentGreen;
            _gameOverOverlay.SetActive(true);
        }

        public void ShowMessage(string message) => _messageLabel.text = message ?? "";

        // ------------------------------------------------------------------
        // helpers
        // ------------------------------------------------------------------

        /// <summary>
        /// A hollow rectangle drawn as four thin bars, so it frames whatever
        /// it's over without covering it — an alpha-blended fill would wash out
        /// the card art underneath.
        /// </summary>
        static GameObject AddOutline(Transform parent, string name, float thickness, Color color, float inset)
        {
            var root = AddRect(parent, name, Vector2.zero, Vector2.one);
            root.offsetMin = new Vector2(inset, inset);
            root.offsetMax = new Vector2(-inset, -inset);

            // (anchorMin, anchorMax, sizeDelta) per edge: each bar stretches
            // along one axis and is `thickness` thick on the other.
            AddBar(root, new Vector2(0, 1), new Vector2(1, 1), new Vector2(0, thickness), color);   // top
            AddBar(root, new Vector2(0, 0), new Vector2(1, 0), new Vector2(0, thickness), color);   // bottom
            AddBar(root, new Vector2(0, 0), new Vector2(0, 1), new Vector2(thickness, 0), color);   // left
            AddBar(root, new Vector2(1, 0), new Vector2(1, 1), new Vector2(thickness, 0), color);   // right

            return root.gameObject;
        }

        static void AddBar(Transform parent, Vector2 anchorMin, Vector2 anchorMax, Vector2 sizeDelta, Color color)
        {
            var go = new GameObject("Bar", typeof(RectTransform), typeof(Image));
            var rect = (RectTransform)go.transform;
            rect.SetParent(parent, false);
            rect.anchorMin = anchorMin;
            rect.anchorMax = anchorMax;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
            rect.sizeDelta = sizeDelta;
            var image = go.GetComponent<Image>();
            image.color = color;
            image.raycastTarget = false;
        }

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
            text.color = TextBright;
            text.supportRichText = true;
            text.horizontalOverflow = HorizontalWrapMode.Overflow;
            text.verticalOverflow = VerticalWrapMode.Overflow;
            text.raycastTarget = false;
            return text;
        }
    }
}
