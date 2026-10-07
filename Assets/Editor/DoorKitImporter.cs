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
    /// <summary>Bakes the approved seven door leaves into the existing hotel frame's axes and
    /// dimensions. Source exports remain untouched and are never referenced by the runtime kit.</summary>
    public static class DoorKitImporter
    {
        const string Output = "Assets/Resources/Art3D/Doors";
        const int TextureSize = 512, TriangleBudget = 4000;

        // Canonical folders from docs/art/doors, followed by verified Tripo export names.
        // Level 6 is Bad Reflection: the rejected eye-door concept is deliberately excluded.
        static readonly string[][] Sources =
        {
            new[] { "door_01_cardboard", "cardboard_crate_3d_model" },
            new[] { "door_02_planks", "rustic_wooden_door_3d_model" },
            new[] { "door_03_hotel", "antique_wooden_door_3d_model" },
            new[] { "door_04_runes", "wooden_door_3d_model" },
            new[] { "door_05_floating", "stone_door_3d_model" },
            new[] { "door_06_mirror", "medieval_wooden_door_3d_model" },
            new[] { "door_07_skull", "skull_door_3d_model" }
        };

        [MenuItem("Bad Apple/Art/Bake imported door models")]
        public static void Bake()
        {
            Directory.CreateDirectory(Output);
            // Register newly extracted FBX/textures before searching the AssetDatabase.
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            string kitPath = Output + "/HotelDoorKit.asset";
            var kit = AssetDatabase.LoadAssetAtPath<DoorKit>(kitPath);
            var pieces = new List<DoorKit.Piece>();
            int baked = 0;
            try
            {
                for (int index = 0; index < Sources.Length; index++)
                {
                    int level = index + 1;
                    string sourcePath = FindSource(Sources[index]);
                    if (sourcePath == null)
                    {
                        // Original exports are local authoring inputs. Rebuilding a partial import
                        // must not erase a previously baked, versioned door from another machine.
                        var previous = kit != null ? kit.Get(level) : null;
                        if (previous != null)
                        {
                            if (previous.BrokenMesh == null) previous.BrokenMesh = BakeBrokenAsset(previous.Mesh, level);
                            pieces.Add(previous);
                        }
                        Debug.LogWarning($"DoorKit: level {level} source is missing; " +
                            (previous != null ? "retaining its baked mesh." : "keeping its sprite fallback."));
                        continue;
                    }
                    EditorUtility.DisplayProgressBar("Bake hotel doors", sourcePath, index / 7f);
                    pieces.Add(BakePiece(level, sourcePath));
                    baked++;
                }
                if (kit == null)
                {
                    kit = ScriptableObject.CreateInstance<DoorKit>();
                    AssetDatabase.CreateAsset(kit, kitPath);
                }
                kit.Pieces = pieces.ToArray();
                EditorUtility.SetDirty(kit);
                AssetDatabase.SaveAssets();
                AssetDatabase.Refresh();
                Debug.Log($"DoorKit: baked {baked} imported level(s), {pieces.Count} available. " +
                    "Levels 8–10 reuse level 7 when available.");
            }
            finally { EditorUtility.ClearProgressBar(); }
        }

        public static void BakeBatch() => Bake();

        static string FindSource(string[] folders)
        {
            foreach (string folder in folders)
            {
                string root = "Assets/TripoModels/" + folder;
                if (!Directory.Exists(root)) continue;
                string expected = root + "/" + folder + ".fbx";
                if (File.Exists(expected)) return expected;
                var paths = Directory.GetFiles(root, "*", SearchOption.AllDirectories)
                    .Where(p => Path.GetExtension(p).Equals(".fbx", StringComparison.OrdinalIgnoreCase))
                    .OrderBy(p => p, StringComparer.Ordinal).ToArray();
                if (paths.Length == 1) return paths[0].Replace('\\', '/');
                if (paths.Length > 1)
                    throw new InvalidOperationException("Ambiguous door source folder: " + root);
            }
            return null;
        }

        static DoorKit.Piece BakePiece(int level, string sourcePath)
        {
            var source = AssetDatabase.LoadAssetAtPath<GameObject>(sourcePath);
            if (source == null) throw new InvalidOperationException("Cannot load door model: " + sourcePath);
            var original = ReadMesh(source);
            AlignPanelToFrame(original);
            int sourceTriangles = original.triangles.Length / 3;
            Mesh mesh = null;
            try
            {
                mesh = Simplify(original);
                ToGameAxes(mesh);
                mesh.name = "door_" + level;
                var albedo = BakeAlbedo(level, Path.GetDirectoryName(sourcePath));
                string meshPath = $"{Output}/door_{level}.asset";
                var existing = AssetDatabase.LoadAssetAtPath<Mesh>(meshPath);
                if (existing != null)
                {
                    EditorUtility.CopySerialized(mesh, existing);
                    Object.DestroyImmediate(mesh);
                    mesh = existing;
                }
                else AssetDatabase.CreateAsset(mesh, meshPath);
                var piece = new DoorKit.Piece
                {
                    Level = level, Mesh = mesh, BrokenMesh = BakeBrokenAsset(mesh, level),
                    Albedo = albedo, Size = mesh.bounds.size,
                    Source = sourcePath, SourceTriangles = sourceTriangles,
                    Triangles = mesh.triangles.Length / 3
                };
                Debug.Log($"DoorKit: level {level}: {sourceTriangles:N0} → {piece.Triangles:N0} triangles; " +
                    $"bounds {mesh.bounds}; source {sourcePath}");
                return piece;
            }
            finally
            {
                Object.DestroyImmediate(original);
                if (mesh != null && !AssetDatabase.Contains(mesh)) Object.DestroyImmediate(mesh);
            }
        }

        static Mesh BakeBrokenAsset(Mesh source, int level)
        {
            var mesh = MakeBrokenFragments(source, level);
            mesh.name = "door_" + level + "_broken";
            string path = $"{Output}/door_{level}_broken.asset";
            var existing = AssetDatabase.LoadAssetAtPath<Mesh>(path);
            if (existing != null)
            {
                EditorUtility.CopySerialized(mesh, existing);
                Object.DestroyImmediate(mesh);
                return existing;
            }
            AssetDatabase.CreateAsset(mesh, path);
            return mesh;
        }

        /// <summary>Break the authored leaf into six independently laid-down chunks. Each source
        /// triangle belongs to one chunk, retaining its UVs and surface normals. The breaks follow
        /// triangle edges instead of stretching the intact door into a flat rubble sprite.</summary>
        static Mesh MakeBrokenFragments(Mesh source, int level)
        {
            var sourceVertices = source.vertices;
            var sourceNormals = source.normals;
            var sourceTangents = source.tangents;
            var sourceUV = source.uv;
            var sourceTriangles = source.triangles;
            bool hasNormals = sourceNormals.Length == sourceVertices.Length;
            bool hasTangents = sourceTangents.Length == sourceVertices.Length;
            bool hasUV = sourceUV.Length == sourceVertices.Length;
            var groups = new List<int>[6];
            for (int i = 0; i < groups.Length; i++) groups[i] = new List<int>();
            for (int i = 0; i < sourceTriangles.Length; i += 3)
            {
                var center = (sourceVertices[sourceTriangles[i]] + sourceVertices[sourceTriangles[i + 1]] +
                    sourceVertices[sourceTriangles[i + 2]]) / 3f;
                int column = Mathf.Clamp(Mathf.FloorToInt(center.x / DoorKit.Width * 2), 0, 1);
                int row = Mathf.Clamp(Mathf.FloorToInt(-center.z / DoorKit.Height * 3), 0, 2);
                var group = groups[row * 2 + column];
                group.Add(sourceTriangles[i]); group.Add(sourceTriangles[i + 1]); group.Add(sourceTriangles[i + 2]);
            }

            var vertices = new List<Vector3>();
            var normals = new List<Vector3>();
            var tangents = new List<Vector4>();
            var uv = new List<Vector2>();
            var triangles = new List<int>(sourceTriangles.Length);
            for (int groupIndex = 0; groupIndex < groups.Length; groupIndex++)
            {
                var group = groups[groupIndex];
                if (group.Count == 0) continue;
                var bounds = new Bounds(sourceVertices[group[0]], Vector3.zero);
                foreach (int index in group) bounds.Encapsulate(sourceVertices[index]);
                // Fixed, tier-dependent offsets make rebakes stable without touching Unity's RNG.
                float seed = level * .79f + groupIndex * 2.17f;
                var rotation = Quaternion.Euler(Mathf.Sin(seed) * 4, Mathf.Cos(seed * 1.3f) * 5,
                    Mathf.Sin(seed * .83f) * 22) * Quaternion.Euler(90, 0, 0);
                var position = new Vector3(.15f + (groupIndex % 3) * .32f,
                    (groupIndex / 3 == 0 ? -.14f : .14f) + Mathf.Sin(seed * 1.7f) * .025f, 0);
                var remap = new Dictionary<int, int>();
                int firstVertex = vertices.Count;
                float lowest = float.NegativeInfinity;
                foreach (int index in group)
                {
                    if (!remap.TryGetValue(index, out int newIndex))
                    {
                        newIndex = vertices.Count;
                        remap.Add(index, newIndex);
                        var vertex = rotation * ((sourceVertices[index] - bounds.center) * .48f) + position;
                        lowest = Mathf.Max(lowest, vertex.z);
                        vertices.Add(vertex);
                        if (hasNormals) normals.Add((rotation * sourceNormals[index]).normalized);
                        if (hasTangents)
                        {
                            var tangent = sourceTangents[index];
                            var direction = rotation * new Vector3(tangent.x, tangent.y, tangent.z);
                            tangents.Add(new Vector4(direction.x, direction.y, direction.z, tangent.w));
                        }
                        if (hasUV) uv.Add(sourceUV[index]);
                    }
                    triangles.Add(newIndex);
                }
                // Each piece contacts the floor, with slight stacking so coincident fragments do
                // not fight for the same depth. Negative Z is above the floor in hotel coordinates.
                float lift = .002f + (groupIndex % 3) * .004f;
                for (int i = firstVertex; i < vertices.Count; i++)
                {
                    var vertex = vertices[i];
                    vertex.z -= lowest + lift;
                    vertices[i] = vertex;
                }
            }

            // Large source triangles sometimes span a partition. Fit the combined fragments to
            // the threshold, never exceeding its width, .6 floor depth or .12 height.
            var allBounds = new Bounds(vertices[0], Vector3.zero);
            foreach (var vertex in vertices) allBounds.Encapsulate(vertex);
            float floorScale = Mathf.Min(1, Mathf.Min(DoorKit.Width / Mathf.Max(.0001f, allBounds.size.x),
                .6f / Mathf.Max(.0001f, allBounds.size.y)));
            float heightScale = Mathf.Min(1, .118f / Mathf.Max(.0001f, allBounds.size.z));
            for (int i = 0; i < vertices.Count; i++)
            {
                var vertex = vertices[i];
                vertices[i] = new Vector3((vertex.x - allBounds.center.x) * floorScale + DoorKit.Width * .5f,
                    (vertex.y - allBounds.center.y) * floorScale, (vertex.z - allBounds.max.z) * heightScale - .002f);
                if (hasNormals)
                {
                    var normal = normals[i];
                    normals[i] = new Vector3(normal.x / floorScale, normal.y / floorScale, normal.z / heightScale).normalized;
                }
                if (hasTangents)
                {
                    var tangent = tangents[i];
                    var direction = new Vector3(tangent.x * floorScale, tangent.y * floorScale, tangent.z * heightScale).normalized;
                    tangents[i] = new Vector4(direction.x, direction.y, direction.z, tangent.w);
                }
            }
            var mesh = new Mesh { indexFormat = vertices.Count > 65535 ? IndexFormat.UInt32 : IndexFormat.UInt16 };
            mesh.SetVertices(vertices);
            mesh.SetTriangles(triangles, 0);
            if (hasUV) mesh.SetUVs(0, uv);
            if (hasNormals) mesh.SetNormals(normals); else mesh.RecalculateNormals();
            if (hasTangents) mesh.SetTangents(tangents);
            var colors = new Color[vertices.Count];
            for (int i = 0; i < colors.Length; i++) colors[i] = Color.white;
            mesh.colors = colors;
            mesh.RecalculateBounds();
            return mesh;
        }

        static Mesh ReadMesh(GameObject source)
        {
            var go = Object.Instantiate(source);
            try
            {
                // Imported Tripo models are Y-up with their front toward +Z. Turn the whole
                // export, including its authored UVs, so the baked front will point outward (-Y).
                var turn = Matrix4x4.Rotate(Quaternion.Euler(0, 180, 0));
                var parts = new List<CombineInstance>();
                foreach (var filter in go.GetComponentsInChildren<MeshFilter>())
                {
                    if (filter.sharedMesh == null) continue;
                    for (int sub = 0; sub < filter.sharedMesh.subMeshCount; sub++)
                        parts.Add(new CombineInstance
                        {
                            mesh = filter.sharedMesh, subMeshIndex = sub,
                            transform = turn * filter.transform.localToWorldMatrix
                        });
                }
                if (parts.Count == 0) throw new InvalidOperationException("Door model has no static mesh: " + source.name);
                var mesh = new Mesh { indexFormat = IndexFormat.UInt32 };
                mesh.CombineMeshes(parts.ToArray(), true, true);
                return mesh;
            }
            finally { Object.DestroyImmediate(go); }
        }

        // Generated exports can contain a door panel turned inside an axis-aligned FBX.
        // Find the dominant upright back face before fitting dimensions; bounding-box fitting
        // alone preserves that skew and makes a closed leaf disagree with its wall.
        static void AlignPanelToFrame(Mesh mesh)
        {
            var vertices = mesh.vertices;
            var indices = mesh.triangles;
            var faces = new List<Vector3>();
            var weights = new float[25]; // Five-degree bins over the rear-facing hemisphere.
            for (int i = 0; i < indices.Length; i += 3)
            {
                var face = Vector3.Cross(vertices[indices[i + 1]] - vertices[indices[i]],
                    vertices[indices[i + 2]] - vertices[indices[i]]);
                float area = face.magnitude;
                if (area < .0000001f || face.z / area < .5f || Mathf.Abs(face.y) / area > .3f) continue;
                float yaw = Mathf.Atan2(face.x, face.z) * Mathf.Rad2Deg;
                weights[Mathf.Clamp(Mathf.RoundToInt((yaw + 60) / 5), 0, 24)] += area;
                faces.Add(face);
            }
            if (faces.Count == 0) throw new InvalidOperationException("Door export has no upright back panel.");
            int peak = 0;
            for (int i = 1; i < weights.Length; i++) if (weights[i] > weights[peak]) peak = i;
            float peakYaw = peak * 5 - 60;
            var normal = Vector3.zero;
            foreach (var face in faces)
                if (Mathf.Abs(Mathf.Atan2(face.x, face.z) * Mathf.Rad2Deg - peakYaw) <= 12) normal += face;
            float correction = -Mathf.Atan2(normal.x, normal.z) * Mathf.Rad2Deg;
            var turn = Quaternion.Euler(0, correction, 0);
            var normals = mesh.normals;
            var tangents = mesh.tangents;
            for (int i = 0; i < vertices.Length; i++)
            {
                vertices[i] = turn * vertices[i];
                if (normals.Length == vertices.Length) normals[i] = turn * normals[i];
                if (tangents.Length == vertices.Length)
                {
                    var t = tangents[i]; var xyz = turn * new Vector3(t.x, t.y, t.z);
                    tangents[i] = new Vector4(xyz.x, xyz.y, xyz.z, t.w);
                }
            }
            mesh.vertices = vertices;
            if (normals.Length == vertices.Length) mesh.normals = normals;
            if (tangents.Length == vertices.Length) mesh.tangents = tangents;
            mesh.RecalculateBounds();
            Debug.Log($"DoorKit: aligned authored panel by {correction:F2} degrees before frame fitting.");
        }

        static Mesh Simplify(Mesh source)
        {
            int triangles = source.triangles.Length / 3;
            if (triangles <= TriangleBudget) return Object.Instantiate(source);
            var reducer = new MeshSimplifier();
            var options = SimplificationOptions.Default;
            options.MaxIterationCount = 200;
            options.PreserveUVSeamEdges = false;
            options.PreserveUVFoldoverEdges = true;
            options.EnableSmartLink = true;
            reducer.SimplificationOptions = options;
            reducer.Initialize(source);
            reducer.SimplifyMesh((float)TriangleBudget / triangles);
            var result = reducer.ToMesh();
            // Very dense exports can contain enough tiny UV foldovers to prevent the first
            // pass reaching a mobile budget. Preserve interpolated UVs but allow those edges.
            int reducedTriangles = result.triangles.Length / 3;
            if (reducedTriangles > TriangleBudget * 1.1f)
            {
                options.PreserveUVFoldoverEdges = false;
                reducer = new MeshSimplifier { SimplificationOptions = options };
                reducer.Initialize(result);
                reducer.SimplifyMesh((float)TriangleBudget / reducedTriangles);
                Object.DestroyImmediate(result);
                result = reducer.ToMesh();
            }
            return result;
        }

        static void ToGameAxes(Mesh mesh)
        {
            mesh.RecalculateBounds();
            var b = mesh.bounds;
            if (b.size.x < .00001f || b.size.y < .00001f || b.size.z < .00001f)
                throw new InvalidOperationException("Door source has a degenerate volume.");
            float sx = DoorKit.Width / b.size.x, sy = DoorKit.Height / b.size.y;
            float sz = Mathf.Min(Mathf.Min(sx, sy), DoorKit.MaxDepth / b.size.z);
            var vertices = mesh.vertices;
            var normals = mesh.normals;
            var tangents = mesh.tangents;
            var pivot = new Vector3(b.min.x, b.min.y, b.center.z);
            var colors = new Color[vertices.Length];
            for (int i = 0; i < vertices.Length; i++)
            {
                var p = vertices[i] - pivot;
                vertices[i] = new Vector3(p.x * sx, p.z * sz, -p.y * sy);
                // Fit is nonuniform: normals need the inverse transpose, while tangents need
                // the forward transform. This rotation/positive scale preserves handedness.
                if (normals.Length == vertices.Length)
                {
                    var n = normals[i];
                    normals[i] = new Vector3(n.x / sx, n.z / sz, -n.y / sy).normalized;
                }
                if (tangents.Length == vertices.Length)
                {
                    var t = tangents[i];
                    var direction = new Vector3(t.x * sx, t.z * sz, -t.y * sy).normalized;
                    tangents[i] = new Vector4(direction.x, direction.y, direction.z, t.w);
                }
                colors[i] = Color.white;
            }
            mesh.vertices = vertices;
            if (normals.Length == vertices.Length) mesh.normals = normals;
            else mesh.RecalculateNormals();
            if (tangents.Length == vertices.Length) mesh.tangents = tangents;
            mesh.colors = colors;
            mesh.RecalculateBounds();
        }

        static Texture2D BakeAlbedo(int level, string sourceFolder)
        {
            Texture2D source = null;
            foreach (string guid in AssetDatabase.FindAssets("t:Texture2D", new[] { sourceFolder }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                if (!Path.GetFileNameWithoutExtension(path).EndsWith("_BaseColor", StringComparison.OrdinalIgnoreCase)) continue;
                if (source != null) throw new InvalidOperationException("Door source needs one albedo atlas: " + sourceFolder);
                source = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
            }
            if (source == null) throw new InvalidOperationException("Missing door albedo: " + sourceFolder);
            var rt = RenderTexture.GetTemporary(TextureSize, TextureSize, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
            var previous = RenderTexture.active;
            var texture = new Texture2D(TextureSize, TextureSize, TextureFormat.RGBA32, false);
            string outputPath = $"{Output}/door_{level}_albedo.png";
            try
            {
                Graphics.Blit(source, rt);
                RenderTexture.active = rt;
                texture.ReadPixels(new Rect(0, 0, TextureSize, TextureSize), 0, 0);
                texture.Apply();
                File.WriteAllBytes(outputPath, texture.EncodeToPNG());
            }
            finally
            {
                RenderTexture.active = previous;
                RenderTexture.ReleaseTemporary(rt);
                Object.DestroyImmediate(texture);
            }
            AssetDatabase.ImportAsset(outputPath, ImportAssetOptions.ForceSynchronousImport);
            var importer = (TextureImporter)AssetImporter.GetAtPath(outputPath);
            importer.textureType = TextureImporterType.Default;
            importer.sRGBTexture = true;
            importer.alphaSource = TextureImporterAlphaSource.None;
            importer.mipmapEnabled = true;
            importer.wrapMode = TextureWrapMode.Clamp;
            importer.filterMode = FilterMode.Point;
            importer.maxTextureSize = TextureSize;
            importer.textureCompression = TextureImporterCompression.CompressedHQ;
            importer.SaveAndReimport();
            return AssetDatabase.LoadAssetAtPath<Texture2D>(outputPath);
        }
    }
}
