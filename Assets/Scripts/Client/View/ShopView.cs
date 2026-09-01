using System;
using Game.Cards;
using Game.Core.Server;
using Game.Core.State;
using UnityEngine;
using UnityEngine.UI;

namespace Game.Client.View
{
    /// <summary>
    /// The shop screen: a full-screen overlay shown only during the Shop slot,
    /// on top of the board. Main view: a scrollable grid of the viewer's shop
    /// offers (click to buy). Header: gold, a live countdown, a free-once-per-
    /// visit "New Deck" reroll, a "Remove Card" button, and Done Shopping.
    ///
    /// "Remove Card" pulls up a full-width overlay over the offers — a
    /// scrollable view of the viewer's current deck, each card with a Remove
    /// button (cost scaling shown live) — doubling as the shop's "look at your
    /// deck" view, since browsing it is exactly what removing from it needs.
    ///
    /// In a hot-seat match (no fixed acting player — see GameController.IsHotSeat)
    /// the Shop slot has no single active player the way an Action slot does,
    /// so a small P1/P2 toggle lets both players act in turn on the one shared
    /// screen; online, ShopViewerId is always the client's own fixed player id
    /// and the toggle is hidden.
    ///
    /// Built once (Build), shown/hidden with SetVisible, rebuilt from scratch
    /// every Redraw — same brute-force philosophy as BoardView.
    /// </summary>
    public class ShopView
    {
        const float CardW = 240f, CardH = 336f;
        const float OfferScale = 0.55f;
        const float DeckScale = 0.4f;

        /// <summary>Extra height each deck cell reserves below the card for its
        /// Remove button.</summary>
        const float DeckCellButtonBand = 40f;

        // Must match CommandResolver's ShopRemoveBaseCost/ShopRemoveCostIncrement —
        // duplicated here only for a live cost-preview label; the server is the
        // sole source of truth and re-validates on every RemoveCardFromDeckCommand.
        const int RemoveBaseCost = 5;
        const int RemoveCostIncrement = 5;

        GameController _controller;
        CardDatabase _db;
        CardSkinLibrary _skins;

        GameObject _canvasRoot;
        Text _goldLabel;
        Text _timerLabel;
        Text _statusLabel;
        Button _rerollButton;
        Text _rerollLabel;
        Button _doneButton;
        Button _removeCardButton;
        RectTransform _offersGrid;

        GameObject _deckPanelRoot;
        Text _deckCountLabel;
        RectTransform _deckGrid;

        GameObject _hotSeatToggle;
        readonly Button[] _hotSeatButtons = new Button[2];
        readonly Text[] _hotSeatLabels = new Text[2];

        // ------------------------------------------------------------------
        // Build (once)
        // ------------------------------------------------------------------

        public void Build(GameController controller, CardDatabase db, CardSkinLibrary skins)
        {
            _controller = controller;
            _db = db;
            _skins = skins;

            var canvasGo = new GameObject("ShopCanvas",
                typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            _canvasRoot = canvasGo;
            var canvas = canvasGo.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 10; // drawn above the board canvas
            var scaler = canvasGo.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);
            scaler.matchWidthOrHeight = 0.5f;

            var bg = AddRect(canvasGo.transform, "Background", Vector2.zero, Vector2.one);
            bg.gameObject.AddComponent<Image>().color = new Color(0.05f, 0.06f, 0.08f, 0.97f);

            BuildHeader(canvasGo.transform);
            _offersGrid = BuildScrollGrid(canvasGo.transform, "Offers",
                new Vector2(0.02f, 0.06f), new Vector2(0.98f, 0.84f), CardW * OfferScale, CardH * OfferScale);
            BuildDeckPanel(canvasGo.transform, new Vector2(0.02f, 0.06f), new Vector2(0.98f, 0.84f));

            _canvasRoot.SetActive(false);
        }

        /// <summary>The "Remove Card" overlay: a solid panel over the same
        /// region the offers grid occupies, with its own sub-header (card
        /// count + a Back button) and the deck's scrollable grid below it.</summary>
        void BuildDeckPanel(Transform canvas, Vector2 anchorMin, Vector2 anchorMax)
        {
            _deckPanelRoot = new GameObject("DeckPanel", typeof(RectTransform), typeof(Image));
            var rect = (RectTransform)_deckPanelRoot.transform;
            rect.SetParent(canvas, false);
            rect.anchorMin = anchorMin;
            rect.anchorMax = anchorMax;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
            _deckPanelRoot.GetComponent<Image>().color = new Color(0.04f, 0.05f, 0.07f, 0.99f);

            var subHeader = AddRect(rect, "SubHeader", new Vector2(0, 1), new Vector2(1, 1));
            subHeader.pivot = new Vector2(0.5f, 1);
            subHeader.sizeDelta = new Vector2(0, 48);
            subHeader.gameObject.AddComponent<Image>().color = new Color(0.12f, 0.1f, 0.16f, 0.95f);

            _deckCountLabel = AddLabel(subHeader, "Count", new Vector2(0, 0), new Vector2(1, 1), 20, TextAnchor.MiddleLeft);
            ((RectTransform)_deckCountLabel.transform).offsetMin = new Vector2(20, 0);

            AddButton(subHeader, "BackButton", new Vector2(1, 0.5f), new Vector2(1, 0.5f),
                new Vector2(-100, 0), new Vector2(180, 38), "Back to Shop", 16,
                () => SetDeckPanelOpen(false));

            var gridArea = AddRect(rect, "GridArea", Vector2.zero, Vector2.one);
            gridArea.offsetMax = new Vector2(0, -48); // leave room for the sub-header

            _deckGrid = BuildScrollGridContent(gridArea, CardW * DeckScale, CardH * DeckScale + DeckCellButtonBand);

            _deckPanelRoot.SetActive(false);
        }

        void SetDeckPanelOpen(bool open) => _deckPanelRoot.SetActive(open);

        void BuildHeader(Transform canvas)
        {
            var bar = AddRect(canvas, "Header", new Vector2(0, 0.86f), new Vector2(1, 1));
            bar.gameObject.AddComponent<Image>().color = new Color(0.15f, 0.13f, 0.05f, 0.95f);

            var title = AddLabel(bar, "Title", new Vector2(0, 0), new Vector2(0.25f, 1), 30, TextAnchor.MiddleLeft);
            title.text = "SHOP";
            title.fontStyle = FontStyle.Bold;
            ((RectTransform)title.transform).offsetMin = new Vector2(24, 0);

            _goldLabel = AddLabel(bar, "Gold", new Vector2(0.25f, 0), new Vector2(0.45f, 1), 24, TextAnchor.MiddleLeft);
            _goldLabel.color = new Color(1f, 0.85f, 0.3f);

            _timerLabel = AddLabel(bar, "Timer", new Vector2(0.45f, 0), new Vector2(0.62f, 1), 26, TextAnchor.MiddleLeft);
            _timerLabel.fontStyle = FontStyle.Bold;

            _statusLabel = AddLabel(bar, "Status", new Vector2(0.62f, 0), new Vector2(1f, 1), 18, TextAnchor.MiddleRight);
            ((RectTransform)_statusLabel.transform).offsetMax = new Vector2(-620, 0);
            _statusLabel.color = new Color(1f, 0.6f, 0.55f);

            _rerollButton = AddButton(bar, "RerollButton", new Vector2(1, 0.5f), new Vector2(1, 0.5f),
                new Vector2(-380, 0), new Vector2(180, 48), "New Deck (free x1)", 16,
                () => _controller.RerollDeck());
            _rerollLabel = _rerollButton.GetComponentInChildren<Text>();

            _removeCardButton = AddButton(bar, "RemoveCardButton", new Vector2(1, 0.5f), new Vector2(1, 0.5f),
                new Vector2(-590, 0), new Vector2(180, 48), "Remove Card", 16,
                () => SetDeckPanelOpen(true));

            _doneButton = AddButton(bar, "DoneButton", new Vector2(1, 0.5f), new Vector2(1, 0.5f),
                new Vector2(-190, 0), new Vector2(170, 48), "Done Shopping", 18,
                () => _controller.EndShop());

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

        /// <summary>A scrollable, cell-sized grid anchored to a sub-region of the
        /// canvas — the same GridLayoutGroup+ScrollRect construction CardGallery
        /// uses for its full-screen debug grid, parameterized down to one panel.</summary>
        static RectTransform BuildScrollGrid(Transform canvas, string name,
            Vector2 anchorMin, Vector2 anchorMax, float cellW, float cellH)
        {
            var panelGo = new GameObject(name + "Panel", typeof(RectTransform), typeof(Image));
            var panelRect = (RectTransform)panelGo.transform;
            panelRect.SetParent(canvas, false);
            panelRect.anchorMin = anchorMin;
            panelRect.anchorMax = anchorMax;
            panelRect.offsetMin = Vector2.zero;
            panelRect.offsetMax = Vector2.zero;
            panelGo.GetComponent<Image>().color = new Color(1f, 1f, 1f, 0.04f);

            return BuildScrollGridContent(panelRect, cellW, cellH);
        }

        /// <summary>The Scroll+clip+GridLayoutGroup content alone, filling
        /// whatever parent rect it's given — factored out of BuildScrollGrid so
        /// BuildDeckPanel can drop it below its own sub-header instead of
        /// filling an entire top-level panel.
        ///
        /// Clips with RectMask2D, NOT Mask: a Mask stencils its children against
        /// its own graphic's alpha, so a see-through mask image (the panel's
        /// background is painted by the parent here, not by this rect) writes an
        /// empty stencil and silently culls every card. RectMask2D clips purely
        /// by rectangle and needs no graphic at all.</summary>
        static RectTransform BuildScrollGridContent(Transform parent, float cellW, float cellH)
        {
            var scrollGo = new GameObject("Scroll", typeof(RectTransform), typeof(ScrollRect), typeof(RectMask2D));
            var scrollRect = (RectTransform)scrollGo.transform;
            scrollRect.SetParent(parent, false);
            scrollRect.anchorMin = Vector2.zero;
            scrollRect.anchorMax = Vector2.one;
            scrollRect.offsetMin = Vector2.zero;
            scrollRect.offsetMax = Vector2.zero;

            var gridGo = new GameObject("Content", typeof(RectTransform), typeof(GridLayoutGroup), typeof(ContentSizeFitter));
            var gridRect = (RectTransform)gridGo.transform;
            gridRect.SetParent(scrollRect, false);
            gridRect.anchorMin = new Vector2(0, 1);
            gridRect.anchorMax = new Vector2(1, 1);
            gridRect.pivot = new Vector2(0.5f, 1);
            gridRect.offsetMin = Vector2.zero;
            gridRect.offsetMax = Vector2.zero;

            var grid = gridGo.GetComponent<GridLayoutGroup>();
            grid.cellSize = new Vector2(cellW, cellH);
            grid.spacing = new Vector2(16, 16);
            grid.padding = new RectOffset(16, 16, 16, 16);
            gridGo.GetComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            var scroll = scrollGo.GetComponent<ScrollRect>();
            scroll.content = gridRect;
            scroll.horizontal = false;

            return gridRect;
        }

        /// <summary>GameController calls this every redraw while CurrentSlotType
        /// is Shop, so "visible" is often already true — only reset the deck
        /// panel closed on the actual hidden->visible transition (a fresh shop
        /// visit), not on every redraw within the same visit.</summary>
        public void SetVisible(bool visible)
        {
            bool wasVisible = _canvasRoot.activeSelf;
            _canvasRoot.SetActive(visible);
            if (visible && !wasVisible) SetDeckPanelOpen(false);
        }

        // ------------------------------------------------------------------
        // Redraw (every event batch, while the Shop slot is active)
        // ------------------------------------------------------------------

        /// <summary>Called every frame while the Shop slot is active (see
        /// GameController.Update) so the countdown ticks smoothly without the
        /// cost of a full Redraw (which respawns every card view). Safe to
        /// call even when the shop canvas is hidden.</summary>
        public void TickCountdown(GameState state) => UpdateTimerLabel(state);

        void UpdateTimerLabel(GameState state)
        {
            if (!state.ShopDeadlineUtc.HasValue)
            {
                _timerLabel.text = "";
                return;
            }

            double remaining = Math.Max(0, (state.ShopDeadlineUtc.Value - DateTime.UtcNow).TotalSeconds);
            _timerLabel.text = $"{Math.Ceiling(remaining):0}s";
            _timerLabel.color = remaining <= 10 ? new Color(1f, 0.4f, 0.35f) : Color.white;
        }

        public void Redraw(GameState state, int viewerPlayerId)
        {
            var player = state.Players[viewerPlayerId];
            var opponent = state.Players[1 - viewerPlayerId];

            UpdateTimerLabel(state);
            _goldLabel.text = $"GOLD {player.Gold}";
            _statusLabel.text = opponent.ShopReady && !player.ShopReady ? "opponent is waiting on you..." : "";

            bool locked = player.ShopReady;
            _rerollButton.interactable = !locked && !player.HasUsedFreeDeckRerollThisVisit;
            _rerollLabel.text = player.HasUsedFreeDeckRerollThisVisit ? "New Deck (used)" : "New Deck (free x1)";
            _doneButton.interactable = !locked;
            _removeCardButton.interactable = !locked;
            _deckCountLabel.text =
                $"YOUR DECK ({CardZones.OwnedCards(state, player).Count} cards — draw pile, hand, and board)";

            bool hotSeat = _controller.IsHotSeat;
            _hotSeatToggle.SetActive(hotSeat);
            if (hotSeat)
            {
                for (int i = 0; i < 2; i++)
                {
                    bool selected = i == viewerPlayerId;
                    _hotSeatButtons[i].image.color = selected ? new Color(0.25f, 0.55f, 0.95f) : new Color(0.3f, 0.3f, 0.32f);
                    _hotSeatLabels[i].text = state.Players[i].ShopReady ? $"P{i + 1} ✓" : $"P{i + 1}";
                }
            }

            RedrawOffers(player, locked);
            RedrawDeck(state, player, locked);
        }

        void RedrawOffers(Player player, bool locked)
        {
            ClearChildren(_offersGrid);

            foreach (var card in player.ShopOffers)
            {
                var def = _db?.Get(card.DefinitionId);
                int price = def?.GoldCost ?? 0;

                bool affordable = player.Gold >= price;
                var wrapper = MakeCardCell(_offersGrid, $"{price}g",
                    affordable ? new Color(1f, 0.85f, 0.3f) : new Color(0.8f, 0.4f, 0.35f));
                var button = wrapper.GetComponent<Button>();
                button.interactable = !locked && affordable;
                button.onClick.AddListener(() => _controller.BuyCard(card));

                var view = CardViewFactory.Spawn(card, wrapper.transform, _db, _skins);
                if (view != null) PlaceCardView(view, OfferScale);
            }
        }

        void RedrawDeck(GameState state, Player player, bool locked)
        {
            ClearChildren(_deckGrid);

            int cost = RemoveBaseCost + RemoveCostIncrement * player.ShopRemovalsThisVisit;
            bool affordable = player.Gold >= cost;

            foreach (var owned in CardZones.OwnedCards(state, player))
            {
                var wrapper = MakeCardCell(_deckGrid, LocationTag(owned.Location), LocationColor(owned.Location));
                // The cell reserves DeckCellButtonBand at the bottom for the
                // button; lift the card by half that so they don't overlap.
                var view = CardViewFactory.Spawn(owned.Card, wrapper.transform, _db, _skins);
                if (view != null) PlaceCardView(view, DeckScale, DeckCellButtonBand * 0.5f);

                var card = owned.Card;
                var removeBtn = AddButton(wrapper.transform, "Remove",
                    new Vector2(0.5f, 0), new Vector2(0.5f, 0), new Vector2(0, 16),
                    new Vector2(CardW * DeckScale - 8, 28), $"Remove ({cost}g)", 13,
                    () => _controller.RemoveCardFromDeck(card));
                removeBtn.interactable = !locked && affordable;
            }
        }

        static string LocationTag(OwnedCardLocation location) => location switch
        {
            OwnedCardLocation.Hand => "HAND",
            OwnedCardLocation.Board => "BOARD",
            _ => "DECK",
        };

        static Color LocationColor(OwnedCardLocation location) => location switch
        {
            OwnedCardLocation.Hand => new Color(0.5f, 0.85f, 1f),
            OwnedCardLocation.Board => new Color(1f, 0.65f, 0.4f),
            _ => new Color(0.75f, 0.75f, 0.8f),
        };

        /// <summary>A clickable card slot: background + Button + CardHolder (the
        /// caller spawns the CardView into it) + an optional corner price tag.</summary>
        static GameObject MakeCardCell(Transform parent, string tag, Color tagColor)
        {
            var wrapper = new GameObject("Cell", typeof(RectTransform), typeof(Image), typeof(Button));
            var rect = (RectTransform)wrapper.transform;
            rect.SetParent(parent, false);
            wrapper.GetComponent<Image>().color = new Color(0, 0, 0, 0.3f);

            if (!string.IsNullOrEmpty(tag))
            {
                var tagLabel = AddLabel(rect, "Tag", new Vector2(0, 1), new Vector2(1, 1), 16, TextAnchor.UpperRight);
                tagLabel.text = tag;
                tagLabel.color = tagColor;
                tagLabel.fontStyle = FontStyle.Bold;
                var tagRect = (RectTransform)tagLabel.transform;
                tagRect.pivot = new Vector2(1, 1);
                tagRect.anchoredPosition = new Vector2(-6, -4);
                tagRect.sizeDelta = new Vector2(80, 24);
            }

            return wrapper;
        }

        /// <param name="yOffset">Shifts the card up inside its cell, so a deck
        /// cell's Remove button (anchored along the bottom) has clear room
        /// instead of sitting on top of the card art.</param>
        static void PlaceCardView(CardView view, float scale, float yOffset = 0f)
        {
            var rect = (RectTransform)view.transform;
            rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = new Vector2(0, yOffset);
            rect.sizeDelta = new Vector2(CardW, CardH);
            rect.localScale = new Vector3(scale, scale, 1f);

            var rootImage = view.GetComponent<Image>();
            if (rootImage != null) rootImage.raycastTarget = false; // clicks go to the wrapper's Button
        }

        // ------------------------------------------------------------------
        // helpers (mirrors BoardView's — kept local rather than shared, since
        // this is the only other view built entirely from code)
        // ------------------------------------------------------------------

        static void ClearChildren(Transform parent)
        {
            for (int i = parent.childCount - 1; i >= 0; i--)
            {
                var child = parent.GetChild(i).gameObject;
                child.SetActive(false);
                UnityEngine.Object.Destroy(child);
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
