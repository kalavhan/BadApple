using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using BadAppleHotel.Config;
using BadAppleHotel.Game;
using NUnit.Framework;
using UnityEngine;

namespace BadAppleHotel.Tests
{
    public class FloorEnergyTests
    {
        const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
        GameManager game;
        Random.State randomState;
        bool? art;
        float timeScale;

        [SetUp] public void SetUp()
        {
            randomState = Random.state; art = Sprites.ArtOverride; timeScale = Time.timeScale;
            game = new GameObject("Floor energy test").AddComponent<GameManager>();
            game.StartSimulation(ConfigLoader.Load(), 241, false);
            typeof(GameManager).GetMethod("BuildScene3D", Private).Invoke(game, null);
            game.Human.IsHuman = true; game.Human.Ai = null;
            foreach (var def in game.Map.Rooms) def.Isolated = false;
        }

        [TearDown] public void TearDown()
        {
            if (game != null) game.DisposeSimulation();
            Random.state = randomState; Sprites.ArtOverride = art; Time.timeScale = timeScale;
        }

        void Update() => typeof(GameManager).GetMethod("UpdateFloorEnergy", Private).Invoke(game, null);

        IEnumerable<Vector2Int> AllCells()
        {
            for (int x = 0; x < game.Map.W; x++) for (int y = 0; y < game.Map.H; y++) yield return new Vector2Int(x, y);
        }

        [Test] public void Claiming_a_room_divides_its_build_squares_with_energy_and_marks_legal_squares()
        {
            Update();
            Assert.IsTrue(AllCells().All(c => game.SpectralAt(c).r == 0), "No energy before any room is claimed.");
            Assert.AreEqual(0, game.SpectralRibbonQuads);

            var own = game.Map.Rooms[0];
            Assert.IsTrue(game.Claim(game.Human, own, true));
            var room = game.Human.Room;
            Update();
            for (int i = 0; i < own.BuildTiles.Count; i++)
            {
                var energy = game.SpectralAt(own.BuildTiles[i]);
                Assert.AreEqual(255, energy.r, "Seam on build square " + own.BuildTiles[i]);
                Assert.AreEqual(game.CanBuildAt(room, i), energy.g == 255, "Plus must match CanBuildAt");
            }
            Assert.AreEqual(0, game.SpectralAt(own.BedTile).r, "The bed keeps normal flooring.");
            Assert.AreEqual(0, game.SpectralAt(own.DoorInside).r, "The doorway square keeps normal flooring.");
            foreach (var cell in own.Walkway)
                if (cell != own.DoorInside) Assert.AreEqual(255, game.SpectralAt(cell).r, "The default walkway is buildable too.");
            Assert.Greater(game.SpectralRibbonQuads, own.BuildTiles.Count);
            Assert.AreEqual(own.BuildTiles.Count, game.SpectralPlusQuads, "One hovering plus per build square of the guest's own room.");

            // Building removes the plus and dims the seam; selection brightens one square.
            int slot = Enumerable.Range(0, room.Slots.Length).First(i => game.CanBuildAt(room, i));
            room.Slots[slot] = new TowerInstance { Def = game.Cfg.towers.towers[0], Tile = own.BuildTiles[slot], SlotIndex = slot };
            int other = Enumerable.Range(0, room.Slots.Length).First(i => room.Slots[i] == null);
            game.SelectedBuildSlot = other;
            Update();
            var built = game.SpectralAt(own.BuildTiles[slot]);
            Assert.That(built.r, Is.InRange(1, 254)); Assert.AreEqual(0, built.g);
            Assert.AreEqual(255, game.SpectralAt(own.BuildTiles[other]).b);
            for (int i = 0; i < room.Slots.Length; i++)
                if (room.Slots[i] == null) Assert.AreEqual(game.CanBuildAt(room, i), game.SpectralAt(own.BuildTiles[i]).g == 255);
        }

        [Test] public void Guests_build_anywhere_except_the_doorway_while_some_path_reaches_the_bed()
        {
            var dirs = new[] { Vector2Int.up, Vector2Int.down, Vector2Int.left, Vector2Int.right };
            var own = game.Map.Rooms.First(def => dirs.Count(d => def.FloorSet.Contains(def.DoorInside + d)) >= 2 &&
                                                  dirs.All(d => !def.IsBedTile(def.DoorInside + d)));
            Assert.IsTrue(game.Claim(game.Human, own, true));
            var room = game.Human.Room;
            Assert.AreEqual(-1, own.BuildIndex(own.DoorInside), "The doorway square is never a build square.");
            Assert.IsTrue(own.Walkway.Where(c => c != own.DoorInside).All(c => own.BuildIndex(c) >= 0), "The default walkway is free to build on.");
            var exits = dirs.Select(d => own.DoorInside + d).Where(own.FloorSet.Contains).OrderBy(e => own.Walkway.Contains(e) ? 1 : 0).ToList();
            // Close every exit from the doorway square but one: the last must stay open.
            foreach (var exit in exits.Take(exits.Count - 1))
            {
                int slot = own.BuildIndex(exit);
                Assert.IsTrue(game.CanBuildAt(room, slot), "Exit " + exit + " is free while another remains");
                room.Slots[slot] = new TowerInstance { Def = game.Cfg.towers.towers[0], Tile = exit, SlotIndex = slot };
                Assert.IsFalse(game.MonsterWalkable(exit.x, exit.y), "Towers block the monster.");
            }
            Assert.IsFalse(game.CanBuildAt(room, own.BuildIndex(exits[exits.Count - 1])), "The last way out of the doorway square stays open.");
            Update();
            Assert.AreEqual(0, game.SpectralAt(exits[exits.Count - 1]).g, "No plus on a square that would cut off the bed.");
        }

        [Test] public void Other_guests_rooms_glow_without_build_invitations_and_dark_rooms_lose_their_energy()
        {
            var theirs = game.Map.Rooms[1];
            var guest = game.Residents.First(r => r != game.Human);
            Assert.IsTrue(game.Claim(guest, theirs, true));
            Update();
            Assert.IsTrue(theirs.BuildTiles.All(c => game.SpectralAt(c).r == 255));
            Assert.IsTrue(theirs.BuildTiles.All(c => game.SpectralAt(c).g == 0), "Only the local guest's room shows plus marks.");
            Assert.AreEqual(0, game.SpectralPlusQuads);
            guest.Alive = false;
            Update();
            Assert.IsTrue(theirs.BuildTiles.All(c => game.SpectralAt(c).r == 0), "A dead guest's room goes quiet.");
            Assert.AreEqual(0, game.SpectralRibbonQuads);
        }

        [Test] public void Floor_finish_is_owned_by_the_room_not_by_each_square()
        {
            var root = (Transform)typeof(GameManager).GetField("worldRoot", Private).GetValue(game);
            var floors = root.GetComponentsInChildren<MeshFilter>().Where(f => f.sharedMesh != null && f.name == "Hotel floors").ToArray();
            Assert.AreEqual(1, floors.Length, "All floors share one mesh and material.");
            var mesh = floors[0].sharedMesh; var vertices = mesh.vertices; var colors = mesh.colors;
            var byRoom = new Dictionary<RoomDef, HashSet<Color>>();
            for (int i = 0; i < vertices.Length; i += 4)
            {
                var center = (vertices[i] + vertices[i + 2]) * .5f;
                var cell = HotelMap.ToTile(center);
                if (game.Map.Get(cell.x, cell.y) != Tile.RoomFloor) continue;
                var def = game.Map.RoomContaining(cell);
                if (!byRoom.TryGetValue(def, out var set)) byRoom[def] = set = new HashSet<Color>();
                set.Add(colors[i]);
                Assert.AreEqual(colors[i], colors[i + 1]); Assert.AreEqual(colors[i], colors[i + 2]);
            }
            Assert.AreEqual(game.Map.Rooms.Count, byRoom.Count);
            foreach (var pair in byRoom) Assert.AreEqual(1, pair.Value.Count, "Room " + pair.Key.Index + " must use one finish and board direction.");
        }
    }
}
