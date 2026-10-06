using System.Collections.Generic;
using System.Linq;
using BadAppleHotel.Config;
using BadAppleHotel.Game;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Rendering;

namespace BadAppleHotel.Tests
{
    public class WallClosureTests
    {
        [Test]
        public void Central_room_has_one_thin_perimeter_four_corners_and_one_door_opening()
        {
            var map=new HotelMap(ConfigLoader.Load().map,10,1);
            map.Rooms.Clear();
            for(int x=0;x<map.W;x++)for(int y=0;y<map.H;y++)map.Tiles[x,y]=Tile.Void;
            for(int x=6;x<20;x++)for(int y=6;y<19;y++)map.Tiles[x,y]=Tile.Corridor;
            for(int x=9;x<17;x++)for(int y=9;y<16;y++)map.Tiles[x,y]=Tile.Wall;
            var room=new RoomDef{Index=0,IsCentral=true,DoorTile=new Vector2Int(12,9),DoorInside=new Vector2Int(12,10),DoorOutside=new Vector2Int(12,8)};
            for(int x=10;x<16;x++)for(int y=10;y<15;y++)
            {var p=new Vector2Int(x,y);map.Tiles[x,y]=Tile.RoomFloor;room.Floor.Add(p);room.FloorSet.Add(p);}
            map.Rooms.Add(room);map.Tiles[12,9]=Tile.Door;
            var graph=WallGraph.Build(map);
            var perimeter=graph.Perimeter;
            var spans=perimeter.Spans.Where(p=>p.Runs.Any(id=>WallVisibility.BordersRoom(graph,graph.Runs[id],room))).ToArray();
            var corners=perimeter.Corners.Where(p=>p.Runs.Any(id=>WallVisibility.BordersRoom(graph,graph.Runs[id],room))).ToArray();
            Assert.AreEqual(5,spans.Length,"Four sides, with the south side split once by the door.");
            Assert.AreEqual(4,corners.Count(p=>p.Piece!="wall_end_cap"),"Exactly one post at each corner, with no outer ring.");
            Assert.AreEqual(2,corners.Count(p=>p.Piece=="wall_end_cap"),"Only the doorway ends the perimeter.");
            var physical=spans.Concat(corners).ToArray();
            foreach(var part in physical)
            {
                Assert.AreEqual(.3f,Mathf.Min(part.Bounds.width,part.Bounds.height),.0001f);
                Assert.IsFalse(part.Bounds.Overlaps(new Rect(12.16f,9.2f,.68f,.6f)),"Door clearance");
            }
            foreach(var corner in new[]{new Vector2(9.5f,9.5f),new Vector2(16.5f,9.5f),new Vector2(9.5f,15.5f),new Vector2(16.5f,15.5f)})
                Assert.AreEqual(1,corners.Count(p=>Vector2.Distance(p.Bounds.center,corner)<.001f));
            // A ray from every room edge crosses exactly one wall, never an inner and an outer shell.
            foreach(float x in new[]{10.5f,11.5f,13.5f,14.5f,15.5f})
            {
                Assert.AreEqual(1,physical.Count(p=>p.Bounds.Contains(new Vector2(x,9.5f))));
                Assert.AreEqual(1,physical.Count(p=>p.Bounds.Contains(new Vector2(x,15.5f))));
            }
        }

        [Test]
        public void Physical_wall_parts_are_unique_thin_and_do_not_overlap_in_a_hundred_hotels()
        {
            var config=ConfigLoader.Load();
            for(int seed=1;seed<=100;seed++)
            {
                var graph=WallGraph.Build(new HotelMap(config.map,10,seed));
                var parts=graph.Perimeter.Spans.Concat(graph.Perimeter.Corners).ToArray();
                var cells=new Dictionary<Vector2Int,List<Rect>>();
                foreach(var part in parts)
                {
                    var a=part.Bounds;
                    Assert.AreEqual(.3f,part.Normal.y!=0?a.height:a.width,.0001f,"Thick separator, seed "+seed);
                    for(int x=Mathf.FloorToInt(a.xMin);x<=Mathf.FloorToInt(a.xMax);x++)
                        for(int y=Mathf.FloorToInt(a.yMin);y<=Mathf.FloorToInt(a.yMax);y++)
                        {
                            var key=new Vector2Int(x,y);
                            if(!cells.TryGetValue(key,out var others))cells.Add(key,others=new List<Rect>());
                            foreach(var b in others)
                                Assert.IsFalse(Mathf.Min(a.xMax,b.xMax)-Mathf.Max(a.xMin,b.xMin)>.0001f &&
                                    Mathf.Min(a.yMax,b.yMax)-Mathf.Max(a.yMin,b.yMin)>.0001f,"Duplicate/crossing wall parts, seed "+seed+" "+a+" "+b);
                            others.Add(a);
                        }
                }
            }
        }

        [Test]
        public void Corridors_have_three_tile_clearance_and_no_disconnected_pockets_across_a_hundred_hotels()
        {
            var config=ConfigLoader.Load();
            for(int seed=1;seed<=100;seed++)
            {
                var map=new HotelMap(config.map,10,seed);var floor=new HashSet<Vector2Int>(map.CorridorTiles());
                var roomy=new HashSet<Vector2Int>();
                foreach(var p in floor)
                {
                    bool full=true;
                    for(int dx=0;dx<3;dx++)for(int dy=0;dy<3;dy++)full&=floor.Contains(p+new Vector2Int(dx,dy));
                    if(full)for(int dx=0;dx<3;dx++)for(int dy=0;dy<3;dy++)roomy.Add(p+new Vector2Int(dx,dy));
                }
                Assert.IsTrue(floor.SetEquals(roomy),"Narrow hook or pocket, seed "+seed);
                var visited=new HashSet<Vector2Int>{map.Lobby};var queue=new Queue<Vector2Int>();queue.Enqueue(map.Lobby);
                while(queue.Count>0)
                {
                    var p=queue.Dequeue();
                    foreach(var d in new[]{Vector2Int.left,Vector2Int.right,Vector2Int.up,Vector2Int.down})
                        if(floor.Contains(p+d)&&visited.Add(p+d))queue.Enqueue(p+d);
                }
                Assert.IsTrue(floor.SetEquals(visited),"Disconnected hallway, seed "+seed);
                Assert.IsTrue(map.Rooms.All(room=>visited.Contains(room.DoorOutside)));
            }
        }

        [Test]
        public void Peek_shader_retains_a_solid_low_base_without_removing_the_neighbour_wall()
        {
            if(SystemInfo.graphicsDeviceType==GraphicsDeviceType.Null)Assert.Ignore("Requires a graphics device.");
            var shader=Resources.Load<Shader>("Shaders/HotelWall");Assert.IsNotNull(shader);
            var batch=new WallMeshBatch();var left=new Rect(0,0,1,1);var right=new Rect(2,0,1,1);
            batch.Box(left,0,2,Color.white);batch.Box(right,0,2,Color.white);batch.Box(left,0,2,Color.white,mode:5);
            var mesh=batch.Bake("Peek base GPU fixture");var root=new GameObject("Peek base");root.layer=31;
            var camera=new GameObject("Peek base camera").AddComponent<Camera>();
            var material=new Material(shader);var states=new Texture2D(2,2,TextureFormat.RGBAFloat,false,true);
            var target=new RenderTexture(384,256,24);var read=new Texture2D(384,256,TextureFormat.RGB24,false,true);
            var previous=RenderTexture.active;
            try
            {
                root.AddComponent<MeshFilter>().sharedMesh=mesh;root.AddComponent<MeshRenderer>().sharedMaterial=material;
                camera.cullingMask=1<<31;camera.orthographic=true;camera.orthographicSize=1.3f;
                camera.transform.position=new Vector3(1.5f,-5,-.65f);camera.transform.rotation=Quaternion.Euler(-90,0,0);
                camera.clearFlags=CameraClearFlags.SolidColor;camera.backgroundColor=Color.black;camera.targetTexture=target;
                states.SetPixels(new[]{new Color(1.7f,1.7f,1,1),new Color(1.7f,1.7f,1,1),Color.clear,Color.clear});states.Apply();
                material.mainTexture=Texture2D.whiteTexture;material.SetTexture("_WallStates",states);material.SetFloat("_WallClock",1);
                material.SetFloat("_HotelLightingEnabled",0);material.SetFloat("_HotelFog",0);material.SetVector("_HotelSize",new Vector4(4,4,0,0));
                material.SetVector("_WallPeekBounds",new Vector4(0,0,1,1));
                void Render(bool peek)
                {
                    material.SetFloat("_WallPeekEnabled",peek?1:0);camera.Render();RenderTexture.active=target;
                    read.ReadPixels(new Rect(0,0,384,256),0,0);read.Apply();
                }
                float Pixel(float x,float height)
                {
                    var p=camera.WorldToScreenPoint(new Vector3(x,.1f,-height));
                    return read.GetPixel(Mathf.RoundToInt(p.x),Mathf.RoundToInt(p.y)).r;
                }
                Render(false);Assert.Greater(Pixel(.5f,.8f),.2f,"Control wall is visible.");
                Render(true);Assert.Less(Pixel(.5f,.8f),.01f,"Only the upper peek section is removed.");
                Assert.Greater(Pixel(.5f,.02f),.2f,"A solid low base remains in the aperture.");
                Assert.Greater(Pixel(2.5f,.8f),.2f,"Neighbour wall stays full height.");
            }
            finally
            {
                RenderTexture.active=previous;camera.targetTexture=null;
                Object.DestroyImmediate(root);Object.DestroyImmediate(camera.gameObject);Object.DestroyImmediate(mesh);
                Object.DestroyImmediate(material);Object.DestroyImmediate(states);Object.DestroyImmediate(read);Object.DestroyImmediate(target);
            }
        }
    }
}
