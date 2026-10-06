using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using BadAppleHotel.Config;
using BadAppleHotel.Game;
using NUnit.Framework;
using UnityEngine;

namespace BadAppleHotel.Tests
{
    public class CoreBackingTests
    {
        const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;

        [TestCase(11)] [TestCase(41)] [TestCase(83)]
        public void Separators_use_one_decorative_span_and_the_same_thin_collision_without_backing_blocks(int seed)
        {
            var random = Random.state; var art = Sprites.ArtOverride; float timeScale = Time.timeScale;
            var game = new GameObject("Separator backing geometry " + seed).AddComponent<GameManager>();
            try
            {
                game.StartSimulation(ConfigLoader.Load(), seed, false);
                typeof(GameManager).GetMethod("BuildScene3D", Private).Invoke(game, null);
                var root = (Transform)typeof(GameManager).GetField("worldRoot", Private).GetValue(game);
                var instances = (WallInstances)typeof(GameManager).GetField("wallInstances", Private).GetValue(game);
                Assert.IsFalse(root.GetComponentsInChildren<MeshFilter>().Any(filter=>filter.sharedMesh!=null&&filter.sharedMesh.name.StartsWith("Hotel walls ")),
                    "The old full-cell backing must not reintroduce a second room enclosure.");
                for(int index=0;index<game.Walls.Perimeter.Spans.Count;index++)
                {
                    var span=game.Walls.Perimeter.Spans[index];
                    int state=game.Walls.Runs.Count+index;
                    var faces=instances.Records.Where(record=>record.StateId==state&&record.Mode==1).ToArray();
                    Assert.IsNotEmpty(faces);
                    Assert.AreEqual(span.Bounds.width*span.Bounds.height,faces.Sum(face=>face.Footprint.width*face.Footprint.height),.0001f,
                        "The single decorated face must cover its complete physical span.");
                    foreach(var face in faces)
                    {
                        Assert.AreEqual(span.Normal,face.Normal);
                        Assert.IsTrue(game.Walls.CollisionFootprints.Contains(span.Bounds),"Render and collision must use the same thin wall.");
                    }
                }
                Assert.IsTrue(game.Walls.Perimeter.Corners.Count>0);

            }
            finally
            {
                if (game != null) game.DisposeSimulation();
                Random.state = random; Sprites.ArtOverride = art; Time.timeScale = timeScale;
            }
        }

    }
}
