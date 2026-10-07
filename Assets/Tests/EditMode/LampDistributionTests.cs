using System.Collections.Generic;
using System.Reflection;
using BadAppleHotel.Config;
using BadAppleHotel.Game;
using NUnit.Framework;
using UnityEngine;

namespace BadAppleHotel.Tests
{
    public class LampDistributionTests
    {
        const BindingFlags Private=BindingFlags.Instance|BindingFlags.NonPublic;
        [TestCase(11)] [TestCase(41)] [TestCase(83)]
        public void Spaced_sconces_cover_each_room_and_corridors_with_useful_light(int seed)
        {
            var random=Random.state;float time=Time.timeScale;var art=Sprites.ArtOverride;
            var game=new GameObject("Lamp coverage "+seed).AddComponent<GameManager>();
            try
            {
                game.StartSimulation(ConfigLoader.Load(),seed,false);
                typeof(GameManager).GetMethod("BuildScene3D",Private).Invoke(game,null);
                AssertCoverage(game);
            }
            finally{game.DisposeSimulation();Random.state=random;Time.timeScale=time;Sprites.ArtOverride=art;}
        }
        public static void AssertCoverage(GameManager game)
        {
            var lamps=(List<HotelLighting.Lamp>)typeof(GameManager).GetField("wallLamps",Private).GetValue(game);
            var light=(HotelLighting)typeof(GameManager).GetField("hotelLighting",Private).GetValue(game);
            Assert.AreEqual(lamps.Count,light.LampCount,"Every visible sconce must actually cast light.");
            for(int i=0;i<lamps.Count;i++)for(int j=i+1;j<lamps.Count;j++)
                Assert.GreaterOrEqual(Vector2.Distance(lamps[i].Position,lamps[j].Position),HotelLampPlan.MinimumSpacing-.0001f,"lamps "+i+","+j+" seed "+game.Map.Seed);
            float minRoom=1;
            foreach(var room in game.Map.Rooms)
            {
                float coverage=Coverage(light,room.Floor);minRoom=Mathf.Min(minRoom,coverage);
                if(coverage<.98f)
                {
                    Debug.Log("DARK ROOM "+room.Index+" FLOOR "+string.Join(";",room.Floor));
                    Debug.Log("DOOR "+room.DoorTile+" INSIDE "+room.DoorInside);
                    var instances=(WallInstances)typeof(GameManager).GetField("wallInstances",Private).GetValue(game);
                    var stays=typeof(GameManager).GetMethod("RoomLampStaysUp",Private);
                    foreach(var record in instances.Records)
                        if(record.Mode==1&&room.ContainsInterior(HotelMap.ToTile(record.Footprint.center+record.Normal*.55f)))
                            Debug.Log("MOUNT "+record.Footprint+" normal "+record.Normal+" stays "+stays.Invoke(game,new object[]{record.StateId,room}));
                    foreach(var lamp in lamps)if(room.ContainsInterior(HotelMap.ToTile(lamp.Position+lamp.Normal*.4f)))
                        Debug.Log("ROOM LAMP "+lamp.Position+" normal "+lamp.Normal+" radius "+lamp.Radius);
                    foreach(var tile in room.Floor)if(light.Sample(HotelMap.Center(tile))<.25f)
                        Debug.Log("DIM TILE "+tile+" light "+light.Sample(HotelMap.Center(tile)));
                }
                Assert.GreaterOrEqual(coverage,.98f,"Room "+room.Index+" seed "+game.Map.Seed);
            }
            float corridor=Coverage(light,game.Map.CorridorTiles());
            // Hallways are a little dimmer than rooms, but most of the floor stays lamp-lit.
            Assert.GreaterOrEqual(corridor,.78f,"Corridor coverage seed "+game.Map.Seed);
            Debug.Log("LAMP COVERAGE seed="+game.Map.Seed+" lamps="+lamps.Count+" roomMin="+minRoom+" corridor="+corridor);
        }
        static float Coverage(HotelLighting light,IReadOnlyList<Vector2Int> tiles)
        {
            int covered=0,total=0;
            foreach(var tile in tiles)foreach(float x in new[]{.25f,.75f})foreach(float y in new[]{.25f,.75f})
            {total++;if(light.Sample((Vector2)tile+new Vector2(x,y))>=.25f)covered++;}
            return (float)covered/total;
        }
    }
}
