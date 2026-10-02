using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace RecoilRivals.Editor
{
    public static class BuildAndroid
    {
        private const string BundleId = "com.recoilrivals.game";

        private static Material GetOrCreateMaterial(string path, string[] shaderNames)
        {
            Material existing = AssetDatabase.LoadAssetAtPath<Material>(path);
            Shader shader = null;

            foreach (string shaderName in shaderNames)
            {
                shader = Shader.Find(shaderName);
                if (shader != null) break;
            }

            if (shader == null)
                throw new InvalidOperationException("Could not find a supported shader for " + path);

            if (existing == null)
            {
                existing = new Material(shader);
                AssetDatabase.CreateAsset(existing, path);
            }
            else if (existing.shader != shader)
            {
                existing.shader = shader;
                EditorUtility.SetDirty(existing);
            }

            return existing;
        }

        [MenuItem("Recoil Rivals/Build Android APK")]
        public static void PerformBuild()
        {
            string projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
            string outputDirectory = Path.Combine(projectRoot, "build", "Android");
            string apkPath = Path.Combine(outputDirectory, "RecoilRivals.apk");

            Directory.CreateDirectory(outputDirectory);

            // Put the runtime controller directly in the build scene. This avoids relying only
            // on RuntimeInitializeOnLoadMethod on mobile/IL2CPP.
            string startupScene = EditorBuildSettings.scenes
                .Where(scene => scene.enabled && File.Exists(scene.path))
                .Select(scene => scene.path)
                .FirstOrDefault();

            if (string.IsNullOrEmpty(startupScene))
                throw new InvalidOperationException("No enabled startup scene was found.");

            var scene = EditorSceneManager.OpenScene(startupScene, OpenSceneMode.Single);
            foreach (var root in scene.GetRootGameObjects())
                UnityEngine.Object.DestroyImmediate(root);

            var runtimeRoot = new GameObject("RECOIL_RIVALS_RUNTIME");
            var runtime = runtimeRoot.AddComponent<RecoilRivals.RecoilRivalsGame>();

            const string generatedFolder = "Assets/Generated";
            if (!AssetDatabase.IsValidFolder(generatedFolder))
                AssetDatabase.CreateFolder("Assets", "Generated");

            runtime.runtimeLitTemplate = GetOrCreateMaterial(
                generatedFolder + "/RR_RuntimeLit.mat",
                new[] { "Universal Render Pipeline/Lit", "Universal Render Pipeline/Simple Lit", "Standard" });

            runtime.runtimeTrailTemplate = GetOrCreateMaterial(
                generatedFolder + "/RR_RuntimeTrail.mat",
                new[] { "Universal Render Pipeline/Unlit", "Unlit/Color", "Sprites/Default" });

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            AssetDatabase.SaveAssets();

            string[] scenes = EditorBuildSettings.scenes
                .Where(scene => scene.enabled && File.Exists(scene.path))
                .Select(scene => scene.path)
                .ToArray();

            if (scenes.Length == 0)
            {
                throw new InvalidOperationException("No enabled scenes were found in Build Settings.");
            }

            PlayerSettings.companyName = "Recoil Rivals";
            PlayerSettings.productName = "RECOIL RIVALS";
            PlayerSettings.SetApplicationIdentifier(NamedBuildTarget.Android, BundleId);
            PlayerSettings.Android.minSdkVersion = AndroidSdkVersions.AndroidApiLevel24;
            EditorUserBuildSettings.buildAppBundle = false;

            Debug.Log($"[Recoil Rivals] Building {scenes.Length} scene(s) to {apkPath}");

            BuildPlayerOptions options = new BuildPlayerOptions
            {
                scenes = scenes,
                locationPathName = apkPath,
                target = BuildTarget.Android,
                options = BuildOptions.None
            };

            BuildReport report = BuildPipeline.BuildPlayer(options);
            BuildSummary summary = report.summary;

            Debug.Log($"[Recoil Rivals] Build result: {summary.result}");
            Debug.Log($"[Recoil Rivals] Build size: {summary.totalSize} bytes");
            Debug.Log($"[Recoil Rivals] Build time: {summary.totalTime}");

            if (summary.result != BuildResult.Succeeded)
            {
                throw new Exception($"Android build failed: {summary.result} ({summary.totalErrors} error(s), {summary.totalWarnings} warning(s)).");
            }

            Debug.Log($"[Recoil Rivals] APK created successfully: {apkPath}");
        }
    }
}
