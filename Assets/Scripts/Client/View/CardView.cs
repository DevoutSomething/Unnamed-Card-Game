using Game.Cards;
using Game.Core.State;
using UnityEngine;
using UnityEngine.UI;

namespace Game.Client.View
{
    /// <summary>
    /// The visual for one card. Lives on a card layout prefab (the GameObject a
    /// CardLayout.Template points to) with its part references wired up there.
    /// Bind() is the only entry point: give it the data and it fills the visuals.
    /// Spawn these via CardViewFactory, not by hand.
    /// </summary>
    public class CardView : MonoBehaviour
    {
        [Header("Wired by the layout prefab")]
        public Image ArtImage;
        public Image BorderImage;
        public Text NameText;
        public Text CostText;
        public Text DescriptionText;
        public Text AttackText;
        public Text HealthText;

        /// <summary>
        /// Fill the visuals. def may be null (unknown id — shows the raw id),
        /// inst may be null (previewing a definition outside a match), and any
        /// skin part may be null (that part just stays hidden).
        /// </summary>
        public void Bind(CardDefinition def, CardInstance inst, CardSkin skin)
        {
            string title = def != null ? def.DisplayName : (inst?.DefinitionId ?? "???");
            SetText(NameText, title);
            SetText(CostText, (inst?.CurrentCost ?? def?.EnergyCost ?? 0).ToString());
            SetText(DescriptionText, def?.Description ?? "");

            var guy = def as GuyCardDefinition;
            bool isGuy = guy != null;
            SetText(AttackText, isGuy ? (inst?.CurrentAttack ?? guy.BaseAttack).ToString() : null);
            SetText(HealthText, isGuy ? (inst?.CurrentHealth ?? guy.BaseHealth).ToString() : null);

            SetSprite(ArtImage, skin?.Art != null ? skin.Art.Image : null);
            SetSprite(BorderImage, skin?.Border != null ? skin.Border.Frame : null);
        }

        static void SetText(Text target, string value)
        {
            if (target == null) return;
            target.gameObject.SetActive(value != null);
            target.text = value ?? "";
        }

        static void SetSprite(Image target, Sprite sprite)
        {
            if (target == null) return;
            target.enabled = sprite != null;   // a sprite-less Image renders a white box
            target.sprite = sprite;
        }
    }
}
