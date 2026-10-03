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

        private static void ConfigureTexture(string path, bool normalMap, bool sRgb, int maxSize)
        {
            var importer = AssetImporter.GetAtPath(path) as TextureImporter;
            if (importer == null) return;

            importer.textureType = normalMap ? TextureImporterType.NormalMap : TextureImporterType.Default;
            importer.sRGBTexture = sRgb;
            importer.maxTextureSize = maxSize;
            importer.mipmapEnabled = true;
            importer.textureCompression = TextureImporterCompression.CompressedHQ;
            importer.SaveAndReimport();
        }

        private static Material GetOrCreatePistolMaterial(string path, string colorPath, string normalPath, string aoPath)
        {
            Shader shader = Shader.Find("Universal Render Pipeline/Lit");
            if (shader == null) shader = Shader.Find("Universal Render Pipeline/Simple Lit");
            if (shader == null) shader = Shader.Find("Standard");
            if (shader == null) throw new InvalidOperationException("No supported lit shader found.");

            Material mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (mat == null)
            {
                mat = new Material(shader);
                AssetDatabase.CreateAsset(mat, path);
            }
            else mat.shader = shader;

            Texture2D color = AssetDatabase.LoadAssetAtPath<Texture2D>(colorPath);
            Texture2D normal = AssetDatabase.LoadAssetAtPath<Texture2D>(normalPath);
            Texture2D ao = AssetDatabase.LoadAssetAtPath<Texture2D>(aoPath);

            if (mat.HasProperty("_BaseMap")) mat.SetTexture("_BaseMap", color);
            if (mat.HasProperty("_MainTex")) mat.SetTexture("_MainTex", color);
            if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", Color.white);
            if (mat.HasProperty("_Color")) mat.SetColor("_Color", Color.white);

            if (normal != null)
            {
                if (mat.HasProperty("_BumpMap")) mat.SetTexture("_BumpMap", normal);
                if (mat.HasProperty("_BumpScale")) mat.SetFloat("_BumpScale", 1f);
                mat.EnableKeyword("_NORMALMAP");
            }

            if (ao != null && mat.HasProperty("_OcclusionMap"))
            {
                mat.SetTexture("_OcclusionMap", ao);
                if (mat.HasProperty("_OcclusionStrength")) mat.SetFloat("_OcclusionStrength", 1f);
            }

            if (mat.HasProperty("_Metallic")) mat.SetFloat("_Metallic", .55f);
            if (mat.HasProperty("_Smoothness")) mat.SetFloat("_Smoothness", .46f);

            EditorUtility.SetDirty(mat);
            AssetDatabase.SaveAssets();
            return mat;
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

            var runtimeRoot = new GameObject("RECOIL_RIVALS_V2_RUNTIME");
            var runtime = runtimeRoot.AddComponent<RecoilRivals2.RRGameV2>();

            const string generatedFolder = "Assets/Generated";
            if (!AssetDatabase.IsValidFolder(generatedFolder))
                AssetDatabase.CreateFolder("Assets", "Generated");

            runtime.menuLitTemplate = GetOrCreateMaterial(
                generatedFolder + "/RR_MenuLit.mat",
                new[] { "Universal Render Pipeline/Lit", "Universal Render Pipeline/Simple Lit", "Standard" });

            const string pistolRoot = "Assets/Imported/Pistol9mm";
            const string pistolFbx = pistolRoot + "/source/HIpistol.fbx";
            const string colorPath = pistolRoot + "/textures/HIpistolColor.png";
            const string normalPath = pistolRoot + "/textures/HIpistolNormal.png";
            const string aoPath = pistolRoot + "/textures/HIpistolAO.png";

            if (!File.Exists(Path.GetFullPath(pistolFbx)))
                throw new FileNotFoundException("Real pistol asset was not found. Expected " + pistolFbx);

            ConfigureTexture(colorPath, false, true, 2048);
            ConfigureTexture(normalPath, true, false, 2048);
            ConfigureTexture(aoPath, false, false, 1024);

            runtime.menuWeaponPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(pistolFbx);
            if (runtime.menuWeaponPrefab == null)
                throw new InvalidOperationException("Unity could not import the pistol FBX at " + pistolFbx);

            runtime.menuWeaponMaterial = GetOrCreatePistolMaterial(
                generatedFolder + "/RR_Pistol9mm.mat",
                colorPath, normalPath, aoPath);

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
