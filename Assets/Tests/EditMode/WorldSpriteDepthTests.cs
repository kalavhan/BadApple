using BadAppleHotel.Game;
using NUnit.Framework;
using UnityEngine;

namespace BadAppleHotel.Tests
{
    public class WorldSpriteDepthTests
    {
        [Test] public void Wall_depth_occludes_a_sprite_even_with_the_highest_sorting_order()
        {
            var root = new GameObject("World sprite depth check");
            var target = new RenderTexture(32, 32, 24);
            var readback = new Texture2D(32, 32, TextureFormat.RGBA32, false);
            var texture = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            var sprite = Sprite.Create(texture, new Rect(0, 0, 2, 2), Vector2.one * .5f, 1);
            var spriteMaterial = new Material(Resources.Load<Shader>("Shaders/CharacterSprite"));
            var wallMaterial = new Material(Shader.Find("Unlit/Color"));
            var previous = RenderTexture.active;
            try
            {
                texture.SetPixels(new[] { Color.white, Color.white, Color.white, Color.white }); texture.Apply();
                var camera = new GameObject("Test camera").AddComponent<Camera>();
                camera.transform.SetParent(root.transform);
                camera.transform.position = new Vector3(0, 0, -10);
                camera.orthographic = true; camera.orthographicSize = 1; camera.enabled = false;
                camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = Color.black;
                camera.cullingMask = 1 << 30; camera.targetTexture = target;
                var card = new GameObject("Sprite behind wall").AddComponent<SpriteRenderer>();
                card.gameObject.layer = 30; card.transform.SetParent(root.transform);
                card.transform.position = new Vector3(0, 0, 1);
                card.sprite = sprite; card.color = Color.red; card.sharedMaterial = spriteMaterial; card.sortingOrder = 32767;
                var wall = GameObject.CreatePrimitive(PrimitiveType.Quad);
                wall.name = "Wall face"; wall.layer = 30; wall.transform.SetParent(root.transform);
                wallMaterial.color = Color.blue; wall.GetComponent<Renderer>().sharedMaterial = wallMaterial;
                camera.Render();
                RenderTexture.active = target; readback.ReadPixels(new Rect(0, 0, 32, 32), 0, 0); readback.Apply();
                var behind = readback.GetPixel(16, 16);
                Assert.Greater(behind.b, .8f); Assert.Less(behind.r, .1f, "sorting order bypassed wall depth");
                card.transform.position = new Vector3(0, 0, -1);
                camera.Render();
                RenderTexture.active = target; readback.ReadPixels(new Rect(0, 0, 32, 32), 0, 0); readback.Apply();
                var ahead = readback.GetPixel(16, 16);
                Assert.Greater(ahead.r, .8f); Assert.Less(ahead.b, .1f, "front sprite failed its depth test");
                wall.transform.position = new Vector3(0, 0, 2);
                card.sortingOrder = -32768;
                var farther = new GameObject("Later sprite behind character").AddComponent<SpriteRenderer>();
                farther.gameObject.layer = 30; farther.transform.SetParent(root.transform);
                farther.transform.position = new Vector3(0, 0, 1);
                farther.sprite = sprite; farther.color = Color.blue;
                farther.sharedMaterial = spriteMaterial; farther.sortingOrder = 32767;
                camera.Render();
                RenderTexture.active = target; readback.ReadPixels(new Rect(0, 0, 32, 32), 0, 0); readback.Apply();
                var depth = readback.GetPixel(16, 16);
                Assert.Greater(depth.r, .8f); Assert.Less(depth.b, .1f, "sprite did not write its own depth");
                card.color = new Color(1, 0, 0, 0);
                wallMaterial.color = Color.green;
                camera.Render();
                RenderTexture.active = target; readback.ReadPixels(new Rect(0, 0, 32, 32), 0, 0); readback.Apply();
                var faded = readback.GetPixel(16, 16);
                Assert.Greater(faded.b, .8f); Assert.Less(faded.r, .1f, "fully faded sprite left invisible depth");
            }
            finally
            {
                RenderTexture.active = previous;
                Object.DestroyImmediate(root); Object.DestroyImmediate(sprite);
                Object.DestroyImmediate(texture); Object.DestroyImmediate(readback);
                Object.DestroyImmediate(spriteMaterial); Object.DestroyImmediate(wallMaterial);
                target.Release(); Object.DestroyImmediate(target);
            }
        }

        [Test] public void Contact_shadow_stays_on_the_ground_and_hides_with_its_owner()
        {
            var root = new GameObject("Contact shadow check");
            try
            {
                var card = new GameObject("Upright character").AddComponent<SpriteRenderer>();
                card.transform.SetParent(root.transform);
                card.transform.SetPositionAndRotation(new Vector3(12, 8, -1), HotelView3D.SpriteRotation);
                card.transform.localScale = HotelView3D.SpriteScale * 1.3f;
                ContactShadow.Attach(card, new Vector2(12, 8), new Vector2(.8f, .5f));
                var shadow = root.GetComponentInChildren<MeshRenderer>();
                Assert.IsNotNull(shadow);
                Assert.Less(Vector3.Distance(new Vector3(12, 8, -.01f), shadow.transform.position), .0001f);
                Assert.Less(Quaternion.Angle(Quaternion.identity, shadow.transform.rotation), .001f);
                card.enabled = false;
                ContactShadow.Place(card, new Vector2(12, 8), true);
                Assert.IsFalse(shadow.enabled, "a fog-hidden sprite must not leave a visible shadow");
                card.enabled = true;
                ContactShadow.Place(card, new Vector2(13, 9), false);
                Assert.IsFalse(shadow.enabled, "sleeping or dead characters have no standing shadow");
            }
            finally { Object.DestroyImmediate(root); }
        }
    }
}
