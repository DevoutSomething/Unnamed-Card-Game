using Game.Cards;
using Game.Core.Augments;
using Game.Core.Server;
using Game.Core.State;
using UnityEngine;
using UnityEngine.UI;

namespace Game.Client.View
{
    /// <summary>
    /// The augment pick: a full-screen overlay shown during the Augment slot,
    /// three large panels side by side, click one to take it permanently.
    /// Modelled on LoL Arena's augment screen — big, readable, one decision.
    ///
    /// Built once (Build), shown/hidden with SetVisible, rebuilt from scratch
    /// every Redraw — the same brute-force philosophy as BoardView and ShopView.
    ///
    /// Hot-seat shares one screen, so (like the shop) a P1/P2 toggle decides who
    /// is picking; online, ShopViewerId is the client's own fixed id and the
    /// toggle is hidden.
    /// </summary>
    public class AugmentView
    {
        const int OptionCount = 3;

        static readonly Color BgColor = new Color(0.035f, 0.045f, 0.065f, 0.995f);
        static readonly Color HeaderColor = new Color(0.08f, 0.10f, 0.14f, 0.98f);
        static readonly Color OptionColor = new Color(0.11f, 0.13f, 0.19f, 1f);
        static readonly Color OptionTakenColor = new Color(0.08f, 0.09f, 0.12f, 1f);
        static readonly Color TextBright = new Color(0.96f, 0.97f, 1f);
        static readonly Color TextDim = new Color(0.62f, 0.66f, 0.76f);
        static readonly Color AccentGold = new Color(1f, 0.82f, 0.35f);

        // Arena-style rarity framing: the frame and the little banner above each
        // option are what make three panels read as three *choices* rather than
        // three paragraphs.
        static readonly Color CommonColor = new Color(0.62f, 0.66f, 0.76f);
        static readonly Color RareColor = new Color(0.36f, 0.66f, 1f);
        static readonly Color EpicColor = new Color(0.72f, 0.45f, 1f);
        static readonly Color LegendaryColor = new Color(1f, 0.72f, 0.28f);

        static Color RarityColor(Rarity rarity) => rarity switch
        {
            Rarity.Rare => RareColor,
            Rarity.Epic => EpicColor,
            Rarity.Legendary => LegendaryColor,
            _ => CommonColor,
        };

        GameController _controller;

        GameObject _canvasRoot;
        Text _titleLabel;
        Text _statusLabel;
        Text _takenLabel;
        Text _timerLabel;

        readonly Button[] _optionButtons = new Button[OptionCount];
        readonly Image[] _optionImages = new Image[OptionCount];
        readonly Text[] _optionNames = new Text[OptionCount];
        readonly Text[] _optionDescs = new Text[OptionCount];
        readonly GameObject[] _optionRoots = new GameObject[OptionCount];
        readonly Image[][] _optionEdges = new Image[OptionCount][];
        readonly Image[] _optionBanners = new Image[OptionCount];
        readonly Text[] _optionRarities = new Text[OptionCount];

        GameObject _hotSeatToggle;
        readonly Button[] _hotSeatButtons = new Button[2];
        readonly Text[] _hotSeatLabels = new Text[2];

        // ------------------------------------------------------------------
        // Build (once)
        // ------------------------------------------------------------------

        public void Build(GameController controller)
        {
            _controller = controller;

            var canvasGo = new GameObject("AugmentCanvas",
                typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            _canvasRoot = canvasGo;
            var canvas = canvasGo.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 20;   // above the board AND the shop
            var scaler = canvasGo.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);
            scaler.matchWidthOrHeight = 0.5f;

            var bg = AddRect(canvasGo.transform, "Background", Vector2.zero, Vector2.one);
            bg.gameObject.AddComponent<Image>().color = BgColor;

            BuildHeader(canvasGo.transform);
            BuildOptions(canvasGo.transform);

            _canvasRoot.SetActive(false);
        }

        void BuildHeader(Transform canvas)
        {
            var bar = AddRect(canvas, "Header", new Vector2(0, 0.86f), new Vector2(1, 1));
            bar.gameObject.AddComponent<Image>().color = HeaderColor;

            _titleLabel = AddLabel(bar, "Title", new Vector2(0, 0.35f), new Vector2(1, 1), 34, TextAnchor.MiddleCenter);
            _titleLabel.text = "CHOOSE AN AUGMENT";
            _titleLabel.fontStyle = FontStyle.Bold;
            _titleLabel.color = AccentGold;

            _statusLabel = AddLabel(bar, "Status", new Vector2(0, 0), new Vector2(1, 0.4f), 18, TextAnchor.MiddleCenter);
            _statusLabel.color = TextDim;

            _timerLabel = AddLabel(bar, "Timer", new Vector2(1, 0), new Vector2(1, 1), 30, TextAnchor.MiddleRight);
            var timerRect = (RectTransform)_timerLabel.transform;
            timerRect.pivot = new Vector2(1, 0.5f);
            timerRect.sizeDelta = new Vector2(180, 0);
            timerRect.anchoredPosition = new Vector2(-40, 0);
            _timerLabel.fontStyle = FontStyle.Bold;

            _takenLabel = AddLabel(canvas, "Taken", new Vector2(0, 0), new Vector2(1, 0.08f), 17, TextAnchor.MiddleCenter);
            _takenLabel.color = TextDim;

            BuildHotSeatToggle(bar);
        }

        void BuildHotSeatToggle(Transform header)
        {
            _hotSeatToggle = new GameObject("HotSeatToggle", typeof(RectTransform));
            var rect = (RectTransform)_hotSeatToggle.transform;
            rect.SetParent(header, false);
            rect.anchorMin = rect.anchorMax = new Vector2(0, 0.5f);
            rect.anchoredPosition = new Vector2(210, 0);
            rect.sizeDelta = new Vector2(160, 40);

            for (int i = 0; i < 2; i++)
            {
                int playerId = i;
                var btn = AddButton(rect, $"P{i + 1}Toggle", new Vector2(0, 0), new Vector2(0, 0),
                    new Vector2(40 + i * 84, 0), new Vector2(76, 40), $"P{i + 1}", 16,
                    () => _controller.SetHotSeatShopper(playerId));
                _hotSeatButtons[i] = btn;
                _hotSeatLabels[i] = btn.GetComponentInChildren<Text>();
            }
        }

        /// <summary>Three big panels across the middle of the screen. Big on
        /// purpose: this is a permanent, one-shot decision, so it gets the whole
        /// screen rather than a card-sized cell.</summary>
        void BuildOptions(Transform canvas)
        {
            const float width = 0.26f, gap = 0.035f;
            float totalWidth = OptionCount * width + (OptionCount - 1) * gap;
            float startX = (1f - totalWidth) * 0.5f;

            for (int i = 0; i < OptionCount; i++)
            {
                float x0 = startX + i * (width + gap);

                // Tall panels with real breathing room — this is a permanent,
                // one-shot decision, so it gets the whole screen.
                var panel = AddRect(canvas, $"Option{i}", new Vector2(x0, 0.14f), new Vector2(x0 + width, 0.80f));
                _optionRoots[i] = panel.gameObject;
                _optionImages[i] = panel.gameObject.AddComponent<Image>();
                _optionImages[i].color = OptionColor;

                var button = panel.gameObject.AddComponent<Button>();
                button.targetGraphic = _optionImages[i];
                // Unity's own tint transition gives the hover response for free.
                var colors = button.colors;
                colors.normalColor = Color.white;
                colors.highlightedColor = new Color(1.35f, 1.35f, 1.35f);
                colors.pressedColor = new Color(0.85f, 0.85f, 0.85f);
                colors.disabledColor = new Color(0.7f, 0.7f, 0.7f);
                colors.fadeDuration = 0.08f;
                button.colors = colors;
                _optionButtons[i] = button;

                _optionEdges[i] = AddOutline(panel, "Edge", 3f, CommonColor);

                // Rarity banner across the top — the Arena tell for how big a
                // pick this is, before you read a word of it.
                var banner = AddRect(panel, "Banner", new Vector2(0, 1), new Vector2(1, 1));
                banner.pivot = new Vector2(0.5f, 1);
                banner.sizeDelta = new Vector2(0, 46);
                _optionBanners[i] = banner.gameObject.AddComponent<Image>();

                _optionRarities[i] = AddLabel(banner, "Rarity", Vector2.zero, Vector2.one, 17, TextAnchor.MiddleCenter);
                _optionRarities[i].fontStyle = FontStyle.Bold;
                _optionRarities[i].color = new Color(0.05f, 0.06f, 0.08f);

                _optionNames[i] = AddLabel(panel, "Name", new Vector2(0.06f, 0.60f), new Vector2(0.94f, 0.86f),
                                           32, TextAnchor.MiddleCenter);
                _optionNames[i].fontStyle = FontStyle.Bold;
                _optionNames[i].horizontalOverflow = HorizontalWrapMode.Wrap;

                _optionDescs[i] = AddLabel(panel, "Desc", new Vector2(0.09f, 0.16f), new Vector2(0.91f, 0.56f),
                                           21, TextAnchor.UpperCenter);
                _optionDescs[i].horizontalOverflow = HorizontalWrapMode.Wrap;
                _optionDescs[i].color = TextDim;

                var hint = AddLabel(panel, "Hint", new Vector2(0, 0.03f), new Vector2(1, 0.11f),
                                    16, TextAnchor.MiddleCenter);
                hint.text = "CLICK TO TAKE";
                hint.color = new Color(1f, 1f, 1f, 0.30f);
            }
        }

        public void SetVisible(bool visible) => _canvasRoot.SetActive(visible);

        // ------------------------------------------------------------------
        // Redraw
        // ------------------------------------------------------------------

        /// <summary>Called every frame while the Augment slot is active (see
        /// GameController.Update) so the countdown ticks smoothly without the
        /// cost of a full Redraw.</summary>
        public void TickCountdown(GameState state) => UpdateTimerLabel(state);

        void UpdateTimerLabel(GameState state)
        {
            if (!state.AugmentDeadlineUtc.HasValue)
            {
                _timerLabel.text = "";
                return;
            }

            double remaining = System.Math.Max(
                0, (state.AugmentDeadlineUtc.Value - System.DateTime.UtcNow).TotalSeconds);
            _timerLabel.text = $"{System.Math.Ceiling(remaining):0}s";
            _timerLabel.color = remaining <= 10 ? new Color(1f, 0.4f, 0.35f) : TextBright;
        }

        public void Redraw(GameState state, int viewerPlayerId)
        {
            var player = state.Players[viewerPlayerId];
            var opponent = state.Players[1 - viewerPlayerId];

            UpdateTimerLabel(state);

            bool waiting = player.AugmentPicked && !opponent.AugmentPicked;
            _statusLabel.text = player.AugmentPicked
                ? (waiting ? "waiting for your opponent to choose..." : "")
                : "Pick one — it lasts the rest of the match. One is chosen for you when the timer runs out.";

            _takenLabel.text = player.Augments.Count == 0
                ? ""
                : "YOURS: " + string.Join("   •   ", DisplayNames(player));

            bool hotSeat = _controller.IsHotSeat;
            _hotSeatToggle.SetActive(hotSeat);
            if (hotSeat)
            {
                for (int i = 0; i < 2; i++)
                {
                    bool selected = i == viewerPlayerId;
                    _hotSeatButtons[i].image.color = selected
                        ? new Color(0.25f, 0.55f, 0.95f)
                        : new Color(0.3f, 0.3f, 0.32f);
                    _hotSeatLabels[i].text = state.Players[i].AugmentPicked ? $"P{i + 1} ✓" : $"P{i + 1}";
                }
            }

            for (int i = 0; i < OptionCount; i++)
            {
                bool hasOption = i < player.AugmentOffers.Count;
                _optionRoots[i].SetActive(hasOption);
                if (!hasOption) continue;

                string augmentId = player.AugmentOffers[i];
                var def = AugmentCatalogRuntime.Get(augmentId);

                var rarity = def?.Rarity ?? Rarity.Common;
                Color frame = RarityColor(rarity);
                // Already-picked options go grey and quiet; the pick is done.
                if (player.AugmentPicked) frame *= 0.45f;

                _optionNames[i].text = def != null ? def.DisplayName.ToUpperInvariant() : augmentId;
                _optionNames[i].color = player.AugmentPicked ? TextDim : TextBright;
                _optionDescs[i].text = def != null ? def.Description : "";
                _optionImages[i].color = player.AugmentPicked ? OptionTakenColor : OptionColor;

                _optionBanners[i].color = frame;
                _optionRarities[i].text = rarity.ToString().ToUpperInvariant();
                foreach (var edge in _optionEdges[i]) edge.color = frame;

                // Rebound every redraw: the offers behind these panels change.
                _optionButtons[i].onClick.RemoveAllListeners();
                string picked = augmentId;
                _optionButtons[i].onClick.AddListener(() => _controller.SelectAugment(picked));
                _optionButtons[i].interactable = !player.AugmentPicked;
            }
        }

        string[] DisplayNames(Player player)
        {
            var names = new string[player.Augments.Count];
            for (int i = 0; i < player.Augments.Count; i++)
            {
                var def = AugmentCatalogRuntime.Get(player.Augments[i]);
                names[i] = def != null ? def.DisplayName : player.Augments[i];
            }
            return names;
        }

        // ------------------------------------------------------------------
        // helpers
        // ------------------------------------------------------------------

        /// <summary>Returns the four edge images so the frame can be recolored per
        /// option as the offers change.</summary>
        static Image[] AddOutline(Transform parent, string name, float thickness, Color color)
        {
            var root = AddRect(parent, name, Vector2.zero, Vector2.one);
            return new[]
            {
                AddBar(root, new Vector2(0, 1), new Vector2(1, 1), new Vector2(0, thickness), color),
                AddBar(root, new Vector2(0, 0), new Vector2(1, 0), new Vector2(0, thickness), color),
                AddBar(root, new Vector2(0, 0), new Vector2(0, 1), new Vector2(thickness, 0), color),
                AddBar(root, new Vector2(1, 0), new Vector2(1, 1), new Vector2(thickness, 0), color),
            };
        }

        static Image AddBar(Transform parent, Vector2 anchorMin, Vector2 anchorMax, Vector2 sizeDelta, Color color)
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
            return image;
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

        static Button AddButton(Transform parent, string name, Vector2 anchorMin, Vector2 anchorMax,
                                Vector2 anchoredPosition, Vector2 size, string label, int fontSize,
                                UnityEngine.Events.UnityAction onClick)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image), typeof(Button));
            var rect = (RectTransform)go.transform;
            rect.SetParent(parent, false);
            rect.anchorMin = anchorMin;
            rect.anchorMax = anchorMax;
            rect.anchoredPosition = anchoredPosition;
            rect.sizeDelta = size;

            go.GetComponent<Image>().color = new Color(0.25f, 0.45f, 0.75f);
            var button = go.GetComponent<Button>();
            button.onClick.AddListener(onClick);

            var text = AddLabel(rect, "Label", Vector2.zero, Vector2.one, fontSize, TextAnchor.MiddleCenter);
            text.text = label;
            text.fontStyle = FontStyle.Bold;

            return button;
        }
    }
}
