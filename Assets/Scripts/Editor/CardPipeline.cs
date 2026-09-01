#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Game.Core.Abilities;
using UnityEditor;
using UnityEngine;

namespace Game.Cards.EditorTools {

    /// <summary>
    /// The JSON &lt;-&gt; asset pipeline. Card JSON in Assets/GameData/cards is the
    /// source of truth for card data; these menu items bake it into the .asset
    /// files the game actually loads (and back, for cards edited in the Inspector).
    /// See CARD_PIPELINE_PLAN.md for the full workflow.
    /// </summary>
    public static class CardPipeline {

        public const string CardJsonDir = "Assets/GameData/cards";
        public const string CardArtDir = "Assets/GameData/art/cards";
        public const string BorderArtDir = "Assets/GameData/art/borders";
        public const string BordersJsonPath = "Assets/GameData/borders.json";
        public const string AbilitiesJsonPath = "Assets/GameData/abilities.json";
        public const string CardAssetDir = "Assets/Resources/Cards";
        public const string ArtAssetDir = "Assets/Resources/Skins/Arts";
        public const string BorderAssetDir = "Assets/Resources/Skins/Borders";
        public const string AbilityAssetDir = "Assets/Resources/Abilities";

        // ------------------------------------------------------------ paths
        // AssetDatabase APIs need project-relative paths ("Assets/...") but all
        // System.IO calls must use absolute ones: Unity's synchronous asset
        // imports (AssetDatabase.ImportAsset / SaveAndReimport) can move the
        // process's current directory mid-run — especially in batch mode — which
        // silently breaks relative File/Directory calls that come after them.

        static string ProjectRoot => Path.GetDirectoryName(Application.dataPath);

        /// <summary>Project-relative -> absolute, for System.IO.</summary>
        internal static string Abs(string projectRelative) =>
            Path.Combine(ProjectRoot, projectRelative).Replace('\\', '/');

        /// <summary>Absolute -> project-relative, for AssetDatabase.</summary>
        internal static string Rel(string absolute) {
            string norm = absolute.Replace('\\', '/');
            string root = ProjectRoot.Replace('\\', '/') + "/";
            return norm.StartsWith(root) ? norm.Substring(root.Length) : norm;
        }

        // ---------------------------------------------------------------- DTOs
        // Field names must match the JSON keys exactly (JsonUtility rule).

        [Serializable]
        public class AbilityRefJson {
            public string id;
            public int x = 1;
        }

        /// <summary>Mirrors ConjureSpec. `count` defaults to 0 so a card whose
        /// JSON omits the whole block simply doesn't conjure.</summary>
        [Serializable]
        public class ConjureJson {
            public int count;
            public string kind = "Any";
            public string rarityFilter = "Any";
            public string rarity = "Common";
            public List<string> archetypes = new();
            public List<string> requiredTags = new();
            public bool filterByEnergyCost;
            public int minEnergyCost;
            public int maxEnergyCost;
            public int costReduction;
        }

        [Serializable]
        public class CardJson {
            public string cardId;
            public string type;          // "guy" | "spell"
            public string displayName;
            public int energyCost;
            public int goldCost;
            public string description;
            public string rarity;
            public List<string> archetypes = new();
            public List<string> tags = new();
            public int baseAttack;
            public int baseHealth = 1;
            public List<AbilityRefJson> abilities = new();
            public int killRewardGold;

            public ConjureJson conjure = new();

            // Spell-only. Ignored on guys.
            public string spellTarget = "None";
            public int damageAmount;
            public int healAmount;
            public int buffAttack;
            public int buffHealth;
            public int drawCount;
        }

        [Serializable]
        public class BorderJson {
            public string borderId;
            public string unlock = "Default";
            public string rarityForDefault = "Common";
            public List<string> requiredArtTags = new();
            public List<string> requiredCardTags = new();
        }

        [Serializable]
        public class BordersFile {
            public List<BorderJson> borders = new();
        }

        // ---------------------------------------------------------------- Import

        [MenuItem("Cards/Pipeline/Import All (JSON to Assets)")]
        public static void ImportAll() {
            int abilities = ImportAbilities();
            int cards = ImportCardJson();
            int arts = ImportCardArt();
            int borders = ImportBorders();
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log($"[CardPipeline] Import done: {cards} card(s), {abilities} abilities, {arts} art(s), {borders} border(s).");
        }

        /// <summary>Publishes abilities.json into Resources so AbilityLoader finds it at runtime.</summary>
        static int ImportAbilities() {
            if (!File.Exists(Abs(AbilitiesJsonPath))) {
                Debug.LogWarning($"[CardPipeline] {AbilitiesJsonPath} not found — cards with abilities won't resolve them.");
                return 0;
            }
            Directory.CreateDirectory(Abs(AbilityAssetDir));
            File.Copy(Abs(AbilitiesJsonPath), Abs($"{AbilityAssetDir}/abilities.json"), overwrite: true);
            AssetDatabase.ImportAsset($"{AbilityAssetDir}/abilities.json");
            return AbilityLoader.Parse(File.ReadAllText(Abs(AbilitiesJsonPath))).Count;
        }

        static int ImportCardJson() {
            Directory.CreateDirectory(Abs(CardAssetDir));
            var validIds = new HashSet<string>();
            int count = 0;

            foreach (string jsonPath in FindFiles(Abs(CardJsonDir), "*.json")) {
                var dto = JsonUtility.FromJson<CardJson>(File.ReadAllText(jsonPath));
                if (dto == null || string.IsNullOrWhiteSpace(dto.cardId)) {
                    Debug.LogError($"[CardPipeline] {jsonPath}: missing/empty cardId, skipped.");
                    continue;
                }
                bool isGuy = dto.type == "guy";
                if (!isGuy && dto.type != "spell") {
                    Debug.LogError($"[CardPipeline] {jsonPath}: unknown type '{dto.type}', skipped.");
                    continue;
                }

                string assetPath = $"{CardAssetDir}/{dto.cardId}.asset";
                var existing = AssetDatabase.LoadAssetAtPath<CardDefinition>(assetPath);

                // Type changed (guy <-> spell): the asset's C# class must change, so recreate it.
                if (existing != null && (existing is GuyCardDefinition) != isGuy) {
                    AssetDatabase.DeleteAsset(assetPath);
                    existing = null;
                }
                if (existing == null) {
                    existing = isGuy
                        ? ScriptableObject.CreateInstance<GuyCardDefinition>()
                        : ScriptableObject.CreateInstance<SpellCardDefinition>();
                    AssetDatabase.CreateAsset(existing, assetPath);
                }

                ApplyJsonToDefinition(dto, existing);
                EditorUtility.SetDirty(existing);
                validIds.Add(dto.cardId);
                count++;
            }

            // Assets whose JSON was deleted: warn, never silently delete data.
            foreach (string guid in AssetDatabase.FindAssets("t:CardDefinition", new[] { CardAssetDir })) {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                var def = AssetDatabase.LoadAssetAtPath<CardDefinition>(path);
                if (def != null && !validIds.Contains(def.CardId))
                    Debug.LogWarning($"[CardPipeline] ORPHAN: {path} (CardId '{def.CardId}') has no JSON. " +
                                     "Delete the asset, or run 'Export All' if the asset is the version you want to keep.");
            }
            return count;
        }

        static void ApplyJsonToDefinition(CardJson dto, CardDefinition def) {
            def.name = dto.cardId; // asset object name
            def.CardId = dto.cardId;
            def.DisplayName = dto.displayName;
            def.EnergyCost = dto.energyCost;
            def.GoldCost = dto.goldCost;
            def.Description = dto.description;
            def.Rarity = ParseEnum(dto.rarity, Rarity.Common, dto.cardId, "rarity");
            def.Archetypes = dto.archetypes
                .Select(a => ParseEnum(a, Archetype.Colorless, dto.cardId, "archetype"))
                .ToList();
            def.Tags = new List<string>(dto.tags);

            var conjure = dto.conjure ?? new ConjureJson();
            def.Conjure = new ConjureSpec {
                Count = conjure.count,
                Kind = ParseEnum(conjure.kind, ConjureKind.Any, dto.cardId, "conjure.kind"),
                RarityFilter = ParseEnum(conjure.rarityFilter, ConjureRarityFilter.Any, dto.cardId, "conjure.rarityFilter"),
                Rarity = ParseEnum(conjure.rarity, Rarity.Common, dto.cardId, "conjure.rarity"),
                Archetypes = conjure.archetypes
                    .Select(a => ParseEnum(a, Archetype.Colorless, dto.cardId, "conjure.archetype"))
                    .ToList(),
                RequiredTags = new List<string>(conjure.requiredTags),
                FilterByEnergyCost = conjure.filterByEnergyCost,
                MinEnergyCost = conjure.minEnergyCost,
                MaxEnergyCost = conjure.maxEnergyCost,
                CostReduction = conjure.costReduction,
            };

            if (def is GuyCardDefinition guy) {
                guy.BaseAttack = dto.baseAttack;
                guy.BaseHealth = dto.baseHealth;
                guy.Abilities = dto.abilities
                    .Where(a => a != null && !string.IsNullOrWhiteSpace(a.id))
                    .Select(a => new AbilityRef { Id = a.id, X = a.x })
                    .ToList();
                guy.KillRewardGold = dto.killRewardGold;
            }
            else if (def is SpellCardDefinition spell) {
                spell.Target = ParseEnum(dto.spellTarget, SpellTarget.None, dto.cardId, "spellTarget");
                spell.DamageAmount = dto.damageAmount;
                spell.HealAmount = dto.healAmount;
                spell.BuffAttack = dto.buffAttack;
                spell.BuffHealth = dto.buffHealth;
                spell.DrawCount = dto.drawCount;
            }
        }

        static T ParseEnum<T>(string value, T fallback, string cardId, string label) where T : struct {
            if (Enum.TryParse(value, out T parsed)) return parsed;
            Debug.LogError($"[CardPipeline] card '{cardId}': bad {label} '{value}', using {fallback}.");
            return fallback;
        }

        /// <summary>PNGs named {cardId}__{artId}.png become Sprite + CardArt assets.
        /// On update only the sprite link is refreshed, so tags/unlock hand-edited
        /// on an existing CardArt asset survive re-imports.</summary>
        static int ImportCardArt() {
            Directory.CreateDirectory(Abs(ArtAssetDir));
            int count = 0;
            foreach (string pngPath in FindFiles(Abs(CardArtDir), "*.png")) {
                string stem = Path.GetFileNameWithoutExtension(pngPath);
                int sep = stem.IndexOf("__", StringComparison.Ordinal);
                if (sep <= 0 || sep + 2 >= stem.Length) {
                    Debug.LogError($"[CardPipeline] {pngPath}: name must be {{cardId}}__{{artId}}.png, skipped.");
                    continue;
                }
                string cardId = stem.Substring(0, sep);
                string artId = stem.Substring(sep + 2);

                var sprite = LoadAsSprite(pngPath);
                if (sprite == null) continue;

                string assetPath = $"{ArtAssetDir}/{stem}.asset";
                var art = AssetDatabase.LoadAssetAtPath<CardArt>(assetPath);
                if (art == null) {
                    art = ScriptableObject.CreateInstance<CardArt>();
                    art.Unlock = UnlockSource.Default;
                    AssetDatabase.CreateAsset(art, assetPath);
                }
                art.name = stem;
                art.ArtId = artId;
                art.CardId = cardId;
                art.Image = sprite;
                EditorUtility.SetDirty(art);
                count++;
            }
            return count;
        }

        static int ImportBorders() {
            if (!File.Exists(Abs(BordersJsonPath))) {
                Debug.LogWarning($"[CardPipeline] {BordersJsonPath} not found, borders skipped.");
                return 0;
            }
            Directory.CreateDirectory(Abs(BorderAssetDir));
            var file = JsonUtility.FromJson<BordersFile>(File.ReadAllText(Abs(BordersJsonPath)));
            int count = 0;
            foreach (var dto in file.borders) {
                if (string.IsNullOrWhiteSpace(dto.borderId)) continue;

                string pngPath = $"{BorderArtDir}/{dto.borderId}.png";
                Sprite frame = File.Exists(Abs(pngPath)) ? LoadAsSprite(pngPath) : null;
                if (frame == null)
                    Debug.LogWarning($"[CardPipeline] border '{dto.borderId}': no PNG at {pngPath} " +
                                     "(run Cards/Setup/Build Default Card Prefab to generate placeholders).");

                string assetPath = $"{BorderAssetDir}/{dto.borderId}.asset";
                var border = AssetDatabase.LoadAssetAtPath<CardBorder>(assetPath);
                if (border == null) {
                    border = ScriptableObject.CreateInstance<CardBorder>();
                    AssetDatabase.CreateAsset(border, assetPath);
                }
                border.name = dto.borderId;
                border.BorderId = dto.borderId;
                border.Frame = frame;
                border.Unlock = ParseEnum(dto.unlock, UnlockSource.Default, dto.borderId, "unlock");
                border.RarityForDefault = ParseEnum(dto.rarityForDefault, Rarity.Common, dto.borderId, "rarityForDefault");
                border.RequiredArtTags = new List<string>(dto.requiredArtTags);
                border.RequiredCardTags = new List<string>(dto.requiredCardTags);
                EditorUtility.SetDirty(border);
                count++;
            }
            return count;
        }

        // ---------------------------------------------------------------- Export

        [MenuItem("Cards/Pipeline/Export All (Assets to JSON)")]
        public static void ExportAll() {
            Directory.CreateDirectory(Abs(CardJsonDir));
            int count = 0;
            foreach (string guid in AssetDatabase.FindAssets("t:CardDefinition", new[] { CardAssetDir })) {
                var def = AssetDatabase.LoadAssetAtPath<CardDefinition>(AssetDatabase.GUIDToAssetPath(guid));
                if (def == null || string.IsNullOrWhiteSpace(def.CardId)) continue;

                var dto = new CardJson {
                    cardId = def.CardId,
                    type = def is GuyCardDefinition ? "guy" : "spell",
                    displayName = def.DisplayName,
                    energyCost = def.EnergyCost,
                    goldCost = def.GoldCost,
                    description = def.Description,
                    rarity = def.Rarity.ToString(),
                    archetypes = def.Archetypes.Select(a => a.ToString()).ToList(),
                    tags = new List<string>(def.Tags),
                };
                if (def.Conjure != null) {
                    dto.conjure = new ConjureJson {
                        count = def.Conjure.Count,
                        kind = def.Conjure.Kind.ToString(),
                        rarityFilter = def.Conjure.RarityFilter.ToString(),
                        rarity = def.Conjure.Rarity.ToString(),
                        archetypes = def.Conjure.Archetypes.Select(a => a.ToString()).ToList(),
                        requiredTags = new List<string>(def.Conjure.RequiredTags),
                        filterByEnergyCost = def.Conjure.FilterByEnergyCost,
                        minEnergyCost = def.Conjure.MinEnergyCost,
                        maxEnergyCost = def.Conjure.MaxEnergyCost,
                        costReduction = def.Conjure.CostReduction,
                    };
                }
                if (def is GuyCardDefinition guy) {
                    dto.baseAttack = guy.BaseAttack;
                    dto.baseHealth = guy.BaseHealth;
                    dto.abilities = guy.Abilities
                        .Select(a => new AbilityRefJson { id = a.Id, x = a.X })
                        .ToList();
                    dto.killRewardGold = guy.KillRewardGold;
                }
                if (def is SpellCardDefinition spell) {
                    dto.spellTarget = spell.Target.ToString();
                    dto.damageAmount = spell.DamageAmount;
                    dto.healAmount = spell.HealAmount;
                    dto.buffAttack = spell.BuffAttack;
                    dto.buffHealth = spell.BuffHealth;
                    dto.drawCount = spell.DrawCount;
                }
                File.WriteAllText(Abs($"{CardJsonDir}/{def.CardId}.json"),
                                  JsonUtility.ToJson(dto, prettyPrint: true) + "\n");
                count++;
            }
            AssetDatabase.Refresh();
            Debug.Log($"[CardPipeline] Exported {count} card(s) to {CardJsonDir}.");
        }

        // ---------------------------------------------------------------- Validate

        [MenuItem("Cards/Pipeline/Validate")]
        public static void Validate() {
            int problems = 0;
            var ids = new Dictionary<string, string>();
            var abilityDb = File.Exists(Abs(AbilitiesJsonPath))
                ? AbilityLoader.Parse(File.ReadAllText(Abs(AbilitiesJsonPath)))
                : new AbilityDatabase();

            foreach (string guid in AssetDatabase.FindAssets("t:CardDefinition", new[] { CardAssetDir })) {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                var def = AssetDatabase.LoadAssetAtPath<CardDefinition>(path);
                if (string.IsNullOrWhiteSpace(def.CardId)) { Debug.LogError($"[Validate] {path}: empty CardId"); problems++; continue; }
                if (ids.TryGetValue(def.CardId, out var other)) { Debug.LogError($"[Validate] duplicate CardId '{def.CardId}': {path} and {other}"); problems++; }
                ids[def.CardId] = path;
                if (string.IsNullOrWhiteSpace(def.DisplayName)) { Debug.LogWarning($"[Validate] {path}: empty DisplayName"); problems++; }
                if (def is GuyCardDefinition g && g.BaseHealth <= 0) { Debug.LogError($"[Validate] {path}: guy BaseHealth must be >= 1"); problems++; }
                if (!File.Exists(Abs($"{CardJsonDir}/{def.CardId}.json"))) { Debug.LogWarning($"[Validate] {path}: no JSON source (orphan — see Import log)"); problems++; }
                if (def is GuyCardDefinition guy)
                    foreach (var ability in guy.Abilities)
                        if (ability != null && !abilityDb.TryGet(ability.Id, out _)) {
                            Debug.LogError($"[Validate] {path}: unknown ability '{ability.Id}' (not in abilities.json)");
                            problems++;
                        }
            }

            foreach (string guid in AssetDatabase.FindAssets("t:CardArt", new[] { ArtAssetDir })) {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                var art = AssetDatabase.LoadAssetAtPath<CardArt>(path);
                if (art.Image == null) { Debug.LogError($"[Validate] {path}: missing sprite"); problems++; }
                if (!ids.ContainsKey(art.CardId)) { Debug.LogWarning($"[Validate] {path}: art for unknown card '{art.CardId}'"); problems++; }
            }

            // Cards with no base art render with the placeholder tint, which is fine
            // for prototyping — surface it as info-level noise only in the summary.
            int missingArt = ids.Keys.Count(id =>
                AssetDatabase.LoadAssetAtPath<CardArt>($"{ArtAssetDir}/{id}__base.asset") == null);

            bool hasDefaultLayout = AssetDatabase.FindAssets("t:CardLayout", new[] { "Assets/Resources/Skins" })
                .Select(g => AssetDatabase.LoadAssetAtPath<CardLayout>(AssetDatabase.GUIDToAssetPath(g)))
                .Any(l => l != null && l.LayoutId == CardSkinLibrary.DefaultLayoutId);
            if (!hasDefaultLayout) {
                Debug.LogError("[Validate] no CardLayout with id 'default' — run Cards/Setup/Build Default Card Prefab.");
                problems++;
            }

            Debug.Log($"[Validate] {ids.Count} card(s): {problems} problem(s), {missingArt} without base art " +
                      "(use Cards/Pipeline/Create Placeholder Art).");
        }

        // ---------------------------------------------------------------- Placeholder art

        [MenuItem("Cards/Pipeline/Create Placeholder Art")]
        public static void CreatePlaceholderArt() {
            Directory.CreateDirectory(Abs(CardArtDir));
            int made = 0;
            foreach (string jsonPath in FindFiles(Abs(CardJsonDir), "*.json")) {
                var dto = JsonUtility.FromJson<CardJson>(File.ReadAllText(jsonPath));
                if (dto == null || string.IsNullOrWhiteSpace(dto.cardId)) continue;
                string pngPath = Abs($"{CardArtDir}/{dto.cardId}__base.png");
                if (File.Exists(pngPath)) continue;

                // Deterministic color per card so placeholders are telling-apart-able.
                float hue = (StableHash(dto.cardId) % 360) / 360f;
                WriteSolidPng(pngPath, Color.HSVToRGB(hue, 0.45f, 0.75f), 512, 512);
                made++;
            }
            AssetDatabase.Refresh();
            if (made > 0) ImportAll();
            Debug.Log($"[CardPipeline] Created {made} placeholder art PNG(s).");
        }

        // ---------------------------------------------------------------- helpers

        static IEnumerable<string> FindFiles(string dir, string pattern) {
            if (!Directory.Exists(dir)) yield break;
            foreach (string f in Directory.GetFiles(dir, pattern, SearchOption.AllDirectories))
                yield return f.Replace('\\', '/');
        }

        internal static Sprite LoadAsSprite(string pngPath) {
            pngPath = Rel(pngPath);   // AssetDatabase wants "Assets/..." paths
            var importer = AssetImporter.GetAtPath(pngPath) as TextureImporter;
            if (importer == null) { AssetDatabase.ImportAsset(pngPath); importer = AssetImporter.GetAtPath(pngPath) as TextureImporter; }
            if (importer == null) { Debug.LogError($"[CardPipeline] cannot import {pngPath}"); return null; }
            // Must be Sprite AND Single: any other spriteImportMode (e.g. the
            // Multiple that batch imports default to) produces no Sprite
            // sub-asset at all, so LoadAssetAtPath<Sprite> comes back null.
            if (importer.textureType != TextureImporterType.Sprite
                || importer.spriteImportMode != SpriteImportMode.Single) {
                importer.textureType = TextureImporterType.Sprite;
                importer.spriteImportMode = SpriteImportMode.Single;
                importer.SaveAndReimport();
            }
            return AssetDatabase.LoadAssetAtPath<Sprite>(pngPath);
        }

        internal static void WriteSolidPng(string path, Color color, int width, int height) {
            var tex = new Texture2D(width, height, TextureFormat.RGBA32, false);
            var pixels = new Color[width * height];
            for (int i = 0; i < pixels.Length; i++) pixels[i] = color;
            tex.SetPixels(pixels);
            tex.Apply();
            File.WriteAllBytes(path, tex.EncodeToPNG());
            UnityEngine.Object.DestroyImmediate(tex);
        }

        static uint StableHash(string s) {
            uint h = 5381;
            foreach (char c in s) h = h * 33 + c;
            return h;
        }
    }
}
#endif
