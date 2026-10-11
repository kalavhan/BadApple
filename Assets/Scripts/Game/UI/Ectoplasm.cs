using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace BadAppleHotel.Game
{
    /// <summary>
    /// Five persistent goo anchors with deterministic fixed-step physics. All positions use the HUD's
    /// top-left-origin canvas. Register anchors before Step/Draw; neither input nor Layout draws geometry.
    /// </summary>
    public sealed class Ectoplasm : IDisposable
    {
        public const int MaxDrips = 5, MaxDroplets = 8, MaxPuddles = 5, MaxSpecks = 20, MaxVertices = 599;
        public const float FixedStep = 1f / 120f;
        public const string LintelA = "Lintel A", LintelB = "Lintel B", LintelC = "Lintel C", Summon1 = "Summon 1", Summon2 = "Summon 2";
        public enum DripState { Refill, Swell, Neck, Recoil, Retract, Paused }

        sealed class Drip
        {
            public string Id;
            public Vector2 Root;
            public float Width, Rest, LandY, Length, Neck, Bulb, Radius, Age, Duration, StartLength, StartBulb, Phase;
            public bool Enabled, Summon, Forced;
            public DripState State;
            public System.Random Random;
        }

        struct Droplet
        {
            public Drip Source;
            public Vector2 Pos, Velocity;
            public float Radius, Delay, SquashAge;
            public bool Squashing;
        }
        struct Puddle { public Drip Source; public Vector2 Pos; public float Radius, Age; }
        struct Speck { public Drip Source; public Vector2 Pos, Velocity; public float Radius, Age; }

        public struct DripSnapshot
        {
            public DripState State;
            public Vector2 Root;
            public float Length, Neck, Bulb, Radius, Age, Duration;
            public bool Enabled;
        }
        public struct DropletSnapshot
        {
            public Vector2 Position, Velocity;
            public float Radius;
            public bool Squashing;
        }

        readonly List<Drip> drips = new List<Drip>(MaxDrips);
        readonly List<Droplet> droplets = new List<Droplet>(MaxDroplets);
        readonly List<Puddle> puddles = new List<Puddle>(MaxPuddles);
        readonly List<Speck> specks = new List<Speck>(MaxSpecks);
        Material bodyMaterial, glowMaterial;
        double accumulator;
        float drawOpacity = 1f;
        float simulationTime, lastRelease = -100f;
        int openSeed, lastFrame = -1;
        bool opened, closing;

        public EctoplasmTuning Tuning = EctoplasmTuning.Default;
        public int DripCount => drips.Count;
        public int DropletCount => droplets.Count;
        public int PuddleCount => puddles.Count;
        public int SpeckCount => specks.Count;
        public int ReleaseCount { get; private set; }
        public int ImpactCount { get; private set; }
        public float SimulationTime => simulationTime;
        public bool IsClosing => closing;
        public bool IsOpen => opened;
        public event Action<string, float, bool> Released;

        public void Open(int seed)
        {
            Clear();
            opened = true;
            openSeed = seed;
        }

        public void Clear()
        {
            drips.Clear(); droplets.Clear(); puddles.Clear(); specks.Clear();
            accumulator = 0; simulationTime = 0; lastRelease = -100f; lastFrame = -1;
            opened = false; closing = false; ReleaseCount = 0; ImpactCount = 0;
        }

        public void BeginClose() { closing = true; }

        /// <summary>Summon anchors use landY as their fade-out limit; other anchors collide with it.</summary>
        public void SetAnchor(string id, Vector2 root, float rootWidth, float restLen, float landY, bool enabled)
        {
            if (!opened || string.IsNullOrEmpty(id)) return;
            var drip = Find(id);
            if (drip == null)
            {
                if (drips.Count >= MaxDrips) return;
                drip = new Drip { Id = id, Root = root, Width = Mathf.Max(1, rootWidth), Rest = Mathf.Max(1, restLen), LandY = landY,
                    Enabled = enabled, Summon = id == Summon1 || id == Summon2, Random = new System.Random(unchecked(StableHash(id) + openSeed)) };
                drip.Phase = Range(drip, 0, Mathf.PI * 2);
                drip.Length = Tuning.RefillLength * drip.Rest;
                drips.Add(drip);
                if (enabled)
                {
                    // Start throughout the cycle, including a thinning neck, without creating a phantom drop.
                    int state = drip.Random.Next(4);
                    Begin(drip, (DripState)state);
                    drip.Age = Range(drip, 0, drip.Duration);
                    Shape(drip);
                }
                else { drip.State = DripState.Paused; drip.Length = 0; }
                return;
            }
            Vector2 delta = root - drip.Root;
            if (delta.sqrMagnitude > 0) TranslateParticles(drip, delta);
            drip.Root = root; drip.Width = Mathf.Max(1, rootWidth); drip.Rest = Mathf.Max(1, restLen); drip.LandY = landY;
            if (drip.Enabled != enabled)
            {
                drip.Enabled = enabled;
                Begin(drip, enabled ? DripState.Refill : DripState.Retract);
            }
        }

        public void PressSummon()
        {
            if (!opened || closing) return;
            foreach (var drip in drips)
            {
                if (!drip.Summon || !drip.Enabled) continue;
                drip.Radius = drip.Bulb = Mathf.Max(Tuning.RestBulb, drip.Radius);
                Begin(drip, DripState.Neck);
                drip.Duration = Mathf.Max(FixedStep, Tuning.PressNeckDuration);
                drip.Forced = true;
            }
        }

        public void Step()
        {
            if (lastFrame == Time.frameCount) return;
            lastFrame = Time.frameCount;
            Advance(HauntedClock.Delta);
        }

        /// <summary>Deterministic simulation entry point used by replay and EditMode tests.</summary>
        public void Advance(float seconds)
        {
            if (!opened || seconds <= 0 || float.IsNaN(seconds) || float.IsInfinity(seconds)) return;
            // Limit catch-up after suspend; ordinary 30/60/120 Hz updates retain identical substeps.
            accumulator += Math.Min(seconds, .25f);
            const double step = 1.0 / 120.0;
            while (accumulator + 1e-9 >= step)
            {
                accumulator -= step;
                simulationTime += FixedStep;
                TickDrips();
                TickDroplets();
                TickSplashes();
            }
        }

        void TickDrips()
        {
            Drip shortest = null;
            int active = 0, swelling = 0;
            foreach (var drip in drips)
            {
                if (drip.State == DripState.Paused) continue;
                drip.Age += FixedStep;
                Shape(drip);
                if (drip.Enabled)
                {
                    active++;
                    if (drip.State == DripState.Swell) { swelling++; if (shortest == null || drip.Length < shortest.Length) shortest = drip; }
                }
                if (drip.Age < drip.Duration) continue;
                switch (drip.State)
                {
                    case DripState.Refill: Begin(drip, DripState.Swell); break;
                    case DripState.Swell:
                        if (!closing && simulationTime - lastRelease >= Tuning.ReleaseGap && !AnotherNeck(drip)) Begin(drip, DripState.Neck);
                        break;
                    case DripState.Neck:
                        if (!closing && (drip.Forced || simulationTime - lastRelease + .0001f >= Tuning.ReleaseGap)) Release(drip);
                        break;
                    case DripState.Recoil: Begin(drip, DripState.Refill); break;
                    case DripState.Retract: drip.Length = 0; drip.State = DripState.Paused; break;
                }
            }
            if (active > 1 && swelling == active && shortest != null) Begin(shortest, DripState.Refill);
        }

        bool AnotherNeck(Drip except)
        {
            foreach (var drip in drips) if (drip != except && drip.State == DripState.Neck && !drip.Forced) return true;
            return false;
        }

        void Begin(Drip drip, DripState state)
        {
            drip.State = state; drip.Age = 0; drip.StartLength = drip.Length; drip.StartBulb = drip.Bulb; drip.Forced = false;
            switch (state)
            {
                case DripState.Refill: drip.Duration = Range(drip, Tuning.RefillDuration); break;
                case DripState.Swell: drip.Duration = Range(drip, Tuning.SwellDuration); break;
                case DripState.Neck: drip.Duration = Range(drip, Tuning.NeckDuration); break;
                case DripState.Recoil: drip.Duration = Tuning.RecoilDuration; break;
                case DripState.Retract: drip.Duration = Tuning.RetractDuration; break;
            }
            drip.Duration = Mathf.Max(FixedStep, drip.Duration);
        }

        void Shape(Drip drip)
        {
            float t = Mathf.Clamp01(drip.Age / drip.Duration), ease;
            float maxBulb = Mathf.Min(Tuning.MaximumBulb, drip.Width * Tuning.BulbWidth);
            switch (drip.State)
            {
                case DripState.Refill:
                    ease = 1 - Mathf.Pow(1 - t, 3);
                    drip.Length = Mathf.Lerp(drip.StartLength, drip.Rest, ease);
                    drip.Bulb = drip.Radius = Mathf.Lerp(drip.StartBulb, Tuning.RestBulb, ease);
                    drip.Neck = Tuning.RestNeck;
                    break;
                case DripState.Swell:
                    ease = t * t;
                    drip.Length = Mathf.Lerp(drip.StartLength, Mathf.Max(drip.StartLength, drip.Rest * Tuning.SwellLength), ease);
                    // A drip waiting its turn continues creeping at 30% of its swell rate.
                    drip.Length += Mathf.Min(drip.Rest * .15f, Mathf.Max(0, drip.Age - drip.Duration) * drip.Rest * .3f / drip.Duration);
                    drip.Bulb = drip.Radius = Mathf.Lerp(drip.StartBulb, maxBulb, ease);
                    drip.Neck = Mathf.Lerp(Tuning.RestNeck, Tuning.SwellNeck, ease);
                    break;
                case DripState.Neck:
                    drip.Length = Mathf.Lerp(drip.StartLength, drip.StartLength * Tuning.NeckStretch, t);
                    drip.Neck = Mathf.Lerp(Tuning.SwellNeck, Tuning.ReleaseNeck, t);
                    drip.Bulb = drip.Radius = Mathf.Max(Tuning.RestBulb, drip.StartBulb);
                    break;
                case DripState.Recoil:
                    float target = Tuning.RefillLength * drip.Rest;
                    float omega = Mathf.Max(.01f, Tuning.SpringFrequency), damping = Mathf.Clamp(Tuning.SpringDamping, 0, .99f);
                    float damped = omega * Mathf.Sqrt(1 - damping * damping);
                    float spring = Mathf.Exp(-damping * omega * drip.Age) * (Mathf.Cos(damped * drip.Age) + damping * omega / damped * Mathf.Sin(damped * drip.Age));
                    drip.Length = Mathf.Max(target * .88f, target + (drip.StartLength - target) * spring);
                    drip.Bulb = drip.Radius = Mathf.Lerp(drip.StartBulb, 0, Mathf.Clamp01(t * 3));
                    drip.Neck = Tuning.RestNeck;
                    break;
                case DripState.Retract:
                    drip.Length = drip.StartLength * (1 - t * t);
                    drip.Bulb = drip.Radius = drip.StartBulb * (1 - t * t);
                    break;
            }
        }

        void Release(Drip drip)
        {
            bool forced = drip.Forced;
            var pos = drip.Root + new Vector2(Sway(drip), drip.Length - drip.Radius);
            var velocity = new Vector2(Tuning.Sway * Tuning.SwaySpeed * Mathf.Cos(simulationTime * Tuning.SwaySpeed + drip.Phase), Tuning.InitialSpeed);
            AddDroplet(drip, pos, velocity, Mathf.Max(.5f, drip.Radius), 0);
            if (drip.Random.NextDouble() < Tuning.SatelliteChance)
                AddDroplet(drip, pos - Vector2.up * drip.Radius, velocity, Mathf.Max(.35f, drip.Radius * Tuning.SatelliteRadius), Tuning.SatelliteDelay);
            lastRelease = simulationTime;
            ReleaseCount++;
            // The detached bulb no longer belongs to the stem.
            drip.Length = Mathf.Max(drip.Rest * Tuning.RefillLength, drip.Length - 2 * drip.Radius);
            Begin(drip, DripState.Recoil);
            Released?.Invoke(drip.Id, simulationTime, forced);
        }

        void AddDroplet(Drip source, Vector2 pos, Vector2 velocity, float radius, float delay)
        {
            if (droplets.Count >= MaxDroplets) return;
            droplets.Add(new Droplet { Source = source, Pos = pos, Velocity = velocity, Radius = radius, Delay = delay });
        }

        void TickDroplets()
        {
            for (int i = droplets.Count - 1; i >= 0; i--)
            {
                var drop = droplets[i];
                if (drop.Delay > 0) { drop.Delay -= FixedStep; droplets[i] = drop; continue; }
                if (drop.Squashing)
                {
                    drop.SquashAge += FixedStep;
                    if (drop.SquashAge >= Tuning.SquashDuration) { droplets.RemoveAt(i); continue; }
                }
                else
                {
                    drop.Velocity.y = Mathf.Min(Tuning.TerminalSpeed, drop.Velocity.y + Tuning.Gravity * FixedStep);
                    drop.Pos += drop.Velocity * FixedStep;
                    if (drop.Source.Summon)
                    {
                        if (drop.Pos.y >= drop.Source.LandY) { droplets.RemoveAt(i); continue; }
                    }
                    else if (drop.Pos.y + drop.Radius >= drop.Source.LandY)
                    {
                        drop.Pos.y = drop.Source.LandY;
                        drop.Squashing = true; drop.SquashAge = 0;
                        Impact(drop);
                    }
                }
                droplets[i] = drop;
            }
        }

        void Impact(Droplet drop)
        {
            ImpactCount++;
            if (puddles.Count == MaxPuddles) puddles.RemoveAt(0);
            puddles.Add(new Puddle { Source = drop.Source, Pos = drop.Pos, Radius = drop.Radius });
            int count = drop.Source.Random.Next(3, 6);
            for (int j = 0; j < count && specks.Count < MaxSpecks; j++)
            {
                float angle = Range(drop.Source, 30, 150) * Mathf.Deg2Rad, speed = Range(drop.Source, Tuning.SpeckSpeed);
                specks.Add(new Speck { Source = drop.Source, Pos = drop.Pos, Radius = Range(drop.Source, Tuning.SpeckRadius),
                    Velocity = new Vector2(Mathf.Cos(angle) * speed, -Mathf.Sin(angle) * speed) });
            }
            if (drop.Source.Id != LintelC || closing) return;
            Drip nearest = null;
            foreach (var drip in drips)
            {
                if (!drip.Summon || !drip.Enabled) continue;
                float added = drip.Length * Tuning.FeedLength;
                drip.Length += added; drip.StartLength += added;
                if (nearest == null || Mathf.Abs(drip.Root.x - drop.Pos.x) < Mathf.Abs(nearest.Root.x - drop.Pos.x)) nearest = drip;
            }
            if (nearest != null && !nearest.Forced) Begin(nearest, DripState.Swell);
        }

        void TickSplashes()
        {
            for (int i = puddles.Count - 1; i >= 0; i--)
            {
                var puddle = puddles[i]; puddle.Age += FixedStep;
                if (puddle.Age >= Tuning.PuddleDuration) puddles.RemoveAt(i); else puddles[i] = puddle;
            }
            for (int i = specks.Count - 1; i >= 0; i--)
            {
                var speck = specks[i]; speck.Age += FixedStep;
                speck.Velocity.y += Tuning.Gravity * FixedStep; speck.Pos += speck.Velocity * FixedStep;
                if (speck.Age >= Tuning.SpeckDuration || speck.Pos.y >= speck.Source.LandY) specks.RemoveAt(i); else specks[i] = speck;
            }
        }

        void TranslateParticles(Drip source, Vector2 delta)
        {
            for (int i = 0; i < droplets.Count; i++) { var p = droplets[i]; if (p.Source != source) continue; p.Pos += delta; droplets[i] = p; }
            for (int i = 0; i < puddles.Count; i++) { var p = puddles[i]; if (p.Source != source) continue; p.Pos += delta; puddles[i] = p; }
            for (int i = 0; i < specks.Count; i++) { var p = specks[i]; if (p.Source != source) continue; p.Pos += delta; specks[i] = p; }
        }

        public bool TryGetDrip(string id, out DripSnapshot snapshot)
        {
            var drip = Find(id);
            snapshot = drip == null ? default : new DripSnapshot { State = drip.State, Root = drip.Root, Length = drip.Length,
                Neck = drip.Neck, Bulb = drip.Bulb, Radius = drip.Radius, Age = drip.Age, Duration = drip.Duration, Enabled = drip.Enabled };
            return drip != null;
        }

        public DropletSnapshot GetDroplet(int index)
        {
            var drop = droplets[index];
            return new DropletSnapshot { Position = drop.Pos, Velocity = drop.Velocity, Radius = drop.Radius, Squashing = drop.Squashing };
        }

        Drip Find(string id) { foreach (var drip in drips) if (drip.Id == id) return drip; return null; }
        static int StableHash(string value) { unchecked { int hash = 17; foreach (char c in value) hash = hash * 31 + c; return hash; } }
        static float Range(Drip drip, float min, float max) => Mathf.Lerp(min, max, (float)drip.Random.NextDouble());
        static float Range(Drip drip, Vector2 range) => Range(drip, range.x, range.y);
        float Sway(Drip drip) => Tuning.Sway * Mathf.Sin(simulationTime * Tuning.SwaySpeed + drip.Phase);

        static readonly Color GooTop = new Color(185 / 255f, 1, 233 / 255f, .92f);
        static readonly Color GooBottom = new Color(95 / 255f, 214 / 255f, 182 / 255f, .92f);
        static readonly Color Mint = new Color(126 / 255f, 245 / 255f, 210 / 255f, .28f);
        static readonly Color Highlight = new Color(1, 1, 1, .45f);

        // Fourteen contour rows when quiet. Under overlapping splashes the same contour is sampled more
        // sparsely so even all object caps together stay below 600 submitted (not just unique) vertices.
        int GeometryRows(out int total)
        {
            int active = 0;
            foreach (var drip in drips) if (drip.Length > .05f) active++;
            int particleVertices = droplets.Count * 25 + puddles.Count * 16 + specks.Count * 3;
            int rows = active == 0 ? 14 : Mathf.Clamp((MaxVertices - particleVertices - active * 23) / (active * 6), 4, 14);
            total = particleVertices + active * (rows * 6 + 23);
            return rows;
        }
        public int GeometryVertexCount { get { GeometryRows(out int total); return total; } }

        public void Draw(float scale)
        {
            if (Event.current == null || Event.current.type != EventType.Repaint || !opened || scale <= 0) return;
            Step();
            if (!EnsureMaterials()) return;
            drawOpacity = GUI.color.a;
            int rows = GeometryRows(out _);
            GL.PushMatrix();
            try
            {
                GL.LoadPixelMatrix(0, Screen.width, Screen.height, 0);
                bodyMaterial.SetPass(0);
                GL.Begin(GL.TRIANGLES);
                foreach (var drip in drips) if (drip.Length > .05f) DrawDrip(drip, rows, scale);
                foreach (var drop in droplets) DrawDroplet(drop, scale);
                foreach (var puddle in puddles)
                {
                    float t = Mathf.Clamp01(puddle.Age / Mathf.Max(FixedStep, Tuning.PuddleDuration));
                    float rx = puddle.Radius * Tuning.PuddleWidth * Mathf.Lerp(1, Tuning.PuddleSpread, t);
                    Ellipse(puddle.Pos, rx, rx * Tuning.PuddleHeight, WithAlpha(GooBottom, .8f * (1 - t)), 4, scale);
                }
                foreach (var speck in specks)
                {
                    float r = speck.Radius;
                    Triangle(speck.Pos + new Vector2(-r, r), speck.Pos + new Vector2(0, -r), speck.Pos + new Vector2(r, r), GooTop, GooTop, GooTop, scale);
                }
                GL.End();
                glowMaterial.mainTexture = DreamSkin.Glow;
                glowMaterial.SetPass(0);
                GL.Begin(GL.QUADS);
                foreach (var drip in drips)
                {
                    if (drip.Length <= .05f) continue;
                    Glow(drip.Root + new Vector2(0, drip.Length * .5f), new Vector2(drip.Width * 2.2f, drip.Length * 1.2f), Mint, scale);
                    Glow(drip.Root + new Vector2(Sway(drip), drip.Length - drip.Radius), Vector2.one * 20, WithAlpha(Mint, .35f * drip.Bulb / Mathf.Max(.1f, Tuning.MaximumBulb)), scale);
                }
                foreach (var drop in droplets) if (drop.Delay <= 0) Glow(drop.Pos, Vector2.one * (drop.Radius * 4), WithAlpha(Mint, .28f * DropAlpha(drop)), scale);
                foreach (var puddle in puddles)
                    Glow(puddle.Pos, new Vector2(puddle.Radius * 7, puddle.Radius * 3), WithAlpha(Mint, .5f * Mathf.Clamp01(1 - puddle.Age / Mathf.Max(FixedStep, Tuning.FlashDuration))), scale);
                GL.End();
            }
            finally { GL.PopMatrix(); }
        }

        bool EnsureMaterials()
        {
            if (bodyMaterial == null)
            {
                var shader = Resources.Load<Shader>("Shaders/UiAlpha");
                if (shader == null) shader = Shader.Find("Hidden/Internal-Colored");
                if (shader == null) return false;
                bodyMaterial = new Material(shader) { hideFlags = HideFlags.DontSave };
                bodyMaterial.SetInt("_SrcBlend", (int)BlendMode.SrcAlpha);
                bodyMaterial.SetInt("_DstBlend", (int)BlendMode.OneMinusSrcAlpha);
                bodyMaterial.SetInt("_ZWrite", 0); bodyMaterial.SetInt("_Cull", (int)CullMode.Off);
                bodyMaterial.SetInt("_ZTest", (int)CompareFunction.Always);
            }
            if (glowMaterial == null)
            {
                var shader = Shader.Find("Hidden/BadAppleHotel/UiAdditive");
                if (shader == null) return false;
                glowMaterial = new Material(shader) { hideFlags = HideFlags.DontSave };
            }
            return true;
        }

        float HalfWidth(Drip drip, float f)
        {
            float radius = Mathf.Max(.2f, drip.Radius), y = drip.Length * f;
            float bulbY = drip.Length - radius;
            float stretch = drip.State == DripState.Neck ? Mathf.Lerp(1, 1.25f, Mathf.Clamp01(drip.Age / drip.Duration)) : 1;
            float q = (y - bulbY) / (.9f * radius * stretch);
            return drip.Width * .5f * Mathf.Pow(1 - f, 1.6f) + drip.Neck + drip.Bulb * Mathf.Exp(-q * q);
        }
        Vector2 Contour(Drip drip, float fraction, float side) => drip.Root + new Vector2(Sway(drip) * fraction * fraction + side * HalfWidth(drip, fraction), drip.Length * fraction);

        void DrawDrip(Drip drip, int rows, float scale)
        {
            float visibility = Mathf.Clamp01(drip.Length / 3f);
            Color top = WithAlpha(GooTop, GooTop.a * visibility), bottom = WithAlpha(GooBottom, GooBottom.a * visibility);
            for (int i = 0; i < rows - 1; i++)
            {
                float f0 = Mathf.Pow(i / (float)(rows - 1), .8f), f1 = Mathf.Pow((i + 1f) / (rows - 1), .8f);
                Color c0 = Color.Lerp(top, bottom, f0), c1 = Color.Lerp(top, bottom, f1);
                Quad(Contour(drip, f0, -1), Contour(drip, f0, 1), Contour(drip, f1, 1), Contour(drip, f1, -1), c0, c0, c1, c1, scale);
            }
            float w = drip.Width * .8f;
            Quad(drip.Root + new Vector2(-w, -3), drip.Root + new Vector2(w, -3), drip.Root + new Vector2(w * .7f, 0), drip.Root + new Vector2(-w * .7f, 0),
                WithAlpha(top, 0), WithAlpha(top, 0), top, top, scale);
            var tip = drip.Root + new Vector2(Sway(drip), drip.Length);
            float cap = HalfWidth(drip, 1);
            for (int i = 0; i < 3; i++)
            {
                float a = i * Mathf.PI / 3, b = (i + 1) * Mathf.PI / 3;
                Triangle(tip, tip + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * cap, tip + new Vector2(Mathf.Cos(b), Mathf.Sin(b)) * cap, bottom, bottom, bottom, scale);
            }
            float end = Mathf.Max(3, drip.Length - drip.Radius), x0 = -HalfWidth(drip, 0) * .4f, x1 = Sway(drip) - HalfWidth(drip, Mathf.Clamp01(end / drip.Length)) * .4f;
            Color shine = WithAlpha(Highlight, Highlight.a * visibility);
            Quad(drip.Root + new Vector2(x0, Mathf.Min(3, drip.Length)), drip.Root + new Vector2(x0 + 1, Mathf.Min(3, drip.Length)),
                drip.Root + new Vector2(x1 + 1, end), drip.Root + new Vector2(x1, end), shine, shine, shine, shine, scale);
        }

        float DropAlpha(Droplet drop) => !drop.Source.Summon ? 1 : Mathf.Clamp01((drop.Source.LandY - drop.Pos.y) / Mathf.Max(1, Tuning.FadeDistance));
        void DrawDroplet(Droplet drop, float scale)
        {
            if (drop.Delay > 0) return;
            float alpha = DropAlpha(drop), stretch = 1 + Mathf.Min(.6f, drop.Velocity.magnitude / 900f);
            float rx = drop.Radius / Mathf.Sqrt(stretch), ry = drop.Radius * stretch;
            Vector2 pos = drop.Pos;
            if (drop.Squashing)
            {
                float t = Mathf.Clamp01(drop.SquashAge / Mathf.Max(FixedStep, Tuning.SquashDuration));
                rx = drop.Radius * Mathf.Lerp(1, 1.7f, t); ry = drop.Radius * Mathf.Lerp(1, .35f, t);
                pos.y -= ry;
            }
            Ellipse(pos, rx, ry, WithAlpha(GooBottom, .92f * alpha), 6, scale);
            Vector2 highlight = pos + new Vector2(-rx * .25f, -ry * .4f);
            Color shine = WithAlpha(Highlight, .7f * alpha);
            Triangle(highlight + Vector2.left, highlight + Vector2.up, highlight + Vector2.right, shine, shine, shine, scale);
        }

        static Color WithAlpha(Color color, float alpha) { color.a = alpha; return color; }
        void Ellipse(Vector2 center, float rx, float ry, Color color, int segments, float scale)
        {
            for (int i = 0; i < segments; i++)
            {
                float a = i * Mathf.PI * 2 / segments, b = (i + 1) * Mathf.PI * 2 / segments;
                Triangle(center, center + new Vector2(Mathf.Cos(a) * rx, Mathf.Sin(a) * ry), center + new Vector2(Mathf.Cos(b) * rx, Mathf.Sin(b) * ry), color, color, color, scale);
            }
        }
        void Vertex(Vector2 point, Color color, float scale) { color.a *= drawOpacity; GL.Color(color); GL.Vertex3(point.x * scale, point.y * scale, 0); }
        void Triangle(Vector2 a, Vector2 b, Vector2 c, Color ca, Color cb, Color cc, float scale) { Vertex(a, ca, scale); Vertex(b, cb, scale); Vertex(c, cc, scale); }
        void Quad(Vector2 a, Vector2 b, Vector2 c, Vector2 d, Color ca, Color cb, Color cc, Color cd, float scale)
        { Triangle(a, b, c, ca, cb, cc, scale); Triangle(a, c, d, ca, cc, cd, scale); }
        void Glow(Vector2 center, Vector2 size, Color color, float scale)
        {
            color.a *= drawOpacity;
            GL.Color(color);
            GL.TexCoord2(0, 0); GL.Vertex3((center.x - size.x * .5f) * scale, (center.y - size.y * .5f) * scale, 0);
            GL.TexCoord2(1, 0); GL.Vertex3((center.x + size.x * .5f) * scale, (center.y - size.y * .5f) * scale, 0);
            GL.TexCoord2(1, 1); GL.Vertex3((center.x + size.x * .5f) * scale, (center.y + size.y * .5f) * scale, 0);
            GL.TexCoord2(0, 1); GL.Vertex3((center.x - size.x * .5f) * scale, (center.y + size.y * .5f) * scale, 0);
        }

        public void Dispose()
        {
            DestroyMaterial(bodyMaterial); DestroyMaterial(glowMaterial);
            bodyMaterial = null; glowMaterial = null; Clear();
        }
        static void DestroyMaterial(Material material)
        {
            if (material == null) return;
            if (Application.isPlaying) UnityEngine.Object.Destroy(material); else UnityEngine.Object.DestroyImmediate(material);
        }
    }
}
