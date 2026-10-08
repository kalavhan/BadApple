using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using BadAppleHotel.Game;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityMeshSimplifier;
using Object = UnityEngine.Object;

namespace BadAppleHotel.EditorTools
{
    /// <summary>Bakes Josue's unrigged Tripo resident exports into mobile meshes in game axes.
    /// The originals under Assets/TripoModels are never edited or referenced at runtime.</summary>
    public static class CharacterKitImporter
    {
        const string Output = "Assets/Resources/Art3D/Characters";
        const int TriangleBudget = 6000, TextureSize = 1024;

        /// <summary>Roster art id → candidate export folders: the docs/art/characters import folder
        /// first, then the generic name Tripo gave the export.</summary>
        static readonly Dictionary<string, string[]> Sources = new Dictionary<string, string[]>
        {
            {"stahl", new[]{"character_stahl", "military_officer_figure_3d_model"}},
            {"fenwick", new[]{"character_fenwick", "mad_scientist_3d_model"}},
            {"hana", new[]{"character_hana", "press_journalist_3d_model"}},
            {"dexter", new[]{"character_dexter", "stylized_character_3d_model"}},
            {"mando", new[]{"character_mando", "office_worker_3d_model"}},
            {"scarlett", new[]{"character_scarlett", "goth_girl_3d_model"}},
            {"cornelius", new[]{"character_cornelius", "victorian_gentleman_3d_model"}},
        };

        [MenuItem("Bad Apple/Art/Bake imported character models")]
        public static void Bake()
        {
            Directory.CreateDirectory(Output);
            bool animated = CharacterAnimationImporter.Bake().Count > 0;
            if (animated) AssetDatabase.ImportAsset(CharacterAnimationImporter.ControllerPath, ImportAssetOptions.ForceUpdate);
            var controller = animated ? AssetDatabase.LoadAssetAtPath<RuntimeAnimatorController>(CharacterAnimationImporter.ControllerPath) : null;
            var models = new List<CharacterKit.Model>();
            try
            {
                int index = 0;
                foreach (var entry in Sources)
                {
                    EditorUtility.DisplayProgressBar("Bake hotel residents", entry.Key, index++ / (float)Sources.Count);
                    string stem = null;
                    foreach (var candidate in entry.Value) if (File.Exists(SourcePath(candidate))) { stem = candidate; break; }
                    if (stem == null) continue;
                    var model = BakeModel(entry.Key, stem);
                    string rigged = controller == null ? null : FindRig(entry.Value);
                    if (rigged != null) BakeRig(model, rigged, controller);
                    models.Add(model);
                }
                var kitPath = Output + "/HotelCharacterKit.asset";
                var kit = AssetDatabase.LoadAssetAtPath<CharacterKit>(kitPath);
                if (kit == null) { kit = ScriptableObject.CreateInstance<CharacterKit>(); AssetDatabase.CreateAsset(kit, kitPath); }
                kit.Models = models.ToArray(); EditorUtility.SetDirty(kit);
                AssetDatabase.SaveAssets(); AssetDatabase.Refresh();
                Debug.Log("CharacterKit: baked " + models.Count + " resident model(s).");
            }
            finally { EditorUtility.ClearProgressBar(); }
        }

        public static void BakeBatch() { Bake(); }

        static string SourcePath(string stem) => $"Assets/TripoModels/{stem}/{stem}.fbx";

        static CharacterKit.Model BakeModel(string id, string stem)
        {
            var source = AssetDatabase.LoadAssetAtPath<GameObject>(SourcePath(stem));
            var original = ReadMesh(source);
            int sourceTriangles = original.triangles.Length / 3;
            var mesh = Reduce(original); Object.DestroyImmediate(original);
            float halfDepth = ToGameAxes(mesh, out float height);
            mesh.name = "character_" + id;
            string meshPath = $"{Output}/{id}.asset";
            var existing = AssetDatabase.LoadAssetAtPath<Mesh>(meshPath);
            if (existing != null) { EditorUtility.CopySerialized(mesh, existing); Object.DestroyImmediate(mesh); mesh = existing; }
            else AssetDatabase.CreateAsset(mesh, meshPath);
            return new CharacterKit.Model
            {
                Id = id, Mesh = mesh, Albedo = BakeAlbedo(id, stem), Height = height, HalfDepth = halfDepth,
                Source = stem, SourceTriangles = sourceTriangles
            };
        }

        /// <summary>The newest export among the candidates (and Tripo's "_1", "_2" re-export names) that has a skinned mesh.</summary>
        static string FindRig(string[] stems)
        {
            string best = null; System.DateTime newest = System.DateTime.MinValue;
            foreach (var stem in stems)
                for (int n = 0; n < 6; n++)
                {
                    string candidate = n == 0 ? stem : stem + "_" + n, path = SourcePath(candidate);
                    if (!File.Exists(path)) continue;
                    var go = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                    if (go == null || go.GetComponentInChildren<SkinnedMeshRenderer>(true) == null) continue;
                    var time = File.GetLastWriteTimeUtc(path);
                    if (time > newest) { newest = time; best = candidate; }
                }
            return best;
        }

        static void BakeRig(CharacterKit.Model model, string stem, RuntimeAnimatorController controller)
        {
            string path = SourcePath(stem);
            var importer = (ModelImporter)AssetImporter.GetAtPath(path);
            if (importer.animationType != ModelImporterAnimationType.Human || importer.importAnimation)
            {
                importer.animationType = ModelImporterAnimationType.Human;
                importer.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
                importer.importAnimation = false; importer.SaveAndReimport();
            }
            var avatar = AssetDatabase.LoadAllAssetsAtPath(path).OfType<Avatar>().FirstOrDefault();
            if (avatar == null || !avatar.isValid || !avatar.isHuman) { Debug.LogWarning("CharacterKit: " + stem + " has no valid Humanoid avatar; keeping the static model."); return; }
            var instance = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(path));
            try
            {
                PrefabUtility.UnpackPrefabInstance(instance, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
                instance.name = model.Id + "_rig";
                foreach (var extra in instance.GetComponentsInChildren<Animation>(true)) Object.DestroyImmediate(extra);
                var skin = instance.GetComponentInChildren<SkinnedMeshRenderer>(true);
                var reduced = Reduce(skin.sharedMesh); reduced.name = "character_" + model.Id + "_skinned";
                reduced.bindposes = skin.sharedMesh.bindposes;
                var white = new Color[reduced.vertexCount]; for (int i = 0; i < white.Length; i++) white[i] = Color.white; reduced.colors = white;
                reduced.RecalculateBounds();
                skin.sharedMesh = SaveAsset(reduced, $"{Output}/{model.Id}_skinned.asset");
                skin.updateWhenOffscreen = false; skin.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; skin.receiveShadows = false;
                // A re-export can carry a new UV layout; always use the rig's own texture.
                var rigTexture = skin.sharedMaterial != null ? skin.sharedMaterial.mainTexture : null;
                if (rigTexture == null) rigTexture = FindAlbedo(stem);
                var albedo = rigTexture != null ? SaveAlbedo(rigTexture, model.Id + "_rig") : model.Albedo;
                var material = new Material(Shader.Find("BadApple/HotelSurface")) { name = model.Id + "_resident", mainTexture = albedo };
                skin.sharedMaterial = SaveAsset(material, $"{Output}/{model.Id}_resident.mat");
                var animator = instance.GetComponent<Animator>() ?? instance.AddComponent<Animator>();
                animator.avatar = SaveAsset(Object.Instantiate(avatar), $"{Output}/{model.Id}_avatar.asset");
                animator.runtimeAnimatorController = controller; animator.applyRootMotion = false;
                animator.cullingMode = AnimatorCullingMode.CullUpdateTransforms;
                instance.transform.position = Vector3.zero; instance.transform.rotation = Quaternion.identity;
                // Measure the bind-pose mesh: some exports are saved mid-animation (crouched or
                // mid-stride), so the posed rest transforms do not give the standing height.
                var bind = skin.sharedMesh.bounds.size; var lossy = skin.transform.lossyScale;
                model.RigHeight = Mathf.Max(bind.x * lossy.x, Mathf.Max(bind.y * lossy.y, bind.z * lossy.z));
                // Reject broken skins: mid-run, a sound rig stays about 90% of its bind height.
                float posed = PosedHeight(instance, skin);
                Debug.Log($"CharacterKit: {stem} running height {posed:F3}, bind height {model.RigHeight:F3}");
                if (posed < model.RigHeight * .75f || posed > model.RigHeight * 1.2f)
                {
                    Debug.LogWarning($"CharacterKit: {stem} deforms badly under the shared clips (running height {posed:F2} vs {model.RigHeight:F2}); keeping the static model.");
                    model.RigHeight = 0;
                    foreach (var leftover in new[] { "_rig.prefab", "_skinned.asset", "_resident.mat", "_avatar.asset", "_rig_albedo.png" })
                        AssetDatabase.DeleteAsset(Output + "/" + model.Id + leftover);
                    return;
                }
                string prefabPath = $"{Output}/{model.Id}_rig.prefab";
                PrefabUtility.SaveAsPrefabAsset(instance, prefabPath);
                // Re-import so the saved prefab resolves the freshly rebuilt controller.
                AssetDatabase.ImportAsset(prefabPath, ImportAssetOptions.ForceUpdate);
                model.Rig = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
                model.RigSource = stem;
            }
            finally { Object.DestroyImmediate(instance); }
        }

        static float PosedHeight(GameObject instance, SkinnedMeshRenderer skin)
        {
            // Running bends every limb; a broken skin sheds feet and coat panels and loses height.
            var idle = AssetDatabase.LoadAssetAtPath<AnimationClip>(CharacterAnimationImporter.Output + "/Run.anim")
                ?? AssetDatabase.LoadAssetAtPath<AnimationClip>(CharacterAnimationImporter.Output + "/Idle.anim");
            if (idle == null) return float.NaN;
            AnimationMode.StartAnimationMode();
            try
            {
                AnimationMode.BeginSampling(); AnimationMode.SampleAnimationClip(instance, idle, .3f); AnimationMode.EndSampling();
                var posed = new Mesh(); skin.BakeMesh(posed, true);
                float low = float.MaxValue, high = float.MinValue;
                foreach (var vertex in posed.vertices) { float y = skin.transform.TransformPoint(vertex).y; low = Mathf.Min(low, y); high = Mathf.Max(high, y); }
                Object.DestroyImmediate(posed);
                return high - low;
            }
            finally { AnimationMode.StopAnimationMode(); }
        }

        internal static T SaveAsset<T>(T asset, string path) where T : Object
        {
            var existing = AssetDatabase.LoadAssetAtPath<T>(path);
            if (existing == null) { AssetDatabase.CreateAsset(asset, path); return asset; }
            EditorUtility.CopySerialized(asset, existing); Object.DestroyImmediate(asset); return existing;
        }

        static Mesh ReadMesh(GameObject source)
        {
            var go = Object.Instantiate(source);
            try
            {
                var parts = new List<CombineInstance>();
                foreach (var filter in go.GetComponentsInChildren<MeshFilter>())
                    for (int sub = 0; sub < filter.sharedMesh.subMeshCount; sub++)
                        parts.Add(new CombineInstance { mesh = filter.sharedMesh, subMeshIndex = sub, transform = filter.transform.localToWorldMatrix });
                var mesh = new Mesh { indexFormat = IndexFormat.UInt32 };
                mesh.CombineMeshes(parts.ToArray(), true, true); return mesh;
            }
            finally { Object.DestroyImmediate(go); }
        }

        internal static Mesh Reduce(Mesh source, int budget = TriangleBudget)
        {
            int triangles = source.triangles.Length / 3;
            if (triangles <= budget) return Object.Instantiate(source);
            var reducer = new MeshSimplifier();
            var options = SimplificationOptions.Default;
            options.MaxIterationCount = 200; options.PreserveUVSeamEdges = false; options.PreserveUVFoldoverEdges = true; options.EnableSmartLink = true;
            reducer.SimplificationOptions = options; reducer.Initialize(source);
            reducer.SimplifyMesh((float)budget / triangles);
            return reducer.ToMesh();
        }

        /// <summary>Source (X width, Y up, Z toward the viewer = face) to game (XY floor, face toward +Y,
        /// negative Z up). Feet at the origin, centered on the body; Tripo exports are already one unit tall.</summary>
        static float ToGameAxes(Mesh mesh, out float height)
        {
            mesh.RecalculateBounds(); var b = mesh.bounds;
            var v = mesh.vertices; var n = mesh.normals;
            for (int i = 0; i < v.Length; i++)
            {
                var p = v[i] - new Vector3(b.center.x, b.min.y, b.center.z);
                v[i] = new Vector3(p.x, p.z, -p.y);
                if (n.Length == v.Length) n[i] = new Vector3(n[i].x, n[i].z, -n[i].y);
            }
            mesh.vertices = v; if (n.Length == v.Length) mesh.normals = n;
            var white = new Color[v.Length]; for (int i = 0; i < white.Length; i++) white[i] = Color.white;
            mesh.colors = white; mesh.RecalculateBounds();
            height = mesh.bounds.size.z;
            return mesh.bounds.extents.y;
        }

        static Texture2D BakeAlbedo(string id, string stem)
        {
            string folder = $"Assets/TripoModels/{stem}/{stem}.fbm";
            Texture2D sourceTexture = null;
            foreach (var guid in AssetDatabase.FindAssets("t:Texture2D", new[] { folder }))
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                if (Path.GetFileNameWithoutExtension(path).EndsWith("_BaseColor", StringComparison.OrdinalIgnoreCase)) sourceTexture = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
            }
            if (sourceTexture == null) throw new InvalidOperationException("Missing character albedo in " + folder);
            return SaveAlbedo(sourceTexture, id);
        }

        /// <summary>The export's base colour: a "_BaseColor" texture, else the only colour texture
        /// (Tripo's rigged exports name it tripo_image_*).</summary>
        internal static Texture2D FindAlbedo(string stem)
        {
            Texture2D any = null;
            foreach (var guid in AssetDatabase.FindAssets("t:Texture2D", new[] { $"Assets/TripoModels/{stem}/{stem}.fbm" }))
            {
                var path = AssetDatabase.GUIDToAssetPath(guid); var name = Path.GetFileNameWithoutExtension(path).ToLowerInvariant();
                if (name.EndsWith("_basecolor")) return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
                if (!name.Contains("normal") && !name.Contains("metal") && !name.Contains("rough") && !name.EndsWith("_rm")) any = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
            }
            return any;
        }

        internal static Texture2D SaveAlbedo(Texture sourceTexture, string name, string folder = Output)
        {
            var rt = RenderTexture.GetTemporary(TextureSize, TextureSize, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
            var previous = RenderTexture.active;
            Graphics.Blit(sourceTexture, rt); RenderTexture.active = rt;
            var tex = new Texture2D(TextureSize, TextureSize, TextureFormat.RGBA32, false);
            tex.ReadPixels(new Rect(0, 0, TextureSize, TextureSize), 0, 0); tex.Apply();
            RenderTexture.active = previous; RenderTexture.ReleaseTemporary(rt);
            string output = $"{folder}/{name}_albedo.png";
            File.WriteAllBytes(output, tex.EncodeToPNG()); Object.DestroyImmediate(tex);
            AssetDatabase.ImportAsset(output, ImportAssetOptions.ForceSynchronousImport);
            var ti = (TextureImporter)AssetImporter.GetAtPath(output);
            ti.textureType = TextureImporterType.Default; ti.sRGBTexture = true; ti.alphaSource = TextureImporterAlphaSource.None;
            ti.mipmapEnabled = true; ti.wrapMode = TextureWrapMode.Clamp; ti.filterMode = FilterMode.Bilinear;
            ti.maxTextureSize = TextureSize; ti.textureCompression = TextureImporterCompression.CompressedHQ; ti.SaveAndReimport();
            return AssetDatabase.LoadAssetAtPath<Texture2D>(output);
        }
    }
}
