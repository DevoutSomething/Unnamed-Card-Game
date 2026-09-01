using System;
using UnityEngine;
using UnityEngine.EventSystems;

namespace Game.Client.View
{
    /// <summary>
    /// Put on a lane's plate so hovering it can blow the lane's rules text up
    /// to a readable size. The plate itself has to stay small — it sits between
    /// two rows of cards — so hover is where the full text lives.
    ///
    /// Deliberately separate from CardHoverTarget: a lane is not a card, and
    /// giving it a fake CardInstance just to reuse that component would be a
    /// lie the preview layer would then have to special-case.
    /// </summary>
    public class LaneHoverTarget : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
    {
        public int LaneIndex;

        /// <summary>Raised with the lane index and the plate's own rect, so the
        /// view can position the enlarged panel over the right column.</summary>
        public Action<int, RectTransform> HoverEnter;
        public Action HoverExit;

        public void OnPointerEnter(PointerEventData eventData) =>
            HoverEnter?.Invoke(LaneIndex, (RectTransform)transform);

        public void OnPointerExit(PointerEventData eventData) => HoverExit?.Invoke();

        /// <summary>A hidden board must not leave its tooltip on screen.</summary>
        void OnDisable() => HoverExit?.Invoke();
    }
}
