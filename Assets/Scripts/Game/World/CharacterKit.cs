using System;
using UnityEngine;

namespace BadAppleHotel.Game
{
    /// <summary>Baked Tripo resident models keyed by roster art id. Meshes are in game axes:
    /// feet at the origin, negative Z up, the face toward +Y, one unit = one tile.
    /// Residents without a model keep their sprite animation set.</summary>
    public sealed class CharacterKit : ScriptableObject
    {
        [Serializable]
        public sealed class Model
        {
            public string Id;
            public Mesh Mesh;
            public Texture2D Albedo;
            public float Height;
            /// <summary>Half the front-to-back thickness, so a lying body rests its back on the bed.</summary>
            public float HalfDepth;
            public string Source;
            public int SourceTriangles;
            /// <summary>Rigged Humanoid prefab with the shared resident Animator Controller, or null
            /// while the character only has the static mesh. Unity axes: Y up, face toward +Z.</summary>
            public GameObject Rig;
            /// <summary>Rest-pose height of <see cref="Rig"/> in its own units.</summary>
            public float RigHeight;
            public string RigSource;
        }

        public Model[] Models = Array.Empty<Model>();
        public Model Get(string id)
        {
            foreach (var model in Models) if (model != null && model.Id == id && model.Mesh != null) return model;
            return null;
        }
        public static CharacterKit Load() => Resources.Load<CharacterKit>("Art3D/Characters/HotelCharacterKit");
    }
}
