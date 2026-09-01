using UnityEngine;

namespace Game.Client.View
{
    /// <summary>
    /// An invisible drop zone over a player's stat readout, so a spell that can
    /// hit "anything" (SpellTarget.AnyCharacter) can be aimed at a hero and not
    /// just at guys. PlayerId is set fresh each Redraw, since which half of the
    /// bar is "you" depends on the viewer.
    /// </summary>
    public class HeroDropTarget : MonoBehaviour
    {
        public int PlayerId = -1;
    }
}
