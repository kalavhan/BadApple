using System.Collections.Generic;
using UnityEngine;

namespace BadAppleHotel.Game
{
    public enum WallDisplayMode { Up, Cutaway, Down }

    public partial class GameManager
    {
        sealed class WallState
        {
            public Rect Bounds;
            public float DefaultHeight, FromHeight, Height, FromFade = 1, Fade = 1, Started;
            public readonly List<int> Runs = new List<int>();
        }
        readonly List<WallState> wallStates = new List<WallState>();
        readonly List<Object> sceneAssets = new List<Object>();
        readonly Dictionary<Vector2Int, WallMeshBatch> wallBatches = new Dictionary<Vector2Int, WallMeshBatch>();
        Texture2D wallStateTexture;
        Color[] wallStatePixels;
        Material hotelWallMaterial;
        WallKit wallKit;
        WallInstances wallInstances;
        System.Func<int,float> wallHeightAt;
        readonly List<MeshRenderer> wallCoreViews = new List<MeshRenderer>();
        readonly Plane[] wallFrustum = new Plane[6];
        public int VisibleWallTriangles { get; private set; }
        public int ActualWallDrawCalls { get; private set; }
        float nextWallUpdate;
        int wallTextureWidth;
        public WallDisplayMode WallMode { get; private set; } = WallDisplayMode.Cutaway;
        public RoomDef WallFocusRoom { get; set; }
        public int WallBatchCount { get; private set; }
        public int WallTriangleCount { get; private set; }
        public readonly Dictionary<string,int> WallPieceCounts = new Dictionary<string,int>();

        public void SetWallMode(WallDisplayMode mode) { WallMode = mode; nextWallUpdate = 0; }

        void OnDestroy()
        {
            Camera.onPreCull -= DrawWallInstances;
            if (Instance == this) Instance = null;
            if (worldRoot != null) RemoveObject(worldRoot.gameObject);
            if (matchRoot != null) RemoveObject(matchRoot.gameObject);
            foreach (var asset in sceneAssets) if (asset != null) RemoveObject(asset);
            if (fogTex != null) RemoveObject(fogTex);
            if (worldSpriteMaterial != null) RemoveObject(worldSpriteMaterial);
        }
        Material Surface(Texture texture, Color tint)
        {
            var material = new Material(Resources.Load<Shader>("Shaders/HotelSurface"));
            material.mainTexture = texture; material.color = tint; sceneAssets.Add(material); return material;
        }
        MeshRenderer MeshObject(string name, Mesh mesh, Material material, Transform parent)
        {
            var go = new GameObject(name); go.transform.SetParent(parent, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var renderer = go.AddComponent<MeshRenderer>(); renderer.sharedMaterial = material;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false; return renderer;
        }
        static void Quad(List<Vector3> vertices, List<int> triangles, List<Vector2> uv, List<Color> colors,
            Vector3 a, Vector3 b, Vector3 c, Vector3 d, Color tint, float width=1, float height=1)
        {
            int i=vertices.Count; vertices.AddRange(new[]{a,b,c,d}); triangles.AddRange(new[]{i,i+1,i+2,i,i+2,i+3});
            uv.AddRange(new[]{Vector2.zero,new Vector2(width,0),new Vector2(width,height),new Vector2(0,height)});
            for(int j=0;j<4;j++) colors.Add(tint);
        }
        Mesh Mesh(List<Vector3> vertices, List<int> triangles, List<Vector2> uv, List<Color> colors)
        {
            var mesh=new Mesh { indexFormat=UnityEngine.Rendering.IndexFormat.UInt32 };
            mesh.SetVertices(vertices);mesh.SetTriangles(triangles,0);mesh.SetUVs(0,uv);mesh.SetColors(colors);mesh.RecalculateBounds();
            sceneAssets.Add(mesh);return mesh;
        }
        void BuildScene3D()
        {
            foreach(var asset in sceneAssets) if(asset!=null) RemoveObject(asset);
            sceneAssets.Clear(); wallStates.Clear(); wallBatches.Clear(); WallPieceCounts.Clear(); nextWallUpdate=0; WallFocusRoom=null;
            wallKit=WallKit.Load(); wallInstances=new WallInstances(); wallCoreViews.Clear();
            wallHeightAt=CurrentWallHeight;
            Camera.onPreCull -= DrawWallInstances; Camera.onPreCull += DrawWallInstances;
            BuildHotelFloors();
            var graph=Walls;
            wallTextureWidth=Mathf.NextPowerOfTwo(graph.Runs.Count+graph.Joins.Count+graph.Cores.Count+Map.Rooms.Count);
            wallStateTexture=new Texture2D(wallTextureWidth,2,TextureFormat.RGBAFloat,false,true) { name="Wall run states",filterMode=FilterMode.Point,wrapMode=TextureWrapMode.Clamp };
            sceneAssets.Add(wallStateTexture); wallStatePixels=new Color[wallTextureWidth*2];
            hotelWallMaterial=new Material(Resources.Load<Shader>("Shaders/HotelWall"));
            hotelWallMaterial.enableInstancing=wallInstances.UsesInstancing;
            hotelWallMaterial.mainTexture=wallKit?.Atlas;hotelWallMaterial.SetTexture("_WallStates",wallStateTexture);sceneAssets.Add(hotelWallMaterial);
            foreach(var run in graph.Runs)
            {
                AddWallState(run.Footprint,run.DefaultHeight);
                foreach(var rect in run.RenderFootprints) FillRun(rect,run.Normal,run.Id);
            }
            foreach(var join in graph.Joins)
            {
                Rect bounds=join.Footprints.Count>0?join.Footprints[0]:new Rect(join.Vertex,Vector2.zero);
                int state=AddWallState(bounds,join.DefaultHeight,join.IncidentRunIds);
                string id=join.Kind==WallJoinKind.InnerCorner?"wall_inner_corner":join.Kind==WallJoinKind.End?"wall_end_cap":"wall_corner";
                if(join.Kind==WallJoinKind.OuterCorner)
                    for(int dx=-1;dx<=0;dx++)for(int dy=-1;dy<=0;dy++)
                        if(Map.Get(join.Vertex.x+dx,join.Vertex.y+dy)==Tile.Door)id="wall_end_cap";
                var normal=join.IncidentRunIds.Count>0?graph.Runs[join.IncidentRunIds[0]].Normal:Vector2.down;
                foreach(var rect in join.Footprints) AddWallPiece(id,rect,normal,state);
            }
            foreach(var core in graph.Cores)
            {
                int state=AddWallState(core.Footprint,core.DefaultHeight,core.IncidentRunIds);
                Batch(core.Footprint).Box(core.Footprint,state,wallTextureWidth,new Color(.22f,.135f,.09f,0),.004f);
                // Core face strips stop at their own corner posts; even a three-sided pier has no overlapping caps.
                var normals=new HashSet<Vector2Int>();
                foreach(var edge in graph.Edges)if(edge.SolidCell==core.Cell)normals.Add(edge.Normal);
                foreach(var side in normals)
                {
                    var r=core.Footprint;float d=graph.Thickness;
                    if(side.y<0)r.height=d;else if(side.y>0)r.yMin=r.yMax-d;
                    else if(side.x<0)r.width=d;else r.xMin=r.xMax-d;
                    if(side.y!=0)
                    {
                        if(normals.Contains(Vector2Int.left))r.xMin+=d;
                        if(normals.Contains(Vector2Int.right))r.xMax-=d;
                    }
                    else
                    {
                        if(normals.Contains(Vector2Int.down))r.yMin+=d;
                        if(normals.Contains(Vector2Int.up))r.yMax-=d;
                    }
                    FillRun(r,side,state);
                }
                foreach(int dx in new[]{-1,1})foreach(int dy in new[]{-1,1})
                    if(normals.Contains(new Vector2Int(dx,0))&&normals.Contains(new Vector2Int(0,dy)))
                    {
                        float d=graph.Thickness;
                        var r=new Rect(dx<0?core.Footprint.xMin:core.Footprint.xMax-d,dy<0?core.Footprint.yMin:core.Footprint.yMax-d,d,d);
                        AddWallPiece("wall_corner",r,new Vector2(0,dy),state);
                    }
            }
            foreach(var def in Map.Rooms)
            {
                var inward=(Vector2)(def.DoorInside-def.DoorTile);
                var center=HotelMap.Center(def.DoorTile);
                bool horizontal=Mathf.Abs(inward.y)>.5f;
                var rect=horizontal?new Rect(center.x-.6f,center.y-graph.Thickness/2,1.2f,graph.Thickness):new Rect(center.x-graph.Thickness/2,center.y-.6f,graph.Thickness,1.2f);
                // Openings stay clear: frame tops are clipped in cutaway mode, posts never move inward.
                int state=AddWallState(rect,WallGraph.CutawayHeight);
                AddWallPiece("door_frame",rect,-inward,state,3);
                var door=MakeSprite("Door "+(def.Index+1),Sprites.DoorOpen,center,-2800,worldRoot);
                PoseDoor(door,def,true);doorSprites[def]=door;
            }
            WallBatchCount=wallInstances.MaxDrawCalls;WallTriangleCount=wallInstances.TotalTriangles;
            foreach(var batch in wallBatches)
            {
                if(batch.Value.VertexCount==0)continue;
                var mesh=batch.Value.Bake("Hotel walls "+batch.Key);sceneAssets.Add(mesh);
                wallCoreViews.Add(MeshObject(mesh.name,mesh,hotelWallMaterial,worldRoot));
                WallBatchCount++;WallTriangleCount+=(int)mesh.GetIndexCount(0)/3;
                // Only tiny core meshes are unique per map; release their CPU copy in the player.
                if(!Application.isEditor)mesh.UploadMeshData(true);
            }
            wallBatches.Clear();
            UpdateWallStateTexture(true);
        }
        void BuildHotelFloors()
        {
            foreach(bool hallway in new[]{false,true})
            {
                var v=new List<Vector3>();var t=new List<int>();var u=new List<Vector2>();var c=new List<Color>();
                for(int x=0;x<Map.W;x++)for(int y=0;y<Map.H;y++)
                {
                    var tile=Map.Get(x,y);
                    if(hallway?tile!=Tile.Corridor:(tile!=Tile.RoomFloor&&tile!=Tile.Door))continue;
                    float shade=.86f+HotelArt.Variant(x,y)*.035f;
                    Quad(v,t,u,c,new Vector3(x,y,.08f),new Vector3(x+1,y,.08f),new Vector3(x+1,y+1,.08f),new Vector3(x,y+1,.08f),Color.white*shade);
                }
                Material mat;
                if(hallway){mat=new Material(Resources.Load<Shader>("Shaders/HotelCarpet"));sceneAssets.Add(mat);}
                else mat=Surface(Resources.Load<Texture2D>("Art/floor_room"),Color.white);
                MeshObject(hallway?"Hallway hexagon carpet":"Room wood floors",Mesh(v,t,u,c),mat,worldRoot);
            }
            var rv=new List<Vector3>();var rt=new List<int>();var ru=new List<Vector2>();var rc=new List<Color>();
            foreach(var room in Map.Rooms)
            {
                var turn=Quaternion.Euler(0,0,room.BedRotation);
                var center=new Vector3(room.BedCenter.x,room.BedCenter.y,.06f);
                // Fits within the bed's reserved two squares, including irregular room corners.
                Quad(rv,rt,ru,rc,center+turn*new Vector3(-.47f,-.94f),center+turn*new Vector3(.47f,-.94f),center+turn*new Vector3(.47f,.94f),center+turn*new Vector3(-.47f,.94f),Color.white);
            }
            var rug=new Material(Resources.Load<Shader>("Shaders/HotelCarpet"));rug.SetFloat("_Rug",1);sceneAssets.Add(rug);
            MeshObject("Bed rugs",Mesh(rv,rt,ru,rc),rug,worldRoot);
        }
        int AddWallState(Rect bounds,float height,IEnumerable<int> runs=null)
        {
            var state=new WallState { Bounds=bounds,DefaultHeight=height,Height=height,FromHeight=height,Started=Time.unscaledTime-.15f };
            if(runs!=null)state.Runs.AddRange(runs);
            wallStates.Add(state);return wallStates.Count-1;
        }
        WallMeshBatch Batch(Rect rect)
        {
            var key=new Vector2Int(Mathf.FloorToInt(rect.center.x/32),Mathf.FloorToInt(rect.center.y/32));
            if(!wallBatches.TryGetValue(key,out var batch)){batch=new WallMeshBatch();wallBatches.Add(key,batch);}return batch;
        }
        void AddWallPiece(string id,Rect rect,Vector2 normal,int state,int mode=0)
        {
            var piece=wallKit?.Get(id);
            if(piece?.Mesh==null){Batch(rect).Box(rect,state,wallTextureWidth,new Color(.30f,.13f,.10f,0));return;}
            WallPieceCounts[id]=WallPieceCounts.TryGetValue(id,out int count)?count+1:1;
            wallInstances.Add(piece,rect,normal,state,wallTextureWidth,mode);
        }
        static uint WallHash(int x,int y) => unchecked((uint)(x*73856093^y*19349663));
        void FillRun(Rect rect,Vector2 normal,int state)
        {
            if(wallKit==null){Batch(rect).Box(rect,state,wallTextureWidth,new Color(.30f,.13f,.10f,0));return;}
            bool horizontal=Mathf.Abs(normal.y)>.5f;
            float start=horizontal?rect.xMin:rect.yMin,end=horizontal?rect.xMax:rect.yMax;
            for(float at=start;at<end-.001f;)
            {
                float next=Mathf.Min(end,Mathf.Floor(at+.001f)+1);var slice=rect;
                if(horizontal){slice.xMin=at;slice.xMax=next;}else{slice.yMin=at;slice.yMax=next;}
                var floor=HotelMap.ToTile(slice.center+normal*(Walls.Thickness/2+.1f));
                // Five-tile bands with a stable one-tile jitter give four-to-six-tile lamp spacing.
                int coordinate=Mathf.FloorToInt(at),band=Mathf.FloorToInt(coordinate/5f);
                int line=Mathf.RoundToInt(horizontal?rect.yMin:rect.xMin);
                int lampAt=band*5+2+(int)(WallHash(band,line)%2);
                bool lamp=Map.Get(floor.x,floor.y)==Tile.Corridor&&coordinate==lampAt&&next-at>.8f;
                AddWallPiece(lamp?"wall_lamp":"wall_straight",slice,normal,state,1);
                AddWallPiece("wall_cutaway_cap",slice,normal,state,2);
                at=next;
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

        float CurrentWallHeight(int index)
        {
            var state=wallStates[index];
            float t=Mathf.SmoothStep(0,1,(Time.unscaledTime-state.Started)/.15f);
            return Mathf.Lerp(state.FromHeight,state.Height,t);
        }
        void DrawWallInstances(Camera camera)
        {
            if(camera!=Cam||wallInstances==null||hotelWallMaterial==null)return;
            wallInstances.Draw(camera,hotelWallMaterial,Time.unscaledTime,wallHeightAt);
            ActualWallDrawCalls=wallInstances.LastDrawCalls;VisibleWallTriangles=wallInstances.SubmittedTriangles;
            GeometryUtility.CalculateFrustumPlanes(camera,wallFrustum);
            foreach(var renderer in wallCoreViews)
                if(renderer!=null&&GeometryUtility.TestPlanesAABB(wallFrustum,renderer.bounds))
                {
                    ActualWallDrawCalls++;
                    VisibleWallTriangles+=(int)renderer.GetComponent<MeshFilter>().sharedMesh.GetIndexCount(0)/3;
                }
        }

        static float DistanceToRect(Vector2 p,Rect rect) => Vector2.Distance(p,new Vector2(Mathf.Clamp(p.x,rect.xMin,rect.xMax),Mathf.Clamp(p.y,rect.yMin,rect.yMax)));
        void UpdateWallOcclusion()
        {
            if(Cam==null||wallStateTexture==null)return;
            hotelWallMaterial.SetFloat("_WallClock",Time.unscaledTime);
            if(Time.unscaledTime<nextWallUpdate)return;
            nextWallUpdate=Time.unscaledTime+.1f;
            UpdateWallStateTexture(false);
        }
        void UpdateWallStateTexture(bool immediate)
        {
            Vector2 player=ViewOrigin;
            bool hallway=InMatch&&Map.Get(HotelMap.ToTile(player).x,HotelMap.ToTile(player).y)==Tile.Corridor;
            RoomDef occupied=null;
            foreach(var room in Map.Rooms)if(room.ContainsInterior(HotelMap.ToTile(player))){occupied=room;break;}
            bool changed=immediate;
            for(int i=0;i<wallStates.Count;i++)
            {
                var state=wallStates[i];
                float height=WallMode==WallDisplayMode.Up?WallGraph.FullHeight:WallMode==WallDisplayMode.Down?WallGraph.DownHeight:state.DefaultHeight;
                float fade=1;
                if(WallMode==WallDisplayMode.Cutaway)
                {
                    if((occupied!=null&&WallVisibility.CoversAny(state.Bounds,height,occupied.Floor))||
                       (WallFocusRoom!=null&&WallVisibility.CoversAny(state.Bounds,height,WallFocusRoom.Floor)))height=Mathf.Min(height,WallGraph.CutawayHeight);
                    var delta=HotelView3D.Facing(state.Bounds.center)-HotelView3D.Facing(player);
                    if(hallway&&DistanceToRect(player,state.Bounds)<4&&WallVisibility.CoversGround(state.Bounds,1.9f,player)&&delta.y<1)
                    { height=Mathf.Min(height,WallGraph.CutawayHeight);fade=.22f; }
                }
                foreach(int run in state.Runs){height=Mathf.Min(height,wallStates[run].Height);fade=Mathf.Min(fade,wallStates[run].Fade);}
                if(immediate){state.FromHeight=state.Height=height;state.FromFade=state.Fade=fade;state.Started=Time.unscaledTime-.15f;}
                else if(Mathf.Abs(state.Height-height)>.001f||Mathf.Abs(state.Fade-fade)>.001f)
                {
                    float t=Mathf.SmoothStep(0,1,(Time.unscaledTime-state.Started)/.15f);
                    state.FromHeight=Mathf.Lerp(state.FromHeight,state.Height,t);state.FromFade=Mathf.Lerp(state.FromFade,state.Fade,t);
                    state.Height=height;state.Fade=fade;state.Started=Time.unscaledTime;changed=true;
                }
                wallStatePixels[i]=new Color(state.FromHeight,state.Height,state.FromFade,state.Fade);
                wallStatePixels[wallTextureWidth+i]=new Color(state.Started,0,0,0);
            }
            if(changed){wallStateTexture.SetPixels(wallStatePixels);wallStateTexture.Apply(false);}
            hotelWallMaterial.SetFloat("_WallClock",Time.unscaledTime);
        }
    }
}
