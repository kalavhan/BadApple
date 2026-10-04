using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace BadAppleHotel.EditorTools
{
    /// <summary>
    /// First-open setup: creates Assets/Scenes/Main.unity, adds it to the build, and sets landscape + names.
    /// Safe to run repeatedly; it only fills in what is missing.
    /// </summary>
    [InitializeOnLoad]
    public static class EditorSetup
    {
        const string ScenePath = "Assets/Scenes/Main.unity";

        static EditorSetup()
        {
            EditorApplication.delayCall += Run;
        }

        [MenuItem("Bad Apple Hotel/Run Project Setup")]
        public static void Run()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) return;

            PlayerSettings.companyName = "kalavhan";
            PlayerSettings.productName = "Bad Apple Hotel";
            PlayerSettings.defaultInterfaceOrientation = UIOrientation.LandscapeLeft;

            if (!File.Exists(ScenePath))
            {
                Directory.CreateDirectory("Assets/Scenes");
                var active = EditorSceneManager.GetActiveScene();
                bool replaceActive = string.IsNullOrEmpty(active.path) && !active.isDirty;
                var scene = EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects,
                    replaceActive ? NewSceneMode.Single : NewSceneMode.Additive);
                EditorSceneManager.SaveScene(scene, ScenePath);
                if (!replaceActive) EditorSceneManager.CloseScene(scene, true);
                AssetDatabase.Refresh();
                Debug.Log("[Bad Apple Hotel] Created " + ScenePath + ". Open it and press Play.");
            }

            var scenes = EditorBuildSettings.scenes.ToList();
            if (!scenes.Any(s => s.path == ScenePath))
            {
                scenes.Insert(0, new EditorBuildSettingsScene(ScenePath, true));
                EditorBuildSettings.scenes = scenes.ToArray();
            }
        }
    }
}
