using System;
using UnityEngine;

namespace BadAppleHotel.Game
{
    /// <summary>Baked mobile meshes share one atlas; X is width, Y height, Z depth.
    /// The front floor-edge pivot is (0,0,0); all vertices lie in positive bounds.</summary>
    public sealed class WallKit : ScriptableObject
    {
        [Serializable]
        public sealed class Piece
        {
            public string Id;
            public Mesh Mesh;
            public Vector3 Size;
            public Sprite Preview;
            public string Source;
            public int SourceTriangles;
            public int Triangles;
        }

        public Texture2D Atlas;
        public Piece[] Pieces = Array.Empty<Piece>();
        public Piece Get(string id)
        {
            foreach (var piece in Pieces) if (piece.Id == id) return piece;
            return null;
        }
        public static WallKit Load() => Resources.Load<WallKit>("Art3D/Walls/HotelWallKit");
    }
}
