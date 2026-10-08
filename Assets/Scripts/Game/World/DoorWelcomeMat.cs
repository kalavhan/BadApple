using UnityEngine;

namespace BadAppleHotel.Game
{
    public enum DoorMatState { Available, Occupied, Awake, Sleeping, Dead, Empty }

    /// <summary>A spectral welcome mat on the corridor side of a door. It never swings with
    /// the leaf or inherits wall cutaways; its color describes the room's current state.</summary>
    public sealed class DoorWelcomeMat : MonoBehaviour
    {
        public const float Width = 1.16f, Depth = .78f, OutsideOffset = .78f, FloorZ = .055f;
        public MeshRenderer Renderer { get; private set; }
        public DoorMatState State { get; private set; }
        MaterialPropertyBlock properties;
        Vector2 doorway;
        float phase;

        public static DoorMatState StateFor(Phase phase, Room room)
        {
            bool occupied = room?.Owner != null;
            if (phase == Phase.Setup) return occupied ? DoorMatState.Occupied : DoorMatState.Available;
            if (!occupied) return DoorMatState.Empty;
            if (!room.Owner.Alive) return DoorMatState.Dead;
            return room.Owner.Asleep ? DoorMatState.Sleeping : DoorMatState.Awake;
        }

        public static Color Tint(DoorMatState state)
        {
            switch (state)
            {
                case DoorMatState.Available: return new Color(.30f, 1f, .42f);
                case DoorMatState.Occupied:
                case DoorMatState.Dead: return new Color(1f, .14f, .18f);
                case DoorMatState.Empty: return new Color(.20f, .48f, 1f);
                default: return new Color(1f, .76f, .16f);
            }
        }

        public static Mesh CreateMesh()
        {
            var mesh = new Mesh { name = "Door welcome mat quad" };
            mesh.vertices = new[] { new Vector3(-Width / 2, -Depth / 2, 0), new Vector3(Width / 2, -Depth / 2, 0),
                new Vector3(Width / 2, Depth / 2, 0), new Vector3(-Width / 2, Depth / 2, 0) };
            mesh.uv = new[] { new Vector2(-1, -1), new Vector2(1, -1), new Vector2(1, 1), new Vector2(-1, 1) };
            mesh.triangles = new[] { 0, 2, 1, 0, 3, 2 };
            mesh.RecalculateBounds();
            return mesh;
        }

        public void Initialize(RoomDef room, Mesh mesh, Material material)
        {
            properties = new MaterialPropertyBlock();
            doorway = HotelMap.Center(room.DoorTile);
            var outward = (Vector2)(room.DoorOutside - room.DoorTile);
            var center = doorway + outward * OutsideOffset;
            transform.SetPositionAndRotation(new Vector3(center.x, center.y, FloorZ), Quaternion.Euler(0, 0, room.DoorRotation));
            phase = room.Index * 2.37f;
            gameObject.AddComponent<MeshFilter>().sharedMesh = mesh;
            Renderer = gameObject.AddComponent<MeshRenderer>();
            Renderer.sharedMaterial = material;
            Renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            Renderer.receiveShadows = false;
            Renderer.enabled = false;
        }

        public void Present(Phase matchPhase, Room room, bool visible, float clock)
        {
            State = StateFor(matchPhase, room);
            Renderer.enabled = visible;
            properties.SetColor("_Color", Tint(State));
            properties.SetFloat("_Intensity", State == DoorMatState.Awake ? .45f : 1f);
            properties.SetFloat("_Clock", clock);
            properties.SetFloat("_Phase", phase);
            properties.SetFloat("_Visibility", visible ? 1 : 0);
            properties.SetVector("_DoorGround", new Vector4(doorway.x, doorway.y, 0, 0));
            Renderer.SetPropertyBlock(properties);
        }
    }
}
