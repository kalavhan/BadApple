using BadAppleHotel.Game;
using NUnit.Framework;
using UnityEngine;

namespace BadAppleHotel.Tests
{
    public class BedKitTests
    {
        [Test] public void All_seven_bed_levels_fit_the_two_bed_tiles_in_game_axes()
        {
            var kit=BedKit.Load();Assert.NotNull(kit,"The baked bed kit must ship with the game.");
            foreach(int level in new[]{1,2,3,4,5,6,7})
            {
                var piece=kit.Get(level);Assert.NotNull(piece,"bed level "+level);Assert.NotNull(piece.Albedo,"bed level "+level);
                var b=piece.Mesh.bounds;
                Assert.LessOrEqual(b.size.x,BedKit.Width+.01f,"width");Assert.LessOrEqual(b.size.y,BedKit.Length+.01f,"length");
                Assert.Less(Mathf.Abs(b.center.x)+Mathf.Abs(b.center.y),.02f,"footprint-centered pivot");
                Assert.AreEqual(0,b.max.z,.001f,"rests on the floor; height is negative Z");
                Assert.Greater(piece.SurfaceHeight,0);Assert.LessOrEqual(piece.SurfaceHeight,-b.min.z+(piece.Motion==3?.15f:.001f),"sleeping surface inside the mesh (plus hover for the floating bed)");
                Assert.LessOrEqual(piece.Mesh.triangles.Length/3,5000,"Android geometry budget");
                Assert.LessOrEqual(piece.Albedo.width,512);
            }
        }
    }
}
