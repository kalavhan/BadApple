using System;
using System.Collections.Generic;
using UnityEngine;
using Object = UnityEngine.Object;

namespace BadAppleHotel.Game
{
    /// <summary>One static, occluded lamp field for the hotel. No Light components or per-frame light lists.</summary>
    public sealed class HotelLighting : IDisposable
    {
        public readonly struct Lamp
        {
            public readonly Vector2 Position, Normal;
            public readonly float Radius, Strength;
            /// <param name="position">XY position on the decorated wall face.</param>
            /// <param name="normal">Outward direction from that wall into its room or corridor.</param>
            public Lamp(Vector2 position, Vector2 normal, float radius = 4f, float strength = 1f)
            {
                Position = position; Normal = normal.normalized;
                Radius = Mathf.Clamp(radius, .25f, 12f); Strength = Mathf.Clamp01(strength);
            }
        }

        public const int SamplesPerTile = 4;
        public static readonly Color WarmColor = new Color(.82f, .43f, .15f, 1);
        static readonly int TextureId = Shader.PropertyToID("_HotelLightMap");
        static readonly int SizeId = Shader.PropertyToID("_HotelLightSize");
        static readonly int ColorId = Shader.PropertyToID("_HotelLampColor");
        static readonly int EnabledId = Shader.PropertyToID("_HotelLightingEnabled");
        public Texture2D LightMap { get; private set; }
        public Vector4 MapSize { get; private set; }
        public int LampCount { get; private set; }

        static HotelLighting boundLighting;
        HotelLighting() { }

        public static HotelLighting Build(HotelMap map, IReadOnlyList<Lamp> lamps)
        {
            if (map == null) throw new ArgumentNullException(nameof(map));
            int width = map.W * SamplesPerTile, height = map.H * SamplesPerTile;
            var intensity = new float[width * height];
            bool Opaque(int x, int y)
            {
                var tile = map.Get(x, y);
                // Doorways remain conservatively closed for this static bake. A lamp in each
                // occupied area illuminates that area without leaking through a closed door.
                return tile != Tile.Corridor && tile != Tile.RoomFloor;
            }
            int count = 0;
            if (lamps != null) foreach (var lamp in lamps)
            {
                if (lamp.Strength <= 0 || lamp.Radius <= 0 || lamp.Normal.sqrMagnitude < .5f) continue;
                // The detailed face may sit slightly inside the .3-wide wall footprint. Start
                // the ray just outside the slab, never inside the opaque source cell.
                Vector2 origin = LampOrigin(lamp, Opaque);
                if (Opaque(Mathf.FloorToInt(origin.x), Mathf.FloorToInt(origin.y))) continue;
                count++;
                int x0 = Mathf.Max(0, Mathf.FloorToInt((lamp.Position.x - lamp.Radius) * SamplesPerTile));
                int x1 = Mathf.Min(width - 1, Mathf.CeilToInt((lamp.Position.x + lamp.Radius) * SamplesPerTile));
                int y0 = Mathf.Max(0, Mathf.FloorToInt((lamp.Position.y - lamp.Radius) * SamplesPerTile));
                int y1 = Mathf.Min(height - 1, Mathf.CeilToInt((lamp.Position.y + lamp.Radius) * SamplesPerTile));
                for (int y = y0; y <= y1; y++) for (int x = x0; x <= x1; x++)
                {
                    if (Opaque(x / SamplesPerTile, y / SamplesPerTile)) continue;
                    var point = new Vector2((x + .5f) / SamplesPerTile, (y + .5f) / SamplesPerTile);
                    float value = Contribution(lamp, origin, point, Opaque);
                    int index = y * width + x;
                    intensity[index] = Mathf.Min(1, intensity[index] + value);
                }
            }
            var pixels = new Color32[intensity.Length];
            for (int i = 0; i < pixels.Length; i++)
            {
                byte value = (byte)Mathf.RoundToInt(intensity[i] * 255);
                pixels[i] = new Color32(value, value, value, 255);
            }
            var texture = new Texture2D(width, height, TextureFormat.RGBA32, false, true)
            {
                name = "Hotel occluded lamp illumination", filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp
            };
            texture.SetPixels32(pixels); texture.Apply(false, false);
            return new HotelLighting { LightMap = texture, MapSize = new Vector4(map.W, map.H, width, height), LampCount = count };
        }

        public static Vector2 LampOrigin(Lamp lamp, Func<int,int,bool> opaque)
        {
            Vector2 origin=lamp.Position+lamp.Normal*.035f;
            for(int step=0;step<7&&opaque(Mathf.FloorToInt(origin.x),Mathf.FloorToInt(origin.y));step++)origin+=lamp.Normal*.05f;
            return origin;
        }

        public static float Contribution(Lamp lamp,Vector2 origin,Vector2 point,Func<int,int,bool> opaque)
        {
            var delta=point-lamp.Position;float distance=delta.magnitude;
            if(distance>=lamp.Radius||Vector2.Dot(delta,lamp.Normal)<-.015f||!Sight.Clear(origin,point,opaque))return 0;
            float radial=1-distance/lamp.Radius;radial=radial*radial*(3-2*radial);
            // A bright pool right under the sconce makes the lamp read as the source;
            // the wider, softer falloff keeps the far side of its area usable.
            float hotspot=Mathf.Exp(-distance*distance/2f);
            float direction=.65f+.35f*Mathf.Clamp01(Vector2.Dot(delta/Mathf.Max(.001f,distance),lamp.Normal));
            return Mathf.Min(1,radial*.6f+hotspot*.5f)*direction*lamp.Strength;
        }

        /// <summary>Diagnostic CPU sample of the same bilinear field sampled by the world shaders.</summary>
        public float Sample(Vector2 position)
        {
            if (LightMap == null || position.x < 0 || position.y < 0 || position.x >= MapSize.x || position.y >= MapSize.y) return 0;
            // Unity's CPU sampler places texel centers at i/size; GPU tex2D places them
            // at (i+.5)/size. Apply the half-texel offset so coverage measures rendered light.
            return LightMap.GetPixelBilinear((position.x * SamplesPerTile - .5f) / LightMap.width,
                (position.y * SamplesPerTile - .5f) / LightMap.height).r;
        }

        public void Bind()
        {
            if (LightMap == null) throw new ObjectDisposedException(nameof(HotelLighting));
            boundLighting = this;
            Shader.SetGlobalTexture(TextureId, LightMap); Shader.SetGlobalVector(SizeId, MapSize);
            Shader.SetGlobalColor(ColorId, WarmColor); Shader.SetGlobalFloat(EnabledId, 1);
        }

        public void Dispose()
        {
            if (boundLighting == this)
            {
                boundLighting = null;
                Shader.SetGlobalFloat(EnabledId, 0);
                Shader.SetGlobalTexture(TextureId, Texture2D.blackTexture);
            }
            if (LightMap != null)
            {
                if (Application.isPlaying) Object.Destroy(LightMap); else Object.DestroyImmediate(LightMap);
            }
            LightMap = null;
        }
    }
}
