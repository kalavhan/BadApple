using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace BadAppleHotel.EditorTools
{
    /// <summary>Temporary pose captures and numerical skin checks; never saves or edits a prefab or scene.</summary>
    public static class CharacterSkinningValidation
    {
        const string Root = "Temp/rig-repair";
        static readonly string[] Ids = { "hana", "dexter", "fenwick", "cornelius", "moldy_matron" };

        /// <summary>Invoke from the editor after compilation. Detailed results and representative meshes go to Temp.</summary>
        public static string Capture(string phase, int samplesPerClip = 8, bool exportEveryClip = false)
        {
            CheckReady(phase, samplesPerClip);
            var reports = new JArray();
            foreach (string id in Ids) reports.Add(CaptureOne(id, phase, samplesPerClip, exportEveryClip));
            string path = Root + "/qa/" + phase + "/summary.json";
            File.WriteAllText(path, reports.ToString(Formatting.Indented));
            return path;
        }

        public static string CaptureCharacter(string id, string phase, int samplesPerClip = 8, bool exportEveryClip = false)
        {
            CheckReady(phase, samplesPerClip);
            if (!Ids.Contains(id)) throw new ArgumentException("Unknown character: " + id);
            return CaptureOne(id, phase, samplesPerClip, exportEveryClip).ToString(Formatting.None);
        }

        static void CheckReady(string phase, int samplesPerClip)
        {
            if (phase != "before" && phase != "after") throw new ArgumentException("Phase must be before or after.");
            if (samplesPerClip < 2 || samplesPerClip > 120) throw new ArgumentOutOfRangeException(nameof(samplesPerClip));
            if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Capture requires edit mode.");
            if (AnimationMode.InAnimationMode()) throw new InvalidOperationException("Finish the active Animation window preview before capturing.");
        }

        static JObject CaptureOne(string id, string phase, int samplesPerClip, bool exportEveryClip)
        {
            string folder = Root + "/qa/" + phase;
            Directory.CreateDirectory(folder);
            string group = id == "moldy_matron" ? "Monsters" : "Characters";
            string prefabPath = "Assets/Resources/Art3D/" + group + "/" + id + "_rig.prefab";
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
            if (prefab == null) throw new InvalidOperationException("Missing prefab: " + prefabPath);
            var original = JObject.Parse(File.ReadAllText(Root + "/" + id + ".json"));
            var originalVertices = (JArray)original["vertices"];
            int[] originalTriangles = original["triangles"].ToObject<int[]>();
            Scene originalScene = SceneManager.GetActiveScene();
            Scene preview = default;
            GameObject instance = null;
            Mesh posed = null;
            Mesh baseline = null;
            bool ownsAnimationMode = false;
            try
            {
                preview = EditorSceneManager.NewPreviewScene();
                instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab, preview);
                instance.hideFlags = HideFlags.HideAndDontSave;
                foreach (var behaviour in instance.GetComponentsInChildren<MonoBehaviour>(true)) behaviour.enabled = false;
                var animator = instance.GetComponentInChildren<Animator>(true);
                var skin = instance.GetComponentInChildren<SkinnedMeshRenderer>(true);
                if (animator == null || skin == null || skin.sharedMesh == null || animator.runtimeAnimatorController == null)
                    throw new InvalidOperationException("Incomplete rig: " + id);
                animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
                animator.applyRootMotion = false;
                skin.updateWhenOffscreen = true;
                if (phase == "before")
                {
                    baseline = Object.Instantiate(skin.sharedMesh);
                    baseline.name = id + "_original_validation_weights";
                    baseline.hideFlags = HideFlags.HideAndDontSave;
                    var originalWeights = (JArray)original["weights"];
                    var originalIndices = (JArray)original["indices"];
                    if (originalWeights.Count != baseline.vertexCount || originalIndices.Count != baseline.vertexCount)
                        throw new InvalidOperationException("Original weights do not match the current mesh: " + id);
                    var restored = new BoneWeight[baseline.vertexCount];
                    for (int i = 0; i < restored.Length; i++)
                        restored[i] = new BoneWeight
                        {
                            weight0 = (float)originalWeights[i][0], weight1 = (float)originalWeights[i][1],
                            weight2 = (float)originalWeights[i][2], weight3 = (float)originalWeights[i][3],
                            boneIndex0 = (int)originalIndices[i][0], boneIndex1 = (int)originalIndices[i][1],
                            boneIndex2 = (int)originalIndices[i][2], boneIndex3 = (int)originalIndices[i][3]
                        };
                    baseline.boneWeights = restored;
                    skin.sharedMesh = baseline;
                }
                Mesh source = skin.sharedMesh;
                Transform[] bones = skin.bones;
                int headIndex = Array.FindIndex(bones, b => b != null && (b.name == "Head" || b.name.EndsWith(":Head", StringComparison.Ordinal)));
                Transform head = headIndex < 0 ? null : bones[headIndex];
                var cap = id == "moldy_matron"
                    ? Enumerable.Range(0, originalVertices.Count).Where(i => (float)originalVertices[i][2] >= .72f).ToArray()
                    : Array.Empty<int>();
                if (source.vertexCount != originalVertices.Count && cap.Length > 0)
                    throw new InvalidOperationException("Vertex order/count changed; original cap mask cannot be applied.");
                posed = new Mesh { name = id + "_validation_pose", hideFlags = HideFlags.HideAndDontSave };
                skin.BakeMesh(posed, false);
                Vector3[] bindWorld = WorldVertices(skin, posed);
                Vector3[] capHeadLocal = head == null ? Array.Empty<Vector3>() : cap.Select(i => head.InverseTransformPoint(bindWorld[i])).ToArray();
                Quaternion[] bindBoneRotations = bones.Select(b => b == null ? Quaternion.identity : b.localRotation).ToArray();
                var weights = source.boneWeights;
                var report = new JObject
                {
                    ["id"] = id, ["phase"] = phase, ["prefab"] = prefabPath,
                    ["vertexCount"] = source.vertexCount, ["triangleCount"] = source.triangles.Length / 3,
                    ["vertexCountUnchanged"] = source.vertexCount == originalVertices.Count,
                    ["triangleIndicesUnchanged"] = source.triangles.SequenceEqual(originalTriangles),
                    ["weights"] = ValidateWeights(weights, source.vertexCount, bones.Length),
                    ["capVertexCount"] = cap.Length, ["headBone"] = head == null ? "" : head.name,
                    ["capVerticesNotFullyAttachedToHead"] = cap.Count(i => HeadWeight(weights[i], headIndex) < .99999f),
                    ["capMaxHeadLocalDrift"] = 0f,
                    ["samplesPerClip"] = samplesPerClip, ["clips"] = new JArray()
                };
                ExportPose(folder + "/" + id + "_Bind.json", id, "Bind", 0, 0, skin, posed, original);
                var clips = ClipStates(animator.runtimeAnimatorController);
                if (clips.Count == 0) throw new InvalidOperationException("Controller has no animation clips: " + id);
                AnimationMode.StartAnimationMode();
                ownsAnimationMode = true;
                float capMaxDrift = 0f;
                foreach (var entry in clips)
                {
                    var clipReport = new JObject { ["state"] = entry.Key, ["clip"] = entry.Value.name, ["length"] = entry.Value.length };
                    var sampleReports = new JArray();
                    float maxBoneAngle = 0f, maxInterSampleDisplacement = 0f;
                    Vector3[] previousWorld = null;
                    for (int sample = 0; sample < samplesPerClip; sample++)
                    {
                        // Cover the loop without duplicating its first frame; nonloops include their endpoint.
                        float fraction = sample / (float)(entry.Value.isLooping ? samplesPerClip : samplesPerClip - 1);
                        float time = fraction * entry.Value.length;
                        AnimationMode.BeginSampling();
                        try { AnimationMode.SampleAnimationClip(animator.gameObject, entry.Value, time); }
                        finally { AnimationMode.EndSampling(); }
                        skin.BakeMesh(posed, false);
                        Vector3[] world = WorldVertices(skin, posed);
                        int nonFinite = 0;
                        float maxBindDisplacement = 0f, drift = 0f;
                        Vector3 low = new Vector3(float.MaxValue, float.MaxValue, float.MaxValue);
                        Vector3 high = new Vector3(float.MinValue, float.MinValue, float.MinValue);
                        for (int i = 0; i < world.Length; i++)
                        {
                            if (!Finite(world[i])) { nonFinite++; continue; }
                            low = Vector3.Min(low, world[i]); high = Vector3.Max(high, world[i]);
                            if (i < bindWorld.Length) maxBindDisplacement = Mathf.Max(maxBindDisplacement, Vector3.Distance(world[i], bindWorld[i]));
                            if (previousWorld != null && i < previousWorld.Length)
                                maxInterSampleDisplacement = Mathf.Max(maxInterSampleDisplacement, Vector3.Distance(world[i], previousWorld[i]));
                        }
                        if (head != null)
                            for (int j = 0; j < cap.Length; j++)
                                drift = Mathf.Max(drift, Vector3.Distance(head.InverseTransformPoint(world[cap[j]]), capHeadLocal[j]));
                        capMaxDrift = Mathf.Max(capMaxDrift, drift);
                        for (int b = 0; b < bones.Length; b++)
                            if (bones[b] != null) maxBoneAngle = Mathf.Max(maxBoneAngle, Quaternion.Angle(bones[b].localRotation, bindBoneRotations[b]));
                        sampleReports.Add(new JObject
                        {
                            ["sample"] = sample, ["time"] = time, ["nonFiniteVertices"] = nonFinite,
                            ["vertexCount"] = world.Length, ["maxDisplacementFromBind"] = maxBindDisplacement,
                            ["capMaxHeadLocalDrift"] = drift,
                            ["worldBoundsMin"] = FloatArray(low), ["worldBoundsMax"] = FloatArray(high)
                        });
                        bool representative = sample == samplesPerClip / 4 || sample == samplesPerClip * 3 / 4;
                        bool selected = exportEveryClip || entry.Key == "Idle" || entry.Key == "Walk" || entry.Key == "Run" || entry.Key == "RunScared" || entry.Key == "Attack" || entry.Key == "Cast" || entry.Key == "Special";
                        if (representative && selected)
                            ExportPose(folder + "/" + id + "_" + SafeName(entry.Key) + "_" + sample + ".json", id, entry.Key, sample, time, skin, posed, original);
                        previousWorld = world;
                    }
                    clipReport["maxBoneAngleFromBind"] = maxBoneAngle;
                    clipReport["sampledBoneMotion"] = maxBoneAngle > .01f;
                    clipReport["maxInterSampleVertexDisplacement"] = maxInterSampleDisplacement;
                    clipReport["samples"] = sampleReports;
                    ((JArray)report["clips"]).Add(clipReport);
                }
                report["capMaxHeadLocalDrift"] = capMaxDrift;
                report["capRigidWithHead"] = cap.Length == 0 || (head != null && capMaxDrift < .0001f && (int)report["capVerticesNotFullyAttachedToHead"] == 0);
                File.WriteAllText(folder + "/" + id + "_metrics.json", report.ToString(Formatting.Indented));
                return report;
            }
            finally
            {
                if (ownsAnimationMode) AnimationMode.StopAnimationMode();
                if (posed != null) Object.DestroyImmediate(posed);
                if (instance != null) Object.DestroyImmediate(instance);
                if (baseline != null) Object.DestroyImmediate(baseline);
                if (preview.IsValid()) EditorSceneManager.ClosePreviewScene(preview);
                if (originalScene.IsValid() && originalScene.isLoaded && SceneManager.GetActiveScene() != originalScene)
                    SceneManager.SetActiveScene(originalScene);
            }
        }

        static JObject ValidateWeights(BoneWeight[] weights, int vertexCount, int boneCount)
        {
            int invalid = 0;
            float maxSumError = 0f;
            foreach (BoneWeight bw in weights)
            {
                var values = new[] { bw.weight0, bw.weight1, bw.weight2, bw.weight3 };
                var indices = new[] { bw.boneIndex0, bw.boneIndex1, bw.boneIndex2, bw.boneIndex3 };
                bool valid = true;
                for (int j = 0; j < 4; j++)
                    if (!Finite(values[j]) || values[j] < 0 || (values[j] > 0 && (indices[j] < 0 || indices[j] >= boneCount))) valid = false;
                float error = Mathf.Abs(values.Sum() - 1f);
                if (!Finite(error) || error > .0001f) valid = false;
                if (Finite(error)) maxSumError = Mathf.Max(maxSumError, error);
                if (!valid) invalid++;
            }
            return new JObject { ["count"] = weights.Length, ["countMatchesVertices"] = weights.Length == vertexCount, ["invalidVertices"] = invalid, ["maxSumError"] = maxSumError };
        }

        static float HeadWeight(BoneWeight weight, int index)
        {
            if (index < 0) return 0;
            return (weight.boneIndex0 == index ? weight.weight0 : 0) + (weight.boneIndex1 == index ? weight.weight1 : 0)
                + (weight.boneIndex2 == index ? weight.weight2 : 0) + (weight.boneIndex3 == index ? weight.weight3 : 0);
        }

        static Dictionary<string, AnimationClip> ClipStates(RuntimeAnimatorController controller)
        {
            var result = new Dictionary<string, AnimationClip>();
            var concrete = controller as AnimatorController;
            if (concrete != null)
                foreach (var layer in concrete.layers) AddStates(layer.stateMachine, result);
            foreach (var clip in controller.animationClips)
                if (clip != null && !result.ContainsValue(clip)) result[clip.name] = clip;
            return result;
        }

        static void AddStates(AnimatorStateMachine machine, Dictionary<string, AnimationClip> clips)
        {
            foreach (var state in machine.states)
                if (state.state.motion is AnimationClip clip) clips[state.state.name] = clip;
            foreach (var child in machine.stateMachines) AddStates(child.stateMachine, clips);
        }

        static void ExportPose(string path, string id, string state, int sample, float time, SkinnedMeshRenderer skin, Mesh mesh, JObject original)
        {
            var vertices = new JArray();
            var localVertices = new JArray();
            foreach (var vertex in mesh.vertices)
            {
                Vector3 world = skin.transform.TransformPoint(vertex);
                vertices.Add(new JArray(world.x, -world.z, world.y));
                localVertices.Add(FloatArray(vertex));
            }
            var uv = new JArray();
            foreach (Vector2 value in skin.sharedMesh.uv) uv.Add(new JArray(value.x, value.y));
            var bones = new JArray();
            Transform[] transforms = skin.bones;
            for (int i = 0; i < transforms.Length; i++)
            {
                var bone = transforms[i];
                var matrix = new JArray();
                Matrix4x4 world = bone == null ? Matrix4x4.identity : bone.localToWorldMatrix;
                for (int row = 0; row < 4; row++)
                    for (int col = 0; col < 4; col++) matrix.Add(world[row, col]);
                bones.Add(new JObject { ["name"] = bone == null ? "" : bone.name, ["parent"] = bone == null ? -1 : Array.IndexOf(transforms, bone.parent), ["unityWorldMatrixRowMajor"] = matrix });
            }
            var output = new JObject
            {
                ["id"] = id, ["state"] = state, ["sample"] = sample, ["time"] = time,
                ["coordinates"] = "Blender Z-up: Unity world (x,-z,y)",
                ["vertices"] = vertices, ["unityMeshLocalVertices"] = localVertices,
                ["triangles"] = JArray.FromObject(skin.sharedMesh.triangles), ["uv"] = uv,
                ["texture"] = original["texture"].DeepClone(), ["bones"] = bones
            };
            File.WriteAllText(path, output.ToString(Formatting.None));
        }

        static Vector3[] WorldVertices(SkinnedMeshRenderer skin, Mesh mesh)
        {
            Vector3[] vertices = mesh.vertices;
            for (int i = 0; i < vertices.Length; i++) vertices[i] = skin.transform.TransformPoint(vertices[i]);
            return vertices;
        }

        static JArray FloatArray(Vector3 value) => new JArray(value.x, value.y, value.z);
        static bool Finite(Vector3 value) => Finite(value.x) && Finite(value.y) && Finite(value.z);
        static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
        static string SafeName(string name) => string.Concat(name.Select(c => char.IsLetterOrDigit(c) || c == '_' ? c : '_'));
    }
}
