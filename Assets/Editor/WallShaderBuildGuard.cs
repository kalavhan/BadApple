using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace BadAppleHotel.EditorTools
{
    /// <summary>Runtime-created wall materials are invisible to Unity's scene-based variant scan.</summary>
    public sealed class WallShaderBuildGuard : IPreprocessBuildWithReport
    {
        // Run before other project build hooks can trigger shader processing.
        public int callbackOrder => -1000;
        public void OnPreprocessBuild(BuildReport report) => PreserveRuntimeInstancing();

        public static void PreserveRuntimeInstancing()
        {
            var settings = AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/GraphicsSettings.asset");
            if (settings.Length == 0)
                throw new BuildFailedException("Graphics settings could not be loaded; cannot preserve runtime hotel-wall shader variants.");
            using (var serialized = new SerializedObject(settings[0]))
            {
                var stripping = serialized.FindProperty("m_InstancingStripping");
                if (stripping == null)
                    throw new BuildFailedException("This Unity version changed the instancing-stripping setting; review the hotel-wall build safeguard before building.");
                // UnityEditor.Rendering.InstancingStrippingMode is internal. Its serialized
                // KeepAll value is 2 in Unity 6000.3; no scene has our runtime wall material.
                const int keepAll = 2;
                if (stripping.intValue == keepAll) return;
                stripping.intValue = keepAll;
                serialized.ApplyModifiedPropertiesWithoutUndo();
                EditorUtility.SetDirty(settings[0]);
                AssetDatabase.SaveAssets();
            }
            Debug.Log("Hotel walls: preserving instancing shader variants for runtime-created materials.");
        }
    }
}
