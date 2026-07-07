using UnityEngine;

namespace Game.Cards {

    /// <summary>
    /// A card back: like a border, but the art on the back of a card, visible
    /// only when the card is face down. The same for all cards in a deck (so it
    /// can't leak info), which is why it lives on the deck/player, not CardSkin.
    /// File name must match the class name — Unity binds .asset files by it.
    /// </summary>
    [CreateAssetMenu(menuName = "Cards/Skins/Back")]
    public class CardBack : ScriptableObject {
        public string BackId;
        public Sprite Image;
        public UnlockSource Unlock = UnlockSource.Default;

        [Tooltip("When Unlock == Rarity, the back used as the default for this rarity.")]
        public Rarity RarityForDefault;
    }
}
