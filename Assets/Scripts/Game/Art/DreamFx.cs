using System.Collections.Generic;
using UnityEngine;

namespace BadAppleHotel.Game
{
    /// <summary>
    /// Every dream effect in one additive mesh: summoning sigils, rising motes, dream threads,
    /// bolts, lightning, flames and shockwaves. Persistent visuals are drawn again each frame;
    /// short-lived particles are simulated here. The mesh is rebuilt once per frame, so the
    /// whole layer costs a single draw call. Brief flashes also light nearby floors, walls and
    /// sprites through a small fixed set of shader lights (no Light components).
    /// </summary>
    public sealed class DreamFx : System.IDisposable
    {
        /// <summary>Procedural shapes drawn by the DreamFx shader (uv1.x).</summary>
        public enum Shape { Glow = 0, Streak = 1, Ring = 2, Sigil = 3, Pillar = 4, Bolt = 5, Flame = 6, Hex = 7, Thread = 8 }

        public struct Particle
        {
            public Shape Shape;
            public Vector3 Pos, Vel;
            public float Size, Grow, Life, Age, Seed, Drag, Rise, Stretch, Height;
            public Color Color;
            public bool Ground;
        }

        public struct FxLight { public Vector2 Pos; public float Radius; public Color Color; }
        struct Flash { public FxLight Light; public float Age, Life; }

        public const int MaxLights = 6;
        public const int MaxParticles = 600;
        static readonly int LightPosId = Shader.PropertyToID("_FxLightPos");
        static readonly int LightColorId = Shader.PropertyToID("_FxLightColor");
        static readonly int LightCountId = Shader.PropertyToID("_FxLightCount");
        static readonly int TimeId = Shader.PropertyToID("_FxTime");

        readonly List<Particle> particles = new List<Particle>(256);
        readonly List<FxLight> lights = new List<FxLight>(16);
        readonly List<Flash> flashes = new List<Flash>(16);
        readonly List<Vector3> vertices = new List<Vector3>(2048);
        readonly List<Color> colors = new List<Color>(2048);
        readonly List<Vector2> uv0 = new List<Vector2>(2048), ground = new List<Vector2>(2048);
        readonly List<Vector4> uv1 = new List<Vector4>(2048);
        readonly List<int> triangles = new List<int>(3072);
        readonly Vector4[] lightPos = new Vector4[MaxLights];
        readonly Vector4[] lightColor = new Vector4[MaxLights];
        Mesh mesh;
        Material material;
        GameObject view;

        // Cosmetic randomness never draws from UnityEngine.Random, so effects cannot shift gameplay rolls.
        static readonly System.Random rng = new System.Random();
        public static float Range(float min, float max) => min + (float)rng.NextDouble() * (max - min);
        public static Vector3 InSphere()
        {
            Vector3 p;
            do p = new Vector3(Range(-1, 1), Range(-1, 1), Range(-1, 1)); while (p.sqrMagnitude > 1);
            return p;
        }
        public static Vector2 InCircle()
        {
            Vector2 p;
            do p = new Vector2(Range(-1, 1), Range(-1, 1)); while (p.sqrMagnitude > 1);
            return p;
        }

        public int ParticleCount => particles.Count;
        public int QuadCount => vertices.Count / 4;
        public int LightCount { get; private set; }
        public IReadOnlyList<FxLight> PendingLights => lights;

        /// <summary>Creates the renderer. Without a parent the layer only simulates (tests, tools).</summary>
        public DreamFx(Transform parent)
        {
            if (parent == null) return;
            var shader = Resources.Load<Shader>("Shaders/DreamFx");
            if (shader == null) return;
            material = new Material(shader) { name = "Dream effects" };
            mesh = new Mesh { name = "Dream effects", indexFormat = UnityEngine.Rendering.IndexFormat.UInt32 };
            mesh.MarkDynamic();
            view = new GameObject("Dream effects");
            view.transform.SetParent(parent, false);
            view.AddComponent<MeshFilter>().sharedMesh = mesh;
            var renderer = view.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;
        }

        // ------------------------------------------------------------------ particles

        public void Emit(Particle p)
        {
            if (particles.Count >= MaxParticles) return;
            if (p.Seed == 0) p.Seed = Range(0, 97f);
            particles.Add(p);
        }

        public void Emit(Shape shape, Vector3 pos, Vector3 vel, float size, float life, Color color,
            float grow = 0, float drag = 0, float rise = 0, bool groundPlane = false, float stretch = 0)
        {
            Emit(new Particle { Shape = shape, Pos = pos, Vel = vel, Size = size, Life = life, Color = color,
                Grow = grow, Drag = drag, Rise = rise, Ground = groundPlane, Stretch = stretch });
        }

        /// <summary>A burst of glowing sparks thrown out from a point (z is negative up).</summary>
        public void Burst(Vector3 at, int count, Color color, float speed, float size, float life, float upward = .5f, float gravity = 0)
        {
            for (int i = 0; i < count; i++)
            {
                var dir = InSphere(); dir.z = -Mathf.Abs(dir.z) * upward - .15f;
                Emit(new Particle { Shape = Shape.Glow, Pos = at, Vel = dir.normalized * speed * Range(.45f, 1f),
                    Size = size * Range(.6f, 1.2f), Life = life * Range(.6f, 1.1f), Color = color,
                    Drag = 2.4f, Rise = -gravity, Stretch = .035f });
            }
        }

        public void Light(Vector2 pos, float radius, Color color)
        {
            if (color.maxColorComponent <= .01f || radius <= 0) return;
            lights.Add(new FxLight { Pos = pos, Radius = radius, Color = color });
        }

        /// <summary>A light that fades out over its life (muzzle flashes, impacts, summons).</summary>
        public void FlashLight(Vector2 pos, float radius, Color color, float life)
        {
            if (flashes.Count < 32) flashes.Add(new Flash { Light = new FxLight { Pos = pos, Radius = radius, Color = color }, Life = Mathf.Max(.01f, life) });
        }

        /// <summary>Advances every particle. Dead particles are removed in place.</summary>
        public void Step(float dt)
        {
            for (int i = flashes.Count - 1; i >= 0; i--)
            {
                var f = flashes[i]; f.Age += dt;
                if (f.Age >= f.Life) { flashes.RemoveAt(i); continue; }
                flashes[i] = f;
            }
            for (int i = particles.Count - 1; i >= 0; i--)
            {
                var p = particles[i];
                p.Age += dt;
                if (p.Age < 0) { particles[i] = p; continue; }
                if (p.Age >= p.Life) { particles[i] = particles[particles.Count - 1]; particles.RemoveAt(particles.Count - 1); continue; }
                p.Vel *= Mathf.Exp(-p.Drag * dt);
                p.Vel.z -= p.Rise * dt;
                p.Pos += p.Vel * dt;
                p.Size += p.Grow * dt;
                particles[i] = p;
            }
        }

        public void Clear()
        {
            particles.Clear(); lights.Clear(); flashes.Clear(); ResetDraw();
            LightCount = 0;
            Shader.SetGlobalFloat(LightCountId, 0);
        }

        // ------------------------------------------------------------------ immediate drawing

        void ResetDraw()
        {
            vertices.Clear(); colors.Clear(); uv0.Clear(); uv1.Clear(); ground.Clear(); triangles.Clear();
        }

        void Quad(Vector3 a, Vector3 b, Vector3 c, Vector3 d, Color color, Shape shape, float age, float seed, float extra, Vector2 anchor,
            Vector2 uvA, Vector2 uvB, Vector2 uvC, Vector2 uvD)
        {
            int i = vertices.Count;
            vertices.Add(a); vertices.Add(b); vertices.Add(c); vertices.Add(d);
            uv0.Add(uvA); uv0.Add(uvB); uv0.Add(uvC); uv0.Add(uvD);
            var data = new Vector4((float)shape, age, seed, extra);
            for (int k = 0; k < 4; k++) { colors.Add(color); uv1.Add(data); ground.Add(anchor); }
            triangles.Add(i); triangles.Add(i + 1); triangles.Add(i + 2); triangles.Add(i); triangles.Add(i + 2); triangles.Add(i + 3);
        }

        /// <summary>A camera-facing quad; uv runs -1..1 on both axes.</summary>
        public void Billboard(Vector3 center, float halfWidth, float halfHeight, Shape shape, Color color, float age = 0, float seed = 0, float extra = 0)
        {
            Vector3 r = HotelView3D.Right * halfWidth, u = HotelView3D.Up * halfHeight;
            Quad(center - r - u, center + r - u, center + r + u, center - r + u, color, shape, age, seed, extra, center,
                new Vector2(-1, -1), new Vector2(1, -1), new Vector2(1, 1), new Vector2(-1, 1));
        }

        /// <summary>A column standing on the floor, facing the camera; uv.y runs 0 at the floor to 1 at the top.</summary>
        public void Column(Vector2 groundPos, float halfWidth, float height, Shape shape, Color color, float age = 0, float seed = 0, float extra = 0)
        {
            Vector3 r = HotelView3D.Right * halfWidth, baseP = new Vector3(groundPos.x, groundPos.y, .05f), top = baseP + Vector3.back * height;
            Quad(baseP - r, baseP + r, top + r, top - r, color, shape, age, seed, extra, groundPos,
                new Vector2(-1, 0), new Vector2(1, 0), new Vector2(1, 1), new Vector2(-1, 1));
        }

        /// <summary>A decal lying on the floor; uv runs -1..1, rotated by angle (radians).</summary>
        public void Decal(Vector2 center, float radius, Shape shape, Color color, float age = 0, float seed = 0, float extra = 0, float angle = 0, float lift = .02f)
        {
            float c = Mathf.Cos(angle) * radius, s = Mathf.Sin(angle) * radius;
            var x = new Vector3(c, s, 0); var y = new Vector3(-s, c, 0);
            var o = new Vector3(center.x, center.y, .08f - lift);
            Quad(o - x - y, o + x - y, o + x + y, o - x + y, color, shape, age, seed, extra, center,
                new Vector2(-1, -1), new Vector2(1, -1), new Vector2(1, 1), new Vector2(-1, 1));
        }

        /// <summary>A camera-facing band from a to b; uv.x runs 0..1 along it, uv.y -1..1 across.
        /// extra carries the band's length so shaders can keep detail density constant.</summary>
        public void Band(Vector3 a, Vector3 b, float halfWidth, Shape shape, Color color, float age = 0, float seed = 0, float u0 = 0, float u1 = 1, float length = -1)
        {
            var along = b - a;
            if (along.sqrMagnitude < 1e-6f) return;
            var side = Vector3.Cross(along, HotelView3D.Forward);
            if (side.sqrMagnitude < 1e-6f) side = HotelView3D.Right;
            side = side.normalized * halfWidth;
            Vector2 anchorA = a, anchorB = b;
            int i = vertices.Count;
            Quad(a - side, b - side, b + side, a + side, color, shape, age, seed, length >= 0 ? length : along.magnitude, anchorA,
                new Vector2(u0, -1), new Vector2(u1, -1), new Vector2(u1, 1), new Vector2(u0, 1));
            ground[i + 1] = anchorB; ground[i + 2] = anchorB;
        }

        /// <summary>A polyline drawn as connected bands (lightning, threads).</summary>
        public void Path(IReadOnlyList<Vector3> points, float halfWidth, Shape shape, Color color, float age = 0, float seed = 0)
        {
            if (points.Count < 2) return;
            float total = 0;
            for (int i = 1; i < points.Count; i++) total += Vector3.Distance(points[i - 1], points[i]);
            if (total <= 0) return;
            float run = 0;
            for (int i = 1; i < points.Count; i++)
            {
                float len = Vector3.Distance(points[i - 1], points[i]);
                Band(points[i - 1], points[i], halfWidth, shape, color, age, seed, run / total, (run + len) / total, total);
                run += len;
            }
        }

        // ------------------------------------------------------------------ frame

        /// <summary>Draws live particles, uploads the mesh and binds this frame's lights.</summary>
        public void Flush(float time)
        {
            Shader.SetGlobalFloat(TimeId, time);
            foreach (var f in flashes)
            {
                float k = 1 - f.Age / f.Life;
                Light(f.Light.Pos, f.Light.Radius, f.Light.Color * (k * k));
            }
            foreach (var p in particles)
            {
                if (p.Age < 0) continue;
                float k = p.Age / Mathf.Max(.0001f, p.Life);
                if (p.Shape == Shape.Pillar) Column(p.Pos, p.Size, p.Height, p.Shape, p.Color, k, p.Seed);
                else if (p.Ground) Decal(p.Pos, p.Size, p.Shape, p.Color, k, p.Seed, 0, p.Seed);
                else if (p.Stretch > 0 && p.Vel.sqrMagnitude > .0004f)
                    Band(p.Pos - p.Vel * p.Stretch, p.Pos, p.Size, p.Shape == Shape.Glow ? Shape.Streak : p.Shape, p.Color, k, p.Seed);
                else Billboard(p.Pos, p.Size, p.Size, p.Shape, p.Color, k, p.Seed);
            }

            lights.Sort((a, b) => (b.Color.maxColorComponent * b.Radius).CompareTo(a.Color.maxColorComponent * a.Radius));
            LightCount = Mathf.Min(MaxLights, lights.Count);
            for (int i = 0; i < MaxLights; i++)
            {
                if (i < LightCount)
                {
                    var l = lights[i];
                    lightPos[i] = new Vector4(l.Pos.x, l.Pos.y, 1f / (l.Radius * l.Radius), 0);
                    lightColor[i] = new Vector4(l.Color.r, l.Color.g, l.Color.b, 0);
                }
                else { lightPos[i] = new Vector4(-999, -999, 1, 0); lightColor[i] = Vector4.zero; }
            }
            lights.Clear();
            if (material != null)
            {
                Shader.SetGlobalVectorArray(LightPosId, lightPos);
                Shader.SetGlobalVectorArray(LightColorId, lightColor);
                Shader.SetGlobalFloat(LightCountId, LightCount);
            }

            if (mesh != null)
            {
                mesh.Clear();
                if (vertices.Count > 0)
                {
                    mesh.SetVertices(vertices); mesh.SetColors(colors);
                    mesh.SetUVs(0, uv0); mesh.SetUVs(1, uv1); mesh.SetUVs(2, ground);
                    mesh.SetTriangles(triangles, 0, false);
                    mesh.bounds = new Bounds(Vector3.zero, Vector3.one * 4000f);
                }
            }
            ResetDraw();
        }

        public void Dispose()
        {
            Clear();
            Remove(view); Remove(mesh); Remove(material);
            view = null; mesh = null; material = null;
        }

        static void Remove(Object obj)
        {
            if (obj == null) return;
            if (Application.isPlaying) Object.Destroy(obj); else Object.DestroyImmediate(obj);
        }
    }
}
