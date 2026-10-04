using System;
using System.IO;
using UnityEditor;
using UnityEditor.Build.Reporting;

namespace BadAppleHotel.EditorTools
{
    /// <summary>Reproducible, debug-signed device build. Run in a separate checkout when the editor is open.</summary>
    public static class AndroidBuild
    {
        public static void BuildTestApk()
        {
            string output = Environment.GetEnvironmentVariable("BADAPPLE_APK_PATH");
            if (string.IsNullOrEmpty(output)) output = Path.GetFullPath("Builds/BadAppleHotel-test.apk");
            Directory.CreateDirectory(Path.GetDirectoryName(output));
            EditorSetup.Run();
            PlayerSettings.SetApplicationIdentifier(UnityEditor.Build.NamedBuildTarget.Android, "com.kalavhan.badapplehotel");
            PlayerSettings.SetScriptingBackend(UnityEditor.Build.NamedBuildTarget.Android, ScriptingImplementation.IL2CPP);
            PlayerSettings.Android.targetArchitectures = AndroidArchitecture.ARM64;
            PlayerSettings.Android.minSdkVersion = AndroidSdkVersions.AndroidApiLevel25;
            PlayerSettings.Android.useCustomKeystore = false;
            PlayerSettings.bundleVersion = "1.1-playtest";
            PlayerSettings.Android.bundleVersionCode = 2;
            EditorUserBuildSettings.buildAppBundle = false;
            var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
            {
                scenes = new[] { "Assets/Scenes/Main.unity" },
                locationPathName = output,
                target = BuildTarget.Android,
                options = BuildOptions.Development,
            });
            if (report.summary.result != BuildResult.Succeeded)
                throw new Exception("Android build failed: " + report.summary.result + ", errors: " + report.summary.totalErrors);
            UnityEngine.Debug.Log("APK built: " + output + " (" + report.summary.totalSize + " bytes)");
        }
    }
}
