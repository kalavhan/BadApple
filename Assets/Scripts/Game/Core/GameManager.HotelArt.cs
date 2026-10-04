using UnityEngine;

namespace BadAppleHotel.Game
{
    public partial class GameManager
    {
        static readonly Vector2Int[] WallSides = { Vector2Int.down, Vector2Int.left, Vector2Int.up, Vector2Int.right };

        void DrawHotelWall(Vector2Int tile, Transform parent)
        {
            var pos = HotelMap.Center(tile);
            bool hallway = false;
            for (int dx = -1; dx <= 1; dx++)
                for (int dy = -1; dy <= 1; dy++)
                    if (Map.Get(tile.x + dx, tile.y + dy) == Tile.Corridor) hallway = true;
            if (!hallway) { MakeSprite("room wall", Sprites.Wall, pos, -2900, parent); return; }

            var cap = MakeSprite("hall wall cap", HotelArt.Get("wall_top", Sprites.Wall), pos, -2900, parent);
            int variant = HotelArt.Variant(tile.x, tile.y);
            cap.transform.rotation = Quaternion.Euler(0, 0, variant * 90f);
            int sides = 0;
            for (int i = 0; i < WallSides.Length; i++)
            {
                var d = WallSides[i];
                var adjacent = tile + d;
                if (Map.Get(adjacent.x, adjacent.y) != Tile.Corridor && Map.Get(adjacent.x, adjacent.y) != Tile.Door) continue;
                sides++;
                float angle = -90f * i;
                var face = MakeSprite("hall wall face", HotelArt.Get("wall_face", Sprites.Wall), pos + (Vector2)d * 0.28f, -2890, parent);
                face.transform.rotation = Quaternion.Euler(0, 0, angle);
                face.transform.localScale = new Vector3(1f, 0.42f, 1f);
                var trim = MakeSprite("baseboard", HotelArt.Get("baseboard", Sprites.Wall), pos + (Vector2)d * 0.45f, -2880, parent);
                trim.transform.rotation = Quaternion.Euler(0, 0, angle);
                trim.transform.localScale = new Vector3(1f, 0.1f, 1f);
            }
            if (sides >= 2)
            {
                var corner = MakeSprite("corner", HotelArt.Get("wall_corner", Sprites.Wall), pos, -2870, parent);
                corner.transform.localScale = Vector3.one * 0.4f;
            }
            if (sides > 0 && (tile.x * 7 + tile.y * 13) % 19 == 0)
            {
                var prop = HotelArt.Get("prop_" + variant, null);
                if (prop != null)
                {
                    var sr = MakeSprite("hall detail", prop, pos, -2860, parent);
                    sr.transform.localScale = Vector3.one * 0.7f;
                }
            }
        }
    }
}
