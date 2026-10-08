using System.Collections.Generic;
using UnityEngine;

namespace BadAppleHotel.Game
{
    /// <summary>
    /// Presentation for the dreamed creatures that act as towers, in the language of the claimed-room
    /// floor energy. A sleeper's dream travels from their bed and the creature materializes from the
    /// floor up over a summoning sigil; it idles with rising motes and a dream thread back to the bed;
    /// every attack has its own elemental bolt, impact and light; upgrades evolve it in a burst; selling
    /// it lets it fade back into the dream. Purely visual: gameplay never reads any of this, and the
    /// simulation skips it. Placement, upgrades and removals are detected from room slots, so the
    /// gameplay code only reports attacks.
    /// </summary>
    public partial class GameManager
    {
        sealed class TowerFxState
        {
            public TowerInstance Tower;
            public Room Room;
            public Vector2Int Tile;
            public int Level, Frame;
            public float Seed, Arrive, FlashAt = -99f, FlashLength = .12f, PulseAt = -99f, FireAt = -99f, NextIncome;
            public int Dir = 6;
            public TowerSpriteSet Art;
            public Color Accent;
            public bool Visible;
        }

        enum ShotStyle { Bolt, Beam, Missile, Lightning, Fireball, Hex, Wisp, Income }

        sealed class TowerShot
        {
            public ShotStyle Style;
            public Vector3 From, To;
            public Monster Target;
            public Resident Receiver;
            public float Born, Duration, Arc, Scale, NextTrail, NextJitter;
            public bool Launched;
            public Color Color;
            public TowerFxState Source;
            public readonly List<Vector3> Path = new List<Vector3>(10);
            public readonly List<Vector3> Branch = new List<Vector3>(5);
        }

        static readonly Color DreamMint = new Color(.30f, 1f, .80f), DreamViolet = new Color(.62f, .38f, 1f);
        const float MuzzleHeight = .95f, TargetHeight = .85f, TowerArrive = .55f, MaterializeSeconds = .75f;
        // Attacks are drawn at gameplay zoom on a phone, so they are sized to read from there.
        const float AttackSize = 2f;
        static readonly Vector2 SigilBack = ((Vector2)HotelView3D.Up).normalized * .15f;

        DreamFx dreamFx;
        readonly Dictionary<TowerInstance, TowerFxState> towerFx = new Dictionary<TowerInstance, TowerFxState>();
        readonly List<TowerFxState> towerFxGone = new List<TowerFxState>();
        readonly List<TowerShot> towerShots = new List<TowerShot>();
        readonly List<Vector3> fxPoints = new List<Vector3>(16);
        MaterialPropertyBlock towerBlock;
        int towerFxFrame;
        float fxNow;

        /// <summary>Live dream effect quads and particles, for tests and tools.</summary>
        public DreamFx Effects => dreamFx;
        public int TowerShotCount => towerShots.Count;

        public static Color TowerAccent(Config.TowerDef def)
        {
            switch (def.damageType)
            {
                case "fire": return new Color(1f, .52f, .16f);
                case "electric": return new Color(.42f, .92f, 1f);
                case "slow": return new Color(.66f, .40f, 1f);
                case "bullet": return new Color(.95f, .88f, .70f);
            }
            if (def.effect == "faith" || def.faithPerSecond > 0) return new Color(1f, .22f, .30f);
            if (def.dreamPerSecond > 0) return new Color(1f, .76f, .30f);
            return new Color(.72f, .52f, 1f);
        }

        static ShotStyle StyleFor(Config.TowerDef def)
        {
            switch (def.id)
            {
                case "sniper_nest": return ShotStyle.Beam;
                case "missile_launcher": return ShotStyle.Missile;
            }
            switch (def.damageType)
            {
                case "electric": return ShotStyle.Lightning;
                case "fire": return ShotStyle.Fireball;
                case "slow": return ShotStyle.Hex;
                default: return ShotStyle.Bolt;
            }
        }

        static Vector3 Lifted(Vector2 ground, float height) => new Vector3(ground.x, ground.y, -height);

        void ClearTowerFx()
        {
            towerFx.Clear(); towerShots.Clear(); towerFxGone.Clear();
            dreamFx?.Dispose(); dreamFx = null;
        }

        void UpdateTowerFx() => UpdateTowerFx(Time.time, Time.deltaTime);

        /// <summary>One presentation frame on an explicit clock (captures drive it frame by frame).</summary>
        void UpdateTowerFx(float now, float dt)
        {
            if (Simulation || matchRoot == null) return;
            if (!InMatch)
            {
                // Results: let what is in flight finish, but start nothing new.
                if (dreamFx != null) { dreamFx.Step(dt); dreamFx.Flush(now); }
                return;
            }
            if (dreamFx == null) dreamFx = new DreamFx(matchRoot);
            if (towerBlock == null) towerBlock = new MaterialPropertyBlock();
            fxNow = now;
            towerFxFrame++;

            foreach (var room in RoomsByDef.Values)
                foreach (var t in room.Slots)
                {
                    if (t == null) continue;
                    if (!towerFx.TryGetValue(t, out var s))
                    {
                        s = new TowerFxState { Tower = t, Room = room, Tile = t.Tile, Level = t.Level, Seed = DreamFx.Range(0f, 50f),
                            Accent = TowerAccent(t.Def), NextIncome = now + DreamFx.Range(.5f, 2f) };
                        towerFx[t] = s;
                        Summon(s, now);
                    }
                    else if (s.Level != t.Level) Evolve(s, now);
                    s.Frame = towerFxFrame;
                    DrawTower(s, now);
                }

            towerFxGone.Clear();
            foreach (var s in towerFx.Values) if (s.Frame != towerFxFrame) towerFxGone.Add(s);
            foreach (var s in towerFxGone) { towerFx.Remove(s.Tower); Banish(s, now); }

            DrawSleepers(now);
            for (int i = towerShots.Count - 1; i >= 0; i--)
                if (!AdvanceShot(towerShots[i], now)) towerShots.RemoveAt(i);
            dreamFx.Step(dt);
            dreamFx.Flush(now);
        }

        // ------------------------------------------------------------------ lifecycle

        void Summon(TowerFxState s, float now)
        {
            ApplyArt(s);
            var center = HotelMap.Center(s.Tile);
            var owner = s.Room.Owner;
            bool fromBed = owner != null && owner.Alive;
            s.Arrive = now + (fromBed ? TowerArrive : .05f);
            SetMaterialize(s, 0);
            if (!fromBed) return;
            towerShots.Add(new TowerShot
            {
                Style = ShotStyle.Wisp, From = Lifted(s.Room.Def.BedCenter, .45f), To = Lifted(center, .35f), Born = now,
                Duration = TowerArrive, Arc = .9f, Color = DreamMint, Source = s, Scale = 1f,
            });
        }

        /// <summary>The wisp lands: a shockwave, a pillar of light and a spray of dream sparks.</summary>
        void SummonBurst(TowerFxState s, float scale)
        {
            if (!s.Visible && !IsTileVisible(s.Tile)) return;
            var center = HotelMap.Center(s.Tile);
            var fx = dreamFx;
            fx.Emit(new DreamFx.Particle { Shape = DreamFx.Shape.Ring, Pos = Lifted(center, 0), Size = .95f * scale, Life = .75f, Color = s.Accent, Ground = true });
            fx.Emit(new DreamFx.Particle { Shape = DreamFx.Shape.Ring, Pos = Lifted(center, 0), Size = .7f * scale, Life = .55f, Color = DreamMint, Ground = true, Age = -.12f });
            fx.Emit(new DreamFx.Particle { Shape = DreamFx.Shape.Pillar, Pos = Lifted(center, 0), Size = .32f * scale, Height = 2.1f * scale, Life = .9f, Color = Color.Lerp(DreamMint, s.Accent, .5f) });
            fx.Burst(Lifted(center, .25f), Mathf.RoundToInt(14 * scale), s.Accent, 3.2f * scale, .05f, .7f, 1.4f, 1.5f);
            fx.Burst(Lifted(center, .1f), Mathf.RoundToInt(8 * scale), DreamMint, 2.2f, .04f, .9f, 2f, -.6f);
            fx.FlashLight(center, 2.4f * scale, Color.Lerp(DreamMint, s.Accent, .5f) * 1.3f, .6f);
        }

        void Evolve(TowerFxState s, float now)
        {
            s.Level = s.Tower.Level;
            s.Arrive = now + .18f;
            ApplyArt(s);
            SetMaterialize(s, 0);
            var center = HotelMap.Center(s.Tile);
            if (!IsTileVisible(s.Tile)) return;
            SummonBurst(s, 1.35f);
            // A spiral of sparks climbs around the new form.
            for (int i = 0; i < 18; i++)
            {
                float a = i * .7f + s.Seed;
                var offset = new Vector3(Mathf.Cos(a), Mathf.Sin(a), 0) * .38f;
                dreamFx.Emit(new DreamFx.Particle { Shape = DreamFx.Shape.Glow, Pos = Lifted(center, .05f) + offset,
                    Vel = new Vector3(-offset.y, offset.x, 0) * 4f + Vector3.back * (1.2f + i * .08f), Size = .05f, Life = .9f,
                    Color = i % 2 == 0 ? s.Accent : Color.white * .8f, Drag = 1.2f, Age = -i * .025f, Stretch = .03f });
            }
            s.FlashAt = now; s.FlashLength = .25f;
        }

        /// <summary>A removed creature (sold, or a disguise unmasked) dissolves back into the dream.</summary>
        void Banish(TowerFxState s, float now)
        {
            if (!IsTileVisible(s.Tile)) return;
            var center = HotelMap.Center(s.Tile);
            dreamFx.Emit(new DreamFx.Particle { Shape = DreamFx.Shape.Ring, Pos = Lifted(center, 0), Size = .7f, Life = .5f, Color = DreamViolet, Ground = true });
            for (int i = 0; i < 16; i++)
            {
                var p = Lifted(center, DreamFx.Range(.1f, 1.4f)) + (Vector3)(DreamFx.InCircle() * .3f);
                dreamFx.Emit(DreamFx.Shape.Glow, p, Vector3.back * DreamFx.Range(.4f, 1.2f), .045f, DreamFx.Range(.6f, 1.1f),
                    i % 3 == 0 ? s.Accent : DreamViolet, drag: .5f);
            }
            dreamFx.FlashLight(center, 1.6f, DreamViolet * .9f, .45f);
            var owner = s.Room.Owner;
            if (owner != null && owner.Alive)
                towerShots.Add(new TowerShot { Style = ShotStyle.Wisp, From = Lifted(center, .6f), To = Lifted(s.Room.Def.BedCenter, .4f),
                    Born = now, Duration = .6f, Arc = .7f, Color = DreamViolet, Scale = .7f });
        }

        /// <summary>Animated art for this form, when it exists, replaces the static tier sprite.</summary>
        void ApplyArt(TowerFxState s)
        {
            s.Art = TowerSpriteSet.Load(s.Tower.Def.id, s.Tower.Level);
            s.FireAt = -99f;
            var sr = s.Tower.Sr;
            if (s.Art == null || sr == null) return;
            sr.sprite = s.Art.Idle(s.Dir, s.Seed);
            HotelView3D.Billboard(sr, HotelMap.Center(s.Tile));
        }

        void SetMaterialize(TowerFxState s, float amount)
        {
            var sr = s.Tower.Sr;
            if (sr == null) return;
            sr.GetPropertyBlock(towerBlock);
            towerBlock.SetFloat("_Materialize", amount);
            sr.SetPropertyBlock(towerBlock);
        }

        // ------------------------------------------------------------------ idle presence

        float RoomDreamStrength(Room room, TowerInstance t)
        {
            var owner = room.Owner;
            if (owner == null || !owner.Alive) return .25f;
            if (t != null && Now < t.DisabledUntil) return .15f + .15f * Mathf.PerlinNoise(fxNow * 9f, t.SlotIndex);
            return owner.Asleep ? 1f : .7f;
        }

        void DrawTower(TowerFxState s, float now)
        {
            var t = s.Tower;
            var sr = t.Sr;
            s.Visible = sr != null && sr.enabled;
            if (sr == null) return;
            float materialize = Mathf.Clamp01((now - s.Arrive) / MaterializeSeconds);
            float strength = RoomDreamStrength(s.Room, t);
            float flash = now >= s.FlashAt ? Mathf.Clamp01(1 - (now - s.FlashAt) / s.FlashLength) : 0f;
            if (s.Art != null)
            {
                // Characters turn to face the monster while it is in reach, then hold that facing.
                var m = Monster;
                if (s.Art.Directional && t.IsWeapon && m != null && !m.Dead && Phase == Phase.Night &&
                    Rules.UpgradeRules.InRange(Cfg.towers, t.Def, t.Level, Vector2.Distance(HotelMap.Center(s.Tile), m.Pos)))
                    s.Dir = CharacterSet.DirIndex(HotelView3D.Facing(m.Pos - HotelMap.Center(s.Tile)));
                var frame = s.Art.Fire(s.Dir, now - s.FireAt) ?? s.Art.Idle(s.Dir, now + s.Seed);
                if (sr.sprite != frame) sr.sprite = frame;
            }

            sr.GetPropertyBlock(towerBlock);
            towerBlock.SetFloat("_Materialize", materialize);
            towerBlock.SetFloat("_DreamGlow", (.3f + .12f * Mathf.Sin(now * 1.7f + s.Seed)) * strength);
            towerBlock.SetColor("_DreamColor", Color.Lerp(DreamMint, s.Accent, .45f));
            towerBlock.SetFloat("_Flash", flash * .18f);
            towerBlock.SetColor("_FlashColor", s.Accent);
            // A slow breath (deeper while the sleeper dreams) and a squash when the creature attacks.
            towerBlock.SetVector("_Breathe", new Vector4(.012f + .008f * strength, s.Seed, 1.4f + .1f * Mathf.Repeat(s.Seed, 3f), flash));
            sr.SetPropertyBlock(towerBlock);
            if (!s.Visible) return;

            var center = HotelMap.Center(s.Tile);
            var fx = dreamFx;
            float sigil = Mathf.Clamp01((now - s.Arrive + .15f) / .45f);
            fx.Decal(center + SigilBack, .44f, DreamFx.Shape.Sigil, s.Accent * new Color(1, 1, 1, .9f * strength), sigil, s.Seed, t.Level);

            // Motes climb from the sigil; more of them as the creature evolves.
            int motes = 2 + t.Level;
            for (int k = 0; k < motes; k++)
            {
                float phase = Mathf.Repeat(now * (.24f + .05f * k) + s.Seed * .37f + k / (float)motes, 1f);
                float a = s.Seed * 7f + k * 2.4f, r = .16f + .14f * Mathf.Repeat(s.Seed * (k + 1) * .618f, 1f);
                var p = Lifted(center + SigilBack + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * r * (1 - phase * .4f), .04f + phase * 1.35f);
                float glow = Mathf.Sin(phase * Mathf.PI) * strength * sigil;
                fx.Billboard(p, .045f + .008f * t.Level, .045f + .008f * t.Level, DreamFx.Shape.Glow,
                    (k % 2 == 0 ? s.Accent : DreamMint) * new Color(1, 1, 1, glow), 0, s.Seed + k);
            }

            // The dream thread from the sleeper's bed.
            var owner = s.Room.Owner;
            if (owner != null && owner.Alive && materialize > 0)
            {
                ThreadPoints(s.Room.Def.BedCenter, center);
                float pulse = now - s.PulseAt < .4f ? (now - s.PulseAt) / .4f : -1f;
                fx.Path(fxPoints, .045f, DreamFx.Shape.Thread, Color.Lerp(s.Accent, DreamMint, .45f) * new Color(1, 1, 1, (owner.Asleep ? .3f : .13f) * materialize), pulse, s.Seed);
            }

            // Dream Power and Faith creatures send their harvest to the owner as a drifting mote.
            if ((t.IsDreamGen || t.IsFaith) && owner != null && owner.Alive && now >= s.NextIncome && Phase != Phase.Setup)
            {
                s.NextIncome = now + DreamFx.Range(1.6f, 2.6f);
                towerShots.Add(new TowerShot { Style = ShotStyle.Income, From = Lifted(center, 1f), Receiver = owner,
                    Born = now, Duration = 1.1f, Arc = .9f, Color = s.Accent, Scale = 1f });
            }
        }

        void ThreadPoints(Vector2 from, Vector2 to)
        {
            fxPoints.Clear();
            const int segments = 8;
            float span = Vector2.Distance(from, to);
            for (int i = 0; i <= segments; i++)
            {
                float u = i / (float)segments;
                fxPoints.Add(Lifted(Vector2.Lerp(from, to, u), Mathf.Lerp(.35f, .12f, u) + Mathf.Sin(u * Mathf.PI) * Mathf.Min(.7f, .12f * span)));
            }
        }

        /// <summary>A sleeping guest's bed breathes a slow column of dream motes.</summary>
        void DrawSleepers(float now)
        {
            foreach (var room in RoomsByDef.Values)
            {
                var owner = room.Owner;
                if (owner == null || !owner.Alive || !owner.Asleep) continue;
                var bed = room.Def.BedCenter;
                if (!IsVisible(bed)) continue;
                for (int k = 0; k < 4; k++)
                {
                    float phase = Mathf.Repeat(now * .18f + k * .25f + room.Def.Index * .13f, 1f);
                    var p = Lifted(bed + new Vector2(Mathf.Sin(now * .7f + k * 2.1f), Mathf.Cos(now * .6f + k)) * .25f, .3f + phase * 1.6f);
                    float glow = Mathf.Sin(phase * Mathf.PI) * .8f;
                    dreamFx.Billboard(p, .06f, .06f, DreamFx.Shape.Glow, (k % 2 == 0 ? DreamMint : DreamViolet) * new Color(1, 1, 1, glow), 0, k);
                }
            }
        }

        // ------------------------------------------------------------------ attacks

        /// <summary>Called when a tower fires at the monster. Returns false when no dream creature stands there.</summary>
        bool TowerAttackFx(Vector2 from, string damageType)
        {
            if (Simulation || dreamFx == null || Monster == null) return false;
            var tile = HotelMap.ToTile(from);
            TowerFxState s = null;
            foreach (var state in towerFx.Values) if (state.Tile == tile) { s = state; break; }
            if (s == null) return false;
            float now = fxNow;
            var m = Monster;
            var style = StyleFor(s.Tower.Def);
            float scale = (1f + .14f * (s.Tower.Level - 1)) * AttackSize;
            var muzzle = Lifted(from, MuzzleHeight * Mathf.Lerp(.85f, 1.1f, (s.Tower.Level - 1) / 3f));
            var aim = Lifted(m.Pos, TargetHeight) - muzzle; aim.z = 0;
            if (aim.sqrMagnitude > .0001f) muzzle += aim.normalized * .22f;
            // With an attack clip, the shot leaves at the clip's release frame instead of instantly.
            float delay = 0;
            if (s.Art != null && s.Art.HasFire)
            {
                if (s.Art.Directional) s.Dir = CharacterSet.DirIndex(HotelView3D.Facing(m.Pos - from));
                s.FireAt = now;
                delay = s.Art.Release;
            }
            bool seen = s.Visible || IsVisible(m.Pos);
            if (!seen) { s.FlashAt = s.PulseAt = now + delay; s.FlashLength = .12f; return true; }

            float dist = Vector2.Distance(from, m.Pos);
            var shot = new TowerShot { Style = style, From = muzzle, Target = m, To = Lifted(m.Pos, TargetHeight), Born = now + delay, Color = s.Accent, Source = s, Scale = scale };
            switch (style)
            {
                case ShotStyle.Beam: shot.Duration = .18f; break;
                case ShotStyle.Lightning: shot.Duration = .2f; break;
                case ShotStyle.Missile: shot.Duration = Mathf.Clamp(dist / 7f, .25f, .6f); shot.Arc = Mathf.Min(1.2f, dist * .18f); break;
                case ShotStyle.Fireball: shot.Duration = Mathf.Clamp(dist / 9f, .12f, .45f); shot.Arc = dist * .05f; break;
                case ShotStyle.Hex: shot.Duration = Mathf.Clamp(dist / 6f, .15f, .5f); shot.Arc = dist * .08f; break;
                default: shot.Duration = Mathf.Clamp(dist / 16f, .05f, .3f); break;
            }
            towerShots.Add(shot);
            if (delay <= 0) Launch(shot, now);
            return true;
        }

        /// <summary>The moment a shot leaves: a bloom at the creature, a light on the floor around it,
        /// the creature's own flash and squash, and a pulse along its dream thread.</summary>
        void Launch(TowerShot shot, float now)
        {
            shot.Launched = true;
            var s = shot.Source;
            if (s != null) { s.FlashAt = s.PulseAt = now; s.FlashLength = .12f; }
            var color = s != null ? s.Accent : shot.Color;
            dreamFx.Emit(DreamFx.Shape.Glow, shot.From, Vector3.zero, .2f * shot.Scale, .14f, Color.Lerp(color, Color.white, .25f));
            dreamFx.FlashLight(shot.From, .8f * shot.Scale, color * .9f, .16f);
            if (shot.Style == ShotStyle.Beam || shot.Style == ShotStyle.Lightning) Impact(shot, now);
        }

        Vector3 ShotPosition(TowerShot shot, float k)
        {
            var p = Vector3.Lerp(shot.From, shot.To, k);
            p.z -= Mathf.Sin(k * Mathf.PI) * shot.Arc;
            return p;
        }

        /// <summary>Moves one shot along; false once it has finished.</summary>
        bool AdvanceShot(TowerShot shot, float now)
        {
            float k = (now - shot.Born) / Mathf.Max(.01f, shot.Duration);
            if (shot.Target != null && !shot.Target.Dead) shot.To = Lifted(shot.Target.Pos, TargetHeight);
            if (k < 0) return true;
            if (!shot.Launched && shot.Target != null) Launch(shot, now);
            if (shot.Receiver != null && shot.Receiver.Alive) shot.To = Lifted(shot.Receiver.Pos, 1.1f);
            var fx = dreamFx;
            if (k >= 1f)
            {
                if (shot.Style == ShotStyle.Wisp && shot.Source != null) SummonBurst(shot.Source, 1f);
                else if (shot.Style != ShotStyle.Beam && shot.Style != ShotStyle.Lightning && shot.Style != ShotStyle.Income) Impact(shot, now);
                else if (shot.Style == ShotStyle.Income)
                    fx.Burst(shot.To, 5, shot.Color, 1.2f, .035f, .4f, 1f);
                return false;
            }
            float s = shot.Scale;
            var head = ShotPosition(shot, k);
            var tail = ShotPosition(shot, Mathf.Max(0, k - .22f));
            switch (shot.Style)
            {
                case ShotStyle.Bolt:
                    fx.Band(tail, head, .06f * s, DreamFx.Shape.Streak, Color.Lerp(shot.Color, DreamMint, .25f) * 1.6f);
                    fx.Billboard(head, .12f * s, .12f * s, DreamFx.Shape.Glow, shot.Color * 1.4f);
                    break;
                case ShotStyle.Beam:
                    fx.Band(shot.From, shot.To, .09f * s, DreamFx.Shape.Bolt, Color.Lerp(shot.Color, DreamMint, .3f) * 1.6f, k, shot.Born);
                    fx.Band(shot.From, shot.To, .02f * s, DreamFx.Shape.Bolt, Color.white, k, shot.Born + 1);
                    break;
                case ShotStyle.Lightning:
                    if (now >= shot.NextJitter) { shot.NextJitter = now + .045f; Jag(shot); }
                    fx.Path(shot.Path, .11f * s, DreamFx.Shape.Bolt, shot.Color * 1.8f, k, shot.Born);
                    fx.Path(shot.Branch, .06f * s, DreamFx.Shape.Bolt, Color.Lerp(shot.Color, DreamMint, .5f) * 1.4f, k, shot.Born + 2);
                    fx.Light(shot.To, 1.3f * s, shot.Color * (1 - k));
                    break;
                case ShotStyle.Missile:
                    fx.Billboard(head, .16f * s, .16f * s, DreamFx.Shape.Glow, Color.Lerp(shot.Color, DreamViolet, .3f) * 1.4f);
                    fx.Billboard(head, .24f * s, .24f * s, DreamFx.Shape.Flame, new Color(1f, .5f, .2f) * 1.3f, .5f, shot.Born);
                    Trail(shot, head, now, .012f, DreamViolet, .07f, .55f);
                    fx.Light(head, .8f * s, new Color(1f, .5f, .25f) * .6f);
                    break;
                case ShotStyle.Fireball:
                    fx.Billboard(head, .3f * s, .3f * s, DreamFx.Shape.Flame, shot.Color * 1.3f, .5f, shot.Born);
                    fx.Billboard(head, .15f * s, .15f * s, DreamFx.Shape.Glow, new Color(1f, .8f, .4f));
                    Trail(shot, head, now, .012f, shot.Color, .06f, .45f);
                    fx.Light(head, .8f * s, shot.Color * .7f);
                    break;
                case ShotStyle.Hex:
                    fx.Band(tail, head, .09f * s, DreamFx.Shape.Streak, Color.Lerp(shot.Color, new Color(.55f, .85f, .35f), .35f) * 1.5f);
                    fx.Billboard(head, .2f * s, .2f * s, DreamFx.Shape.Glow, shot.Color * 1.5f);
                    Trail(shot, head, now, .02f, new Color(.55f, .85f, .35f), .05f, .5f);
                    break;
                case ShotStyle.Wisp:
                    fx.Billboard(head, .24f * s, .24f * s, DreamFx.Shape.Glow, shot.Color);
                    fx.Band(tail, head, .1f * s, DreamFx.Shape.Streak, Color.Lerp(shot.Color, DreamViolet, .4f));
                    Trail(shot, head, now, .02f, DreamViolet, .06f, .6f);
                    break;
                case ShotStyle.Income:
                    fx.Billboard(head, .11f, .11f, DreamFx.Shape.Glow, shot.Color * new Color(1, 1, 1, Mathf.Sin(k * Mathf.PI) + .3f));
                    break;
            }
            return true;
        }

        void Trail(TowerShot shot, Vector3 at, float now, float every, Color color, float size, float life)
        {
            if (now < shot.NextTrail) return;
            shot.NextTrail = now + every;
            dreamFx.Emit(DreamFx.Shape.Glow, at + DreamFx.InSphere() * .04f, Vector3.back * .3f + DreamFx.InSphere() * .2f,
                size * shot.Scale, life, color, grow: .05f, drag: 1f);
        }

        /// <summary>A fresh jagged lightning path (and one fork) between the creature and its target.</summary>
        void Jag(TowerShot shot)
        {
            shot.Path.Clear(); shot.Branch.Clear();
            const int n = 9;
            var along = shot.To - shot.From;
            var side = Vector3.Cross(along, HotelView3D.Forward).normalized;
            for (int i = 0; i <= n; i++)
            {
                float u = i / (float)n, swing = Mathf.Sin(u * Mathf.PI);
                shot.Path.Add(shot.From + along * u + (side * DreamFx.Range(-.24f, .24f) + DreamFx.InSphere() * .08f) * swing);
            }
            int fork = Mathf.FloorToInt(DreamFx.Range(3, 7));
            var start = shot.Path[fork];
            var dir = (along.normalized + side * DreamFx.Range(-1f, 1f) + Vector3.forward * .3f).normalized;
            for (int i = 0; i < 4; i++) shot.Branch.Add(start + dir * (i * .22f) + DreamFx.InSphere() * .06f * i);
        }

        void Impact(TowerShot shot, float now)
        {
            if (shot.Target != null && !shot.Target.Dead) shot.Target.Anim?.Flash(Color.Lerp(shot.Color, Color.white, .3f), .55f);
            var at = shot.To;
            Vector2 ground = at;
            float s = shot.Scale;
            var fx = dreamFx;
            switch (shot.Style)
            {
                case ShotStyle.Bolt:
                    fx.Burst(at, 8, Color.Lerp(shot.Color, DreamMint, .3f), 1.6f * s, .028f * s, .32f, .6f, 2f);
                    fx.Emit(DreamFx.Shape.Glow, at, Vector3.zero, .26f * s, .16f, shot.Color);
                    fx.Emit(new DreamFx.Particle { Shape = DreamFx.Shape.Ring, Pos = ground, Size = .45f * s, Life = .3f, Color = shot.Color, Ground = true });
                    fx.FlashLight(ground, 1.1f * s, shot.Color * .8f, .18f);
                    break;
                case ShotStyle.Beam:
                    fx.Burst(at, 14, shot.Color, 2f * s, .03f * s, .38f, .8f, 2.5f);
                    fx.Emit(DreamFx.Shape.Glow, at, Vector3.zero, .38f * s, .2f, Color.Lerp(shot.Color, Color.white, .4f));
                    fx.Emit(new DreamFx.Particle { Shape = DreamFx.Shape.Ring, Pos = ground, Size = .75f * s, Life = .4f, Color = shot.Color, Ground = true });
                    fx.FlashLight(ground, 1.4f * s, shot.Color, .25f);
                    break;
                case ShotStyle.Lightning:
                    fx.Burst(at, 12, Color.Lerp(shot.Color, Color.white, .4f), 2.2f * s, .024f * s, .28f, .7f, 1f);
                    fx.Emit(new DreamFx.Particle { Shape = DreamFx.Shape.Ring, Pos = ground, Size = .6f * s, Life = .3f, Color = shot.Color, Ground = true });
                    fx.FlashLight(ground, 1.5f * s, shot.Color * 1.1f, .3f);
                    break;
                case ShotStyle.Missile:
                    for (int i = 0; i < 4; i++)
                        fx.Emit(new DreamFx.Particle { Shape = DreamFx.Shape.Flame, Pos = at + (Vector3)(DreamFx.InCircle() * .2f) + Vector3.forward * .3f,
                            Vel = Vector3.back * .6f, Size = .32f * s * DreamFx.Range(.8f, 1.2f), Life = .55f, Color = new Color(1f, .45f, .18f), Age = -i * .04f });
                    fx.Burst(at, 20, new Color(1f, .6f, .25f), 2f * s, .03f * s, .6f, 1f, 3f);
                    fx.Emit(new DreamFx.Particle { Shape = DreamFx.Shape.Ring, Pos = ground, Size = 1.05f * s, Life = .55f, Color = new Color(1f, .5f, .3f), Ground = true });
                    fx.FlashLight(ground, 1.8f * s, new Color(1f, .55f, .25f) * 1.4f, .4f);
                    break;
                case ShotStyle.Fireball:
                    for (int i = 0; i < 3; i++)
                        fx.Emit(new DreamFx.Particle { Shape = DreamFx.Shape.Flame, Pos = at + (Vector3)(DreamFx.InCircle() * .18f) + Vector3.forward * .35f,
                            Vel = Vector3.back * .7f, Size = .28f * s * DreamFx.Range(.8f, 1.15f), Life = .5f, Color = shot.Color, Age = -i * .05f });
                    fx.Burst(at, 16, new Color(1f, .7f, .3f), 1.4f * s, .028f * s, .7f, 1.4f, -.8f);
                    fx.Emit(new DreamFx.Particle { Shape = DreamFx.Shape.Ring, Pos = ground, Size = .7f * s, Life = .45f, Color = shot.Color, Ground = true });
                    fx.FlashLight(ground, 1.6f * s, shot.Color * 1.3f, .4f);
                    break;
                case ShotStyle.Hex:
                    fx.Emit(new DreamFx.Particle { Shape = DreamFx.Shape.Hex, Pos = ground, Size = .62f * s, Life = 1f, Color = shot.Color, Ground = true, Seed = now });
                    fx.Burst(at, 12, new Color(.55f, .85f, .35f), .8f * s, .03f * s, .8f, .4f, -.3f);
                    fx.FlashLight(ground, 1.2f * s, shot.Color * .9f, .5f);
                    break;
            }
        }
    }
}
