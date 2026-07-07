using System.Collections.Generic;
using Game.Cards;
using Game.Core.State;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Game.Client.View
{
    /// <summary>
    /// Attached to each hand-card wrapper. Lets the player drag the card onto a
    /// lane to play it, instead of clicking to select then clicking a lane.
    /// Dropping anywhere that isn't a lane just snaps the card back to hand.
    /// </summary>
    public class DraggableHandCard : MonoBehaviour, IBeginDragHandler, IDragHandler, IEndDragHandler
    {
        public CardInstance Card;
        public GameController Controller;

        RectTransform _rect;
        CanvasGroup _canvasGroup;
        Transform _originalParent;
        int _originalSiblingIndex;

        void Awake()
        {
            _rect = (RectTransform)transform;
            _canvasGroup = gameObject.AddComponent<CanvasGroup>();
        }

        public void OnBeginDrag(PointerEventData eventData)
        {
            _originalParent = _rect.parent;
            _originalSiblingIndex = _rect.GetSiblingIndex();

            var rootCanvas = GetComponentInParent<Canvas>();

            _rect.SetParent(rootCanvas.transform, true);
            _rect.SetAsLastSibling();

            _canvasGroup.blocksRaycasts = false;    
            _canvasGroup.alpha = 0.85f;
        }

        public void OnDrag(PointerEventData eventData)
        {
            _rect.position = eventData.position;
        }

        public void OnEndDrag(PointerEventData eventData)
        {
            _canvasGroup.blocksRaycasts = true;
            _canvasGroup.alpha = 1f;

            var lane = FindLaneUnderPointer(eventData);
            if (lane != null)
            {
                // PlayCard() triggers a Redraw() that rebuilds the real hand/
                // board from state, but this dragged wrapper was reparented
                // onto the canvas root in OnBeginDrag, so it's no longer a
                // child of the hand strip and Redraw() won't clean it up.
                Controller.PlayCard(Card, lane.LaneIndex);
                Destroy(gameObject);
                return;
            }

            // Missed every lane: snap back to its spot in the hand.
            _rect.SetParent(_originalParent, false);
            _rect.SetSiblingIndex(_originalSiblingIndex);
        }

        static LaneDropTarget FindLaneUnderPointer(PointerEventData eventData)
        {
            var results = new List<RaycastResult>();
            EventSystem.current.RaycastAll(eventData, results);
            foreach (var result in results)
            {
                var lane = result.gameObject.GetComponentInParent<LaneDropTarget>();
                if (lane != null) return lane;
            }
            return null;
        }
    }
}
