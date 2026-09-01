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

        RectTransform _canvasRect;
        RectTransform _root;
        CardDatabase _db;
        CardSkinLibrary _skins;
        float _scale;
        bool _suppressed;

        public void Build(Transform canvas, CardDatabase db, CardSkinLibrary skins, float scale = 1.3f)
        {
            _canvasRect = (RectTransform)canvas;
            _db = db;
            _skins = skins;
            _scale = scale;

            var go = new GameObject("HoverPreview", typeof(RectTransform));
            _root = (RectTransform)go.transform;
            _root.SetParent(canvas, false);
            _root.anchorMin = Vector2.zero;
            _root.anchorMax = Vector2.one;
            _root.offsetMin = Vector2.zero;
            _root.offsetMax = Vector2.zero;
            go.SetActive(false);
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
                return;
            }

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
