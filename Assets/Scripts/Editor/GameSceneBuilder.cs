#if UNITY_EDITOR
using Game.Client;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Game.Cards.EditorTools {

    /// <summary>
    /// One-click scene setup: a camera + a GameObject with GameController.
    /// GameController builds the whole board UI from code at runtime, so this
    /// scene is intentionally almost empty. Safe to re-run (overwrites).
    /// </summary>
    public static class GameSceneBuilder {

        public const string ScenePath = "Assets/Scenes/Game.unity";

        [MenuItem("Cards/Setup/Build Game Scene")]
        public static void Build() {
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            var cameraGo = new GameObject("Main Camera", typeof(Camera), typeof(AudioListener));
            cameraGo.tag = "MainCamera";
            var camera = cameraGo.GetComponent<Camera>();
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(0.09f, 0.11f, 0.13f);
            camera.orthographic = true;

            new GameObject("Game", typeof(GameController));

            EditorSceneManager.SaveScene(scene, ScenePath);
            Debug.Log($"[GameSceneBuilder] Saved {ScenePath}. Open it and press Play.");
        }
    }
}
#endif
