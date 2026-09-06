using System;
using System.Collections.Generic;
using Game.Core.Abilities;
using Game.Core.Augments;
using Game.Core.Server;
using UnityEngine;

namespace Game.Cards {

    /// <summary>
    /// Unity-side adapter that parses augments.json into the pure-C#
    /// AugmentDatabase, mirroring HeroLoader and AbilityLoader. The pipeline
    /// copies Assets/GameData/augments.json to Resources/Augments/augments.json;
    /// call Bootstrap() once at startup (GameController does). A headless server
    /// reads the same JSON file and calls Parse() with its text.
    /// </summary>
    public static class AugmentLoader {

        public const string ResourcePath = "Augments/augments";

        [Serializable]
        class AbilityRefJson {
            public string id;
            public int x = 1;
        }

        [Serializable]
        class AugmentJson {
            public string augmentId;
            public string displayName;
            public string description;
            public string rarity = "Common";
            public List<string> tags = new();
            public List<AbilityRefJson> abilities = new();
        }

        [Serializable]
        class AugmentsFile {
            public List<AugmentJson> augments = new();
        }

        /// <summary>Load + parse from Resources and configure AugmentCatalogRuntime.</summary>
        public static void Bootstrap() => AugmentCatalogRuntime.Configure(LoadFromResources());

        public static AugmentDatabase LoadFromResources() {
            var text = Resources.Load<TextAsset>(ResourcePath);
            if (text == null) {
                Debug.LogWarning($"AugmentLoader: no TextAsset at Resources/{ResourcePath} — " +
                                 "no augments will be offered. Run Cards > Pipeline > Import All.");
                return new AugmentDatabase();
            }
            return Parse(text.text);
        }

        public static AugmentDatabase Parse(string json) {
            var db = new AugmentDatabase();
            var file = JsonUtility.FromJson<AugmentsFile>(json);
            if (file?.augments == null) {
                Debug.LogError("AugmentLoader: augments.json is malformed (expected { \"augments\": [...] }).");
                return db;
            }

            foreach (var dto in file.augments) {
                if (dto == null || string.IsNullOrWhiteSpace(dto.augmentId)) continue;

                var abilities = new List<AbilityRef>();
                if (dto.abilities != null)
                    foreach (var entry in dto.abilities)
                        if (entry != null && !string.IsNullOrWhiteSpace(entry.id))
                            abilities.Add(new AbilityRef { Id = entry.id, X = entry.x });

                if (!Enum.TryParse<Rarity>(dto.rarity, out var rarity)) {
                    Debug.LogError($"AugmentLoader: augment '{dto.augmentId}' has bad rarity " +
                                   $"'{dto.rarity}', using Common.");
                    rarity = Rarity.Common;
                }

                db.Register(new AugmentDefinition {
                    AugmentId = dto.augmentId,
                    DisplayName = dto.displayName,
                    Description = dto.description,
                    Rarity = rarity,
                    Tags = new List<string>(dto.tags ?? new List<string>()),
                    Abilities = abilities,
                });
            }
            return db;
        }
    }
}
