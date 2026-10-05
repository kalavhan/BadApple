using BadAppleHotel.Config;
using BadAppleHotel.Game;
using NUnit.Framework;
using UnityEngine;

namespace BadAppleHotel.Tests
{
    public class HotelLightingTests
    {
        HotelMap map;
        [SetUp] public void EmptyFloorPatch()
        {
            map = new HotelMap(ConfigLoader.Load().map, 10, 31);
            for (int x = 0; x < map.W; x++) for (int y = 0; y < map.H; y++) map.Tiles[x,y] = Tile.Void;
            for (int x = 2; x < 17; x++) for (int y = 2; y < 17; y++) map.Tiles[x,y] = Tile.Corridor;
        }

        [Test] public void Lamp_pool_falls_off_and_does_not_light_behind_its_mounting_face()
        {
            using (var lighting = HotelLighting.Build(map, new[] { new HotelLighting.Lamp(new Vector2(5,8), Vector2.right, 6) }))
            {
                Assert.AreEqual(1, lighting.LampCount);
                Assert.Greater(lighting.Sample(new Vector2(6,8)), .7f);
                Assert.Greater(lighting.Sample(new Vector2(6,8)), lighting.Sample(new Vector2(9,8)) * 2);
                Assert.AreEqual(0, lighting.Sample(new Vector2(4,8)), .0001f);
                Assert.AreEqual(0, lighting.Sample(new Vector2(12,8)), .0001f);
                Assert.AreEqual(0, lighting.Sample(new Vector2(-1,8)));
                Assert.AreEqual(map.W * 4, lighting.LightMap.width);
                Assert.AreEqual(1, lighting.LightMap.mipmapCount);
                Assert.IsTrue(lighting.LightMap.isReadable);
                Assert.Less(HotelLighting.WarmColor.b, HotelLighting.WarmColor.g);
                Assert.Less(HotelLighting.WarmColor.g, HotelLighting.WarmColor.r);
            }
        }

        [TestCase(Tile.Wall)] [TestCase(Tile.Door)]
        public void Solid_walls_and_closed_doors_stop_illumination_even_within_the_lamp_radius(Tile blocker)
        {
            for (int y = 2; y < 17; y++) map.Tiles[8,y] = blocker;
            using (var lighting = HotelLighting.Build(map, new[] { new HotelLighting.Lamp(new Vector2(5,8), Vector2.right, 8) }))
            {
                Assert.Greater(lighting.Sample(new Vector2(7,8)), .5f);
                Assert.AreEqual(0, lighting.Sample(new Vector2(9,8)), .0001f);
                Assert.AreEqual(0, lighting.Sample(new Vector2(8.5f,8.5f)), .0001f);
            }
        }

        [Test] public void Touching_diagonal_wall_corners_do_not_leak_light()
        {
            map.Tiles[6,5] = Tile.Wall; map.Tiles[5,6] = Tile.Wall;
            using (var lighting = HotelLighting.Build(map, new[] { new HotelLighting.Lamp(new Vector2(5.5f,5.5f), Vector2.one, 6) }))
                Assert.AreEqual(0, lighting.Sample(new Vector2(6.5f,6.5f)), .0001f);
        }

        [Test] public void Nearby_room_lamps_can_light_both_sides_without_crossing_the_partition()
        {
            for (int y = 2; y < 17; y++) map.Tiles[8,y] = Tile.Wall;
            var lamps = new[] { new HotelLighting.Lamp(new Vector2(8,8), Vector2.left), new HotelLighting.Lamp(new Vector2(9,8), Vector2.right) };
            using (var lighting = HotelLighting.Build(map, lamps))
            {
                Assert.AreEqual(2, lighting.LampCount);
                Assert.Greater(lighting.Sample(new Vector2(7,8)), .7f);
                Assert.Greater(lighting.Sample(new Vector2(10,8)), .7f);
                Assert.AreEqual(0, lighting.Sample(new Vector2(8.5f,8.5f)), .0001f);
            }
        }

        [Test] public void Disposing_old_or_already_destroyed_maps_does_not_leave_stale_global_lighting()
        {
            var first = HotelLighting.Build(map, null); var second = HotelLighting.Build(map, null);
            try
            {
                first.Bind(); second.Bind(); first.Dispose(); first.Dispose();
                Assert.AreEqual(second.LightMap, Shader.GetGlobalTexture("_HotelLightMap"));
                Assert.AreEqual(1, Shader.GetGlobalFloat("_HotelLightingEnabled"));
                Object.DestroyImmediate(second.LightMap); second.Dispose(); second.Dispose();
                Assert.AreEqual(0, Shader.GetGlobalFloat("_HotelLightingEnabled"));
                Assert.IsNull(second.LightMap);
            }
            finally { first.Dispose(); second.Dispose(); }
        }
    }
}
