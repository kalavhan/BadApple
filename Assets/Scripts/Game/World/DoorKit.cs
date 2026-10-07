using System;
using UnityEngine;

namespace BadAppleHotel.Game
{
    /// <summary>Baked door leaves for the existing shared hotel frame. Local X runs from the
    /// lower-left hinge to the latch, Y runs inward, and negative Z is height above the floor.
    /// The closed front faces -Y. A leaf opens by rotating its hinge about local Z.</summary>
    public sealed class DoorKit : ScriptableObject
    {
        public const float Width = .94f, Height = 1.4f, MaxDepth = .2f;
        public const int LastArtLevel = 7;

        [Serializable]
        public sealed class Piece
        {
            public int Level;
            public Mesh Mesh;
            /// <summary>Six textured leaf fragments laid on the floor around the closed threshold.
            /// Uses the same lower-left origin and albedo as Mesh; render with powered effects off.</summary>
            public Mesh BrokenMesh;
            public Texture2D Albedo;
            public Vector3 Size;
            public string Source;
            public int Triangles;
            public int SourceTriangles;
        }

        public Piece[] Pieces = Array.Empty<Piece>();

        public Piece Get(int level)
        {
            // The gameplay progression has ten levels; the approved art pass ends at Deadbolt.
            // A missing intermediate export must still use its own sprite fallback.
            level = Mathf.Min(level, LastArtLevel);
            if (Pieces == null) return null;
            foreach (var piece in Pieces)
                if (piece != null && piece.Level == level && piece.Mesh != null) return piece;
            return null;
        }

        public static DoorKit Load() => Resources.Load<DoorKit>("Art3D/Doors/HotelDoorKit");
    }
}
