using Game.Cards;
using Game.Core.State;
using UnityEngine;

namespace Game.Client.View
{
    /// <summary>
    /// Spawns the on-screen visual for a CardInstance: looks up its definition
    /// (CardDatabase) and skin (CardSkinLibrary), instantiates the skin's layout
    /// prefab, and binds the data. This is the seam between the UnityEngine-free
    /// game state and the rendered UI.
    /// </summary>
    public static class CardViewFactory
    {
        public static CardView Spawn(CardInstance inst, Transform parent,
                                     CardDatabase db, CardSkinLibrary skins)
        {
            if (inst == null)
            {
                Debug.LogError("CardViewFactory: CardInstance is null");
                return null;
            }

            var def = db?.Get(inst.DefinitionId);
            if (def == null)
                Debug.LogWarning($"CardViewFactory: no definition for '{inst.DefinitionId}' — rendering raw id.");

            CardSkin skin = ResolveSkin(def, inst, skins);

            var template = skin?.Layout != null ? skin.Layout.Template : skins?.DefaultLayout?.Template;
            if (template == null)
            {
                Debug.LogError("CardViewFactory: no card layout prefab. Run Cards > Setup > Build Default Card Prefab, " +
                               "then Cards > Pipeline > Import All.");
                return null;
            }

            var view = Object.Instantiate(template, parent).GetComponent<CardView>();
            if (view == null)
            {
                Debug.LogError($"CardViewFactory: layout prefab '{template.name}' has no CardView component.");
                return null;
            }
            view.Bind(def, inst, skin);
            return view;
        }

        /// <summary>Default skin for the card, overridden by the instance's ArtId when set and valid.</summary>
        static CardSkin ResolveSkin(CardDefinition def, CardInstance inst, CardSkinLibrary skins)
        {
            if (skins == null || def == null) return null;

            var skin = skins.ResolveDefault(def);
            if (string.IsNullOrEmpty(inst.ArtId)) return skin;

            var alt = skins.ArtsFor(def.CardId).Find(a => a.ArtId == inst.ArtId);
            if (alt == null)
            {
                Debug.LogWarning($"CardViewFactory: card '{def.CardId}' has no art '{inst.ArtId}', using default.");
                return skin;
            }

            var custom = new CardSkin { Art = alt, Border = skin.Border, Layout = skin.Layout };
            if (custom.IsValidFor(def, out string error)) return custom;

            Debug.LogWarning($"CardViewFactory: art '{inst.ArtId}' on '{def.CardId}' is invalid ({error}), using default.");
            return skin;
        }
    }
}
