using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace BadAppleHotel.EditorTools
{
    /// <summary>Removes auto-rig arm spill from the inspected Tripo coats and mushroom cap.
    /// Runs on the derived mesh after simplification; never edits the source FBX.</summary>
    public static class CharacterSkinningRepair
    {
        // Bind-space (Z up) boundaries measured through the gaps between coat and sleeves.
        // Values are height / half width, in the exports' approximately one-unit scale.
        static readonly Dictionary<string, Vector2[]> CoatProfiles = new Dictionary<string, Vector2[]>
        {
            { "hana", Profile(.30f,.18f, .43f,.16f, .51f,.146f, .55f,.132f, .63f,.099f, .70f,.075f, .78f,.046f, .83f,.025f) },
            { "dexter", Profile(.30f,.21f, .42f,.20f, .47f,.197f, .51f,.191f, .55f,.177f, .59f,.149f, .65f,.12f, .72f,.077f, .79f,.035f) },
            { "fenwick", Profile(.28f,.18f, .38f,.16f, .43f,.152f, .47f,.149f, .51f,.135f, .55f,.126f, .63f,.105f, .70f,.072f, .77f,.037f) },
            { "cornelius", Profile(.28f,.17f, .38f,.15f, .43f,.139f, .47f,.132f, .51f,.123f, .55f,.108f, .59f,.093f, .63f,.091f, .69f,.064f, .75f,.031f) },
        };

        static Vector2[] Profile(params float[] values)
        {
            var result = new Vector2[values.Length / 2];
            for (int i = 0; i < result.Length; i++) result[i] = new Vector2(values[i * 2], values[i * 2 + 1]);
            return result;
        }

        public static bool Supports(string id) => id == "moldy_matron" || CoatProfiles.ContainsKey(id);

        public static float Protection(string id, Vector3 vertex)
        {
            // The rear brim dips to Z=.725; include the whole cap and upper veil, with
            // the blend in the narrow neck below it, above the outstretched sleeves.
            if (id == "moldy_matron") return Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(.675f, .70f, vertex.z));
            Vector2[] profile;
            if (!CoatProfiles.TryGetValue(id, out profile) || vertex.z > profile[profile.Length - 1].x) return 0f;
            float width = profile[0].y;
            for (int i = 1; i < profile.Length; i++)
            {
                if (vertex.z > profile[i].x) { width = profile[i].y; continue; }
                width = Mathf.Lerp(profile[i - 1].y, profile[i].y, Mathf.InverseLerp(profile[i - 1].x, profile[i].x, vertex.z));
                break;
            }
            // The small transition lies inside the measured empty space, except at the shoulder seam.
            float lateral = 1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(width - .008f, width + .008f, Mathf.Abs(vertex.x)));
            float top = profile[profile.Length - 1].x;
            float shoulder = 1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(top - .035f, top, vertex.z));
            return lateral * shoulder;
        }

        public static bool IsArm(string name)
        {
            return name.Contains("Arm") || name.Contains("Hand") || name.Contains("Shoulder");
        }

        public static int Apply(string id, Mesh mesh, Transform[] bones)
        {
            if (!Supports(id)) return 0;
            if (mesh == null) throw new ArgumentNullException(nameof(mesh));
            if (bones == null || bones.Length == 0 || bones.Any(b => b == null))
                throw new InvalidOperationException(id + ": missing skin bones.");
            var original = mesh.boneWeights;
            if (original.Length != mesh.vertexCount || mesh.bindposes.Length != bones.Length)
                throw new InvalidOperationException(id + ": incomplete skin data.");
            var names = bones.Select(b => b.name.Split(':').Last()).ToArray();
            int head = Array.IndexOf(names, "Head");
            int neck = Array.IndexOf(names, "NeckTwist01");
            var torso = new[] { "Hips", "Spine", "Spine1", "Spine2" }.Select(n => Array.IndexOf(names, n)).ToArray();
            if (head < 0 || (id == "moldy_matron" ? neck < 0 : torso.Any(i => i < 0)))
                throw new InvalidOperationException(id + ": the repair profile does not match this skeleton.");
            var positions = mesh.bindposes.Select(p => p.inverse.MultiplyPoint3x4(Vector3.zero)).ToArray();
            if (positions.Any(p => !Finite(p.x) || !Finite(p.y) || !Finite(p.z)))
                throw new InvalidOperationException(id + ": invalid bind matrices.");
            if (id != "moldy_matron" && (positions[head].z <= positions[torso[0]].z ||
                Enumerable.Range(1, torso.Length - 1).Any(i => positions[torso[i]].z <= positions[torso[i - 1]].z)))
                throw new InvalidOperationException(id + ": expected a Z-up bind-space mesh.");
            if (id == "moldy_matron" && positions[head].z <= positions[neck].z)
                throw new InvalidOperationException(id + ": expected a Z-up mushroom bind-space mesh.");
            var badBones = names.Select((name, i) => id == "moldy_matron" ? i != head : IsArm(name)).ToArray();
            var vertices = mesh.vertices;
            var repaired = (BoneWeight[])original.Clone();
            int changed = 0;
            for (int v = 0; v < vertices.Length; v++)
            {
                if (!Finite(vertices[v].x) || !Finite(vertices[v].y) || !Finite(vertices[v].z))
                    throw new InvalidOperationException(id + ": invalid position at vertex " + v + ".");
                float protection = Protection(id, vertices[v]);
                if (protection <= 0f) continue;
                var old = original[v];
                int[] indices = { old.boneIndex0, old.boneIndex1, old.boneIndex2, old.boneIndex3 };
                float[] weights = { old.weight0, old.weight1, old.weight2, old.weight3 };
                float total = 0f, bad = 0f;
                for (int j = 0; j < 4; j++)
                {
                    if (!Finite(weights[j]) || weights[j] < 0f ||
                        (weights[j] > 0f && (indices[j] < 0 || indices[j] >= bones.Length)))
                        throw new InvalidOperationException(id + ": invalid skin weight at vertex " + v + ".");
                    total += weights[j];
                    if (weights[j] > 0f && badBones[indices[j]]) bad += weights[j];
                }
                if (!Finite(total) || total <= 0f) throw new InvalidOperationException(id + ": invalid total weight at vertex " + v + ".");
                float allowance = 1f - protection;
                // An absolute cap is stable on repeated bakes; multiplying by (1-protection)
                // would progressively strip correctly blended boundary vertices on every run.
                if (bad / total <= allowance + .000001f) continue;
                float retainedBad = bad > 0f ? Mathf.Min(1f, allowance * total / bad) : 1f;
                var next = new Dictionary<int, float>();
                float removed = 0f;
                for (int j = 0; j < 4; j++)
                {
                    if (weights[j] <= 0f) continue;
                    float take = badBones[indices[j]] ? weights[j] * (1f - retainedBad) / total : 0f;
                    removed += take;
                    Add(next, indices[j], weights[j] / total - take);
                }
                int[] destinations;
                if (id == "moldy_matron") { Add(next, head, removed); destinations = new[] { head }; }
                else
                {
                    int low = 0;
                    while (low < torso.Length - 1 && vertices[v].z > positions[torso[low + 1]].z) low++;
                    int high = Mathf.Min(low + 1, torso.Length - 1);
                    float blend = low == high ? 0f : Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(positions[torso[low]].z, positions[torso[high]].z, vertices[v].z));
                    Add(next, torso[low], removed * (1f - blend));
                    Add(next, torso[high], removed * blend);
                    destinations = new[] { torso[low], torso[high] };
                }
                repaired[v] = Pack(next, badBones, allowance, destinations);
                changed++;
            }
            if (changed > 0) mesh.boneWeights = repaired;
            return changed;
        }

        static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);

        static BoneWeight Pack(Dictionary<int, float> weights, bool[] badBones, float allowance, int[] destinations)
        {
            var ordered = weights.Where(p => p.Value > 0f).OrderByDescending(p => p.Value).ThenBy(p => p.Key).ToList();
            var selected = ordered.Take(4).ToList();
            float sum = selected.Sum(p => p.Value);
            // Dropping a small torso/head influence can raise the arm fraction above the cap.
            // Keep a destination in the four slots so that final normalization cannot undo repair.
            if (!selected.Any(p => destinations.Contains(p.Key)))
            {
                selected[selected.Count - 1] = ordered.First(p => destinations.Contains(p.Key));
                sum = selected.Sum(p => p.Value);
            }
            var packed = selected.ToDictionary(p => p.Key, p => p.Value / sum);
            float bad = packed.Where(p => badBones[p.Key]).Sum(p => p.Value);
            if (bad > allowance)
            {
                float destinationWeight = packed.Where(p => destinations.Contains(p.Key)).Sum(p => p.Value);
                foreach (int bone in packed.Keys.ToArray())
                {
                    if (badBones[bone]) packed[bone] *= allowance / bad;
                    else if (destinations.Contains(bone)) packed[bone] += (bad - allowance) * packed[bone] / destinationWeight;
                }
            }
            var final = packed.Where(p => p.Value > 0f).OrderByDescending(p => p.Value).ThenBy(p => p.Key).ToArray();
            var result = new BoneWeight();
            if (final.Length > 0) { result.boneIndex0 = final[0].Key; result.weight0 = final[0].Value; }
            if (final.Length > 1) { result.boneIndex1 = final[1].Key; result.weight1 = final[1].Value; }
            if (final.Length > 2) { result.boneIndex2 = final[2].Key; result.weight2 = final[2].Value; }
            if (final.Length > 3) { result.boneIndex3 = final[3].Key; result.weight3 = final[3].Value; }
            return result;
        }

        static void Add(Dictionary<int, float> weights, int bone, float weight)
        {
            float previous; weights.TryGetValue(bone, out previous); weights[bone] = previous + weight;
        }

        [MenuItem("Bad Apple/Art/Repair coat and mushroom skin weights")]
        public static void RepairBakedMeshes()
        {
            foreach (var id in CoatProfiles.Keys.Concat(new[] { "moldy_matron" }))
            {
                string folder = id == "moldy_matron" ? "Monsters" : "Characters";
                string path = "Assets/Resources/Art3D/" + folder + "/" + id + "_rig.prefab";
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                if (prefab == null) throw new InvalidOperationException("Missing rig: " + path);
                var skin = prefab.GetComponentInChildren<SkinnedMeshRenderer>(true);
                if (skin == null || skin.sharedMesh == null) throw new InvalidOperationException("Missing skin: " + path);
                int count = Apply(id, skin.sharedMesh, skin.bones);
                if (count > 0) EditorUtility.SetDirty(skin.sharedMesh);
                Debug.Log("Skin repair: " + id + ", corrected " + count + " vertices.");
            }
            AssetDatabase.SaveAssets();
        }
    }
}
