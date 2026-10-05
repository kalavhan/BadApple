using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace BadAppleHotel.Game
{
    /// <summary>Repeated hotel pieces keep one shared source mesh and submit only visible instances.</summary>
    public sealed class WallInstances
    {
        public sealed class Record
        {
            public WallKit.Piece Piece;
            public Mesh Mesh;
            public Rect Footprint;
            public Vector2 Normal;
            public Matrix4x4 Matrix;
            public Vector4 Data;
            public Bounds Bounds;
            public int StateId, Mode, TriangleCount;
        }

        sealed class Group
        {
            public Mesh Mesh;
            public readonly List<Record> Items = new List<Record>();
        }

        // Each instance has two 64-byte transform matrices. 128 instances fit the GLES3
        // minimum 16KB uniform block; the separate per-instance property block is 2KB.
        // Keep this equal to HotelWall.shader's documented forcemaxcount pragma.
        public const int MaxBatchCapacity = 128;
        static readonly int InstanceProperty = Shader.PropertyToID("_WallInstance");
        static readonly int ClockProperty = Shader.PropertyToID("_WallClock");
        readonly Dictionary<Mesh, Group> byMesh = new Dictionary<Mesh, Group>();
        readonly List<Group> groups = new List<Group>();
        readonly List<Record> records = new List<Record>();
        readonly Plane[] planes = new Plane[6];
        readonly Matrix4x4[] matrices;
        readonly Vector4[] data;
        readonly MaterialPropertyBlock batchProperties = new MaterialPropertyBlock();
        readonly MaterialPropertyBlock singleProperties = new MaterialPropertyBlock();

        public int BatchCapacity => MaxBatchCapacity;
        public bool UsesInstancing { get; }

        public static bool CanUseInstancing(bool supported, GraphicsDeviceType device, string name, string version)
        {
            if (!supported) return false;
            // Observed on Unity 6000.3.24f1 with the Android Emulator's legacy SwiftShader
            // 4.0 driver: both instancing APIs report support but draw nothing, even with
            // a minimal two-instance shader. Ordinary shared-mesh draws render correctly.
            // Restrict this workaround to that tested driver; physical GPUs and newer
            // SwiftShader versions keep hardware instancing.
            bool legacyEmulator = device == GraphicsDeviceType.OpenGLES3 &&
                Contains(name, "Android Emulator OpenGL ES Translator") && Contains(name, "SwiftShader") &&
                Contains(version, "SwiftShader 4.0.");
            return !legacyEmulator;
        }

        static bool Contains(string value, string part) =>
            value != null && value.IndexOf(part, StringComparison.OrdinalIgnoreCase) >= 0;

        public WallInstances()
        {
            UsesInstancing = CanUseInstancing(SystemInfo.supportsInstancing, SystemInfo.graphicsDeviceType,
                SystemInfo.graphicsDeviceName, SystemInfo.graphicsDeviceVersion);
            matrices = new Matrix4x4[BatchCapacity];
            // The array itself must obey the uniform budget, not just the draw count.
            data = new Vector4[BatchCapacity];
        }

        public IReadOnlyList<Record> Records => records;
        public int InstanceCount => records.Count;
        public int Count => records.Count;
        public int GroupCount => groups.Count;
        public int TotalTriangles { get; private set; }
        public int SubmittedTriangles { get; private set; }
        public int LastDrawCalls { get; private set; }
        public int MaxDrawCalls
        {
            get
            {
                if (!UsesInstancing) return records.Count;
                int total = 0;
                foreach (var group in groups) total += (group.Items.Count + BatchCapacity - 1) / BatchCapacity;
                return total;
            }
        }

        public void Add(WallKit.Piece piece, Rect rect, Vector2 normal, int stateIndex, int textureWidth, int mode)
        {
            if (piece?.Mesh == null || rect.width < .0001f || rect.height < .0001f) return;
            if (textureWidth <= 0 || piece.Size.x <= 0 || piece.Size.y <= 0 || piece.Size.z <= 0)
                throw new ArgumentOutOfRangeException(nameof(piece), "Wall instances need positive canonical dimensions and a state texture width.");
            bool horizontal = Mathf.Abs(normal.y) > .5f;
            // Preserve handedness on every face. Reusing right/up on opposite faces
            // reflects asymmetric kit details instead of rotating the same wall piece.
            Vector2 tangent = new Vector2(-normal.y, normal.x);
            Vector2 origin = horizontal
                ? new Vector2(tangent.x > 0 ? rect.xMin : rect.xMax, normal.y < 0 ? rect.yMin : rect.yMax)
                : new Vector2(normal.x < 0 ? rect.xMin : rect.xMax, tangent.y > 0 ? rect.yMin : rect.yMax);
            float sx = (horizontal ? rect.width : rect.height) / piece.Size.x;
            float sz = (horizontal ? rect.height : rect.width) / piece.Size.z;
            // Canonical X runs along the face, Y is height, and +Z goes into the solid cell.
            var matrix = new Matrix4x4(
                new Vector4(tangent.x * sx, tangent.y * sx, 0, 0),
                new Vector4(0, 0, -1, 0),
                new Vector4(-normal.x * sz, -normal.y * sz, 0, 0),
                new Vector4(origin.x, origin.y, .04f, 1));
            var bounds = TransformBounds(matrix, piece.Mesh.bounds);
            // Height transitions stretch the low cap slightly near the switch threshold.
            // Conservatively include full wall height so shader deformation never escapes culling.
            var min = bounds.min; var max = bounds.max;
            min.z = Mathf.Min(min.z, .04f - WallGraph.FullHeight);
            max.z = Mathf.Max(max.z, .04f);
            bounds.SetMinMax(min, max);
            var record = new Record
            {
                Piece = piece, Mesh = piece.Mesh, Footprint = rect, Normal = normal,
                Matrix = matrix, Data = new Vector4((stateIndex + .5f) / textureWidth, piece.Size.y, mode, 1),
                Bounds = bounds, StateId = stateIndex, Mode = mode,
                TriangleCount = (int)piece.Mesh.GetIndexCount(0) / 3
            };
            records.Add(record);
            TotalTriangles += record.TriangleCount;
            if (!byMesh.TryGetValue(piece.Mesh, out var group))
            {
                group = new Group { Mesh = piece.Mesh };
                byMesh.Add(piece.Mesh, group); groups.Add(group);
            }
            group.Items.Add(record);
        }

        public void Draw(Camera camera, Material shared, float now, Func<int, float> heightAt)
        {
            LastDrawCalls = 0; SubmittedTriangles = 0;
            if (camera == null || shared == null || records.Count == 0) return;
            GeometryUtility.CalculateFrustumPlanes(camera, planes);
            shared.SetFloat(ClockProperty, now);
            if (!UsesInstancing) shared.enableInstancing = false;
            bool instancing = UsesInstancing && shared.enableInstancing;
            foreach (var group in groups)
            {
                int count = 0;
                foreach (var record in group.Items)
                {
                    float height = heightAt == null ? WallGraph.FullHeight : heightAt(record.StateId);
                    if ((record.Mode == 1 && height <= .46f) || (record.Mode == 2 && height > .46f)) continue;
                    if (!GeometryUtility.TestPlanesAABB(planes, record.Bounds)) continue;
                    SubmittedTriangles += record.TriangleCount;
                    if (!instancing)
                    {
                        singleProperties.SetVector(InstanceProperty, record.Data);
                        Graphics.DrawMesh(record.Mesh, record.Matrix, shared, 0, camera, 0,
                            singleProperties, ShadowCastingMode.Off, false, null, LightProbeUsage.Off);
                        LastDrawCalls++;
                        continue;
                    }
                    matrices[count] = record.Matrix; data[count] = record.Data; count++;
                    if (count == BatchCapacity)
                    {
                        Flush(group.Mesh, shared, camera, count);
                        count = 0;
                    }
                }
                if (count > 0) Flush(group.Mesh, shared, camera, count);
            }
        }

        void Flush(Mesh mesh, Material shared, Camera camera, int count)
        {
            // Set a fixed-size property array: Unity does not grow an existing MPB array later.
            batchProperties.SetVectorArray(InstanceProperty, data);
            Graphics.DrawMeshInstanced(mesh, 0, shared, matrices, count, batchProperties,
                ShadowCastingMode.Off, false, 0, camera, LightProbeUsage.Off);
            LastDrawCalls++;
        }

        static Bounds TransformBounds(Matrix4x4 matrix, Bounds bounds)
        {
            var e = bounds.extents;
            var x = matrix.MultiplyVector(new Vector3(e.x, 0, 0));
            var y = matrix.MultiplyVector(new Vector3(0, e.y, 0));
            var z = matrix.MultiplyVector(new Vector3(0, 0, e.z));
            var extents = new Vector3(Mathf.Abs(x.x) + Mathf.Abs(y.x) + Mathf.Abs(z.x),
                Mathf.Abs(x.y) + Mathf.Abs(y.y) + Mathf.Abs(z.y), Mathf.Abs(x.z) + Mathf.Abs(y.z) + Mathf.Abs(z.z));
            return new Bounds(matrix.MultiplyPoint3x4(bounds.center), extents * 2);
        }
    }
}
