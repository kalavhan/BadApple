using System.Collections.Generic;
using UnityEngine;

namespace BadAppleHotel.Game
{
    /// <summary>
    /// Spectral energy in claimed rooms: once a living resident claims a room, its build
    /// squares are divided by glowing seams with low aurora ribbons rising from them, and the
    /// local guest's legal empty squares show a quiet plus. Placement eligibility is cached
    /// and recomputed only when room ownership, tower occupancy or the selection changes.
    /// </summary>
    public partial class GameManager
    {
        Texture2D floorCellTexture, spectralTexture;
        Color32[] spectralPixels;
        Material spectralRibbonMaterial, spectralPlusMaterial;
        MeshRenderer spectralRibbonView, spectralPlusView;
        int spectralSignature = int.MinValue, ribbonSignature = int.MinValue;

        /// <summary>The build slot selected in the HUD for the human's own room, or -1.</summary>
        public int SelectedBuildSlot { get; set; } = -1;
        public int SpectralRibbonQuads { get; private set; }
        public int SpectralPlusQuads { get; private set; }
        /// <summary>Seam strength (r), plus (g) and selection (b) of a cell, for tests and tools.</summary>
        public Color32 SpectralAt(Vector2Int cell) =>
            spectralPixels != null && Map.InBounds(cell.x, cell.y) ? spectralPixels[cell.y * Map.W + cell.x] : default;

        void BuildFloorEnergyTextures()
        {
            floorCellTexture = new Texture2D(Map.W, Map.H, TextureFormat.RGBA32, false, true)
                { name = "Floor cells", filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp };
            var cells = new Color32[Map.W * Map.H];
            for (int x = 0; x < Map.W; x++) for (int y = 0; y < Map.H; y++)
                cells[y * Map.W + x] = Map.Get(x, y) == Tile.Corridor ? new Color32(255, 0, 0, 255) : new Color32(0, 0, 0, 255);
            floorCellTexture.SetPixels32(cells); floorCellTexture.Apply(false); sceneAssets.Add(floorCellTexture);
            floorMaterial.SetTexture("_FloorCells", floorCellTexture);

            spectralTexture = new Texture2D(Map.W, Map.H, TextureFormat.RGBA32, false, true)
                { name = "Claimed-room energy", filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp };
            spectralPixels = new Color32[Map.W * Map.H];
            spectralTexture.SetPixels32(spectralPixels); spectralTexture.Apply(false); sceneAssets.Add(spectralTexture);
            Shader.SetGlobalTexture("_SpectralCells", spectralTexture);
            Shader.SetGlobalVector("_SpectralSize", new Vector4(Map.W, Map.H, 0, 0));

            spectralRibbonMaterial = new Material(Resources.Load<Shader>("Shaders/HotelSpectralRibbon")); sceneAssets.Add(spectralRibbonMaterial);
            spectralPlusMaterial = new Material(Resources.Load<Shader>("Shaders/HotelSpectralPlus")); sceneAssets.Add(spectralPlusMaterial);
            spectralRibbonView = spectralPlusView = null; SpectralRibbonQuads = SpectralPlusQuads = 0;
            spectralSignature = ribbonSignature = int.MinValue;
        }

        bool Energized(Room room) => room?.Owner != null && room.Owner.Alive;

        void UpdateFloorEnergy()
        {
            if (spectralTexture == null || Map == null) return;
            int rooms = 17, slots = 17;
            if (InMatch)
                foreach (var def in Map.Rooms)
                {
                    if (!RoomsByDef.TryGetValue(def, out var room) || !Energized(room)) continue;
                    rooms = rooms * 31 + def.Index + 1;
                    slots = slots * 31 + def.Index + 1;
                    foreach (var tower in room.Slots) slots = slots * 3 + (tower != null ? 1 : 2);
                }
            var mine = InMatch && HumanRole == Role.Resident && Human != null && Human.Alive ? Human.Room : null;
            rooms = rooms * 31 + (mine?.Def.Index ?? -1);
            slots = (slots * 31 + (mine?.Def.Index ?? -1)) * 31 + SelectedBuildSlot;
            if (rooms != ribbonSignature) { ribbonSignature = rooms; BuildSpectralRibbons(); BuildSpectralPluses(mine); }
            if (slots == spectralSignature) return;
            spectralSignature = slots;

            System.Array.Clear(spectralPixels, 0, spectralPixels.Length);
            if (InMatch)
                foreach (var room in RoomsByDef.Values)
                {
                    if (!Energized(room)) continue;
                    bool own = room == mine;
                    for (int i = 0; i < room.Slots.Length; i++)
                    {
                        var cell = room.Def.BuildTiles[i];
                        bool empty = room.Slots[i] == null;
                        spectralPixels[cell.y * Map.W + cell.x] = new Color32(
                            empty ? (byte)255 : (byte)90,
                            own && empty && CanBuildAt(room, i) ? (byte)255 : (byte)0,
                            own && i == SelectedBuildSlot ? (byte)255 : (byte)0, 255);
                    }
                }
            spectralTexture.SetPixels32(spectralPixels); spectralTexture.Apply(false);
        }

        void DropView(ref MeshRenderer view)
        {
            if (view == null) return;
            var mesh = view.GetComponent<MeshFilter>().sharedMesh;
            RemoveObject(view.gameObject); sceneAssets.Remove(mesh); RemoveObject(mesh); view = null;
        }

        /// <summary>A hovering plus over every build square of the local guest's room; the shader
        /// shows only the legal empty ones (cell energy g), so building needs no rebuild.</summary>
        void BuildSpectralPluses(Room mine)
        {
            DropView(ref spectralPlusView); SpectralPlusQuads = 0;
            if (!InMatch || mine == null) return;
            var vertices = new List<Vector3>(); var triangles = new List<int>(); var uv = new List<Vector2>();
            var cells = new List<Vector2>(); var rights = new List<Vector3>(); var ups = new List<Vector3>();
            const float size = .2f, lift = .3f;
            Vector3 right = HotelView3D.Right * size, up = HotelView3D.Up * size;
            foreach (var cell in mine.Def.BuildTiles)
            {
                var center = (Vector3)HotelMap.Center(cell) + Vector3.back * lift;
                int i = vertices.Count;
                foreach (var corner in new[] { new Vector2(-1, -1), new Vector2(1, -1), new Vector2(1, 1), new Vector2(-1, 1) })
                {
                    vertices.Add(center + right * corner.x + up * corner.y); uv.Add(corner);
                    cells.Add(cell); rights.Add(right); ups.Add(up);
                }
                triangles.AddRange(new[] { i, i + 1, i + 2, i, i + 2, i + 3 });
            }
            var mesh = new Mesh { name = "Spectral build plus marks" };
            mesh.SetVertices(vertices); mesh.SetTriangles(triangles, 0); mesh.SetUVs(0, uv); mesh.SetUVs(1, cells);
            mesh.SetUVs(2, rights); mesh.SetUVs(3, ups); mesh.RecalculateBounds();
            sceneAssets.Add(mesh);
            spectralPlusView = MeshObject(mesh.name, mesh, spectralPlusMaterial, worldRoot);
            SpectralPlusQuads = vertices.Count / 4;
        }

        /// <summary>One rising aurora ribbon per seam edge of every energized room.</summary>
        void BuildSpectralRibbons()
        {
            DropView(ref spectralRibbonView); SpectralRibbonQuads = 0;
            if (!InMatch) return;
            var vertices = new List<Vector3>(); var triangles = new List<int>(); var uv = new List<Vector2>(); var cells = new List<Vector2>();
            const float floor = .07f, height = .45f;
            foreach (var room in RoomsByDef.Values)
            {
                if (!Energized(room)) continue;
                var build = new HashSet<Vector2Int>(room.Def.BuildTiles);
                foreach (var cell in build)
                {
                    // Right and top edges always; left and bottom only where no build square adds them.
                    void Edge(Vector2 a, Vector2 b)
                    {
                        int i = vertices.Count;
                        float along = a.x + a.y;
                        vertices.Add(new Vector3(a.x, a.y, floor)); vertices.Add(new Vector3(b.x, b.y, floor));
                        vertices.Add(new Vector3(b.x, b.y, floor - height)); vertices.Add(new Vector3(a.x, a.y, floor - height));
                        uv.Add(new Vector2(along, 0)); uv.Add(new Vector2(along + 1, 0)); uv.Add(new Vector2(along + 1, 1)); uv.Add(new Vector2(along, 1));
                        for (int k = 0; k < 4; k++) cells.Add(cell);
                        triangles.AddRange(new[] { i, i + 1, i + 2, i, i + 2, i + 3 });
                    }
                    float x = cell.x, y = cell.y;
                    Edge(new Vector2(x + 1, y), new Vector2(x + 1, y + 1));
                    Edge(new Vector2(x, y + 1), new Vector2(x + 1, y + 1));
                    if (!build.Contains(cell + Vector2Int.left)) Edge(new Vector2(x, y), new Vector2(x, y + 1));
                    if (!build.Contains(cell + Vector2Int.down)) Edge(new Vector2(x, y), new Vector2(x + 1, y));
                }
            }
            if (vertices.Count == 0) return;
            var mesh = new Mesh { name = "Claimed-room aurora", indexFormat = UnityEngine.Rendering.IndexFormat.UInt32 };
            mesh.SetVertices(vertices); mesh.SetTriangles(triangles, 0); mesh.SetUVs(0, uv); mesh.SetUVs(1, cells); mesh.RecalculateBounds();
            sceneAssets.Add(mesh);
            spectralRibbonView = MeshObject(mesh.name, mesh, spectralRibbonMaterial, worldRoot);
            SpectralRibbonQuads = vertices.Count / 4;
        }
    }
}
