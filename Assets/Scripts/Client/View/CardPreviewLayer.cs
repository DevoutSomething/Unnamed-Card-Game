using Game.Cards;
using Game.Core.State;
using UnityEngine;
using UnityEngine.UI;

namespace Game.Client.View
{
    /// <summary>
    /// A floating, enlarged copy of whatever card the pointer is over
    /// (Marvel Snap / PvZ Heroes style).
    ///
    /// Build() it near-last under a screen's canvas so it draws above that
    /// screen's content, then Attach() it to each card cell. Shared by
    /// ShopView and BoardView so hovering reads identically in the shop, in
    /// hand, and on the board.
    ///
    /// The preview is spawned through the normal CardViewFactory, so it always
    /// shows exactly what the small cell shows — bounty included — just big
    /// enough to actually read.
    /// </summary>
    public class CardPreviewLayer
    {
        const float CardW = 240f, CardH = 336f;

        static readonly Color ReticleColor = new Color(1f, 0.85f, 0.4f, 0.95f);

        RectTransform _canvasRect;
        RectTransform _root;
        CardDatabase _db;
        CardSkinLibrary _skins;
        float _scale;
        bool _suppressed;

        // The reticle frames the small card you're actually pointing at, while
        // the enlarged copy floats above it — together they answer "which one
        // am I on" and "what does it say" at the same time.
        GameObject _reticle;
        RectTransform _reticleRect;

        public void Build(Transform canvas, CardDatabase db, CardSkinLibrary skins, float scale = 1.3f)
        {
            _canvasRect = (RectTransform)canvas;
            _db = db;
            _skins = skins;
            _scale = scale;

            BuildReticle(canvas);

            var go = new GameObject("HoverPreview", typeof(RectTransform));
            _root = (RectTransform)go.transform;
            _root.SetParent(canvas, false);
            _root.anchorMin = Vector2.zero;
            _root.anchorMax = Vector2.one;
            _root.offsetMin = Vector2.zero;
            _root.offsetMax = Vector2.zero;
            go.SetActive(false);
        }

        /// <summary>Four corner brackets rather than a full box: they read as a
        /// targeting reticle and leave the card's own border visible.</summary>
        void BuildReticle(Transform canvas)
        {
            _reticle = new GameObject("HoverReticle", typeof(RectTransform));
            _reticleRect = (RectTransform)_reticle.transform;
            _reticleRect.SetParent(canvas, false);
            _reticleRect.anchorMin = _reticleRect.anchorMax = new Vector2(0.5f, 0.5f);
            _reticleRect.pivot = new Vector2(0.5f, 0.5f);

            const float arm = 20f, thick = 3f;
            for (int cx = 0; cx <= 1; cx++)
            {
                for (int cy = 0; cy <= 1; cy++)
                {
                    var corner = new Vector2(cx, cy);
                    float dx = cx == 0 ? 1f : -1f;
                    float dy = cy == 0 ? 1f : -1f;

                    AddBar(_reticleRect, corner, new Vector2(arm, thick),
                           new Vector2(dx * arm * 0.5f, dy * thick * 0.5f));
                    AddBar(_reticleRect, corner, new Vector2(thick, arm),
                           new Vector2(dx * thick * 0.5f, dy * arm * 0.5f));
                }
            }

            _reticle.SetActive(false);
        }

        static void AddBar(Transform parent, Vector2 corner, Vector2 size, Vector2 offset)
        {
            var go = new GameObject("Bar", typeof(RectTransform), typeof(Image));
            var rect = (RectTransform)go.transform;
            rect.SetParent(parent, false);
            rect.anchorMin = rect.anchorMax = corner;
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.sizeDelta = size;
            rect.anchoredPosition = offset;
            var image = go.GetComponent<Image>();
            image.color = ReticleColor;
            image.raycastTarget = false;
        }

        /// <summary>Frames the hovered cell, matching its size.</summary>
        void PlaceReticle(RectTransform sourceCell)
        {
            if (_reticle == null) return;
            if (sourceCell == null)
            {
                _reticle.SetActive(false);
                return;
            }

            Vector2 screenPoint = RectTransformUtility.WorldToScreenPoint(null, sourceCell.position);
            RectTransformUtility.ScreenPointToLocalPointInRectangle(
                _canvasRect, screenPoint, null, out Vector2 local);

            _reticleRect.anchoredPosition = local;
            _reticleRect.sizeDelta = sourceCell.rect.size + new Vector2(10f, 10f);
            _reticle.SetActive(true);
        }

        /// <summary>
        /// Makes hovering this cell preview <paramref name="card"/>. Reuses an
        /// existing hover component, so calling it every redraw just refreshes
        /// which card a persistent cell (a board slot) points at. Pass a null
        /// card for an empty cell — hovering it then previews nothing.
        /// </summary>
        public void Attach(GameObject cell, CardInstance card)
        {
            if (cell == null || _root == null) return;

            var hover = cell.GetComponent<CardHoverTarget>();
            if (hover == null) hover = cell.AddComponent<CardHoverTarget>();

            hover.Card = card;
            hover.HoverEnter = Show;
            hover.HoverExit = Hide;
        }

        /// <summary>Turns previewing off entirely — used while a hand card is
        /// mid-drag, where a giant card pinned under the cursor is only ever in
        /// the way.</summary>
        public void SetSuppressed(bool suppressed)
        {
            _suppressed = suppressed;
            if (suppressed) Hide();
        }

        public void Show(CardInstance card, RectTransform sourceCell)
        {
            if (_root == null || _suppressed) return;

            ClearChildren(_root);
            if (card == null)
            {
                _root.gameObject.SetActive(false);
                if (_reticle != null) _reticle.SetActive(false);
                return;
            }

            PlaceReticle(sourceCell);

            var view = CardViewFactory.Spawn(card, _root, _db, _skins);
            if (view == null) return;

            var rect = (RectTransform)view.transform;
            rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.sizeDelta = new Vector2(CardW, CardH);
            rect.localScale = new Vector3(_scale, _scale, 1f);
            rect.anchoredPosition = PositionFor(sourceCell);

            // The preview must never eat the pointer: hovering it would fire
            // OnPointerExit on the cell keeping it open, and flicker forever.
            foreach (var graphic in _root.GetComponentsInChildren<Graphic>(true))
                graphic.raycastTarget = false;

            _root.gameObject.SetActive(true);
        }

        public void Hide()
        {
            if (_reticle != null) _reticle.SetActive(false);
            if (_root == null) return;
            ClearChildren(_root);
            _root.gameObject.SetActive(false);
        }

        /// <summary>Centres the preview on the hovered cell, nudged up clear of
        /// the cursor, then clamped so a card hovered at the very edge of the
        /// screen still shows in full.</summary>
        Vector2 PositionFor(RectTransform sourceCell)
        {
            float w = CardW * _scale;
            float h = CardH * _scale;

            Vector2 local = Vector2.zero;
            if (sourceCell != null)
            {
                Vector2 screenPoint = RectTransformUtility.WorldToScreenPoint(null, sourceCell.position);
                RectTransformUtility.ScreenPointToLocalPointInRectangle(
                    _canvasRect, screenPoint, null, out local);
            }

            local.y += h * 0.30f;

            Vector2 canvasSize = _canvasRect.rect.size;
            float maxX = Mathf.Max(0f, (canvasSize.x - w) * 0.5f);
            float maxY = Mathf.Max(0f, (canvasSize.y - h) * 0.5f);
            local.x = Mathf.Clamp(local.x, -maxX, maxX);
            local.y = Mathf.Clamp(local.y, -maxY, maxY);
            return local;
        }

        static void ClearChildren(Transform parent)
        {
            for (int i = parent.childCount - 1; i >= 0; i--)
            {
                var child = parent.GetChild(i).gameObject;
                child.SetActive(false);        // hide now; Destroy is end-of-frame
                Object.Destroy(child);
            }
        }
    }
}
