using System;
using System.Collections.Generic;
using System.IO;
using BadAppleHotel.Game;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

namespace BadAppleHotel.EditorTools
{
    /// <summary>Bakes Josue's Tripo bed exports into game-axis meshes and small albedo textures.
    /// The originals under Assets/TripoModels are never edited or referenced at runtime.</summary>
    public static class BedKitImporter
    {
        const string Output = "Assets/Resources/Art3D/Beds";
        const int TextureSize = 512;

        /// <summary>Candidate export folders per bed level: the docs/art/beds import folder first,
        /// then the generic name Tripo gave the export when it was imported.</summary>
        static readonly Dictionary<int, string[]> Sources = new Dictionary<int, string[]>
        {
            {1, new[]{"bed_01_paper", "95ff6c85-e1f1-459b-8f97-655703808af1"}},
            {2, new[]{"bed_02_cardboard", "cardboard_sheet_3d_model"}},
            {3, new[]{"bed_03_mattress", "1766abd6-4018-434b-8053-e1f1e9866f4b"}},
            {4, new[]{"bed_04_normal", "wooden_bed_3d_model"}},
            {5, new[]{"bed_05_floating", "c12a3930-1316-49f5-8b37-006107da2a02"}},
            {6, new[]{"bed_06_coffin", "coffin_3d_model"}},
            {7, new[]{"bed_07_skull", "skull_bed_3d_model"}},
        };

        /// <summary>Idle motions from the docs/art/beds manifests that the HotelBed shader implements.</summary>
        static readonly Dictionary<int, int> Motions = new Dictionary<int, int> { {1, 1}, {2, 2}, {5, 3} };
        /// <summary>Level 5 is legless and levitates; the shader lifts it by this much (tiles), plus a slow bob.</summary>
        const float FloatHeight = .14f;

        [MenuItem("Bad Apple/Art/Bake imported bed models")]
        public static void Bake()
        {
            Directory.CreateDirectory(Output);
            var pieces = new List<BedKit.Piece>();
            try
            {
                foreach (var entry in Sources)
                {
                    string stem = FindSource(entry.Value);
                    if (stem == null) continue;
                    EditorUtility.DisplayProgressBar("Bake hotel beds", stem, entry.Key / 7f);
                    pieces.Add(BakePiece(entry.Key, stem));
                }
                var kitPath = Output + "/HotelBedKit.asset";
                var kit = AssetDatabase.LoadAssetAtPath<BedKit>(kitPath);
                if (kit == null) { kit = ScriptableObject.CreateInstance<BedKit>(); AssetDatabase.CreateAsset(kit, kitPath); }
                kit.Pieces = pieces.ToArray(); EditorUtility.SetDirty(kit);
                AssetDatabase.SaveAssets(); AssetDatabase.Refresh();
                Debug.Log("BedKit: baked " + pieces.Count + " bed level(s). Levels without a Tripo export keep their sprites.");
            }
            finally { EditorUtility.ClearProgressBar(); }
        }

        public static void BakeBatch() { Bake(); }

        static string FindSource(string[] stems)
        {
            foreach (var stem in stems) if (File.Exists(SourcePath(stem))) return stem;
            return null;
        }
        static string SourcePath(string stem) => $"Assets/TripoModels/{stem}/{stem}.fbx";

        static BedKit.Piece BakePiece(int level, string stem)
        {
            var source = AssetDatabase.LoadAssetAtPath<GameObject>(SourcePath(stem));
            var mesh = ReadMesh(source);
            int sourceTriangles = mesh.triangles.Length / 3;
            float surface = ToGameAxes(mesh);
            if (Motions.TryGetValue(level, out int floating) && floating == 3) surface += FloatHeight;
            mesh.name = "bed_" + level;
            string meshPath = $"{Output}/bed_{level}.asset";
            var existing = AssetDatabase.LoadAssetAtPath<Mesh>(meshPath);
            if (existing != null) { EditorUtility.CopySerialized(mesh, existing); Object.DestroyImmediate(mesh); mesh = existing; }
            else AssetDatabase.CreateAsset(mesh, meshPath);
            return new BedKit.Piece
            {
                Level = level, Mesh = mesh, Albedo = BakeAlbedo(level, stem), Size = mesh.bounds.size,
                SurfaceHeight = surface, Motion = Motions.TryGetValue(level, out int motion) ? motion : 0, Source = stem, Triangles = sourceTriangles
            };
        }

        /// <summary>Tripo exports are Y up with the head end toward +Z once the FBX node rotation is applied.</summary>
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

        /// <summary>Source (X width, Y up, Z length) to game (XY floor, head toward +Y, negative Z up).
        /// Uniformly fits the two reserved bed tiles; pivot is the footprint center on the floor.
        /// Returns the sleeping-surface height measured over the middle of the bed.</summary>
        static float ToGameAxes(Mesh mesh)
        {
            OrientHeadToPlusZ(mesh);
            mesh.RecalculateBounds(); var b = mesh.bounds;
            float scale = Mathf.Min(BedKit.Width / b.size.x, BedKit.Length / b.size.z);
            var v = mesh.vertices; var n = mesh.normals;
            var turn = new Func<Vector3, Vector3>(p => new Vector3(p.x, p.z, -p.y));
            for (int i = 0; i < v.Length; i++)
            {
                var p = v[i] - new Vector3(b.center.x, b.min.y, b.center.z);
                v[i] = turn(p) * scale;
                if (n.Length == v.Length) n[i] = turn(n[i]);
            }
            mesh.vertices = v; if (n.Length == v.Length) mesh.normals = n;
            var white = new Color[v.Length]; for (int i = 0; i < white.Length; i++) white[i] = Color.white;
            mesh.colors = white; mesh.RecalculateBounds();
            var size = mesh.bounds.size; float surface = 0;
            foreach (var p in v)
                // Torso-to-knee band only: headboards, skulls and pillows sit at the head (+Y) end.
                if (Mathf.Abs(p.x) < size.x * .25f && p.y > -size.y * .3f && p.y < size.y * .05f) surface = Mathf.Max(surface, -p.z);
            return surface;
        }

        /// <summary>Tripo does not keep a consistent facing: lay the long side along Z, then put the
        /// head end (taller headboard, skull or crescent; the wider end of a coffin) toward +Z.</summary>
        static void OrientHeadToPlusZ(Mesh mesh)
        {
            mesh.RecalculateBounds(); var v = mesh.vertices; var n = mesh.normals;
            bool hasNormals = n.Length == v.Length;
            void Turn(Func<Vector3, Vector3> f)
            {
                for (int i = 0; i < v.Length; i++) { v[i] = f(v[i]); if (hasNormals) n[i] = f(n[i]); }
                mesh.vertices = v; if (hasNormals) mesh.normals = n; mesh.RecalculateBounds();
            }
            if (mesh.bounds.size.x > mesh.bounds.size.z) Turn(p => new Vector3(p.z, p.y, -p.x));
            var b = mesh.bounds; float quarter = b.size.z * .25f;
            float EndScore(int sign)
            {
                float top = b.min.y, left = float.MaxValue, right = float.MinValue;
                foreach (var p in v)
                    if ((p.z - b.center.z) * sign > quarter) { top = Mathf.Max(top, p.y); left = Mathf.Min(left, p.x); right = Mathf.Max(right, p.x); }
                return (top - b.min.y) + .5f * Mathf.Max(0, right - left);
            }
            if (EndScore(-1) > EndScore(1) * 1.03f) Turn(p => new Vector3(-p.x, p.y, -p.z));
        }

        static Texture2D BakeAlbedo(int level, string stem)
        {
            Texture2D sourceTexture = null;
            foreach (var guid in AssetDatabase.FindAssets("t:Texture2D", new[] { $"Assets/TripoModels/{stem}/{stem}.fbm" }))
            {
                var texturePath = AssetDatabase.GUIDToAssetPath(guid);
                if (Path.GetFileNameWithoutExtension(texturePath).EndsWith("_BaseColor", StringComparison.OrdinalIgnoreCase)) sourceTexture = AssetDatabase.LoadAssetAtPath<Texture2D>(texturePath);
            }
            if (sourceTexture == null) throw new InvalidOperationException("Missing bed albedo for " + stem);
            var rt = RenderTexture.GetTemporary(TextureSize, TextureSize, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
            var previous = RenderTexture.active;
            Graphics.Blit(sourceTexture, rt); RenderTexture.active = rt;
            var tex = new Texture2D(TextureSize, TextureSize, TextureFormat.RGBA32, false);
            tex.ReadPixels(new Rect(0, 0, TextureSize, TextureSize), 0, 0); tex.Apply();
            RenderTexture.active = previous; RenderTexture.ReleaseTemporary(rt);
            string path = $"{Output}/bed_{level}_albedo.png";
            File.WriteAllBytes(path, tex.EncodeToPNG()); Object.DestroyImmediate(tex);
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
            var ti = (TextureImporter)AssetImporter.GetAtPath(path);
            ti.textureType = TextureImporterType.Default; ti.sRGBTexture = true; ti.alphaSource = TextureImporterAlphaSource.None;
            ti.mipmapEnabled = true; ti.wrapMode = TextureWrapMode.Clamp; ti.filterMode = FilterMode.Point;
            ti.maxTextureSize = TextureSize; ti.textureCompression = TextureImporterCompression.CompressedHQ; ti.SaveAndReimport();
            return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        }
    }
}
