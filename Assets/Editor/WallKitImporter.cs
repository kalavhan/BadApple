using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using BadAppleHotel.Game;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityMeshSimplifier;
using Object = UnityEngine.Object;

namespace BadAppleHotel.EditorTools
{
    /// <summary>Explicit, repeatable bake. The large Tripo originals are never edited or referenced at runtime.</summary>
    public static class WallKitImporter
    {
        const string Output = "Assets/Resources/Art3D/Walls";
        const string Previews = "Assets/Resources/Art/Walls";
        const int TargetTriangles = 1500, StraightTriangles = 1200, SconceTriangles = 300;
        const int Cell = 256, Padding = 4;
        sealed class Spec
        {
            public readonly string Id, Source; public readonly Vector3 Size; public readonly float Turn;
            public Spec(string id, string source, float width, float height, float depth, float turn = 0)
            { Id=id; Source=source; Size=new Vector3(width,height,depth); Turn=turn; }
        }
        static readonly Spec[] Specs =
        {
            new Spec("wall_straight", "ornate_wooden_cabinet_3d_model", 1,1.7f,.3f,90),
            new Spec("wall_lamp", "decorative_wall_panel_3d_model", 1,1.7f,.3f,90),
            new Spec("wall_corner", "ornate_wooden_pedestal_3d_model", .3f,1.7f,.3f),
            new Spec("wall_inner_corner", "ornate_column_3d_model", .3f,1.7f,.3f),
            new Spec("wall_end_cap", "ornate_wooden_pedestal_3d_model_1", .3f,1.7f,.3f,90),
            new Spec("door_frame", "wooden_console_table_3d_model", 1.2f,1.7f,.3f),
            new Spec("wall_cutaway_cap", "wooden_cabinet_3d_model", 1,.45f,.3f)
        };

        [MenuItem("Bad Apple/Art/Bake imported wall kit")]
        public static void Bake()
        {
            foreach (var spec in Specs)
                if (!File.Exists(SourcePath(spec))) throw new FileNotFoundException("Wall source is missing. Restore the original Tripo export to regenerate the baked kit.",SourcePath(spec));
            Directory.CreateDirectory(Output); Directory.CreateDirectory(Previews);
            var atlas=new Texture2D(Cell*8,Cell*8,TextureFormat.RGBA32,false);
            var atlasPixels=new Color32[atlas.width*atlas.height];
            for(int i=0;i<atlasPixels.Length;i++) atlasPixels[i]=new Color32(40,20,16,255);
            var pieces=new List<WallKit.Piece>();
            try
            {
                for(int index=0;index<Specs.Length;index++)
                {
                    var spec=Specs[index];
                    EditorUtility.DisplayProgressBar("Bake hotel wall kit",spec.Id,(float)index/Specs.Length);
                    var source=AssetDatabase.LoadAssetAtPath<GameObject>(SourcePath(spec));
                    Mesh original=ReadMesh(source,spec.Turn); int sourceTriangles=original.triangles.Length/3;
                    // Tripo reconstructed the straight reference a few degrees off-axis. Bounds
                    // normalization alone preserves that yaw and makes a step at every butt joint.
                    // Align the actual broad panel faces before fitting the modular footprint.
                    if(spec.Id=="wall_straight") AlignBroadFaces(original);
                    Normalize(original,spec.Size);
                    if(spec.Id=="wall_straight") SquarePanelEnds(original);
                    if(spec.Id=="door_frame") FitDoorOpening(original);
                    var texPath=$"Assets/TripoModels/{spec.Source}/{spec.Source}.fbm/{spec.Source}_BaseColor.JPEG";
                    var sourceTexture=AssetDatabase.LoadAssetAtPath<Texture2D>(texPath);
                    if(sourceTexture==null) throw new InvalidOperationException("Missing wall albedo: "+texPath);
                    var facePixels=BakeFaces(original,sourceTexture,spec.Size);
                    for(int face=0;face<6;face++)
                    {
                        int slot=index*6+face,column=slot%8,row=slot/8,inner=Cell-Padding*2;
                        for(int y=0;y<Cell;y++)for(int x=0;x<Cell;x++)
                            atlasPixels[(row*Cell+y)*atlas.width+column*Cell+x]=facePixels[face][Mathf.Clamp(y-Padding,0,inner-1)*inner+Mathf.Clamp(x-Padding,0,inner-1)];
                    }
                    Mesh mesh;
                    if(spec.Id=="wall_lamp")
                    {
                        // Every run uses exactly the same panel, crown and base profile. The
                        // independently generated lamp panel is recessed and cannot tile with it.
                        var fixture=ExtractSconce(original);var reduced=Reduce(fixture,SconceTriangles);Object.DestroyImmediate(fixture);
                        var projected=ProjectAtlas(reduced,spec,index,facePixels);Object.DestroyImmediate(reduced);
                        var positions=projected.vertices;
                        // The relief fits in the master's front recess, keeping the .3-tile wall
                        // footprint. UVs are projected before fitting so the shade keeps its art.
                        for(int i=0;i<positions.Length;i++)positions[i].z*=.061f/.142f;
                        projected.vertices=positions;projected.RecalculateBounds();
                        mesh=new Mesh();mesh.CombineMeshes(new[]
                        {
                            new CombineInstance{mesh=pieces[0].Mesh,transform=Matrix4x4.identity},
                            new CombineInstance{mesh=projected,transform=Matrix4x4.identity}
                        },true,true);Object.DestroyImmediate(projected);
                    }
                    else
                    {
                        mesh=Reduce(original,spec.Id=="wall_straight"?StraightTriangles:TargetTriangles);Normalize(mesh,spec.Size);
                        if(spec.Id=="door_frame") FitDoorOpening(mesh);
                        if(spec.Id=="wall_straight"||spec.Id=="wall_cutaway_cap") SnapEnds(mesh,spec.Size.x);
                        var projected=ProjectAtlas(mesh,spec,index,facePixels);Object.DestroyImmediate(mesh);mesh=projected;
                    }
                    Object.DestroyImmediate(original);
                    if(spec.Id=="wall_corner") RotateOuterCorner(mesh,spec.Size);
                    mesh.name=spec.Id;mesh.RecalculateBounds();mesh.RecalculateNormals();
                    // Keep CPU data readable for geometry validation; the runtime instances these shared meshes.
                    string meshPath=Output+"/"+spec.Id+".asset";
                    var existing=AssetDatabase.LoadAssetAtPath<Mesh>(meshPath);
                    if(existing!=null)
                    {
                        // CopySerialized changes native mesh data without refreshing its graphics buffers.
                        // Assign through the Mesh API so repeated bakes also render correctly immediately.
                        existing.Clear();existing.indexFormat=mesh.indexFormat;existing.vertices=mesh.vertices;existing.triangles=mesh.triangles;existing.normals=mesh.normals;existing.uv=mesh.uv;existing.colors=mesh.colors;existing.bounds=mesh.bounds;EditorUtility.SetDirty(existing);Object.DestroyImmediate(mesh);mesh=existing;
                    }
                    else AssetDatabase.CreateAsset(mesh,meshPath);
                    pieces.Add(new WallKit.Piece { Id=spec.Id,Mesh=mesh,Size=spec.Size,Source=SourcePath(spec),SourceTriangles=sourceTriangles,Triangles=mesh.triangles.Length/3 });
                    Debug.Log($"WallKit: baked {spec.Id}: {mesh.triangles.Length/3:N0} triangles, bounds {mesh.bounds}");
                }
                atlas.SetPixels32(atlasPixels);atlas.Apply();
                string atlasPath=Output+"/hotel_wall_atlas.png";File.WriteAllBytes(atlasPath,atlas.EncodeToPNG());
                AssetDatabase.ImportAsset(atlasPath,ImportAssetOptions.ForceSynchronousImport);
                var ti=(TextureImporter)AssetImporter.GetAtPath(atlasPath);ti.textureType=TextureImporterType.Default;ti.sRGBTexture=true;ti.alphaSource=TextureImporterAlphaSource.None;ti.mipmapEnabled=true;ti.wrapMode=TextureWrapMode.Clamp;ti.filterMode=FilterMode.Point;ti.maxTextureSize=2048;ti.textureCompression=TextureImporterCompression.CompressedHQ;ti.SaveAndReimport();
                var importedAtlas=AssetDatabase.LoadAssetAtPath<Texture2D>(atlasPath);
                foreach(var piece in pieces) piece.Preview=RenderPreview(piece,importedAtlas);
                var kit=AssetDatabase.LoadAssetAtPath<WallKit>(Output+"/HotelWallKit.asset");
                if(kit==null){kit=ScriptableObject.CreateInstance<WallKit>();AssetDatabase.CreateAsset(kit,Output+"/HotelWallKit.asset");}
                kit.Atlas=importedAtlas;kit.Pieces=pieces.ToArray();EditorUtility.SetDirty(kit);
                AssetDatabase.SaveAssets();AssetDatabase.Refresh();
                Debug.Log("WallKit: finished baking all seven pieces. Portraits and hallway props are intentionally excluded.");
            }
            finally {Object.DestroyImmediate(atlas);EditorUtility.ClearProgressBar();}
        }
        public static void BakeBatch() { try{Bake();EditorApplication.Exit(0);}catch(Exception e){Debug.LogException(e);EditorApplication.Exit(1);} }
        static string SourcePath(Spec s)=>$"Assets/TripoModels/{s.Source}/{s.Source}.fbx";
        static Mesh ReadMesh(GameObject source,float turn)
        {
            var go=Object.Instantiate(source);
            try
            {
                var rotation=Matrix4x4.Rotate(Quaternion.Euler(0,turn,0));
                var parts=new List<CombineInstance>();
                foreach(var filter in go.GetComponentsInChildren<MeshFilter>())
                    for(int sub=0;sub<filter.sharedMesh.subMeshCount;sub++) parts.Add(new CombineInstance {mesh=filter.sharedMesh,subMeshIndex=sub,transform=rotation*filter.transform.localToWorldMatrix});
                var mesh=new Mesh { indexFormat=IndexFormat.UInt32 };
                mesh.CombineMeshes(parts.ToArray(),true,true);return mesh;
            }
            finally{Object.DestroyImmediate(go);}
        }
        static void Normalize(Mesh mesh,Vector3 size)
        {
            mesh.RecalculateBounds();var b=mesh.bounds;var v=mesh.vertices;var n=mesh.normals;
            var scale=new Vector3(size.x/b.size.x,size.y/b.size.y,size.z/b.size.z);
            for(int i=0;i<v.Length;i++){v[i]=Vector3.Scale(v[i]-b.min,scale);if(n.Length==v.Length)n[i]=new Vector3(n[i].x/scale.x,n[i].y/scale.y,n[i].z/scale.z).normalized;}
            mesh.vertices=v;if(n.Length==v.Length)mesh.normals=n;mesh.RecalculateBounds();
        }
        static void RotateOuterCorner(Mesh mesh,Vector3 size)
        {
            // Turn the outer pilaster left around its upright axis. Rotate its authored UVs
            // with the geometry, then refit the original thin square footprint and floor pivot.
            var vertices=mesh.vertices;var center=mesh.bounds.center;var turn=Quaternion.Euler(0,45,0);
            for(int i=0;i<vertices.Length;i++)vertices[i]=turn*(vertices[i]-center);
            mesh.vertices=vertices;Normalize(mesh,size);mesh.RecalculateNormals();
        }
        static void AlignBroadFaces(Mesh mesh)
        {
            mesh.RecalculateBounds();var bounds=mesh.bounds;var vertices=mesh.vertices;var triangles=mesh.triangles;
            Vector3 sum=Vector3.zero;float area=0;
            for(int i=0;i<triangles.Length;i+=3)
            {
                var a=vertices[triangles[i]];var b=vertices[triangles[i+1]];var c=vertices[triangles[i+2]];
                float height=((a.y+b.y+c.y)/3-bounds.min.y)/bounds.size.y;
                if(height<.5f||height>.85f)continue;
                var cross=Vector3.Cross(b-a,c-a);float weight=cross.magnitude;
                if(weight<1e-10f)continue;var normal=cross/weight;
                if(Mathf.Abs(normal.z)<.9f||Mathf.Abs(normal.y)>.1f)continue;
                if(normal.z<0)normal=-normal;
                sum+=normal*weight;area+=weight;
            }
            if(area<1e-8f)throw new InvalidOperationException("Straight wall has no usable broad face for modular alignment.");
            float angle=Mathf.Atan2(sum.x,sum.z)*Mathf.Rad2Deg;
            var rotation=Quaternion.Euler(0,-angle,0);var normals=mesh.normals;
            for(int i=0;i<vertices.Length;i++){vertices[i]=rotation*(vertices[i]-bounds.center);if(normals.Length==vertices.Length)normals[i]=rotation*normals[i];}
            mesh.vertices=vertices;if(normals.Length==vertices.Length)mesh.normals=normals;mesh.RecalculateBounds();
            Debug.Log($"WallKit: aligned straight panel yaw by {-angle:F4} degrees before normalization");
        }
        static Mesh Reduce(Mesh source,int triangleBudget)
        {
            var geometry=WeldGeometry(source);var reducer=new MeshSimplifier();
            var options=SimplificationOptions.Default;options.MaxIterationCount=150;
            options.EnableSmartLink=false; // Geometry was welded; the atlas carries the UV seams.
            reducer.SimplificationOptions=options;reducer.Initialize(geometry);
            reducer.SimplifyMesh(Mathf.Min(1,(float)triangleBudget/(geometry.triangles.Length/3)));
            var reduced=reducer.ToMesh();Object.DestroyImmediate(geometry);return reduced;
        }
        static Mesh ExtractSconce(Mesh source)
        {
            var vertices=source.vertices;var triangles=source.triangles;
            var output=new List<Vector3>();var indices=new List<int>();
            const float mountingPlane=.142f;
            for(int i=0;i<triangles.Length;i+=3)
            {
                var a=vertices[triangles[i]];var b=vertices[triangles[i+1]];var c=vertices[triangles[i+2]];
                var center=(a+b+c)/3;
                if(center.x<.2f||center.x>.8f||center.y<.8f||center.y>1.5f)continue;
                if(a.z>=mountingPlane&&b.z>=mountingPlane&&c.z>=mountingPlane)continue;
                // Clip at the mounting plane instead of leaving wall triangles attached to the
                // fixture. The open back is embedded inside the shared straight panel.
                var polygon=new List<Vector3>(4);var input=new[]{a,b,c};
                for(int k=0;k<3;k++)
                {
                    var from=input[k];var to=input[(k+1)%3];bool inside=from.z<=mountingPlane,nextInside=to.z<=mountingPlane;
                    if(inside)polygon.Add(from);
                    if(inside!=nextInside)polygon.Add(Vector3.Lerp(from,to,(mountingPlane-from.z)/(to.z-from.z)));
                }
                for(int k=1;k+1<polygon.Count;k++)
                {
                    indices.Add(output.Count);output.Add(polygon[0]);indices.Add(output.Count);output.Add(polygon[k]);indices.Add(output.Count);output.Add(polygon[k+1]);
                }
            }
            if(indices.Count<300)throw new InvalidOperationException("Imported lamp has no separable sconce in front of its wall panel.");
            var mesh=new Mesh{indexFormat=IndexFormat.UInt32};mesh.SetVertices(output);mesh.SetTriangles(indices,0);mesh.RecalculateBounds();return mesh;
        }
        static void SquarePanelEnds(Mesh mesh)
        {
            // A freestanding Tripo panel has crown overhangs and inset slab ends. A repeating
            // wall needs the slab itself to reach both joins; flatten the short end shoulders
            // before rendering the atlas so background pixels cannot become vertical seams.
            var vertices=mesh.vertices;
            for(int i=0;i<vertices.Length;i++)vertices[i].x=Mathf.Clamp01((vertices[i].x-.055f)/.89f);
            mesh.vertices=vertices;mesh.RecalculateBounds();mesh.RecalculateNormals();
        }
        static void SnapEnds(Mesh mesh,float width)
        {
            var v=mesh.vertices;for(int i=0;i<v.Length;i++){if(v[i].x<.018f)v[i].x=0;else if(v[i].x>width-.018f)v[i].x=width;}mesh.vertices=v;mesh.RecalculateBounds();
        }
        static void FitDoorOpening(Mesh mesh)
        {
            var v=mesh.vertices;float left=0,right=1.2f;
            foreach(var p in v)if(p.y>=0&&p.y<1.15f){if(p.x<.6f)left=Mathf.Max(left,p.x);else right=Mathf.Min(right,p.x);}
            if(left<.02f||right>1.18f||right-left<.4f)throw new InvalidOperationException("Door frame has no usable opening.");
            for(int i=0;i<v.Length;i++)v[i].x=v[i].x<=left?v[i].x*.1f/left:v[i].x>=right?1.1f+(v[i].x-right)*.1f/(1.2f-right):.1f+(v[i].x-left)/(right-left);
            mesh.vertices=v;mesh.RecalculateNormals();mesh.RecalculateBounds();
        }
        // The source has hundreds of thousands of UV seams. Reducing those charts directly keeps
        // ~200,000 triangles. Reprojection decouples texture detail from the geometric silhouette.
        static Mesh WeldGeometry(Mesh source)
        {
            var positions=source.vertices;var indices=source.triangles;
            var unique=new List<Vector3>();var lookup=new Dictionary<Vector3Int,int>();var remap=new int[positions.Length];
            for(int i=0;i<positions.Length;i++)
            {
                var p=positions[i];var key=new Vector3Int(Mathf.RoundToInt(p.x*100000),Mathf.RoundToInt(p.y*100000),Mathf.RoundToInt(p.z*100000));
                if(!lookup.TryGetValue(key,out int id)){id=unique.Count;lookup.Add(key,id);unique.Add(p);}remap[i]=id;
            }
            var triangles=new List<int>(indices.Length);
            for(int i=0;i<indices.Length;i+=3)
            {
                int a=remap[indices[i]],b=remap[indices[i+1]],c=remap[indices[i+2]];
                if(a==b||b==c||a==c)continue;
                if(Vector3.Cross(unique[b]-unique[a],unique[c]-unique[a]).sqrMagnitude<1e-16f)continue;
                triangles.Add(a);triangles.Add(b);triangles.Add(c);
            }
            var result=new Mesh{indexFormat=IndexFormat.UInt32};result.SetVertices(unique);result.SetTriangles(triangles,0);result.RecalculateBounds();return result;
        }
        static readonly Vector3[] FaceNormals={Vector3.back,Vector3.forward,Vector3.left,Vector3.right,Vector3.up,Vector3.down};
        static Color32[][] BakeFaces(Mesh mesh,Texture texture,Vector3 size)
        {
            var go=new GameObject("Wall albedo reprojection");go.layer=31;go.AddComponent<MeshFilter>().sharedMesh=mesh;
            var material=new Material(Shader.Find("Unlit/Texture")){mainTexture=texture};go.AddComponent<MeshRenderer>().sharedMaterial=material;
            var camera=new GameObject("Wall bake camera").AddComponent<Camera>();camera.cullingMask=1<<31;camera.clearFlags=CameraClearFlags.SolidColor;camera.backgroundColor=new Color(.13f,.065f,.045f,1);camera.orthographic=true;camera.nearClipPlane=.001f;camera.farClipPlane=20;
            var previous=RenderTexture.active;int inner=Cell-Padding*2;var rt=RenderTexture.GetTemporary(inner,inner,24,RenderTextureFormat.ARGB32,RenderTextureReadWrite.sRGB);camera.targetTexture=rt;
            var pixels=new Color32[6][];
            try
            {
                for(int face=0;face<6;face++)
                {
                    float width=face<2||face>=4?size.x:size.z,height=face<4?size.y:size.z;
                    camera.orthographicSize=height*.5f;camera.aspect=width/height;
                    camera.transform.position=mesh.bounds.center+FaceNormals[face]*4;
                    camera.transform.LookAt(mesh.bounds.center,face==4?Vector3.forward:face==5?Vector3.back:Vector3.up);
                    camera.Render();RenderTexture.active=rt;var image=new Texture2D(inner,inner,TextureFormat.RGBA32,false);image.ReadPixels(new Rect(0,0,inner,inner),0,0);image.Apply();pixels[face]=image.GetPixels32();Object.DestroyImmediate(image);
                }
            }
            finally{RenderTexture.active=previous;camera.targetTexture=null;RenderTexture.ReleaseTemporary(rt);Object.DestroyImmediate(camera.gameObject);Object.DestroyImmediate(go);Object.DestroyImmediate(material);}
            return pixels;
        }
        static Mesh ProjectAtlas(Mesh source,Spec spec,int pieceIndex,Color32[][] facePixels)
        {
            var vertices=source.vertices;var triangles=source.triangles;var v=new Vector3[triangles.Length];var uv=new Vector2[v.Length];var colors=new Color[v.Length];var indices=new int[v.Length];
            for(int t=0;t<triangles.Length;t+=3)
            {
                var normal=Vector3.Cross(vertices[triangles[t+1]]-vertices[triangles[t]],vertices[triangles[t+2]]-vertices[triangles[t]]).normalized;
                int face=Mathf.Abs(normal.z)>=Mathf.Abs(normal.x)&&Mathf.Abs(normal.z)>=Mathf.Abs(normal.y)?(normal.z>0?1:0):Mathf.Abs(normal.x)>=Mathf.Abs(normal.y)?(normal.x>0?3:2):(normal.y>0?4:5);
                if(spec.Id=="wall_lamp")
                {
                    // Front-project the sconce and its mounting face. A side projection sees the
                    // lamp in front of the slab and would stamp a second lamp onto the slab's edge.
                    var center=(vertices[triangles[t]]+vertices[triangles[t+1]]+vertices[triangles[t+2]])/3;
                    if(center.z<.2f&&Mathf.Abs(normal.y)<.85f)face=0;
                    if(center.x>.25f&&center.x<.75f&&center.y>.99f&&center.y<1.4f&&center.z<.14f)face=0;
                }
                int slot=(spec.Id=="wall_lamp"&&(face==2||face==3)?0:pieceIndex)*6+face,column=slot%8,row=slot/8,inner=Cell-Padding*2;
                for(int k=0;k<3;k++)
                {
                    int i=t+k;var p=vertices[triangles[i]];v[i]=p;indices[i]=i;
                    float x=p.x/spec.Size.x,y=p.y/spec.Size.y,z=p.z/spec.Size.z;
                    var sample=face==0?new Vector2(x,y):face==1?new Vector2(1-x,y):face==2?new Vector2(z,y):face==3?new Vector2(1-z,y):face==4?new Vector2(x,z):new Vector2(x,1-z);
                    sample.x=Mathf.Clamp01(sample.x);sample.y=Mathf.Clamp01(sample.y);
                    uv[i]=new Vector2((column*Cell+Padding+.5f+sample.x*(inner-1))/(Cell*8),(row*Cell+Padding+.5f+sample.y*(inner-1))/(Cell*8));
                    float emission=0;
                    if(spec.Id=="wall_lamp"&&p.x>.25f&&p.x<.75f&&p.y>.99f&&p.y<1.4f&&p.z<.14f)
                    {Color c=facePixels[face][Mathf.RoundToInt(sample.y*(inner-1))*inner+Mathf.RoundToInt(sample.x*(inner-1))];if(c.r>.48f&&c.g>.22f&&c.b<c.r*.8f)emission=.65f;}
                    colors[i]=new Color(1,1,1,emission);
                }
            }
            return new Mesh{vertices=v,triangles=indices,uv=uv,colors=colors};
        }
        static Sprite RenderPreview(WallKit.Piece piece,Texture atlas)
        {
            var go=new GameObject(piece.Id);go.layer=31;go.AddComponent<MeshFilter>().sharedMesh=piece.Mesh;
            var mat=new Material(Shader.Find("Unlit/Texture")){mainTexture=atlas};go.AddComponent<MeshRenderer>().sharedMaterial=mat;
            var camera=new GameObject("Wall preview camera").AddComponent<Camera>();camera.cullingMask=1<<31;camera.clearFlags=CameraClearFlags.SolidColor;camera.backgroundColor=Color.clear;camera.orthographic=true;
            // Same 45-degree azimuth and 50-degree elevation as HotelView3D, converted to Y-up.
            camera.orthographicSize=Mathf.Max(piece.Size.y*.64f,piece.Size.x*.7f);camera.nearClipPlane=.01f;camera.farClipPlane=20;
            camera.transform.position=piece.Mesh.bounds.center+new Vector3(1,1.685f,-1).normalized*6;camera.transform.LookAt(piece.Mesh.bounds.center);
            var previous=RenderTexture.active;var rt=RenderTexture.GetTemporary(512,512,24,RenderTextureFormat.ARGB32,RenderTextureReadWrite.sRGB);camera.targetTexture=rt;camera.Render();RenderTexture.active=rt;
            var tex=new Texture2D(512,512,TextureFormat.RGBA32,false);tex.ReadPixels(new Rect(0,0,512,512),0,0);tex.Apply();string path=Previews+"/"+piece.Id+".png";File.WriteAllBytes(path,tex.EncodeToPNG());
            RenderTexture.active=previous;camera.targetTexture=null;RenderTexture.ReleaseTemporary(rt);Object.DestroyImmediate(tex);Object.DestroyImmediate(camera.gameObject);Object.DestroyImmediate(go);Object.DestroyImmediate(mat);
            AssetDatabase.ImportAsset(path,ImportAssetOptions.ForceSynchronousImport);var ti=(TextureImporter)AssetImporter.GetAtPath(path);ti.textureType=TextureImporterType.Sprite;ti.spriteImportMode=SpriteImportMode.Single;ti.spritePixelsPerUnit=256;ti.alphaIsTransparency=true;ti.mipmapEnabled=false;ti.textureCompression=TextureImporterCompression.CompressedHQ;ti.SaveAndReimport();return AssetDatabase.LoadAssetAtPath<Sprite>(path);
        }
    }
}
