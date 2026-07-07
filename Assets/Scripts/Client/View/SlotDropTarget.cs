using UnityEngine;

namespace Game.Client.View
{
    /// <summary>
    /// Marks one card slot as a precise place a dragged hand card can be
    /// dropped on — front vs back, not just "somewhere in this lane". SlotIndex
    /// uses the same front(0)/back(1) numbering on both the near and far row
    /// (see BoardView.BuildLanes), so dropping on either row's slot N targets
    /// your own row's slot N: the visual mirror of the row you're not on.
    /// </summary>
    public class SlotDropTarget : MonoBehaviour
    {
        public int LaneIndex;
        public int SlotIndex;
    }
}
