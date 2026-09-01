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

        /// <summary>Which player's guy actually stands here, set fresh each
        /// Redraw (the near/far rows swap by viewer, so this can't be baked in
        /// at build time). Guy drops ignore it — they always go to your own
        /// row — but a spell has to know whose guy it just got dropped on.</summary>
        public int OwnerPlayerId = -1;
    }
}
