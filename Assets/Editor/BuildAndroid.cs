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
            importer.filterMode = FilterMode.Trilinear;
            importer.anisoLevel = 8;
            importer.textureCompression = TextureImporterCompression.CompressedHQ;
            importer.SaveAndReimport();
        }

        private static void SetTextureReadable(string path, bool readable)
        {
            var importer = AssetImporter.GetAtPath(path) as TextureImporter;
            if (importer == null) return;
            if (importer.isReadable == readable) return;
            importer.isReadable = readable;
            importer.SaveAndReimport();
        }

        private static string CreateMetallicSmoothnessMap(string metallicPath, string roughnessPath, string outputPath)
        {
            SetTextureReadable(metallicPath, true);
            SetTextureReadable(roughnessPath, true);

            Texture2D metallic = AssetDatabase.LoadAssetAtPath<Texture2D>(metallicPath);
            Texture2D roughness = AssetDatabase.LoadAssetAtPath<Texture2D>(roughnessPath);
            if (metallic == null || roughness == null)
                throw new InvalidOperationException("Metallic or roughness texture is missing while building " + outputPath);

            int width = Mathf.Min(metallic.width, roughness.width);
            int height = Mathf.Min(metallic.height, roughness.height);

            Texture2D packed = new Texture2D(width, height, TextureFormat.RGBA32, false, true);
            Color[] m = metallic.GetPixels(0, 0, width, height);
            Color[] r = roughness.GetPixels(0, 0, width, height);
            Color[] o = new Color[m.Length];

            for (int i = 0; i < o.Length; i++)
            {
                float metal = m[i].r;
                float smooth = 1f - r[i].r;
                o[i] = new Color(metal, 0f, 0f, smooth);
            }

            packed.SetPixels(o);
            packed.Apply(false, false);

            string absolute = Path.GetFullPath(outputPath);
            Directory.CreateDirectory(Path.GetDirectoryName(absolute));
            File.WriteAllBytes(absolute, packed.EncodeToPNG());
            UnityEngine.Object.DestroyImmediate(packed);

            AssetDatabase.ImportAsset(outputPath, ImportAssetOptions.ForceSynchronousImport);
            ConfigureTexture(outputPath, false, false, 2048);

            SetTextureReadable(metallicPath, false);
            SetTextureReadable(roughnessPath, false);
            return outputPath;
        }

        private static Material GetOrCreateWeaponMaterial(string path, string colorPath, string normalPath, string aoPath, string metallicSmoothnessPath)
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
            Texture2D metallic = string.IsNullOrEmpty(metallicSmoothnessPath) ? null : AssetDatabase.LoadAssetAtPath<Texture2D>(metallicSmoothnessPath);

            if (mat.HasProperty("_BaseMap")) mat.SetTexture("_BaseMap", color);
            if (mat.HasProperty("_MainTex")) mat.SetTexture("_MainTex", color);
            if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", Color.white);
            if (mat.HasProperty("_Color")) mat.SetColor("_Color", Color.white);

            if (normal != null)
            {
                if (mat.HasProperty("_BumpMap")) mat.SetTexture("_BumpMap", normal);
                if (mat.HasProperty("_BumpScale")) mat.SetFloat("_BumpScale", 1.15f);
                mat.EnableKeyword("_NORMALMAP");
            }

            if (ao != null && mat.HasProperty("_OcclusionMap"))
            {
                mat.SetTexture("_OcclusionMap", ao);
                if (mat.HasProperty("_OcclusionStrength")) mat.SetFloat("_OcclusionStrength", .92f);
                mat.EnableKeyword("_OCCLUSIONMAP");
            }

            if (metallic != null && mat.HasProperty("_MetallicGlossMap"))
            {
                mat.SetTexture("_MetallicGlossMap", metallic);
                mat.EnableKeyword("_METALLICSPECGLOSSMAP");
            }

            if (mat.HasProperty("_WorkflowMode")) mat.SetFloat("_WorkflowMode", 1f);
            if (mat.HasProperty("_SmoothnessTextureChannel")) mat.SetFloat("_SmoothnessTextureChannel", 0f);
            if (mat.HasProperty("_Metallic")) mat.SetFloat("_Metallic", 1f);
            if (mat.HasProperty("_Smoothness")) mat.SetFloat("_Smoothness", 1f);
            if (mat.HasProperty("_EnvironmentReflections")) mat.SetFloat("_EnvironmentReflections", 1f);
            if (mat.HasProperty("_SpecularHighlights")) mat.SetFloat("_SpecularHighlights", 1f);

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
            const string pistolMetallicPath = pistolRoot + "/textures/HIpistolMetallic.png";
            const string pistolRoughnessPath = pistolRoot + "/textures/HIpistolRoughness.png";

            if (!File.Exists(Path.GetFullPath(pistolFbx)))
                throw new FileNotFoundException("Real pistol asset was not found. Expected " + pistolFbx);

            ConfigureTexture(colorPath, false, true, 2048);
            ConfigureTexture(normalPath, true, false, 2048);
            ConfigureTexture(aoPath, false, false, 2048);
            ConfigureTexture(pistolMetallicPath, false, false, 2048);
            ConfigureTexture(pistolRoughnessPath, false, false, 2048);
            string pistolPacked = CreateMetallicSmoothnessMap(
                pistolMetallicPath, pistolRoughnessPath, generatedFolder + "/RR_Pistol_MetalSmooth.png");

            runtime.menuWeaponPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(pistolFbx);
            if (runtime.menuWeaponPrefab == null)
                throw new InvalidOperationException("Unity could not import the pistol FBX at " + pistolFbx);

            runtime.menuWeaponMaterial = GetOrCreateWeaponMaterial(
                generatedFolder + "/RR_Pistol9mm.mat",
                colorPath, normalPath, aoPath, pistolPacked);

            const string shotgunRoot = "Assets/Imported/Shotgun";
            const string shotgunFbx = shotgunRoot + "/source/shotgun.fbx";
            const string shotgunColor = shotgunRoot + "/textures/shotgunColor.png";
            const string shotgunNormal = shotgunRoot + "/textures/shotgunNormal.png";
            const string shotgunAO = shotgunRoot + "/textures/shotgunAO.png";
            const string shotgunMetallic = shotgunRoot + "/textures/shotgunMetallic.png";
            const string shotgunRoughness = shotgunRoot + "/textures/shotgunRoughness.png";

            if (!File.Exists(Path.GetFullPath(shotgunFbx)))
                throw new FileNotFoundException("Real shotgun asset was not found. Expected " + shotgunFbx);

            ConfigureTexture(shotgunColor, false, true, 2048);
            ConfigureTexture(shotgunNormal, true, false, 2048);
            ConfigureTexture(shotgunAO, false, false, 2048);
            ConfigureTexture(shotgunMetallic, false, false, 2048);
            ConfigureTexture(shotgunRoughness, false, false, 2048);
            string shotgunPacked = CreateMetallicSmoothnessMap(
                shotgunMetallic, shotgunRoughness, generatedFolder + "/RR_Shotgun_MetalSmooth.png");

            runtime.menuShotgunPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(shotgunFbx);
            if (runtime.menuShotgunPrefab == null)
                throw new InvalidOperationException("Unity could not import the shotgun FBX at " + shotgunFbx);

            runtime.menuShotgunMaterial = GetOrCreateWeaponMaterial(
                generatedFolder + "/RR_BreachShotgun.mat",
                shotgunColor, shotgunNormal, shotgunAO, shotgunPacked);

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
