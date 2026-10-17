using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace BadAppleHotel.Tests
{
    public class CharacterSkinningTests
    {
        const float WeightTolerance = .00001f;
        const float PoseTolerance = .00001f;

        [TestCase("hana")]
        [TestCase("dexter")]
        [TestCase("fenwick")]
        [TestCase("cornelius")]
        [TestCase("moldy_matron")]
        public void Shipped_skin_weights_are_finite_normalized_and_reference_existing_bones(string id)
        {
            var skin = Skin(Prefab(id));
            var mesh = skin.sharedMesh;
            var weights = mesh.boneWeights;
            Assert.AreEqual(mesh.vertexCount, weights.Length, id);
            Assert.Greater(skin.bones.Length, 0, id);
            Assert.AreEqual(skin.bones.Length, mesh.bindposes.Length, id);
            Assert.IsTrue(skin.bones.All(b => b != null), id + " has a missing bone");
            for (int v = 0; v < weights.Length; v++)
            {
                var bw = weights[v];
                float[] values = { bw.weight0, bw.weight1, bw.weight2, bw.weight3 };
                int[] indices = { bw.boneIndex0, bw.boneIndex1, bw.boneIndex2, bw.boneIndex3 };
                for (int j = 0; j < values.Length; j++)
                {
                    string context = id + " vertex " + v + " influence " + j;
                    Assert.IsFalse(float.IsNaN(values[j]) || float.IsInfinity(values[j]), context);
                    Assert.GreaterOrEqual(values[j], 0f, context);
                    Assert.GreaterOrEqual(indices[j], 0, context);
                    Assert.Less(indices[j], skin.bones.Length, context);
                }
                Assert.AreEqual(1f, values.Sum(), WeightTolerance, id + " vertex " + v);
            }
        }

        [TestCase("hana")]
        [TestCase("dexter")]
        [TestCase("fenwick")]
        [TestCase("cornelius")]
        public void Central_coat_vertices_have_no_arm_hand_or_shoulder_influence(string id)
        {
            var skin = Skin(Prefab(id));
            var armBones = ArmBones(skin);
            var weights = skin.sharedMesh.boneWeights;
            var protectedVertices = ProtectedVertices(id, skin.sharedMesh.vertices);
            Assert.Greater(protectedVertices.Length, 0, id + " coat region must be represented");
            foreach (int v in protectedVertices)
                Assert.AreEqual(0f, WeightFor(weights[v], armBones), WeightTolerance, id + " coat vertex " + v);
        }

        [TestCase("hana")]
        [TestCase("dexter")]
        [TestCase("fenwick")]
        [TestCase("cornelius")]
        public void Both_outer_sleeves_and_hands_remain_attached_to_the_arms(string id)
        {
            var skin = Skin(Prefab(id));
            var armBones = ArmBones(skin);
            var weights = skin.sharedMesh.boneWeights;
            var vertices = skin.sharedMesh.vertices;
            foreach (int side in new[] { -1, 1 })
            {
                var sleeve = SleeveVertices(id, vertices, side);
                Assert.Greater(sleeve.Length, 0, id + " sleeve side " + side + " must be represented");
                Assert.Greater(sleeve.Average(v => WeightFor(weights[v], armBones)), .9f,
                    id + " sleeve side " + side + " must retain meaningful arm influence");
            }
        }

        [Test]
        public void Widow_Mildreds_mushroom_cap_is_fully_attached_to_the_head()
        {
            var skin = Skin(Prefab("moldy_matron"));
            bool[] head = skin.bones.Select(b => BoneName(b) == "Head").ToArray();
            Assert.AreEqual(1, head.Count(value => value), "Expected one Head bone");
            var weights = skin.sharedMesh.boneWeights;
            var cap = ProtectedVertices("moldy_matron", skin.sharedMesh.vertices);
            Assert.Greater(cap.Length, 0, "The cap region must be represented");
            foreach (int v in cap)
                Assert.AreEqual(1f, WeightFor(weights[v], head), WeightTolerance, "Mushroom cap vertex " + v);
        }

        [TestCase("hana")]
        [TestCase("dexter")]
        [TestCase("fenwick")]
        [TestCase("cornelius")]
        [TestCase("moldy_matron")]
        public void Moving_both_upper_arms_does_not_pull_the_central_coat_or_mushroom_cap(string id)
        {
            Scene preview = default;
            GameObject instance = null;
            Mesh baked = null;
            try
            {
                // A preview scene isolates pose edits from the user's open scene and prefab assets.
                preview = EditorSceneManager.NewPreviewScene();
                instance = (GameObject)PrefabUtility.InstantiatePrefab(Prefab(id), preview);
                instance.hideFlags = HideFlags.HideAndDontSave;
                foreach (var animator in instance.GetComponentsInChildren<Animator>(true)) animator.enabled = false;
                foreach (var behaviour in instance.GetComponentsInChildren<MonoBehaviour>(true)) behaviour.enabled = false;
                var skin = Skin(instance);
                skin.updateWhenOffscreen = true;
                var bindVertices = skin.sharedMesh.vertices;
                var protectedVertices = ProtectedVertices(id, bindVertices);
                Assert.Greater(protectedVertices.Length, 0, id + " protected region must be represented");
                baked = new Mesh { name = id + " skinning regression pose", hideFlags = HideFlags.HideAndDontSave };
                skin.BakeMesh(baked, false);
                var before = baked.vertices;
                string[] upperArmNames = id == "moldy_matron"
                    ? new[] { "L_Upperarm", "R_Upperarm" } : new[] { "LeftArm", "RightArm" };
                for (int armIndex = 0; armIndex < upperArmNames.Length; armIndex++)
                {
                    string name = upperArmNames[armIndex];
                    var arm = skin.bones.SingleOrDefault(b => BoneName(b) == name);
                    Assert.NotNull(arm, id + " is missing " + name);
                    arm.localRotation *= Quaternion.Euler(armIndex == 0 ? 70f : -70f, 0f, 0f);
                }
                skin.BakeMesh(baked, false);
                var after = baked.vertices;
                Assert.AreEqual(before.Length, after.Length, id);
                foreach (int v in protectedVertices)
                    Assert.LessOrEqual(Vector3.Distance(before[v], after[v]), PoseTolerance,
                        id + " protected vertex " + v + " moved with the arms");

                // Also prove that the pose actually moved each arm; a frozen rig must not pass.
                foreach (int side in new[] { -1, 1 })
                {
                    var sleeve = SleeveVertices(id, bindVertices, side);
                    Assert.Greater(sleeve.Length, 0, id + " sleeve side " + side);
                    Assert.Greater(sleeve.Max(v => Vector3.Distance(before[v], after[v])), .001f,
                        id + " sleeve side " + side + " should move when its arm rotates");
                }
            }
            finally
            {
                if (baked != null) Object.DestroyImmediate(baked);
                if (instance != null) Object.DestroyImmediate(instance);
                if (preview.IsValid()) EditorSceneManager.ClosePreviewScene(preview);
            }
        }

        static GameObject Prefab(string id)
        {
            string group = id == "moldy_matron" ? "Monsters" : "Characters";
            var prefab = Resources.Load<GameObject>("Art3D/" + group + "/" + id + "_rig");
            Assert.NotNull(prefab, id + " rig prefab must ship with the game");
            return prefab;
        }

        static SkinnedMeshRenderer Skin(GameObject root)
        {
            var skin = root.GetComponentInChildren<SkinnedMeshRenderer>(true);
            Assert.NotNull(skin, root.name);
            Assert.NotNull(skin.sharedMesh, root.name);
            return skin;
        }

        static string BoneName(Transform bone) => bone.name.Split(':').Last();

        static bool[] ArmBones(SkinnedMeshRenderer skin) => skin.bones.Select(b =>
            b.name.Contains("Arm") || b.name.Contains("Hand") || b.name.Contains("Shoulder")).ToArray();

        static float WeightFor(BoneWeight weight, bool[] bones) =>
            (bones[weight.boneIndex0] ? weight.weight0 : 0f) + (bones[weight.boneIndex1] ? weight.weight1 : 0f)
            + (bones[weight.boneIndex2] ? weight.weight2 : 0f) + (bones[weight.boneIndex3] ? weight.weight3 : 0f);

        // Independent, conservative bind-space regions: these do not reproduce the repair's taper profile.
        // Mesh coordinates are Z up. Leg influences on the coat are allowed; arm influences are not.
        static int[] ProtectedVertices(string id, Vector3[] vertices) => Enumerable.Range(0, vertices.Length)
            .Where(v => id == "moldy_matron" ? vertices[v].z >= .72f
                : vertices[v].z >= .30f && vertices[v].z <= .50f && Mathf.Abs(vertices[v].x) < .09f).ToArray();

        static int[] SleeveVertices(string id, Vector3[] vertices, int side) => Enumerable.Range(0, vertices.Length)
            .Where(v => id == "moldy_matron"
                ? vertices[v].x * side > .30f && vertices[v].z >= .60f && vertices[v].z <= .75f
                : vertices[v].x * side > (id == "dexter" ? .24f : .18f)
                    && vertices[v].z >= .45f && vertices[v].z <= .65f).ToArray();
    }
}
