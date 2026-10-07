using System.Collections.Generic;
using UnityEngine;

namespace BadAppleHotel.Game
{
    public partial class GameManager
    {
        /// <summary>The bed rug sits at this depth; 3D beds rest just above it.</summary>
        const float BedGroundZ = .055f, SpriteSleepZ = -.06f;
        BedKit bedKit;
        bool bedKitLoaded;
        static readonly int PhaseId = Shader.PropertyToID("_Phase");
        readonly Dictionary<int, Material> bedMaterials = new Dictionary<int, Material>();

        BedKit.Piece BedPiece(int level)
        {
            if (Simulation) return null;
            if (!bedKitLoaded) { bedKit = BedKit.Load(); bedKitLoaded = true; }
            return bedKit != null ? bedKit.Get(level) : null;
        }

        /// <summary>Shows the room's current bed level: the baked 3D model when one exists, else the sprite.</summary>
        void ApplyBedLook(Room room)
        {
            var piece = BedPiece(room.BedLevel);
            if (piece == null)
            {
                if (room.BedModel != null) room.BedModel.gameObject.SetActive(false);
                room.BedSr.sprite = Sprites.Bed(room.BedLevel);
                room.BedSr.transform.localScale = SleepPose.BedScale(room.BedSr.sprite);
                return;
            }
            // The sprite renderer stays as the bed's sorting and visibility anchor.
            room.BedSr.sprite = null;
            room.BedSr.transform.localScale = Vector3.one;
            if (room.BedModel == null)
            {
                room.BedModel = MeshObject("Bed model", piece.Mesh, null, matchRoot);
                var center = room.Def.BedCenter;
                room.BedModel.transform.SetPositionAndRotation(new Vector3(center.x, center.y, BedGroundZ), Quaternion.Euler(0f, 0f, room.Def.BedRotation));
            }
            room.BedModel.GetComponent<MeshFilter>().sharedMesh = piece.Mesh;
            room.BedModel.sharedMaterial = BedMaterial(piece);
            var block = new MaterialPropertyBlock(); block.SetFloat(PhaseId, room.Def.Index);
            room.BedModel.SetPropertyBlock(block);
            room.BedModel.gameObject.SetActive(true);
            room.BedModel.enabled = room.BedSr.enabled;
        }

        Material BedMaterial(BedKit.Piece piece)
        {
            if (bedMaterials.TryGetValue(piece.Level, out var material) && material != null) return material;
            material = new Material(Resources.Load<Shader>("Shaders/HotelBed")) { name = "Bed " + piece.Level, mainTexture = piece.Albedo };
            material.SetFloat("_Motion", piece.Motion);
            sceneAssets.Add(material); bedMaterials[piece.Level] = material;
            return material;
        }

        /// <summary>Depth for a sleeping resident: on the 3D mattress surface, or the sprite bed's layer.</summary>
        float SleepZ(Room room)
        {
            var piece = room.BedModel != null && room.BedModel.gameObject.activeSelf ? BedPiece(room.BedLevel) : null;
            return piece == null ? SpriteSleepZ : BedGroundZ - piece.SurfaceHeight - .02f;
        }
    }
}
