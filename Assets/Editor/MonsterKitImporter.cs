using System.Collections.Generic;
using System.IO;
using System.Linq;
using BadAppleHotel.Game;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using Object = UnityEngine.Object;

namespace BadAppleHotel.EditorTools
{
    /// <summary>
    /// Bakes Josue's rigged Tripo monster exports into Humanoid rigs with a mobile mesh, and gives each monster an
    /// Animator Controller built from the Mixamo clips in Assets/TripoModels/MonsterClips. A clip that is not there
    /// yet falls back to the shared monster clip, then to the residents' baked idle, walk and run.
    /// Sources under Assets/TripoModels are never edited or referenced at runtime.
    /// </summary>
    public static class MonsterKitImporter
    {
        public const string Output = "Assets/Resources/Art3D/Monsters";
        const string ClipFolder = "Assets/TripoModels/MonsterClips";
        const string ResidentClips = "Assets/Resources/Art3D/Characters/Animations";
        const int TriangleBudget = 8000;

        /// <summary>Monster id → the folder name Tripo gave the export.</summary>
        static readonly Dictionary<string, string> Sources = new Dictionary<string, string>
        {
            {"stitchwork_chef", "stylized_pig_humanoid_3d_model"},
            {"moldy_matron", "mushroom_lady_3d_model"},
            {"bellhop_wraith", "stylized_ghost_officer_3d_model"},
        };

        sealed class ClipSpec
        {
            public readonly string State, File; public readonly bool Loop;
            public ClipSpec(string state, string file, bool loop) { State = state; File = file; Loop = loop; }
        }

        /// <summary>Animator states every monster gets, from the shared clips (file names in MonsterClips).</summary>
        static readonly ClipSpec[] Shared =
        {
            new ClipSpec("Idle", "Monster-Idle", true),
            new ClipSpec("Walk", "Monster-Walk", true),
            new ClipSpec("Run", "Monster-Run", true),
            new ClipSpec("Attack", "Porter-Lash", false),
            new ClipSpec("Cast", "Monster-Cast", false),
            new ClipSpec("Special", "Monster-Cast", false),
            new ClipSpec("Eat", "Monster-Eat", true),
            new ClipSpec("Death", "Monster-Death", false),
        };

        /// <summary>Per-monster replacements for a state's clip.</summary>
        static readonly Dictionary<string, Dictionary<string, string>> Own = new Dictionary<string, Dictionary<string, string>>
        {
            {"stitchwork_chef", new Dictionary<string, string> { {"Attack", "Gorge-Cleave"}, {"Special", "Gorge-Hook"} }},
            {"moldy_matron", new Dictionary<string, string>()},
            {"bellhop_wraith", new Dictionary<string, string> { {"Attack", "Porter-Lash"}, {"Cast", "Porter-Bell"} }},
        };

        static readonly Dictionary<string, string> ResidentFallback = new Dictionary<string, string> { {"Idle", "Idle"}, {"Walk", "Walk"}, {"Run", "Run"} };

        [MenuItem("Bad Apple/Art/Bake imported monster models")]
        public static void Bake()
        {
            Directory.CreateDirectory(Output);
            Directory.CreateDirectory(Output + "/Animations");
            var clips = BakeClips();
            var models = new List<CharacterKit.Model>();
            foreach (var entry in Sources)
            {
                string path = $"Assets/TripoModels/{entry.Value}/{entry.Value}.fbx";
                if (!File.Exists(path)) { Debug.LogWarning("MonsterKit: no export at " + path); continue; }
                var controller = BuildController(entry.Key, clips);
                var model = BakeRig(entry.Key, entry.Value, path, controller);
                if (model != null) models.Add(model);
            }
            var kitPath = Output + "/HotelMonsterKit.asset";
            var kit = AssetDatabase.LoadAssetAtPath<CharacterKit>(kitPath);
            if (kit == null) { kit = ScriptableObject.CreateInstance<CharacterKit>(); AssetDatabase.CreateAsset(kit, kitPath); }
            kit.Models = models.ToArray(); EditorUtility.SetDirty(kit);
            AssetDatabase.SaveAssets(); AssetDatabase.Refresh();
            Debug.Log("MonsterKit: baked " + models.Count + " monster model(s): " + string.Join(", ", models.Select(m => m.Id)));
        }

        /// <summary>Copies every Mixamo clip that exists into an .anim asset (Humanoid, in place).</summary>
        static Dictionary<string, AnimationClip> BakeClips()
        {
            var baked = new Dictionary<string, AnimationClip>();
            var files = Shared.Select(s => s.File).Concat(Own.Values.SelectMany(o => o.Values)).Distinct();
            foreach (var file in files)
            {
                string path = $"{ClipFolder}/{file}.fbx";
                if (!File.Exists(path)) continue;
                bool loop = Shared.Any(s => s.File == file && s.Loop);
                var importer = (ModelImporter)AssetImporter.GetAtPath(path);
                importer.animationType = ModelImporterAnimationType.Human;
                importer.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
                importer.importAnimation = true;
                var defs = importer.defaultClipAnimations;
                if (defs.Length == 0) continue;
                var clip = defs[0];
                clip.name = file; clip.loopTime = loop; clip.loopPose = loop;
                clip.lockRootRotation = true; clip.keepOriginalOrientation = true;
                clip.lockRootPositionXZ = false; clip.keepOriginalPositionXZ = false;
                clip.lockRootHeightY = true; clip.keepOriginalPositionY = false; clip.heightFromFeet = true;
                importer.clipAnimations = new[] { clip };
                importer.SaveAndReimport();
                var source = AssetDatabase.LoadAllAssetsAtPath(path).OfType<AnimationClip>().FirstOrDefault(c => !c.name.StartsWith("__preview"));
                if (source == null) continue;
                var copy = Object.Instantiate(source); copy.name = file;
                baked[file] = CharacterKitImporter.SaveAsset(copy, $"{Output}/Animations/{file}.anim");
            }
            Debug.Log("MonsterKit clips: " + (baked.Count == 0 ? "none yet (resident fallbacks)" : string.Join(", ", baked.Keys)));
            return baked;
        }

        static AnimationClip ClipFor(string monster, ClipSpec spec, Dictionary<string, AnimationClip> clips)
        {
            if (Own[monster].TryGetValue(spec.State, out var own) && clips.TryGetValue(own, out var mine)) return mine;
            if (clips.TryGetValue(spec.File, out var shared)) return shared;
            return ResidentFallback.TryGetValue(spec.State, out var resident)
                ? AssetDatabase.LoadAssetAtPath<AnimationClip>($"{ResidentClips}/{resident}.anim") : null;
        }

        static RuntimeAnimatorController BuildController(string monster, Dictionary<string, AnimationClip> clips)
        {
            string path = $"{Output}/Animations/{monster}.controller";
            // Rebuilt in place so the rig prefab keeps its reference.
            var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(path) ?? AnimatorController.CreateAnimatorControllerAtPath(path);
            var machine = controller.layers[0].stateMachine;
            foreach (var old in machine.states.ToArray()) machine.RemoveState(old.state);
            foreach (var spec in Shared)
            {
                var clip = ClipFor(monster, spec, clips);
                if (clip == null) continue;
                var state = machine.AddState(spec.State); state.motion = clip; state.writeDefaultValues = true;
                if (spec.State == "Idle") machine.defaultState = state;
            }
            EditorUtility.SetDirty(controller); AssetDatabase.SaveAssets();
            return controller;
        }

        static float IdleHeight(GameObject instance, SkinnedMeshRenderer skin, RuntimeAnimatorController controller)
        {
            var idle = controller.animationClips.FirstOrDefault(c => c.name.Contains("Idle"));
            AnimationMode.StartAnimationMode();
            try
            {
                if (idle != null) { AnimationMode.BeginSampling(); AnimationMode.SampleAnimationClip(instance, idle, .3f); AnimationMode.EndSampling(); }
                var posed = new Mesh(); skin.BakeMesh(posed, true);
                float low = float.MaxValue, high = float.MinValue;
                foreach (var vertex in posed.vertices) { float y = skin.transform.TransformPoint(vertex).y; low = Mathf.Min(low, y); high = Mathf.Max(high, y); }
                Object.DestroyImmediate(posed);
                return high - low;
            }
            finally { AnimationMode.StopAnimationMode(); }
        }

        static CharacterKit.Model BakeRig(string id, string stem, string path, RuntimeAnimatorController controller)
        {
            var importer = (ModelImporter)AssetImporter.GetAtPath(path);
            if (importer.animationType != ModelImporterAnimationType.Human || importer.importAnimation)
            {
                importer.animationType = ModelImporterAnimationType.Human;
                importer.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
                importer.importAnimation = false; importer.SaveAndReimport();
            }
            var avatar = AssetDatabase.LoadAllAssetsAtPath(path).OfType<Avatar>().FirstOrDefault();
            if (avatar == null || !avatar.isValid || !avatar.isHuman) { Debug.LogWarning("MonsterKit: " + stem + " has no valid Humanoid avatar."); return null; }
            var instance = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(path));
            try
            {
                PrefabUtility.UnpackPrefabInstance(instance, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
                instance.name = id + "_rig";
                foreach (var extra in instance.GetComponentsInChildren<Animation>(true)) Object.DestroyImmediate(extra);
                var skin = instance.GetComponentInChildren<SkinnedMeshRenderer>(true);
                int sourceTriangles = skin.sharedMesh.triangles.Length / 3;
                var reduced = CharacterKitImporter.Reduce(skin.sharedMesh, TriangleBudget); reduced.name = "monster_" + id + "_skinned";
                reduced.bindposes = skin.sharedMesh.bindposes;
                var white = new Color[reduced.vertexCount]; for (int i = 0; i < white.Length; i++) white[i] = Color.white; reduced.colors = white;
                reduced.RecalculateBounds();
                skin.sharedMesh = CharacterKitImporter.SaveAsset(reduced, $"{Output}/{id}_skinned.asset");
                skin.updateWhenOffscreen = false; skin.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; skin.receiveShadows = false;
                var source = skin.sharedMaterial != null ? skin.sharedMaterial.mainTexture : null;
                if (source == null) source = CharacterKitImporter.FindAlbedo(stem);
                var albedo = source != null ? CharacterKitImporter.SaveAlbedo(source, id, Output) : null;
                var material = new Material(Shader.Find("BadApple/HotelSurface")) { name = id + "_monster", mainTexture = albedo };
                skin.sharedMaterial = CharacterKitImporter.SaveAsset(material, $"{Output}/{id}_monster.mat");
                var animator = instance.GetComponent<Animator>() ?? instance.AddComponent<Animator>();
                animator.avatar = CharacterKitImporter.SaveAsset(Object.Instantiate(avatar), $"{Output}/{id}_avatar.asset");
                animator.runtimeAnimatorController = controller; animator.applyRootMotion = false;
                animator.cullingMode = AnimatorCullingMode.CullUpdateTransforms;
                instance.transform.position = Vector3.zero; instance.transform.rotation = Quaternion.identity;
                // Standing height in the idle pose (a T-pose's arm span can be wider than the body is tall).
                float rigHeight = IdleHeight(instance, skin, controller);
                string prefabPath = $"{Output}/{id}_rig.prefab";
                PrefabUtility.SaveAsPrefabAsset(instance, prefabPath);
                AssetDatabase.ImportAsset(prefabPath, ImportAssetOptions.ForceUpdate);
                Debug.Log($"MonsterKit: {id} from {stem}, {sourceTriangles} → {skin.sharedMesh.triangles.Length / 3} triangles, rig height {rigHeight:F3}");
                return new CharacterKit.Model
                {
                    Id = id, Mesh = skin.sharedMesh, Albedo = albedo, Height = 1f, HalfDepth = .25f, Source = stem,
                    SourceTriangles = sourceTriangles, Rig = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath), RigHeight = rigHeight, RigSource = stem,
                };
            }
            finally { Object.DestroyImmediate(instance); }
        }
    }
}
