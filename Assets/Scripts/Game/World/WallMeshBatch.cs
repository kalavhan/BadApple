using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace BadAppleHotel.Game
{
    /// <summary>Batches the small separator cores; detailed kit pieces use shared GPU instances.</summary>
    public sealed class WallMeshBatch
    {
        readonly List<Vector3> vertices = new List<Vector3>();
        readonly List<Vector3> normals = new List<Vector3>();
        readonly List<Vector2> uv = new List<Vector2>(), state = new List<Vector2>(), modes = new List<Vector2>();
        readonly List<Color> colors = new List<Color>();
        readonly List<int> triangles = new List<int>();
        public int VertexCount => vertices.Count;

        public void Box(Rect rect, int stateIndex, int textureWidth, Color tint, float inset = 0, float height = 1.7f, int mode = 4)
        {
            float x0 = rect.xMin + inset, x1 = rect.xMax - inset, y0 = rect.yMin + inset, y1 = rect.yMax - inset;
            const float bottom = .04f;
            float top = bottom-height;
            Face(new Vector3(x0,y0,bottom),new Vector3(x1,y0,bottom),new Vector3(x1,y0,top),new Vector3(x0,y0,top),Vector3.down,tint,stateIndex,textureWidth,mode);
            Face(new Vector3(x1,y1,bottom),new Vector3(x0,y1,bottom),new Vector3(x0,y1,top),new Vector3(x1,y1,top),Vector3.up,tint,stateIndex,textureWidth,mode);
            Face(new Vector3(x0,y1,bottom),new Vector3(x0,y0,bottom),new Vector3(x0,y0,top),new Vector3(x0,y1,top),Vector3.left,tint,stateIndex,textureWidth,mode);
            Face(new Vector3(x1,y0,bottom),new Vector3(x1,y1,bottom),new Vector3(x1,y1,top),new Vector3(x1,y0,top),Vector3.right,tint,stateIndex,textureWidth,mode);
            Face(new Vector3(x0,y0,top),new Vector3(x1,y0,top),new Vector3(x1,y1,top),new Vector3(x0,y1,top),Vector3.back,tint,stateIndex,textureWidth,mode);
        }
        void Face(Vector3 a, Vector3 b, Vector3 c, Vector3 d, Vector3 normal, Color tint, int index, int width,int mode)
        {
            int offset = vertices.Count;
            vertices.AddRange(new[]{a,b,c,d}); normals.AddRange(new[]{normal,normal,normal,normal});
            uv.AddRange(new[]{Vector2.zero,Vector2.right,Vector2.one,Vector2.up});
            for (int i = 0; i < 4; i++)
            {
                colors.Add(tint); state.Add(new Vector2((index+.5f)/width,1.7f)); modes.Add(new Vector2(mode,0));
            }
            triangles.AddRange(new[]{offset,offset+1,offset+2,offset,offset+2,offset+3});
        }
        public Mesh Bake(string name)
        {
            var mesh = new Mesh { name = name, indexFormat = IndexFormat.UInt32 };
            mesh.SetVertices(vertices); mesh.SetNormals(normals); mesh.SetUVs(0,uv); mesh.SetUVs(1,state); mesh.SetUVs(2,modes);
            mesh.SetColors(colors); mesh.SetTriangles(triangles,0); mesh.RecalculateBounds(); return mesh;
        }
    }
}
