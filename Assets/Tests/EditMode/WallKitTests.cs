using System.Linq;
using BadAppleHotel.Game;
using NUnit.Framework;
using UnityEngine;

namespace BadAppleHotel.Tests
{
    public class WallKitTests
    {
        static readonly string[] Ids={"wall_straight","wall_lamp","wall_corner","wall_inner_corner","wall_end_cap","door_frame","wall_cutaway_cap"};

        [Test] public void Imported_kit_is_complete_and_fits_tile_footprints_with_mobile_triangle_budget()
        {
            var kit=WallKit.Load();Assert.NotNull(kit,"The baked kit must ship with the game.");
            CollectionAssert.AreEquivalent(Ids,kit.Pieces.Select(p=>p.Id));Assert.NotNull(kit.Atlas);
            Assert.LessOrEqual(kit.Atlas.width,2048);Assert.LessOrEqual(kit.Atlas.height,2048);
            foreach(var id in Ids)
            {
                var piece=kit.Get(id);Assert.NotNull(piece.Mesh,id);Assert.NotNull(piece.Preview,id);
                var expected=id=="door_frame"?new Vector3(1.2f,1.7f,.3f):id=="wall_cutaway_cap"?new Vector3(1,.45f,.3f):id=="wall_straight"||id=="wall_lamp"?new Vector3(1,1.7f,.3f):new Vector3(.3f,1.7f,.3f);
                Assert.Less(Vector3.Distance(piece.Mesh.bounds.min,Vector3.zero),.01f,id+" floor edge pivot");
                Assert.Less(Vector3.Distance(piece.Mesh.bounds.max,expected),.01f,id+" footprint");
                Assert.AreEqual(1,piece.Mesh.subMeshCount,id+" atlas batching");Assert.IsTrue(piece.Mesh.isReadable,id+" runtime mesh batching");
                Assert.LessOrEqual(piece.Mesh.triangles.Length/3,1600,id+" Android geometry budget");
                Assert.Greater(piece.Mesh.triangles.Length/3,0,id);Assert.AreEqual(piece.Mesh.vertexCount,piece.Mesh.uv.Length,id);
                Assert.IsTrue(piece.Mesh.uv.All(uv=>uv.x>0&&uv.y>0&&uv.x<1&&uv.y<1),id+" UV padding inside atlas");
                Assert.IsTrue(piece.Mesh.vertices.All(v=>IsFinite(v.x)&&IsFinite(v.y)&&IsFinite(v.z)),id);
            }
        }

        [Test] public void Outer_pilaster_broad_faces_turn_diagonally_without_widening_the_wall()
        {
            var piece=WallKit.Load().Get("wall_corner");var vertices=piece.Mesh.vertices;var triangles=piece.Mesh.triangles;
            float diagonalArea=0,verticalArea=0;
            for(int i=0;i<triangles.Length;i+=3)
            {
                var a=vertices[triangles[i]];var b=vertices[triangles[i+1]];var c=vertices[triangles[i+2]];
                float y=(a.y+b.y+c.y)/3;var cross=Vector3.Cross(b-a,c-a);var normal=cross.normalized;
                if(y<.2f||y>1.5f||Mathf.Abs(normal.y)>.1f)continue;
                verticalArea+=cross.magnitude;
                if(Mathf.Abs(normal.x)>.55f&&Mathf.Abs(normal.z)>.55f)diagonalArea+=cross.magnitude;
            }
            Assert.Greater(verticalArea,.1f);
            Assert.Greater(diagonalArea/verticalArea,.7f,"The outer pilaster should present its rotated broad faces, not its old axis-aligned orientation.");
            Assert.Less(Vector3.Distance(piece.Mesh.bounds.min,Vector3.zero),.001f);
            Assert.Less(Vector3.Distance(piece.Mesh.bounds.size,new Vector3(.3f,1.7f,.3f)),.001f);
        }

        [Test] public void Door_frame_leaves_one_tile_of_clear_passage_between_physical_posts()
        {
            var piece=WallKit.Load().Get("door_frame");var vertices=piece.Mesh.vertices;var triangles=piece.Mesh.triangles;
            // Rays across the entire foot/body passage catch both protruding posts and accidental
            // triangles spanning the opening, which a bounding-box-only check cannot detect.
            for(float x=.101f;x<1.1f;x+=.025f)for(float y=.02f;y<1.15f;y+=.07f)
            {
                var origin=new Vector3(x,y,-.1f);
                for(int i=0;i<triangles.Length;i+=3)
                    Assert.IsFalse(Hits(origin,vertices[triangles[i]],vertices[triangles[i+1]],vertices[triangles[i+2]]),$"Door opening blocked at {x:F3},{y:F3}");
            }
            Assert.IsTrue(vertices.Any(v=>v.x<.099f&&v.y<.1f),"left post reaches the floor");
            Assert.IsTrue(vertices.Any(v=>v.x>1.101f&&v.y<.1f),"right post reaches the floor");
        }

        [Test] public void Straight_panel_faces_are_axis_aligned_before_repeating_them_along_a_wall()
        {
            var mesh=WallKit.Load().Get("wall_straight").Mesh;var vertices=mesh.vertices;var triangles=mesh.triangles;var sum=Vector3.zero;
            for(int i=0;i<triangles.Length;i+=3)
            {
                var a=vertices[triangles[i]];var b=vertices[triangles[i+1]];var c=vertices[triangles[i+2]];float y=(a.y+b.y+c.y)/3;
                var cross=Vector3.Cross(b-a,c-a);var normal=cross.normalized;
                if(y<.85f||y>1.445f||Mathf.Abs(normal.z)<.9f||Mathf.Abs(normal.y)>.1f)continue;
                if(cross.z<0)cross=-cross;sum+=cross;
            }
            Assert.Greater(sum.sqrMagnitude,.01f,"The panel must contain measurable wall faces.");
            Assert.Less(Mathf.Abs(Mathf.Atan2(sum.x,sum.z)*Mathf.Rad2Deg),1f,"Source yaw must be corrected before bounds fitting; the old mesh was skewed almost five degrees.");
        }

        [TestCase("wall_straight",1.7f)]
        [TestCase("wall_cutaway_cap",.45f)]
        public void Repeating_panels_reach_both_ends_with_matching_front_and_back_profiles(string id,float height)
        {
            var mesh=WallKit.Load().Get(id).Mesh;var vertices=mesh.vertices;var triangles=mesh.triangles;
            // Cross-section rays detect inset slab ends, crown steps and skewed panels that
            // still have perfectly valid overall bounding boxes. Sample close to each joint.
            for(int i=1;i<40;i++)
            {
                float y=height*i/40;var left=Profile(vertices,triangles,.025f,y);var right=Profile(vertices,triangles,.975f,y);
                Assert.Less(Mathf.Abs(left.x-right.x),.02f,$"{id} front seam at height {y:F3}");
                Assert.Less(Mathf.Abs(left.y-right.y),.02f,$"{id} back seam at height {y:F3}");
            }
        }

        [Test] public void Lamp_and_plain_panels_share_the_same_wall_and_crown_cross_sections()
        {
            var plain=WallKit.Load().Get("wall_straight").Mesh;var lamp=WallKit.Load().Get("wall_lamp").Mesh;
            var pv=plain.vertices;var pt=plain.triangles;var lv=lamp.vertices;var lt=lamp.triangles;
            foreach(float x in new[]{.025f,.1f,.9f,.975f})for(int i=1;i<40;i++)
            {
                float y=1.7f*i/40;var expected=Profile(pv,pt,x,y);var actual=Profile(lv,lt,x,y);
                Assert.Less(Vector2.Distance(expected,actual),.001f,$"Lamp body must continue the plain panel at {x:F3},{y:F3}");
            }
        }

        static Vector2 Profile(Vector3[] vertices,int[] triangles,float x,float y)
        {
            float near=float.PositiveInfinity,far=float.NegativeInfinity;
            for(int i=0;i<triangles.Length;i+=3)
            {
                var a=vertices[triangles[i]];var b=vertices[triangles[i+1]];var c=vertices[triangles[i+2]];
                float det=(b.y-c.y)*(a.x-c.x)+(c.x-b.x)*(a.y-c.y);if(Mathf.Abs(det)<1e-9f)continue;
                float u=((b.y-c.y)*(x-c.x)+(c.x-b.x)*(y-c.y))/det;
                float v=((c.y-a.y)*(x-c.x)+(a.x-c.x)*(y-c.y))/det;
                if(u<-.000001f||v<-.000001f||u+v>1.000001f)continue;
                float z=u*a.z+v*b.z+(1-u-v)*c.z;near=Mathf.Min(near,z);far=Mathf.Max(far,z);
            }
            Assert.IsTrue(IsFinite(near)&&IsFinite(far),$"Missing wall cross section at {x:F3},{y:F3}");return new Vector2(near,far);
        }

        [Test] public void Only_sconce_shades_emit_light_in_the_shared_wall_material()
        {
            var kit=WallKit.Load();
            foreach(var piece in kit.Pieces)
            {
                Assert.AreEqual(piece.Mesh.vertexCount,piece.Mesh.colors.Length,piece.Id);
                if(piece.Id=="wall_lamp")Assert.IsTrue(piece.Mesh.colors.Any(c=>c.a>.1f),"lamp shade emission must survive simplification");
                else Assert.IsTrue(piece.Mesh.colors.All(c=>c.a==0),piece.Id+" should not glow");
            }
        }
        static bool IsFinite(float f)=>!float.IsNaN(f)&&!float.IsInfinity(f);
        static bool Hits(Vector3 origin,Vector3 a,Vector3 b,Vector3 c)
        {
            var e1=b-a;var e2=c-a;var h=Vector3.Cross(Vector3.forward,e2);float det=Vector3.Dot(e1,h);
            if(Mathf.Abs(det)<.0000001f)return false;
            float inv=1/det;var s=origin-a;float u=inv*Vector3.Dot(s,h);if(u<0||u>1)return false;
            var q=Vector3.Cross(s,e1);float v=inv*Vector3.Dot(Vector3.forward,q);if(v<0||u+v>1)return false;
            return inv*Vector3.Dot(e2,q)>0;
        }
    }
}
