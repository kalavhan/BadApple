using System.Collections.Generic;
using UnityEngine;

namespace BadAppleHotel.Game
{
    /// <summary>Static modular hotel art. Position hashes choose variants without consuming match RNG.</summary>
    public static class HotelArt
    {
        static readonly Dictionary<string, Sprite> cache = new Dictionary<string, Sprite>();
        public static Sprite Get(string key, Sprite fallback)
        {
            if (!Sprites.UseArt) return fallback;
            if (cache.TryGetValue(key, out var hit) && hit != null) return hit;
            var tex = Resources.Load<Texture2D>("Art/Hotel/" + key);
            if (tex == null) return fallback;
            var sprite = Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height), new Vector2(0.5f, 0.5f), tex.width);
            cache[key] = sprite;
            return sprite;
        }
        public static int Variant(int x, int y) => (int)((uint)(x * 73856093 ^ y * 19349663) % 4);
    }
}
