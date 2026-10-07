using System;
using UnityEngine;

namespace BadAppleHotel.Game
{
    /// <summary>Baked Tripo bed models, one per bed level. Meshes are already in game axes:
    /// XY floor with the head end toward +Y, negative Z height, pivot at the footprint's
    /// center on the ground. Levels without a piece keep their sprite.</summary>
    public sealed class BedKit : ScriptableObject
    {
        public const float Width = .94f, Length = 1.9f;

        [Serializable]
        public sealed class Piece
        {
            public int Level;
            public Mesh Mesh;
            public Texture2D Albedo;
            public Vector3 Size;
            /// <summary>Height of the sleeping surface above the floor, measured from the mesh.</summary>
            public float SurfaceHeight;
            /// <summary>Idle motion in the HotelBed shader: 0 none, 1 corner flutter, 2 foot-end flap.</summary>
            public int Motion;
            public string Source;
            public int Triangles;
        }

        public Piece[] Pieces = Array.Empty<Piece>();
        public Piece Get(int level)
        {
            foreach (var piece in Pieces) if (piece != null && piece.Level == level && piece.Mesh != null) return piece;
            return null;
        }
        public static BedKit Load() => Resources.Load<BedKit>("Art3D/Beds/HotelBedKit");
    }
}
