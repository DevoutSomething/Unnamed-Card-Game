using System;
using System.Collections.Generic;
using Game.Core.Abilities;
using UnityEngine;

namespace Game.Cards {

    /// <summary>
    /// Unity-side adapter that parses abilities.json into the pure-C#
    /// AbilityDatabase. The pipeline copies Assets/GameData/abilities.json to
    /// Resources/Abilities/abilities.json; call Bootstrap() once at startup
    /// (CardGallery does; a real bootstrap scene should too). A headless server
    /// reads the same JSON file and calls Parse() with its text.
    /// </summary>
    public static class AbilityLoader {

        public const string ResourcePath = "Abilities/abilities";

        [Serializable]
        class AbilityJson {
            public string abilityId;
            public string displayName;
            public string description;
            public string trigger = "Passive";
            public string effect;
            public string target = "Self";
            public string statusId;
            // costPerX / flatCost also live in the JSON but are only read by
            // tools/cards.py cost-check — the runtime doesn't need them.
        }

        [Serializable]
        class AbilitiesFile {
            public List<AbilityJson> abilities = new();
        }

        /// <summary>Load + parse from Resources and configure AbilityRuntime.</summary>
        public static void Bootstrap() => AbilityRuntime.Configure(LoadFromResources());

        public static AbilityDatabase LoadFromResources() {
            var text = Resources.Load<TextAsset>(ResourcePath);
            if (text == null) {
                Debug.LogWarning($"AbilityLoader: no TextAsset at Resources/{ResourcePath} — " +
                                 "abilities won't resolve. Run Cards > Pipeline > Import All.");
                return new AbilityDatabase();
            }
            return Parse(text.text);
        }

        public static AbilityDatabase Parse(string json) {
            var db = new AbilityDatabase();
            var file = JsonUtility.FromJson<AbilitiesFile>(json);
            if (file?.abilities == null) {
                Debug.LogError("AbilityLoader: abilities.json is malformed (expected { \"abilities\": [...] }).");
                return db;
            }
            foreach (var dto in file.abilities) {
                if (string.IsNullOrWhiteSpace(dto.abilityId)) continue;
                var def = new AbilityDefinition {
                    AbilityId = dto.abilityId,
                    DisplayName = dto.displayName,
                    DescriptionTemplate = dto.description,
                    Trigger = ParseEnum(dto.trigger, AbilityTrigger.Passive, dto.abilityId),
                    Effect = ParseEnum(dto.effect, AbilityEffect.ApplyStatus, dto.abilityId),
                    Target = ParseEnum(dto.target, AbilityTarget.Self, dto.abilityId),
                    StatusId = dto.statusId,
                };
                db.Register(def);
            }
            return db;
        }

        static T ParseEnum<T>(string value, T fallback, string abilityId) where T : struct {
            if (Enum.TryParse(value, out T parsed)) return parsed;
            Debug.LogError($"AbilityLoader: ability '{abilityId}' has bad {typeof(T).Name} '{value}', using {fallback}.");
            return fallback;
        }
    }
}
