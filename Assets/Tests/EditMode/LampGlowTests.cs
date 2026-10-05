using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Rendering;

namespace BadAppleHotel.Tests
{
    public class LampGlowTests
    {
        [Test]
        public void Visible_bulbs_glow_but_lowered_hidden_and_peeked_away_lamps_do_not()
        {
            if(SystemInfo.graphicsDeviceType==GraphicsDeviceType.Null)Assert.Ignore("Requires a graphics device.");
            var shader=Resources.Load<Shader>("Shaders/HotelLampGlow");Assert.IsNotNull(shader);
            var root=new GameObject("Glow probe");var camera=new GameObject("Glow camera").AddComponent<Camera>();
            var material=new Material(shader);var states=new Texture2D(2,2,TextureFormat.RGBAFloat,false,true);
            var vision=new Texture2D(2,2,TextureFormat.RGBA32,false,true);var target=new RenderTexture(64,64,24);
            var read=new Texture2D(64,64,TextureFormat.RGB24,false,true);var mesh=new Mesh();
            var previous=RenderTexture.active;
            try
            {
                root.layer=31;camera.cullingMask=1<<31;camera.orthographic=true;camera.orthographicSize=1;
                camera.transform.position=new Vector3(0,0,-5);camera.clearFlags=CameraClearFlags.SolidColor;camera.backgroundColor=Color.black;camera.targetTexture=target;
                mesh.vertices=new[]{new Vector3(-.8f,-.8f),new Vector3(.8f,-.8f),new Vector3(.8f,.8f),new Vector3(-.8f,.8f)};
                mesh.triangles=new[]{0,1,2,0,2,3};mesh.uv=new[]{new Vector2(-1,-1),new Vector2(1,-1),Vector2.one,new Vector2(-1,1)};
                mesh.SetUVs(1,new List<Vector2>{Vector2.one*.25f,Vector2.one*.25f,Vector2.one*.25f,Vector2.one*.25f});
                mesh.SetUVs(2,new List<Vector2>{Vector2.one*1.5f,Vector2.one*1.5f,Vector2.one*1.5f,Vector2.one*1.5f});
                root.AddComponent<MeshFilter>().sharedMesh=mesh;root.AddComponent<MeshRenderer>().sharedMaterial=material;
                material.SetTexture("_WallStates",states);material.SetTexture("_HotelVision",vision);material.SetVector("_HotelSize",new Vector4(4,4,0,0));material.SetFloat("_HotelFog",1);material.SetFloat("_WallClock",1);
                float Render(float height,bool visible,bool peek)
                {
                    states.SetPixels(new[]{new Color(height,height,1,1),new Color(height,height,1,1),Color.clear,Color.clear});states.Apply();
                    var color=visible?Color.white:Color.black;vision.SetPixels(new[]{color,color,color,color});vision.Apply();
                    material.SetFloat("_WallPeekEnabled",peek?1:0);material.SetVector("_WallPeekBounds",new Vector4(1,1,2,2));
                    camera.Render();RenderTexture.active=target;read.ReadPixels(new Rect(0,0,64,64),0,0);read.Apply();
                    return read.GetPixel(32,32).r;
                }
                Assert.Greater(Render(1.7f,true,false),.3f,"Visible bulb must produce a warm halo.");
                Assert.Less(Render(.1f,true,false),.01f,"Lowering the wall removes its halo.");
                Assert.Less(Render(1.7f,false,false),.01f,"Fog must hide the halo.");
                Assert.Less(Render(1.7f,true,true),.01f,"The local wall window must clip the halo.");
            }
            finally
            {
                RenderTexture.active=previous;camera.targetTexture=null;
                Object.DestroyImmediate(root);Object.DestroyImmediate(camera.gameObject);Object.DestroyImmediate(mesh);Object.DestroyImmediate(material);
                Object.DestroyImmediate(states);Object.DestroyImmediate(vision);Object.DestroyImmediate(read);Object.DestroyImmediate(target);
            }
        }
    }
}
