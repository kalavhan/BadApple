using System.Collections.Generic;
using UnityEngine;

namespace BadAppleHotel.Game
{
    /// <summary>Static views only: eight columns, four upgrade-tier rows. No animation or runtime generation.</summary>
    public static class TowerDirections
    {
        static readonly Dictionary<string, Sprite[,]> cache = new Dictionary<string, Sprite[,]>();
        public static bool Supports(string id) => id == "gun_turret" || id == "sniper_nest";
        public static Sprite Get(string id, int level, Vector2 facing)
        {
            if (!Supports(id) || !Sprites.UseArt) return null;
            if (!cache.TryGetValue(id, out var frames) || frames[0,0] == null)
            {
                var texture = Resources.Load<Texture2D>("Art/Directions/" + id);
                if (texture == null) return null;
                float w = texture.width / 8f, h = texture.height / 4f;
                frames = new Sprite[4,8];
                for (int tier=0;tier<4;tier++) for(int dir=0;dir<8;dir++)
                    frames[tier,dir] = Sprite.Create(texture,new Rect(dir*w,(3-tier)*h,w,h),new Vector2(0.5f,0.08f),h/(id=="gun_turret"?1.45f:1.7f),0,SpriteMeshType.FullRect);
                cache[id] = frames;
            }
            return frames[Mathf.Clamp(level-1,0,3),CharacterSet.DirIndex(HotelView3D.Facing(facing))];
        }
    }
}
