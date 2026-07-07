using System.Collections.Generic;
using UnityEngine;

namespace Game.Cards {

    /// <summary>
    /// A card template that determines where art / name / costs / text / abilities
    /// are rendered. Most cards share one default layout; special skins swap it for
    /// a different template prefab. Can also be gated by art/card tags.
    /// File name must match the class name — Unity binds .asset files by it.
    /// </summary>
    [CreateAssetMenu(menuName = "Cards/Skins/Layout")]
    public class CardLayout : ScriptableObject {
        public string LayoutId;

        [Tooltip("Prefab that defines where each element renders for this layout.")]
        public GameObject Template;

        [Tooltip("If non-empty, this layout is only valid with arts that have ALL of these tags.")]
        public List<string> RequiredArtTags = new();

        [Tooltip("If non-empty, this layout is only valid on cards that have ALL of these meta tags (CardDefinition.Tags).")]
        public List<string> RequiredCardTags = new();

        public bool IsCompatibleWith(CardArt art) => CardBorder.HasAllTags(art, RequiredArtTags);
        public bool IsCompatibleWith(CardDefinition card) => CardBorder.HasAllCardTags(card, RequiredCardTags);
    }
}
