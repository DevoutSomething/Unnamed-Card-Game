using System.Collections.Generic;
using Game.Cards;
using Game.Core.State;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Game.Client.View
{
    /// <summary>
    /// Attached to each hand-card wrapper. Two things depending on where the
    /// pointer is: while it's still over the hand strip, dragging reorders the
    /// hand live (the wrapper's sibling index moves under the pointer and the
    /// strip's HorizontalLayoutGroup reflows the rest for free); once it
    /// leaves the strip, this becomes a play-to-slot drag exactly as before —
    /// front vs back, auto-placed if not dropped on a specific slot.
    ///
    /// Ending a drag always resolves to exactly one of three outcomes, never
    /// left wherever the pointer happened to release: reordered in hand,
    /// played into a slot, or snapped back to its original spot.
    /// </summary>
    public class DraggableHandCard : MonoBehaviour, IBeginDragHandler, IDragHandler, IEndDragHandler
    {
        public CardInstance Card;
        public GameController Controller;
        public RectTransform HandRoot;

        RectTransform _rect;
        CanvasGroup _canvasGroup;
        LayoutElement _layoutElement;
        Transform _rootCanvasTransform;
        Transform _originalParent;
        int _originalSiblingIndex;
        bool _inHand;   // true while parented under HandRoot (reordering); false while aimed at the board

        void Awake()
        {
            _rect = (RectTransform)transform;
            _canvasGroup = gameObject.AddComponent<CanvasGroup>();
            _layoutElement = GetComponent<LayoutElement>();
        }

        public void OnBeginDrag(PointerEventData eventData)
        {
            _originalParent = _rect.parent;
            _originalSiblingIndex = _rect.GetSiblingIndex();
            _rootCanvasTransform = GetComponentInParent<Canvas>().transform;
            _inHand = true;   // it already is — resting in the hand strip is where every drag starts
            Controller.SetHandDragInProgress(true);

            // Excluded from the hand strip's layout for the whole drag: the
            // layout group would otherwise fight to snap this card back into
            // a grid slot every frame while _rect.position is set to the
            // pointer below. The other (non-ignored) cards still reflow
            // around wherever its sibling index currently sits.
            if (_layoutElement != null) _layoutElement.ignoreLayout = true;

            _canvasGroup.blocksRaycasts = false;
            _canvasGroup.alpha = 0.85f;
        }

        public void OnDrag(PointerEventData eventData)
        {
            _rect.position = eventData.position;

            bool overHand = HandRoot != null && RectTransformUtility.RectangleContainsScreenPoint(
                HandRoot, eventData.position, eventData.pressEventCamera);

            if (overHand)
            {
                if (!_inHand)
                {
                    _rect.SetParent(HandRoot, true);
                    _inHand = true;
                }
                ReorderWithinHand(eventData);
                Controller.ClearDropPreview();
                return;
            }

            if (_inHand)
            {
                _rect.SetParent(_rootCanvasTransform, true);
                _rect.SetAsLastSibling();
                _inHand = false;
            }

            // Hovering anywhere in a lane — including the opponent's half —
            // always previews a slot on this card's own side, since that's
            // the only place it could ever land: the specific front/back slot
            // under the pointer if there is one, else the closest empty one.
            var (laneIndex, slotIndex) = FindDropTarget(eventData);
            if (laneIndex >= 0) Controller.ShowDropPreview(laneIndex, slotIndex, Card.OwnerId);
            else Controller.ClearDropPreview();
        }

        public void OnEndDrag(PointerEventData eventData)
        {
            _canvasGroup.blocksRaycasts = true;
            _canvasGroup.alpha = 1f;
            Controller.ClearDropPreview();
            Controller.SetHandDragInProgress(false);   // before PlayCard below, whose Redraw() may need the hand rebuilt

            var (laneIndex, slotIndex) = FindDropTarget(eventData);
            if (laneIndex >= 0)
            {
                // PlayCard() triggers a Redraw() that rebuilds the real hand/
                // board from state, but this dragged wrapper may no longer be
                // a child of the hand strip, so Redraw() won't clean it up.
                Controller.PlayCard(Card, laneIndex, slotIndex);
                Destroy(gameObject);
                return;
            }

            if (!_inHand)
            {
                // Missed the hand and every lane: back to its original spot,
                // not wherever the pointer happened to release.
                _rect.SetParent(_originalParent, false);
                _rect.SetSiblingIndex(_originalSiblingIndex);
            }

            // Either outcome above rejoins the layout at a real sibling slot —
            // re-include it and force that layout to apply this instant
            // rather than trust it'll happen before the next frame renders.
            // Without this it can sit at wherever the pointer let go (still
            // showing the last _rect.position = eventData.position from
            // OnDrag) until something else happens to trigger a rebuild.
            if (_layoutElement != null) _layoutElement.ignoreLayout = false;
            LayoutRebuilder.ForceRebuildLayoutImmediate((RectTransform)_originalParent);

            if (_inHand) Controller.CommitHandOrder();
        }

        /// <summary>Moves this wrapper's sibling index to match where the
        /// pointer sits among the other hand cards — the layout group does
        /// the rest, sliding everyone else over for free.</summary>
        void ReorderWithinHand(PointerEventData eventData)
        {
            int newIndex = 0;
            for (int i = 0; i < HandRoot.childCount; i++)
            {
                var sibling = HandRoot.GetChild(i);
                if (sibling == _rect) continue;
                if (sibling.position.x < eventData.position.x) newIndex++;
            }
            if (_rect.GetSiblingIndex() != newIndex) _rect.SetSiblingIndex(newIndex);
        }

        /// <summary>
        /// (laneIndex, slotIndex): laneIndex -1 means the pointer isn't over
        /// any lane at all. slotIndex -1 means it's over a lane but not a
        /// specific slot (the divider, layout gaps) — PlayCard's own -1
        /// default then auto-places, same as a slot-less drop always has.
        /// </summary>
        static (int laneIndex, int slotIndex) FindDropTarget(PointerEventData eventData)
        {
            var results = new List<RaycastResult>();
            EventSystem.current.RaycastAll(eventData, results);

            int laneIndex = -1;
            foreach (var result in results)
            {
                var slotTarget = result.gameObject.GetComponentInParent<SlotDropTarget>();
                if (slotTarget != null) return (slotTarget.LaneIndex, slotTarget.SlotIndex);

                if (laneIndex < 0)
                {
                    var lane = result.gameObject.GetComponentInParent<LaneDropTarget>();
                    if (lane != null) laneIndex = lane.LaneIndex;
                }
            }
            return (laneIndex, -1);
        }
    }
}
