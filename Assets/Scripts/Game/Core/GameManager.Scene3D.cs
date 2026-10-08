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
            public RoomDef DoorRoom;
            public Vector2 DoorInward;
            public bool RoomBoundary;
        }
        readonly List<WallState> wallStates = new List<WallState>();
        readonly List<Object> sceneAssets = new List<Object>();
        readonly Dictionary<Vector2Int, WallMeshBatch> wallBatches = new Dictionary<Vector2Int, WallMeshBatch>();
        Texture2D wallStateTexture;
        Color[] wallStatePixels;
        Material hotelWallMaterial;
        WallKit wallKit;
        WallInstances wallInstances;
        HotelLighting hotelLighting;
        readonly List<HotelLighting.Lamp> wallLamps = new List<HotelLighting.Lamp>();
        readonly List<WallPanel> wallPanels = new List<WallPanel>();
        readonly struct WallPanel
        {
            public readonly Rect Rect;public readonly Vector2 Normal;public readonly int State;
            public WallPanel(Rect rect,Vector2 normal,int state){Rect=rect;Normal=normal;State=state;}
        }
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
            hotelLighting?.Dispose(); hotelLighting = null;
            ClearTowerFx();
            if (Instance == this) Instance = null;
            if (worldRoot != null) RemoveObject(worldRoot.gameObject);
            if (matchRoot != null) RemoveObject(matchRoot.gameObject);
            foreach (var asset in sceneAssets) if (asset != null) RemoveObject(asset);
            if (fogTex != null) RemoveObject(fogTex);
            if (worldSpriteMaterial != null) RemoveObject(worldSpriteMaterial);
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
            hotelLighting?.Dispose(); hotelLighting = null; wallLamps.Clear(); wallPanels.Clear(); lampGlowMaterial=null; lampGlowView=null;
            foreach(var asset in sceneAssets) if(asset!=null) RemoveObject(asset);
            sceneAssets.Clear(); wallStates.Clear(); wallBatches.Clear(); WallPieceCounts.Clear(); nextWallUpdate=0; WallFocusRoom=null;
            doorModels.Clear(); doorMaterials.Clear();
            doorMats.Clear(); doorMatMesh = null; doorMatMaterial = null;
            wallKit=WallKit.Load(); wallInstances=new WallInstances(); wallCoreViews.Clear();
            wallHeightAt=CurrentWallHeight;
            Camera.onPreCull -= DrawWallInstances; Camera.onPreCull += DrawWallInstances;
            BuildHotelFloors();
            var graph=Walls;
            wallTextureWidth=Mathf.NextPowerOfTwo(graph.Runs.Count+graph.Perimeter.Spans.Count+graph.Perimeter.Corners.Count+Map.Rooms.Count);
            wallStateTexture=new Texture2D(wallTextureWidth,2,TextureFormat.RGBAFloat,false,true) { name="Wall run states",filterMode=FilterMode.Point,wrapMode=TextureWrapMode.Clamp };
            sceneAssets.Add(wallStateTexture); wallStatePixels=new Color[wallTextureWidth*2];
            hotelWallMaterial=new Material(Resources.Load<Shader>("Shaders/HotelWall"));
            hotelWallMaterial.enableInstancing=wallInstances.UsesInstancing;
            hotelWallMaterial.mainTexture=wallKit?.Atlas;hotelWallMaterial.SetTexture("_WallStates",wallStateTexture);sceneAssets.Add(hotelWallMaterial);
            foreach(var run in graph.Runs)
            {
                int state=AddWallState(run.Footprint,run.DefaultHeight);
                foreach(int edge in run.EdgeIds)
                    if(Map.Get(graph.Edges[edge].WalkableCell.x,graph.Edges[edge].WalkableCell.y) is Tile.RoomFloor or Tile.Door)wallStates[state].RoomBoundary=true;

            }
            foreach(var span in graph.Perimeter.Spans)
            {
                int state=AddWallState(span.Bounds,WallGraph.FullHeight,span.Runs);
                FillRun(span.Bounds,span.Normal,state);
            }
            foreach(var corner in graph.Perimeter.Corners)
            {
                int state=AddWallState(corner.Bounds,WallGraph.FullHeight,corner.Runs);
                AddWallPiece(corner.Piece,corner.Bounds,corner.Normal,state);
                AddWallPiece("wall_cutaway_cap",corner.Bounds,corner.Normal,state,6);
            }
            foreach(var def in Map.Rooms)
            {
                var inward=(Vector2)(def.DoorInside-def.DoorTile);
                var center=HotelMap.Center(def.DoorTile);
                bool horizontal=Mathf.Abs(inward.y)>.5f;
                var rect=horizontal?new Rect(center.x-.6f,center.y-graph.Thickness/2,1.2f,graph.Thickness):new Rect(center.x-graph.Thickness/2,center.y-.6f,graph.Thickness,1.2f);
                // Openings stay clear: frame tops are clipped in cutaway mode, posts never move inward.
                int state=AddWallState(rect,WallGraph.FullHeight);
                wallStates[state].DoorRoom=def;wallStates[state].DoorInward=inward;
                wallStates[state].RoomBoundary=true;
                AddWallPiece("door_frame",rect,-inward,state,3);
                var door=MakeSprite("Door "+(def.Index+1),Sprites.DoorOpen,center,-2800,worldRoot);
                PoseDoor(door,def,true);doorSprites[def]=door;
                ApplyDoorLook(def);
                CreateDoorMat(def);
            }
            BuildWallPanels();
            WallBatchCount=wallInstances.MaxDrawCalls+(lampGlowView!=null?1:0);WallTriangleCount=wallInstances.TotalTriangles+wallLamps.Count*2;
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
            hotelLighting=HotelLighting.Build(Map,wallLamps);hotelLighting.Bind();
            sceneAssets.Add(hotelLighting.LightMap);
            UpdateWallStateTexture(true);
        }
        void BuildHotelFloors()
        {
            // One continuous floor mesh: walnut is mapped in world space with a finish,
            // board direction and offset owned by each room; the corridor runner, its
            // binding and the claimed-room energy are resolved per pixel by HotelFloor.
            var v=new List<Vector3>();var t=new List<int>();var u=new List<Vector2>();var c=new List<Color>();
            for(int x=0;x<Map.W;x++)for(int y=0;y<Map.H;y++)
            {
                if(!WallGraph.IsWalkable(Map.Get(x,y)))continue;
                Quad(v,t,u,c,new Vector3(x,y,.08f),new Vector3(x+1,y,.08f),new Vector3(x+1,y+1,.08f),new Vector3(x,y+1,.08f),FloorZone(new Vector2Int(x,y)));
            }
            // Fill the recovered margin once, on a shared subdivision. Expanding
            // whole floor quads would overlap at stepped corners and cause flicker.
            float margin=(1-Walls.Thickness)*.5f;
            var cuts=new[]{0f,margin,1-margin,1f};
            for(int x=0;x<Map.W;x++)for(int y=0;y<Map.H;y++)
            {
                if(WallGraph.IsWalkable(Map.Get(x,y)))continue;
                for(int sx=0;sx<3;sx++)for(int sy=0;sy<3;sy++)
                {
                    var center=new Vector2(x+(cuts[sx]+cuts[sx+1])*.5f,y+(cuts[sy]+cuts[sy+1])*.5f);
                    Tile owner=Tile.Void;var ownerCell=Vector2Int.zero;
                    for(int dx=-1;dx<=1;dx++)for(int dy=-1;dy<=1;dy++)
                    {
                        var type=Map.Get(x+dx,y+dy);if(!WallGraph.IsWalkable(type))continue;
                        var expanded=new Rect(x+dx-margin,y+dy-margin,1+2*margin,1+2*margin);
                        if(expanded.Contains(center)&&(owner==Tile.Void||type!=Tile.Corridor)){owner=type;ownerCell=new Vector2Int(x+dx,y+dy);}
                    }
                    if(owner==Tile.Void)continue;
                    float x0=x+cuts[sx],x1=x+cuts[sx+1],y0=y+cuts[sy],y1=y+cuts[sy+1];
                    // Margin fragments share the finish of the floor they extend.
                    Quad(v,t,u,c,new Vector3(x0,y0,.08f),new Vector3(x1,y0,.08f),new Vector3(x1,y1,.08f),new Vector3(x0,y1,.08f),FloorZone(ownerCell));
                }
            }
            floorMaterial=new Material(Resources.Load<Shader>("Shaders/HotelFloor"));sceneAssets.Add(floorMaterial);
            floorMaterial.SetTexture("_WoodA",FloorTexture("floor-walnut-continuous-a"));
            floorMaterial.SetTexture("_WoodB",FloorTexture("floor-walnut-continuous-b"));
            floorMaterial.SetTexture("_Carpet",FloorTexture("carpet-burgundy-field"));
            floorMaterial.SetTexture("_Binding",FloorTexture("carpet-antique-binding"));
            BuildFloorEnergyTextures();
            MeshObject("Hotel floors",Mesh(v,t,u,c),floorMaterial,worldRoot);

            var rv=new List<Vector3>();var rt=new List<int>();var ru=new List<Vector2>();var rc=new List<Color>();
            foreach(var room in Map.Rooms)
            {
                var turn=Quaternion.Euler(0,0,room.BedRotation);
                var center=new Vector3(room.BedCenter.x,room.BedCenter.y,.06f);
                // Fits within the bed's reserved two squares, including irregular room corners.
                var design=room.Index%3==1?Color.white:Color.black;
                Quad(rv,rt,ru,rc,center+turn*new Vector3(-.47f,-.94f),center+turn*new Vector3(.47f,-.94f),center+turn*new Vector3(.47f,.94f),center+turn*new Vector3(-.47f,.94f),design);
            }
            var rug=new Material(Resources.Load<Shader>("Shaders/HotelCarpet"));sceneAssets.Add(rug);
            rug.SetTexture("_FieldA",FloorTexture("carpet-burgundy-field"));
            rug.SetTexture("_FieldB",FloorTexture("carpet-charcoal-field"));
            rug.SetTexture("_Binding",FloorTexture("carpet-antique-binding"));
            MeshObject("Bed rugs",Mesh(rv,rt,ru,rc),rug,worldRoot);
        }
        Material floorMaterial;
        static Texture2D FloorTexture(string name) => Resources.Load<Texture2D>("Art/Floors/"+name);
        /// <summary>Vertex colour for a floor cell: r finish, g board direction, b offset seed,
        /// a room interior (eligible for claimed-room energy). Deterministic per room.</summary>
        Color FloorZone(Vector2Int cell)
        {
            var tile=Map.Get(cell.x,cell.y);
            var room=tile==Tile.Door?Map.RoomAtDoor(cell):tile==Tile.RoomFloor?Map.RoomContaining(cell):null;
            if(room==null)return new Color(0,0,.37f,0);
            int minX=int.MaxValue,maxX=int.MinValue,minY=int.MaxValue,maxY=int.MinValue;
            foreach(var f in room.Floor){minX=Mathf.Min(minX,f.x);maxX=Mathf.Max(maxX,f.x);minY=Mathf.Min(minY,f.y);maxY=Mathf.Max(maxY,f.y);}
            // Boards run along the room's longer axis; roughly a third of rooms get the cooler finish.
            float finish=(room.Index*5+2)%3==0?1:0, vertical=maxY-minY>maxX-minX?1:0;
            return new Color(finish,vertical,Mathf.Repeat(room.Index*.6180339f,1),tile==Tile.RoomFloor?1:0);
        }
        int AddWallState(Rect bounds,float height,IEnumerable<int> runs=null)
        {
            var state=new WallState { Bounds=bounds,DefaultHeight=height,Height=height,FromHeight=height,Started=Time.unscaledTime-.15f };
            if(runs!=null)state.Runs.AddRange(runs);
            foreach(int run in state.Runs)state.RoomBoundary|=wallStates[run].RoomBoundary;
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
            wallInstances.Add(piece,rect,CameraWallNormal(normal),state,wallTextureWidth,mode);
        }
        bool RoomLampStaysUp(int state,RoomDef room)
        {
            if(state<Walls.Runs.Count)return WallVisibility.RoomHeight(Walls,Walls.Runs[state],room)==WallGraph.FullHeight;
            bool borders=false;
            foreach(int run in wallStates[state].Runs)
                if(WallVisibility.BordersRoom(Walls,Walls.Runs[run],room))
                {
                    borders=true;
                    if(WallVisibility.RoomHeight(Walls,Walls.Runs[run],room)!=WallGraph.FullHeight)return false;
                }
            return borders;
        }
        static Vector2 CameraWallNormal(Vector2 normal) => Mathf.Abs(normal.y)>.5f?Vector2.down:Vector2.left;
        void FillRun(Rect rect,Vector2 normal,int state)
        {
            if(wallKit==null){Batch(rect).Box(rect,state,wallTextureWidth,new Color(.30f,.13f,.10f,0));return;}
            bool horizontal=Mathf.Abs(normal.y)>.5f;
            float start=horizontal?rect.xMin:rect.yMin,end=horizontal?rect.xMax:rect.yMax;
            for(float at=start;at<end-.001f;)
            {
                float next=Mathf.Min(end,at+1);var slice=rect;
                if(horizontal){slice.xMin=at;slice.xMax=next;}else{slice.yMin=at;slice.yMax=next;}
                wallPanels.Add(new WallPanel(slice,normal,state));
                at=next;
            }
        }
        void BuildWallPanels()
        {
            var candidates=new List<HotelLampPlan.Candidate>();var indices=new Dictionary<int,int>();
            for(int i=0;i<wallPanels.Count;i++)
            {
                var panel=wallPanels[i];bool horizontal=Mathf.Abs(panel.Normal.y)>.5f;
                if((horizontal?panel.Rect.width:panel.Rect.height)<.6f)continue;
                float depth=horizontal?panel.Rect.height:panel.Rect.width;
                var position=panel.Rect.center+panel.Normal*depth/2;
                var tile=HotelMap.ToTile(position+panel.Normal*.4f);
                var type=Map.Get(tile.x,tile.y);if(type!=Tile.Corridor&&type!=Tile.RoomFloor)continue;
                var room=type==Tile.RoomFloor?Map.RoomContaining(tile):null;
                indices[i]=candidates.Count;
                candidates.Add(new HotelLampPlan.Candidate(position,panel.Normal,room?.Index??Map.Rooms.Count,room!=null&&RoomLampStaysUp(panel.State,room),
                    room!=null?HotelLampPlan.RoomLampRadius:HotelLampPlan.CorridorLampRadius));
            }
            var chosen=HotelLampPlan.Select(Map,candidates);
            var glowPanels=new List<WallInstances.Record>();
            for(int i=0;i<wallPanels.Count;i++)
            {
                var panel=wallPanels[i];bool lamp=indices.TryGetValue(i,out int candidate)&&chosen.Contains(candidate);
                AddWallPiece(lamp?"wall_lamp":"wall_straight",panel.Rect,panel.Normal,panel.State,1);
                if(lamp){wallLamps.Add(candidates[candidate].Lamp);glowPanels.Add(wallInstances.Records[wallInstances.Count-1]);}
                AddWallPiece("wall_cutaway_cap",panel.Rect,panel.Normal,panel.State,2);
                if(wallStates[panel.State].RoomBoundary)AddWallPiece("wall_cutaway_cap",panel.Rect,panel.Normal,panel.State,6);
            }
            BuildLampGlows(glowPanels);wallPanels.Clear();
        }

        void BuildLampGlows(List<WallInstances.Record> panels)
        {
            if(panels.Count==0)return;
            var vertices=new List<Vector3>();var triangles=new List<int>();var uv=new List<Vector2>();var states=new List<Vector2>();var grounds=new List<Vector2>();
            var source=wallKit.Get("wall_lamp").Mesh;var sourceVertices=source.vertices;var colors=source.colors;
            Vector3 anchor=Vector3.zero;float weight=0;
            for(int i=0;i<sourceVertices.Length;i++)if(colors[i].a>0){anchor+=sourceVertices[i]*colors[i].a;weight+=colors[i].a;}
            anchor=weight>0?anchor/weight:new Vector3(.5f,1.2f,0);
            foreach(var panel in panels)
            {
                // The authored emissive vertices locate the bulb, avoiding a floating glow
                // below or beside differently oriented sconces. Wall coordinates also ensure
                // the halo disappears inside the exact same monster peek window as its lamp.
                var ground=panel.Footprint.center;
                var center=panel.Matrix.MultiplyPoint3x4(anchor)+(Vector3)panel.Normal*.08f-HotelView3D.Forward*.02f;
                int index=vertices.Count;
                foreach(var corner in new[]{new Vector2(-1,-1),new Vector2(1,-1),new Vector2(1,1),new Vector2(-1,1)})
                {
                    vertices.Add(center+HotelView3D.Right*(corner.x*.36f)+HotelView3D.Up*(corner.y*.36f));
                    uv.Add(corner);states.Add(new Vector2((panel.StateId+.5f)/wallTextureWidth,0));grounds.Add(ground);
                }
                triangles.AddRange(new[]{index,index+1,index+2,index,index+2,index+3});
            }
            var mesh=new Mesh{name="Wall sconce glow halos"};mesh.SetVertices(vertices);mesh.SetTriangles(triangles,0);mesh.SetUVs(0,uv);mesh.SetUVs(1,states);mesh.SetUVs(2,grounds);mesh.RecalculateBounds();sceneAssets.Add(mesh);
            var material=new Material(Resources.Load<Shader>("Shaders/HotelLampGlow"));material.SetTexture("_WallStates",wallStateTexture);sceneAssets.Add(material);
            lampGlowView=MeshObject(mesh.name,mesh,material,worldRoot);
            lampGlowMaterial=material;
        }
        Material lampGlowMaterial;
        MeshRenderer lampGlowView;

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
            if(lampGlowView!=null&&GeometryUtility.TestPlanesAABB(wallFrustum,lampGlowView.bounds))
            {ActualWallDrawCalls++;VisibleWallTriangles+=wallLamps.Count*2;}
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
            if(lampGlowMaterial!=null)lampGlowMaterial.SetFloat("_WallClock",Time.unscaledTime);
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
            // An explicit Up/Down choice overrides the automatic room presentation.
            RoomDef viewRoom=occupied??(HumanRole==Role.Resident?WallFocusRoom:null);
            bool monsterOutside=WallMode==WallDisplayMode.Cutaway&&hallway&&HumanRole==Role.Monster;
            var peek=ActiveWallPeek;
            bool showPeek=InMatch&&peek!=null&&
                (HumanRole==Role.Monster||(HumanRole==Role.Resident&&Human!=null&&Human.Alive&&occupied==peek.Room));
            hotelWallMaterial.SetFloat("_WallPeekEnabled",showPeek?1:0);
            if(showPeek)hotelWallMaterial.SetVector("_WallPeekBounds",new Vector4(peek.Bounds.xMin,peek.Bounds.yMin,peek.Bounds.xMax,peek.Bounds.yMax));
            if(lampGlowMaterial!=null)
            {
                lampGlowMaterial.SetFloat("_WallPeekEnabled",showPeek?1:0);
                if(showPeek)lampGlowMaterial.SetVector("_WallPeekBounds",new Vector4(peek.Bounds.xMin,peek.Bounds.yMin,peek.Bounds.xMax,peek.Bounds.yMax));
            }
            bool changed=immediate;
            for(int i=0;i<wallStates.Count;i++)
            {
                var state=wallStates[i];
                float height=WallMode==WallDisplayMode.Up?WallGraph.FullHeight:WallMode==WallDisplayMode.Down?WallGraph.DownHeight:state.DefaultHeight;
                if(monsterOutside&&state.RoomBoundary)height=WallGraph.FullHeight;
                float fade=1;
                if(WallMode==WallDisplayMode.Cutaway)
                {
                    if(i<Walls.Runs.Count&&WallVisibility.BordersRoom(Walls,Walls.Runs[i],viewRoom))
                        height=WallVisibility.RoomHeight(Walls,Walls.Runs[i],viewRoom);
                    if(viewRoom!=null&&state.DoorRoom==viewRoom)height=WallVisibility.RoomHeight(state.DoorInward);
                    var delta=HotelView3D.Facing(state.Bounds.center)-HotelView3D.Facing(player);
                    if(state.RoomBoundary&&hallway&&HumanRole!=Role.Monster&&DistanceToRect(player,state.Bounds)<4&&WallVisibility.CoversGround(state.Bounds,1.9f,player)&&delta.y<1)
                    { height=Mathf.Min(height,WallGraph.CutawayHeight);fade=.22f; }
                }
                bool roomJoin=false;
                if(WallMode==WallDisplayMode.Cutaway&&viewRoom!=null)
                    foreach(int run in state.Runs)
                        if(WallVisibility.BordersRoom(Walls,Walls.Runs[run],viewRoom))
                        {
                            // Opposite corridor faces of a separator must not lower the room's far wall.
                            height=roomJoin?Mathf.Min(height,wallStates[run].Height):wallStates[run].Height;
                            fade=roomJoin?Mathf.Min(fade,wallStates[run].Fade):wallStates[run].Fade;
                            roomJoin=true;
                        }
                if(!roomJoin&&!(monsterOutside&&state.RoomBoundary))foreach(int run in state.Runs){height=Mathf.Min(height,wallStates[run].Height);fade=Mathf.Min(fade,wallStates[run].Fade);}
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
            if(lampGlowMaterial!=null)lampGlowMaterial.SetFloat("_WallClock",Time.unscaledTime);
        }
    }
}
