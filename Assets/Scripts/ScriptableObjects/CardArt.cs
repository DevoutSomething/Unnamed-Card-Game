using System.Collections.Generic;
using UnityEngine;

namespace Game.Cards {

    /// <summary>
    /// A single artwork option for a specific card. A card can have many arts
    /// (base art, alternate arts, full-art versions, ...). Tags let borders and
    /// layouts declare which arts they are allowed to combine with.
    /// File name must match the class name — Unity binds .asset files by it.
    /// </summary>
    [CreateAssetMenu(menuName = "Cards/Skins/Art")]
    public class CardArt : ScriptableObject {
        public string ArtId;
        public string CardId;                 // the card this art belongs to
        public Sprite Image;
        public UnlockSource Unlock = UnlockSource.Default;

        [Tooltip("Free-form tags (e.g. 'holo', 'fullart'). Borders/layouts can require these.")]
        public List<string> Tags = new();

        public bool HasTag(string tag) => Tags != null && Tags.Contains(tag);
    }
}
