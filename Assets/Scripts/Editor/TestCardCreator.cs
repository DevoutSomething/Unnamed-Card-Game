#if UNITY_EDITOR
using System.Collections.Generic;
using System.IO;
using Game.Core.Abilities;
using UnityEditor;
using UnityEngine;

namespace Game.Cards.EditorTools {

    /// <summary>
    /// One-click creator for an example card to test the card system with.
    /// Run via the menu: Cards → Create Example Card (Knight).
    /// Unity wires the script reference itself, so the asset always binds correctly.
    /// </summary>
    public static class TestCardCreator {

        [MenuItem("Cards/Create Example Card (Knight)")]
        public static void CreateKnight() {
            // Must live under a Resources folder so CardDatabase.Load() discovers it.
            const string dir = "Assets/Resources/Cards";
            Directory.CreateDirectory(dir);

            var knight = ScriptableObject.CreateInstance<GuyCardDefinition>();

            // Identity
            knight.CardId = "knight_01";
            knight.DisplayName = "Knight";

            // Costs  (Cost 1 = energy to play; GoldCost = shop price, not specified -> 0)
            knight.EnergyCost = 1;
            knight.GoldCost = 0;

            // Info
            knight.Description = "Hes a Guy";
            knight.Rarity = Rarity.Common;
            // "Tank" + secondary "Classless" (== Archetype.Colorless in the enum)
            knight.Archetypes = new List<Archetype> { Archetype.Tank, Archetype.Colorless };

            // Guy stats  (Stats "1 | 2" = attack | health)
            knight.BaseAttack = 1;
            knight.BaseHealth = 2;
            knight.Abilities = new List<AbilityRef> { new AbilityRef { Id = "armored", X = 1 } };
            knight.KillRewardGold = 10;

            string path = AssetDatabase.GenerateUniqueAssetPath($"{dir}/Knight.asset");
            AssetDatabase.CreateAsset(knight, path);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            Selection.activeObject = knight;
            EditorGUIUtility.PingObject(knight);
            Debug.Log($"Created example card '{knight.DisplayName}' (id: {knight.CardId}) at {path}");
        }
    }
}
#endif
