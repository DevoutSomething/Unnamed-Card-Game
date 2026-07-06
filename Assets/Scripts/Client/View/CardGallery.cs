using Game.Cards;
using Game.Core.Server;
using Game.Core.State;
using UnityEngine;
using UnityEngine.UI;

namespace Game.Client.View
{
    /// <summary>
    /// Debug view: spawns every card in the database into a scrollable grid.
    /// The end-to-end smoke test that data, art, skins, and rendering all work.
    ///
    /// Usage: empty scene -> empty GameObject -> add this component -> press Play.
    /// It builds its own Canvas if you don't assign Content.
    /// </summary>
    public class CardGallery : MonoBehaviour
    {
        [Tooltip("Parent for the spawned cards. Leave empty to auto-build a scrollable canvas grid.")]
        public Transform Content;

        void Start()
        {
            AbilityLoader.Bootstrap();
            var db = new CardDatabase();
            var skins = new CardSkinLibrary();
            var state = new GameState(seed: 0);

            if (Content == null) Content = BuildCanvasGrid();

            int spawned = 0;
            foreach (var def in db.All)
            {
                if (!CardFactory.TryCreate(state, def, ownerId: 0, out var inst, out string error))
                {
                    Debug.LogError($"CardGallery: {error}");
                    continue;
                }
                if (CardViewFactory.Spawn(inst, Content, db, skins) != null) spawned++;
            }
            Debug.Log($"CardGallery: spawned {spawned}/{db.All.Count} card(s). " +
                      (spawned == 0 ? "Run Cards > Pipeline > Import All and Cards > Setup > Build Default Card Prefab." : ""));
        }

        static Transform BuildCanvasGrid()
        {
            var canvasGo = new GameObject("CardGalleryCanvas",
                typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            var canvas = canvasGo.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            var scaler = canvasGo.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);

            var scrollGo = new GameObject("Scroll", typeof(RectTransform), typeof(ScrollRect), typeof(Image), typeof(Mask));
            scrollGo.transform.SetParent(canvasGo.transform, false);
            var scrollRect = (RectTransform)scrollGo.transform;
            scrollRect.anchorMin = Vector2.zero;
            scrollRect.anchorMax = Vector2.one;
            scrollRect.offsetMin = Vector2.zero;
            scrollRect.offsetMax = Vector2.zero;
            scrollGo.GetComponent<Image>().color = new Color(0.1f, 0.1f, 0.12f);

            var gridGo = new GameObject("Content", typeof(RectTransform), typeof(GridLayoutGroup), typeof(ContentSizeFitter));
            gridGo.transform.SetParent(scrollGo.transform, false);
            var gridRect = (RectTransform)gridGo.transform;
            gridRect.anchorMin = new Vector2(0, 1);
            gridRect.anchorMax = new Vector2(1, 1);
            gridRect.pivot = new Vector2(0.5f, 1);
            gridRect.offsetMin = Vector2.zero;
            gridRect.offsetMax = Vector2.zero;

            var grid = gridGo.GetComponent<GridLayoutGroup>();
            grid.cellSize = new Vector2(240, 336);
            grid.spacing = new Vector2(24, 24);
            grid.padding = new RectOffset(24, 24, 24, 24);

            gridGo.GetComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            var scroll = scrollGo.GetComponent<ScrollRect>();
            scroll.content = gridRect;
            scroll.horizontal = false;

            return gridGo.transform;
        }
    }
}
