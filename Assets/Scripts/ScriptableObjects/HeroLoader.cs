using System;
using System.Collections.Generic;
using Game.Core.Heroes;
using UnityEngine;

namespace Game.Cards {

    /// <summary>
    /// Unity-side adapter that parses heroes.json into the pure-C# HeroDatabase,
    /// mirroring AbilityLoader. The pipeline copies Assets/GameData/heroes.json to
    /// Resources/Heroes/heroes.json; call Bootstrap() once at startup
    /// (GameController does). A headless server reads the same JSON file and calls
    /// Parse() with its text.
    /// </summary>
    public static class HeroLoader {

        public const string ResourcePath = "Heroes/heroes";

        [Serializable]
        class HeroJson {
            public string heroId;
            public string displayName;
            public List<BaseDeckEntryJson> baseDeck = new();
        }

        /// <summary>One base-deck line: a card id and how many copies of it.</summary>
        [Serializable]
        class BaseDeckEntryJson {
            public string cardId;
            public int quantity = 1;
        }

        [Serializable]
        class HeroesFile {
            public List<HeroJson> heroes = new();
        }

        /// <summary>Load + parse from Resources and configure HeroRuntime.</summary>
        public static void Bootstrap() => HeroRuntime.Configure(LoadFromResources());

        public static HeroDatabase LoadFromResources() {
            var text = Resources.Load<TextAsset>(ResourcePath);
            if (text == null) {
                Debug.LogWarning($"HeroLoader: no TextAsset at Resources/{ResourcePath} — " +
                                 "heroes won't resolve. Run Cards > Pipeline > Import All.");
                return new HeroDatabase();
            }
            return Parse(text.text);
        }

        public static HeroDatabase Parse(string json) {
            var db = new HeroDatabase();
            var file = JsonUtility.FromJson<HeroesFile>(json);
            if (file?.heroes == null) {
                Debug.LogError("HeroLoader: heroes.json is malformed (expected { \"heroes\": [...] }).");
                return db;
            }
            foreach (var dto in file.heroes) {
                if (string.IsNullOrWhiteSpace(dto.heroId)) continue;

                var baseDeck = new List<(string CardId, int Quantity)>();
                if (dto.baseDeck != null)
                    foreach (var entry in dto.baseDeck)
                        if (entry != null && !string.IsNullOrWhiteSpace(entry.cardId) && entry.quantity > 0)
                            baseDeck.Add((entry.cardId, entry.quantity));

                db.Register(new HeroDefinition {
                    HeroId = dto.heroId,
                    DisplayName = dto.displayName,
                    BaseDeck = baseDeck,
                });
            }
            return db;
        }
    }
}
