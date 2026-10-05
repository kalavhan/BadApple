using System.Collections.Generic;
using UnityEngine;

namespace BadAppleHotel.Game
{
    public partial class GameManager
    {
        class WallView { public Vector2Int Tile; public Vector2 Center; public MeshRenderer Renderer; public bool Faded; public float Height; }
        readonly List<WallView> wallViews = new List<WallView>();
        readonly List<Object> sceneAssets = new List<Object>();
        MaterialPropertyBlock wallProperties;
        float nextWallUpdate;

        void OnDestroy()
        {
            if (Instance == this) Instance = null;
            if (worldRoot != null) RemoveObject(worldRoot.gameObject);
            if (matchRoot != null) RemoveObject(matchRoot.gameObject);
            foreach (var asset in sceneAssets) if (asset != null) RemoveObject(asset);
            if (fogTex != null) RemoveObject(fogTex);
        }

        Material Surface(Texture texture, Color tint)
        {
            var material = new Material(Resources.Load<Shader>("Shaders/HotelSurface"));
            material.mainTexture = texture; material.color = tint;
            sceneAssets.Add(material);
            return material;
        }
        MeshRenderer MeshObject(string name, Mesh mesh, Material material, Transform parent)
        {
            var go = new GameObject(name); go.transform.SetParent(parent, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var renderer = go.AddComponent<MeshRenderer>(); renderer.sharedMaterial = material;
            return renderer;
        }
        static void Quad(List<Vector3> vertices, List<int> triangles, List<Vector2> uv, List<Color> colors,
            Vector3 a, Vector3 b, Vector3 c, Vector3 d, Color tint, float width=1, float height=1)
        {
            int i=vertices.Count; vertices.AddRange(new[]{a,b,c,d});
            triangles.AddRange(new[]{i,i+1,i+2,i,i+2,i+3});
            uv.AddRange(new[]{Vector2.zero,new Vector2(width,0),new Vector2(width,height),new Vector2(0,height)});
            for(int j=0;j<4;j++)colors.Add(tint);
        }
        Mesh Mesh(List<Vector3> vertices, List<int> triangles, List<Vector2> uv, List<Color> colors)
        {
            var mesh=new Mesh(); mesh.SetVertices(vertices);mesh.SetTriangles(triangles,0);mesh.SetUVs(0,uv);mesh.SetColors(colors);mesh.RecalculateBounds();
            sceneAssets.Add(mesh);return mesh;
        }
        void BuildScene3D()
        {
            wallProperties = new MaterialPropertyBlock();
            foreach(var asset in sceneAssets) if(asset!=null)RemoveObject(asset);
            sceneAssets.Clear(); wallViews.Clear(); nextWallUpdate=0;
            var wallTexture=Resources.Load<Texture2D>("Art/Materials/haunted_wall");
            var wallMaterial=Surface(wallTexture,Color.white);
            foreach(bool hallway in new[]{false,true})
            {
                var vertices=new List<Vector3>();var triangles=new List<int>();var uv=new List<Vector2>();var colors=new List<Color>();
                for(int x=0;x<Map.W;x++)for(int y=0;y<Map.H;y++)
                {
                    var tile=Map.Get(x,y);
                    if(hallway ? tile!=Tile.Corridor : (tile!=Tile.RoomFloor&&tile!=Tile.Door))continue;
                    float shade=0.78f+HotelArt.Variant(x,y)*0.055f;
                    Quad(vertices,triangles,uv,colors,new Vector3(x,y,0.08f),new Vector3(x+1,y,0.08f),new Vector3(x+1,y+1,0.08f),new Vector3(x,y+1,0.08f),Color.white*shade);
                }
                var texture=Resources.Load<Texture2D>(hallway?"Art/floor_corridor":"Art/floor_room");
                if(texture!=null)texture.filterMode=FilterMode.Point;
                MeshObject(hallway?"3D corridor floor":"3D room floor",Mesh(vertices,triangles,uv,colors),Surface(texture,Color.white),worldRoot);
            }
            // One shared cube mesh; wall height varies without allocating individual geometry.
            var v=new List<Vector3>();var t=new List<int>();var u=new List<Vector2>();var c=new List<Color>();
            Quad(v,t,u,c,new Vector3(0,0,0),new Vector3(1,0,0),new Vector3(1,0,-1),new Vector3(0,0,-1),Color.white,0.5f,1);
            Quad(v,t,u,c,new Vector3(1,1,0),new Vector3(0,1,0),new Vector3(0,1,-1),new Vector3(1,1,-1),Color.white*0.75f,0.5f,1);
            Quad(v,t,u,c,new Vector3(0,1,0),new Vector3(0,0,0),new Vector3(0,0,-1),new Vector3(0,1,-1),Color.white*0.68f,0.5f,1);
            Quad(v,t,u,c,new Vector3(1,0,0),new Vector3(1,1,0),new Vector3(1,1,-1),new Vector3(1,0,-1),Color.white*0.85f,0.5f,1);
            Quad(v,t,u,c,new Vector3(0,0,-1),new Vector3(1,0,-1),new Vector3(1,1,-1),new Vector3(0,1,-1),new Color(0.44f,0.37f,0.48f),0.1f,0.1f);
            var cube=Mesh(v,t,u,c);
            const float thickness = 0.14f;
            var sides = new[]{Vector2Int.down,Vector2Int.left,Vector2Int.up,Vector2Int.right};
            for(int x=0;x<Map.W;x++)for(int y=0;y<Map.H;y++)
            {
                if(Map.Get(x,y)!=Tile.Wall)continue;
                foreach(var side in sides)
                {
                    var adjacent=Map.Get(x+side.x,y+side.y);
                    if(adjacent!=Tile.RoomFloor&&adjacent!=Tile.Corridor&&adjacent!=Tile.Door)continue;
                    bool farSide=side==Vector2Int.down||side==Vector2Int.left;
                    float height=farSide?(adjacent==Tile.RoomFloor?1.7f:1.35f):0.22f;
                    // A partition hugs the walkable floor edge instead of occupying a whole wall cell.
                    // Short end caps join perpendicular segments neatly at corners.
                    Vector2 origin;Vector2 size;
                    if(side.y!=0)
                    {
                        origin=new Vector2(x-thickness,side.y<0?y:y+1-thickness);
                        size=new Vector2(1+2*thickness,thickness);
                    }
                    else
                    {
                        origin=new Vector2(side.x<0?x:x+1-thickness,y-thickness);
                        size=new Vector2(thickness,1+2*thickness);
                    }
                    var renderer=MeshObject(adjacent==Tile.RoomFloor?"Room partition":"Hall partition",cube,wallMaterial,worldRoot);
                    renderer.transform.position=new Vector3(origin.x,origin.y,0.06f);
                    renderer.transform.localScale=new Vector3(size.x,size.y,height);
                    wallViews.Add(new WallView { Tile=new Vector2Int(x,y),Center=origin+size/2,Renderer=renderer,Height=height });
                }
            }
            foreach(var def in Map.Rooms)
            {
                var door=MakeSprite("Door "+(def.Index+1),Sprites.DoorOpen,HotelMap.Center(def.DoorTile),-2800,worldRoot);
                PoseDoor(door,def,true);
                doorSprites[def]=door;
            }
        }

        void PoseDoor(SpriteRenderer door,RoomDef def,bool open)
        {
            if(Simulation)return;
            var inward=(Vector3)(Vector2)(def.DoorInside-def.DoorTile);
            var rotation=Quaternion.LookRotation(inward,Vector3.back);
            var pos=HotelMap.Center(def.DoorTile);
            door.transform.SetPositionAndRotation(new Vector3(pos.x,pos.y,open?-0.12f:-0.72f),rotation);
            var bounds=door.sprite.bounds.size;
            door.transform.localScale=new Vector3(0.94f/bounds.x,(open?0.22f:1.4f)/bounds.y,1);
            door.sortingOrder=5000-Mathf.RoundToInt((pos.x+pos.y)*10);
        }

        void UpdateWallOcclusion()
        {
            if(Cam==null||Time.unscaledTime<nextWallUpdate)return;
            nextWallUpdate=Time.unscaledTime+0.08f;
            Vector2 player=HumanRole==Role.Monster&&Monster!=null?Monster.Pos:Human!=null?Human.Pos:Vector2.zero;
            bool walking=InMatch&&(HumanRole==Role.Monster&&Monster!=null||Human!=null&&Human.Alive&&!Human.Asleep);
            bool hallway=walking&&Map.Get(Mathf.FloorToInt(player.x),Mathf.FloorToInt(player.y))==Tile.Corridor;
            Vector2 projected=HotelView3D.Facing(player);
            foreach(var wall in wallViews)
            {
                Vector2 delta=HotelView3D.Facing(wall.Center)-projected;
                // Only nearby walls in the player's view column dissolve; remote walls remain cover.
                bool fade=hallway&&wall.Height>0.3f&&Mathf.Abs(delta.x)<1.1f&&delta.y<0.5f&&delta.y>-wall.Height*0.65f-0.6f&&
                    Vector2.Distance(player,wall.Center)<3f;
                if(fade==wall.Faded)continue;
                wall.Faded=fade; wallProperties.SetFloat("_Fade",fade?0.18f:1f);wall.Renderer.SetPropertyBlock(wallProperties);
            }
        }
    }
}
