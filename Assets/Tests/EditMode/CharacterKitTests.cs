using System.Linq;
using BadAppleHotel.Config;
using BadAppleHotel.Game;
using NUnit.Framework;
using UnityEngine;

namespace BadAppleHotel.Tests
{
    public class CharacterKitTests
    {
        [Test] public void Every_roster_resident_has_a_mobile_3D_model_standing_on_its_feet()
        {
            var kit=CharacterKit.Load();Assert.NotNull(kit,"The baked character kit must ship with the game.");
            foreach(var art in ConfigLoader.Load().residents.roster.Select(c=>c.art))
            {
                var model=kit.Get(art);Assert.NotNull(model,art);Assert.NotNull(model.Albedo,art);
                var b=model.Mesh.bounds;
                Assert.AreEqual(0,b.max.z,.001f,art+" feet on the floor; height is negative Z");
                Assert.AreEqual(model.Height,b.size.z,.001f,art);
                Assert.Less(Mathf.Abs(b.center.x)+Mathf.Abs(b.center.y),.02f,art+" centered over its feet");
                Assert.Greater(model.HalfDepth,0,art);
                Assert.LessOrEqual(model.Mesh.triangles.Length/3,6100,art+" Android geometry budget");
                Assert.AreEqual(model.Mesh.vertexCount,model.Mesh.uv.Length,art);
            }
        }
    }
}
