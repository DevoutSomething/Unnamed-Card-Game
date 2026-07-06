#if UNITY_EDITOR
using System.IO;
using Game.Client.View;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

namespace Game.Cards.EditorTools {

    /// <summary>
    /// One-time setup: programmatically builds the default card prefab (uGUI),
    /// the "default" CardLayout asset that points at it, and placeholder border
    /// PNGs for the rarities in borders.json. Safe to re-run — it overwrites the
    /// prefab/layout and only creates border PNGs that are missing.
    /// </summary>
    public static class CardViewPrefabBuilder {

        const string PrefabDir = "Assets/Prefabs/Cards";
        const string PrefabPath = PrefabDir + "/CardView_Default.prefab";
        const string LayoutAssetPath = "Assets/Resources/Skins/Layouts/default.asset";

        static readonly Font UiFont = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");

        [MenuItem("Cards/Setup/Build Default Card Prefab")]
        public static void Build() {
            CreatePlaceholderBorders();

            var root = BuildCardHierarchy();
            Directory.CreateDirectory(PrefabDir);
            var prefab = PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
            Object.DestroyImmediate(root);

            // The "default" layout is the fallback CardSkinLibrary.ResolveDefault uses.
            Directory.CreateDirectory(Path.GetDirectoryName(LayoutAssetPath));
            var layout = AssetDatabase.LoadAssetAtPath<CardLayout>(LayoutAssetPath);
            if (layout == null) {
                layout = ScriptableObject.CreateInstance<CardLayout>();
                AssetDatabase.CreateAsset(layout, LayoutAssetPath);
            }
            layout.name = "default";
            layout.LayoutId = CardSkinLibrary.DefaultLayoutId;
            layout.Template = prefab;
            EditorUtility.SetDirty(layout);

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log($"[CardViewPrefabBuilder] Built {PrefabPath} + default layout. " +
                      "Now run Cards > Pipeline > Import All to (re)link borders.");
        }

        static void CreatePlaceholderBorders() {
            Directory.CreateDirectory(CardPipeline.BorderArtDir);
            var colors = new (string id, Color color)[] {
                ("common",    new Color(0.55f, 0.55f, 0.55f)),
                ("rare",      new Color(0.25f, 0.55f, 0.90f)),
                ("epic",      new Color(0.60f, 0.30f, 0.85f)),
                ("legendary", new Color(0.95f, 0.60f, 0.15f)),
            };
            foreach (var (id, color) in colors) {
                string path = $"{CardPipeline.BorderArtDir}/{id}.png";
                if (!File.Exists(path)) WriteFramePng(path, color, 480, 672, edge: 20);
            }
            AssetDatabase.Refresh();
        }

        /// <summary>A rectangular frame: colored edge, transparent center.</summary>
        static void WriteFramePng(string path, Color color, int width, int height, int edge) {
            var tex = new Texture2D(width, height, TextureFormat.RGBA32, false);
            var pixels = new Color[width * height];
            for (int y = 0; y < height; y++)
                for (int x = 0; x < width; x++) {
                    bool onEdge = x < edge || x >= width - edge || y < edge || y >= height - edge;
                    pixels[y * width + x] = onEdge ? color : Color.clear;
                }
            tex.SetPixels(pixels);
            tex.Apply();
            File.WriteAllBytes(path, tex.EncodeToPNG());
            Object.DestroyImmediate(tex);
        }

        // ---------------------------------------------------------------- hierarchy

        static GameObject BuildCardHierarchy() {
            var root = new GameObject("CardView_Default", typeof(RectTransform), typeof(Image), typeof(CardView));
            ((RectTransform)root.transform).sizeDelta = new Vector2(240, 336);
            root.GetComponent<Image>().color = new Color(0.15f, 0.15f, 0.18f);

            var view = root.GetComponent<CardView>();
            view.ArtImage = AddImage(root, "Art", new Vector2(0.07f, 0.42f), new Vector2(0.93f, 0.90f));
            view.ArtImage.preserveAspect = false;
            view.NameText = AddText(root, "Name", new Vector2(0.07f, 0.90f), new Vector2(0.93f, 0.99f),
                                    16, FontStyle.Bold, TextAnchor.MiddleCenter);
            view.DescriptionText = AddText(root, "Description", new Vector2(0.09f, 0.12f), new Vector2(0.91f, 0.40f),
                                           12, FontStyle.Normal, TextAnchor.UpperCenter);
            view.CostText = AddText(root, "Cost", new Vector2(0.01f, 0.88f), new Vector2(0.18f, 1.00f),
                                    22, FontStyle.Bold, TextAnchor.MiddleCenter);
            view.CostText.color = new Color(0.45f, 0.75f, 1f);
            view.AttackText = AddText(root, "Attack", new Vector2(0.02f, 0.00f), new Vector2(0.20f, 0.12f),
                                      22, FontStyle.Bold, TextAnchor.MiddleCenter);
            view.AttackText.color = new Color(1f, 0.75f, 0.35f);
            view.HealthText = AddText(root, "Health", new Vector2(0.80f, 0.00f), new Vector2(0.98f, 0.12f),
                                      22, FontStyle.Bold, TextAnchor.MiddleCenter);
            view.HealthText.color = new Color(1f, 0.40f, 0.40f);

            // Border last so the frame draws on top of everything.
            view.BorderImage = AddImage(root, "Border", Vector2.zero, Vector2.one);
            return root;
        }

        static RectTransform AddChild(GameObject parent, string name, Vector2 anchorMin, Vector2 anchorMax,
                                      params System.Type[] components) {
            var go = new GameObject(name, components);
            var rect = (RectTransform)go.transform;
            rect.SetParent(parent.transform, false);
            rect.anchorMin = anchorMin;
            rect.anchorMax = anchorMax;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
            return rect;
        }

        static Image AddImage(GameObject parent, string name, Vector2 anchorMin, Vector2 anchorMax) {
            var image = AddChild(parent, name, anchorMin, anchorMax, typeof(Image)).GetComponent<Image>();
            image.raycastTarget = false;
            return image;
        }

        static Text AddText(GameObject parent, string name, Vector2 anchorMin, Vector2 anchorMax,
                            int size, FontStyle style, TextAnchor align) {
            var text = AddChild(parent, name, anchorMin, anchorMax, typeof(Text)).GetComponent<Text>();
            text.font = UiFont;
            text.fontSize = size;
            text.fontStyle = style;
            text.alignment = align;
            text.color = Color.white;
            text.horizontalOverflow = HorizontalWrapMode.Wrap;
            text.verticalOverflow = VerticalWrapMode.Truncate;
            text.raycastTarget = false;
            return text;
        }
    }
}
#endif
