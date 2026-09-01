using System;
using Game.Core.State;
using UnityEngine;
using UnityEngine.EventSystems;

namespace Game.Client.View
{
    /// <summary>
    /// Put on a card cell to report pointer hover in/out, so the owning view can
    /// float an enlarged copy of that card (Marvel Snap / PvZ Heroes style).
    ///
    /// Only the cell wrapper needs this: Unity's EventSystem walks UP the
    /// hierarchy to find a handler, so hovering the card art, its name, or any
    /// other child still resolves to this component.
    ///
    /// The view supplies the callbacks — this deliberately owns no preview
    /// visuals itself, so board/hand cards can reuse it with a different
    /// presentation later.
    /// </summary>
    public class CardHoverTarget : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
    {
        public CardInstance Card;

        /// <summary>Raised with the hovered card and the cell it lives in, so the
        /// view can position the preview relative to what's being hovered.</summary>
        public Action<CardInstance, RectTransform> HoverEnter;
        public Action HoverExit;

        public void OnPointerEnter(PointerEventData eventData) =>
            HoverEnter?.Invoke(Card, (RectTransform)transform);

        public void OnPointerExit(PointerEventData eventData) => HoverExit?.Invoke();

        /// <summary>A destroyed/disabled cell must not leave its preview stuck
        /// on screen — Redraw tears cells down constantly.</summary>
        void OnDisable() => HoverExit?.Invoke();
    }
}
