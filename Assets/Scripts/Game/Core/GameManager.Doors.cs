using System.Collections.Generic;
using UnityEngine;

namespace BadAppleHotel.Game
{
    public partial class GameManager
    {
        DoorKit doorKit;
        bool doorKitLoaded;
        readonly Dictionary<RoomDef, DoorModel> doorModels = new Dictionary<RoomDef, DoorModel>();
        readonly Dictionary<int, Material> doorMaterials = new Dictionary<int, Material>();
        readonly Dictionary<RoomDef, DoorWelcomeMat> doorMats = new Dictionary<RoomDef, DoorWelcomeMat>();
        Mesh doorMatMesh;
        Material doorMatMaterial;

        void CreateDoorMat(RoomDef def)
        {
            if (doorMatMesh == null)
            {
                doorMatMesh = DoorWelcomeMat.CreateMesh(); sceneAssets.Add(doorMatMesh);
                doorMatMaterial = new Material(Resources.Load<Shader>("Shaders/HotelDoorMat")); sceneAssets.Add(doorMatMaterial);
            }
            var go = new GameObject("Door welcome mat " + (def.Index + 1));
            go.transform.SetParent(worldRoot, false);
            var mat = go.AddComponent<DoorWelcomeMat>();
            mat.Initialize(def, doorMatMesh, doorMatMaterial); doorMats[def] = mat;
        }

        DoorKit.Piece DoorPiece(int level)
        {
            if (Simulation) return null;
            if (!doorKitLoaded) { doorKit = DoorKit.Load(); doorKitLoaded = true; }
            return doorKit != null ? doorKit.Get(level) : null;
        }

        Material DoorMaterial(DoorKit.Piece piece)
        {
            if (doorMaterials.TryGetValue(piece.Level, out var material) && material != null) return material;
            material = new Material(Resources.Load<Shader>("Shaders/HotelDoor"))
                { name = "Door " + piece.Level, mainTexture = piece.Albedo };
            material.SetFloat("_Tier", piece.Level);
            sceneAssets.Add(material); doorMaterials[piece.Level] = material;
            return material;
        }

        void ApplyDoorLook(RoomDef def, Room room = null)
        {
            if (!doorSprites.TryGetValue(def, out var sprite) || sprite == null) return;
            var piece = DoorPiece(room?.DoorLevel ?? 1);
            bool broken = room != null && room.DoorBroken;
            bool open = room == null || room.DoorOpen;
            if (piece != null)
            {
                if (!doorModels.TryGetValue(def, out var model) || model == null)
                {
                    var go = new GameObject("Door leaf " + (def.Index + 1));
                    go.transform.SetParent(worldRoot, false);
                    model = go.AddComponent<DoorModel>(); model.Initialize(def); doorModels[def] = model;
                }
                model.gameObject.SetActive(true);
                model.SetState(piece, DoorMaterial(piece), open, broken, room?.Owner != null && room.Owner.Alive,
                    room == null ? 1 : room.DoorHp / MaxDoorHp(room), Time.time, room?.DoorLevel ?? 1);
                if (!broken || piece.BrokenMesh != null) { sprite.sprite = null; sprite.enabled = false; return; }
            }
            else if (doorModels.TryGetValue(def, out var model) && model != null) model.gameObject.SetActive(false);
            // Legacy fallback for missing art: keep a low marker and a visibly clear passage.
            sprite.sprite = broken ? Sprites.DoorBroken : open ? Sprites.DoorOpen : Sprites.Door(room.DoorLevel);
            sprite.enabled = true;
            PoseDoor(sprite, def, broken || open);
        }

        void UpdateDoorViews()
        {
            if (Simulation || Map == null) return;
            foreach (var def in Map.Rooms)
            {
                RoomsByDef.TryGetValue(def, out var room);
                bool visible = IsTileVisible(def.DoorTile);
                if (doorMats.TryGetValue(def, out var mat))
                    mat.Present(Phase, room, InMatch && visible, Time.time);
                if (doorModels.TryGetValue(def, out var model) && model != null && model.gameObject.activeSelf)
                {
                    var piece = DoorPiece(room?.DoorLevel ?? 1);
                    if (piece == null) continue;
                    model.SetState(piece, DoorMaterial(piece), room == null || room.DoorOpen,
                        room != null && room.DoorBroken, room?.Owner != null && room.Owner.Alive,
                        room == null ? 1 : room.DoorHp / MaxDoorHp(room), Time.time, room?.DoorLevel ?? 1);
                    // Door leaves are standalone objects: wall cutaways never crop or fade them.
                    model.Present(Time.deltaTime, Time.time, visible);
                }
                if (doorSprites.TryGetValue(def, out var sprite) && sprite != null)
                    sprite.enabled = visible && sprite.sprite != null;
            }
        }

        void ResetDoorViews()
        {
            foreach (var def in doorSprites.Keys) ApplyDoorLook(def);
            UpdateDoorViews();
        }
    }
}
