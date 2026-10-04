using UnityEngine;

namespace BadAppleHotel.Game
{
    /// <summary>
    /// Fog of war for the human resident at night: you see your own room (walls included), a few tiles around your
    /// door and around yourself. A Crystal ball in your room lifts the fog everywhere and unlocks the hotel view.
    /// The monster, ghosts and everyone during setup see the whole hotel.
    /// </summary>
    public partial class GameManager
    {
        Texture2D fogTex;
        SpriteRenderer fogSr;
        Color32[] fogPx;
        bool[] visible;

        const byte FogAlpha = 248;

        public bool FogActive =>
            Phase == Phase.Night && HumanRole == Role.Resident && Human != null && Human.Alive &&
            Human.Room != null && !Human.Room.HasClairvoyance();

        void CreateFog()
        {
            fogTex = new Texture2D(Map.W, Map.H, TextureFormat.RGBA32, false)
            {
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp,
            };
            fogPx = new Color32[Map.W * Map.H];
            visible = new bool[Map.W * Map.H];
            var sprite = Sprite.Create(fogTex, new Rect(0, 0, Map.W, Map.H), Vector2.zero, 1f);
            var go = new GameObject("Fog");
            go.transform.SetParent(worldRoot, false);
            go.transform.position = Vector3.zero;
            fogSr = go.AddComponent<SpriteRenderer>();
            fogSr.sprite = sprite;
            fogSr.sortingOrder = 7000;
            fogSr.enabled = false;
        }

        public bool IsVisible(Vector2 world)
        {
            if (!FogActive || visible == null) return true;
            int x = Mathf.FloorToInt(world.x), y = Mathf.FloorToInt(world.y);
            if (!Map.InBounds(x, y)) return false;
            return visible[y * Map.W + x];
        }

        public bool IsTileVisible(Vector2Int t) => IsVisible(HotelMap.Center(t));

        void UpdateVision()
        {
            if (fogSr == null || Map == null) return;
            bool fog = FogActive;
            fogSr.enabled = fog;
            if (!fog)
            {
                SetEntityVisibility(false);
                return;
            }

            System.Array.Clear(visible, 0, visible.Length);
            var room = Human.Room;
            foreach (var f in room.Def.Floor)
                for (int dx = -1; dx <= 1; dx++)
                    for (int dy = -1; dy <= 1; dy++) Mark(f.x + dx, f.y + dy);
            float radius = Cfg.residents.visionRadiusTiles;
            MarkCircle(HotelMap.Center(room.Def.DoorOutside), radius);
            MarkCircle(Human.Pos, radius);

            for (int i = 0; i < fogPx.Length; i++)
                fogPx[i] = new Color32(0x0E, 0x0B, 0x13, visible[i] ? (byte)0 : (SleepingCamera ? (byte)180 : FogAlpha));
            fogTex.SetPixels32(fogPx);
            fogTex.Apply(false);
            SetEntityVisibility(true);
        }

        void Mark(int x, int y)
        {
            if (Map.InBounds(x, y)) visible[y * Map.W + x] = true;
        }

        void MarkCircle(Vector2 c, float r)
        {
            int x0 = Mathf.FloorToInt(c.x - r), x1 = Mathf.CeilToInt(c.x + r);
            int y0 = Mathf.FloorToInt(c.y - r), y1 = Mathf.CeilToInt(c.y + r);
            for (int x = x0; x <= x1; x++)
                for (int y = y0; y <= y1; y++)
                    if (Vector2.Distance(HotelMap.Center(new Vector2Int(x, y)), c) <= r) Mark(x, y);
        }

        /// <summary>Hides other residents and body parts in the dark (the monster handles its own renderer).</summary>
        void SetEntityVisibility(bool fog)
        {
            foreach (var r in Residents)
                if (r.Sr != null) r.Sr.enabled = !fog || r == Human || IsVisible(r.Pos);
            foreach (var p in Parts)
                if (p.Sr != null) p.Sr.enabled = !fog || IsTileVisible(p.Tile);
        }
    }
}
