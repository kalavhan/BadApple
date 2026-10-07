using UnityEngine;

namespace BadAppleHotel.Game
{
    /// <summary>A visual leaf hinged to the existing frame. Gameplay owns passage blocking.</summary>
    public sealed class DoorModel : MonoBehaviour
    {
        public MeshRenderer Renderer { get; private set; }
        public float OpenAngle { get; private set; }
        public int Tier { get; private set; }
        public bool Broken { get; private set; }
        public bool Powered { get; private set; }
        public const float OpenDegrees = 90f;
        MaterialPropertyBlock properties;
        MeshFilter meshFilter;
        Quaternion closedRotation;
        Vector2 ground;
        bool initialized, open, hasRubble;
        int upgradeLevel;
        float lastHp, hitAt = -100, pulseAt = -100, phase;

        public void Initialize(RoomDef room)
        {
            properties = new MaterialPropertyBlock();
            closedRotation = Quaternion.Euler(0, 0, room.DoorRotation);
            ground = HotelMap.Center(room.DoorTile);
            var hinge = (Vector3)ground - closedRotation * new Vector3(DoorKit.Width * .5f, 0, 0);
            transform.SetPositionAndRotation(hinge, closedRotation);
            phase = room.Index * 2.37f;
            meshFilter = gameObject.AddComponent<MeshFilter>();
            Renderer = gameObject.AddComponent<MeshRenderer>();
            Renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            Renderer.receiveShadows = false;
        }

        public void SetState(DoorKit.Piece piece, Material material, bool isOpen, bool broken,
            bool powered, float hpFraction, float clock, int logicalLevel = 0)
        {
            if (logicalLevel <= 0) logicalLevel = piece.Level;
            if (initialized)
            {
                if (upgradeLevel != logicalLevel || (Broken && !broken)) pulseAt = clock;
                // Damage arrives in simulation substeps; one short rattle per strike interval.
                if (hpFraction < lastHp - .00001f && clock - hitAt >= .38f) hitAt = clock;
            }
            else OpenAngle = isOpen ? OpenDegrees : 0;
            Tier = piece.Level; open = isOpen; Broken = broken; Powered = powered && !broken;
            hasRubble = piece.BrokenMesh != null;
            upgradeLevel = logicalLevel;
            lastHp = hpFraction; initialized = true;
            var mesh = broken && hasRubble ? piece.BrokenMesh : piece.Mesh;
            if (meshFilter.sharedMesh != mesh) meshFilter.sharedMesh = mesh;
            if (Renderer.sharedMaterial != material) Renderer.sharedMaterial = material;
            Renderer.enabled = !broken || hasRubble;
            properties.SetFloat("_Damage", 1 - Mathf.Clamp01(hpFraction));
        }

        public void Present(float dt, float clock, bool visible)
        {
            if (!initialized) return;
            OpenAngle = Broken ? 0 : Mathf.MoveTowards(OpenAngle, open ? OpenDegrees : 0, Mathf.Max(0, dt) * 420f);
            float hit = Broken ? 0 : Mathf.Clamp01(1 - (clock - hitAt) / .3f);
            float pulse = Broken ? 0 : Mathf.Clamp01(1 - (clock - pulseAt) / .65f);
            float rattle = Mathf.Sin((clock - hitAt) * 65) * hit * (Tier <= 2 ? 2.4f : .8f);
            transform.rotation = closedRotation * Quaternion.Euler(0, 0, OpenAngle + rattle);
            Renderer.enabled = visible && (!Broken || hasRubble);
            properties.SetFloat("_Phase", phase);
            properties.SetFloat("_Clock", clock);
            properties.SetFloat("_Hit", hit);
            properties.SetFloat("_Pulse", pulse);
            properties.SetFloat("_Power", Powered && visible ? 1 : 0);
            properties.SetFloat("_Visibility", visible ? 1 : 0);
            properties.SetVector("_DoorGround", new Vector4(ground.x, ground.y, 0, 0));
            Renderer.SetPropertyBlock(properties);
        }
    }
}
