using System.Collections.Generic;
using Game.Cards;
using Game.Core.State;
using UnityEngine;
using UnityEngine.UI;

namespace Game.Client.View
{
    /// <summary>
    /// The whole match screen, built from code so no scene/prefab hand-wiring is
    /// needed: top stat bar, 5 lane columns (P1 on top, P0 on bottom), the active
    /// player's hand strip, an End Phase button, and a game-over overlay.
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
        static readonly Color SlotColor = new Color(1f, 1f, 1f, 0.06f);
        static readonly Color ActiveColor = new Color(1f, 0.85f, 0.3f);
        static readonly Color SelectedColor = new Color(1f, 0.85f, 0.3f, 0.9f);

        GameController _controller;
        CardDatabase _db;
        CardSkinLibrary _skins;

        Text _phaseLabel;
        Text _messageLabel;
        readonly Text[] _playerLabels = new Text[2];

        // [lane, playerId, slot] -> container the card view gets spawned into
        RectTransform[,,] _slotContainers;
        RectTransform _handRoot;
        GameObject _gameOverOverlay;
        Text _gameOverLabel;

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

            var canvasGo = new GameObject("BoardCanvas",
                typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            var canvas = canvasGo.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            var scaler = canvasGo.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);
            scaler.matchWidthOrHeight = 0.5f;

            EnsureEventSystem();

            var bg = AddRect(canvasGo.transform, "Background", Vector2.zero, Vector2.one);
            bg.gameObject.AddComponent<Image>().color = new Color(0.09f, 0.11f, 0.13f);

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
            bar.sizeDelta = new Vector2(0, 64);
            bar.gameObject.AddComponent<Image>().color = PanelColor;

            _playerLabels[0] = AddLabel(bar, "P0Stats", new Vector2(0, 0), new Vector2(0.35f, 1),
                                        20, TextAnchor.MiddleLeft);
            ((RectTransform)_playerLabels[0].transform).offsetMin = new Vector2(20, 0);

            _phaseLabel = AddLabel(bar, "Phase", new Vector2(0.35f, 0), new Vector2(0.65f, 1),
                                   22, TextAnchor.MiddleCenter);
            _phaseLabel.fontStyle = FontStyle.Bold;

            _playerLabels[1] = AddLabel(bar, "P1Stats", new Vector2(0.65f, 0), new Vector2(1, 1),
                                        20, TextAnchor.MiddleRight);
            ((RectTransform)_playerLabels[1].transform).offsetMax = new Vector2(-20, 0);

            _messageLabel = AddLabel(canvas, "Message", new Vector2(0.2f, 1), new Vector2(0.8f, 1),
                                     18, TextAnchor.MiddleCenter);
            var msgRect = (RectTransform)_messageLabel.transform;
            msgRect.pivot = new Vector2(0.5f, 1);
            msgRect.anchoredPosition = new Vector2(0, -68);
            msgRect.sizeDelta = new Vector2(0, 32);
            _messageLabel.color = new Color(1f, 0.55f, 0.5f);
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
                var col = new GameObject($"Lane{lane}", typeof(RectTransform), typeof(Image), typeof(Button));
                var colRect = (RectTransform)col.transform;
                colRect.SetParent(lanesRoot, false);
                colRect.sizeDelta = new Vector2(colW, colH);
                col.GetComponent<Image>().color = PanelColor;

                int laneIndex = lane;   // capture for the closure
                col.GetComponent<Button>().onClick.AddListener(() => _controller.ClickLane(laneIndex));

                var stack = col.AddComponent<VerticalLayoutGroup>();
                stack.padding = new RectOffset(0, 0, 6, 6);
                stack.spacing = 6f;
                stack.childAlignment = TextAnchor.MiddleCenter;
                stack.childControlWidth = false;
                stack.childControlHeight = false;
                stack.childForceExpandWidth = false;
                stack.childForceExpandHeight = false;

                // Top half: P1's side, back slot first so its front faces the middle.
                for (int slot = slotsPerSide - 1; slot >= 0; slot--)
                    _slotContainers[lane, 1, slot] = AddSlot(colRect, $"P1_Slot{slot}", slotW, slotH);

                var divider = AddRect(colRect, "Divider", Vector2.zero, Vector2.zero);
                divider.sizeDelta = new Vector2(colW - 16f, 8f);
                divider.gameObject.AddComponent<Image>().color = new Color(1f, 1f, 1f, 0.25f);
                divider.gameObject.AddComponent<LayoutElement>().preferredHeight = 8f;

                // Bottom half: P0's side, front slot facing the middle.
                for (int slot = 0; slot < slotsPerSide; slot++)
                    _slotContainers[lane, 0, slot] = AddSlot(colRect, $"P0_Slot{slot}", slotW, slotH);
            }
        }

        RectTransform AddSlot(Transform parent, string name, float w, float h)
        {
            var slot = AddRect(parent, name, Vector2.zero, Vector2.zero);
            slot.sizeDelta = new Vector2(w, h);
            var img = slot.gameObject.AddComponent<Image>();
            img.color = SlotColor;
            img.raycastTarget = false;   // clicks fall through to the lane button
            var le = slot.gameObject.AddComponent<LayoutElement>();
            le.preferredWidth = w;
            le.preferredHeight = h;
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
            var button = rect.gameObject.AddComponent<Button>();
            button.onClick.AddListener(() => _controller.EndPhase());

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

            var btnRect = AddRect(rect, "Restart", new Vector2(0.5f, 0.38f), new Vector2(0.5f, 0.38f));
            btnRect.sizeDelta = new Vector2(240, 72);
            btnRect.gameObject.AddComponent<Image>().color = new Color(0.25f, 0.6f, 0.3f);
            btnRect.gameObject.AddComponent<Button>().onClick.AddListener(() => _controller.Restart());
            var btnLabel = AddLabel(btnRect, "Label", Vector2.zero, Vector2.one, 26, TextAnchor.MiddleCenter);
            btnLabel.text = "Play Again";
            btnLabel.fontStyle = FontStyle.Bold;

            _gameOverOverlay.SetActive(false);
        }

        // ------------------------------------------------------------------
        // Redraw (every event batch)
        // ------------------------------------------------------------------

        public void Redraw(GameState state, CardInstance selectedCard)
        {
            for (int lane = 0; lane < state.Lanes.Length; lane++)
            {
                RedrawSublane(state.Lanes[lane].P1, lane);
                RedrawSublane(state.Lanes[lane].P2, lane);
            }

            RedrawHand(state, selectedCard);
            RedrawLabels(state);
            RedrawGameOver(state);
        }

        void RedrawSublane(Sublane sublane, int lane)
        {
            for (int slot = 0; slot < sublane.Slots.Length; slot++)
            {
                var container = _slotContainers[lane, sublane.PlayerId, slot];
                ClearChildren(container);

                var card = sublane.Slots[slot];
                if (card == null) continue;

                var view = CardViewFactory.Spawn(card, container, _db, _skins);
                if (view == null) continue;
                PlaceCardView(view, SlotScale);
            }
        }

        void RedrawHand(GameState state, CardInstance selectedCard)
        {
            ClearChildren(_handRoot);
            var player = state.Players[state.ActivePlayerId];

            foreach (var card in player.cardsInHand)
            {
                var wrapper = new GameObject("HandCard",
                    typeof(RectTransform), typeof(Image), typeof(Button), typeof(LayoutElement));
                var rect = (RectTransform)wrapper.transform;
                rect.SetParent(_handRoot, false);
                float w = CardW * HandScale + 8, h = CardH * HandScale + 8;
                rect.sizeDelta = new Vector2(w, h);
                var le = wrapper.GetComponent<LayoutElement>();
                le.preferredWidth = w;
                le.preferredHeight = h;

                // The wrapper doubles as the selection frame.
                wrapper.GetComponent<Image>().color =
                    card == selectedCard ? SelectedColor : new Color(0, 0, 0, 0.25f);

                var captured = card;
                wrapper.GetComponent<Button>().onClick.AddListener(() => _controller.SelectCard(captured));

                var view = CardViewFactory.Spawn(card, rect, _db, _skins);
                if (view != null) PlaceCardView(view, HandScale);
            }
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

            // Clicks must reach the wrapper button (hand) or lane button (board).
            var rootImage = view.GetComponent<Image>();
            if (rootImage != null) rootImage.raycastTarget = false;
        }

        void RedrawLabels(GameState state)
        {
            for (int id = 0; id < 2; id++)
            {
                var p = state.Players[id];
                _playerLabels[id].text =
                    $"P{id}   HP {p.Health}   EN {p.CurrentEnergy}/{p.EnergyPerTurn}   " +
                    $"GOLD {p.Gold}   HAND {p.cardsInHand.Count}   DECK {p.Deck.Count}";
                _playerLabels[id].color = state.ActivePlayerId == id ? ActiveColor : Color.white;
            }

            _phaseLabel.text = state.CurrentSlotType == SlotType.Action
                ? $"P{state.ActivePlayerId}'s turn  (rotation {state.RotationIndex + 1})"
                : $"{state.CurrentSlotType}...";
        }

        void RedrawGameOver(GameState state)
        {
            if (!state.IsGameOver)
            {
                _gameOverOverlay.SetActive(false);
                return;
            }

            bool p0Dead = state.Players[0].Health <= 0;
            bool p1Dead = state.Players[1].Health <= 0;
            _gameOverLabel.text = (p0Dead && p1Dead) ? "Draw!"
                : p0Dead ? "Player 1 wins!" : "Player 0 wins!";
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
