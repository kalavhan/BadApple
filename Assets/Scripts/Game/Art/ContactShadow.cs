using UnityEngine;

namespace BadAppleHotel.Game
{
    /// <summary>A ground-plane oval whose visibility follows its owning world sprite.</summary>
    public sealed class ContactShadow : MonoBehaviour
    {
        static Mesh quad;
        static Material material;
        SpriteRenderer source;
        MeshRenderer shadow;
        MaterialPropertyBlock properties;
        Vector2 ground, size;
        bool visible = true;
        float opacity = -1;

        public static ContactShadow Attach(SpriteRenderer source, Vector2 ground, Vector2 size)
        {
            if (source == null) return null;
            var component = source.GetComponent<ContactShadow>();
            if (component == null) component = source.gameObject.AddComponent<ContactShadow>();
            component.source = source;
            component.ground = ground;
            component.size = size;
            component.Initialize();
            component.Sync();
            return component;
        }

        public static void Place(SpriteRenderer source, Vector2 ground, bool visible)
        {
            if (source != null && source.TryGetComponent<ContactShadow>(out var shadow))
            {
                shadow.ground = ground;
                shadow.visible = visible;
                shadow.Sync();
            }
        }

        void Initialize()
        {
            if (shadow != null) return;
            if (quad == null)
            {
                quad = new Mesh { name = "Contact shadow quad" };
                quad.vertices = new[] { new Vector3(-.5f, -.5f, 0), new Vector3(.5f, -.5f, 0), new Vector3(.5f, .5f, 0), new Vector3(-.5f, .5f, 0) };
                quad.uv = new[] { Vector2.zero, Vector2.right, Vector2.one, Vector2.up };
                quad.triangles = new[] { 0, 1, 2, 0, 2, 3 };
                quad.RecalculateBounds();
            }
            if (material == null)
            {
                var shader = Resources.Load<Shader>("Shaders/ContactShadow");
                if (shader == null) return;
                material = new Material(shader) { name = "Ground contact shadows" };
            }
            var go = new GameObject("Contact shadow");
            go.transform.SetParent(transform, false);
            go.AddComponent<MeshFilter>().sharedMesh = quad;
            shadow = go.AddComponent<MeshRenderer>();
            shadow.sharedMaterial = material;
            shadow.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            shadow.receiveShadows = false;
            properties = new MaterialPropertyBlock();
        }

        void LateUpdate() => Sync();
        void OnEnable() => Sync();
        void OnDisable() { if (shadow != null) shadow.enabled = false; }

        void Sync()
        {
            if (shadow == null || source == null) return;
            shadow.enabled = visible && source.enabled && source.color.a > .01f;
            if (!shadow.enabled) return;
            shadow.transform.SetPositionAndRotation(new Vector3(ground.x, ground.y, -.01f), Quaternion.identity);
            // The parent is an upright sprite card and can rotate or grow. Restore the shadow's
            // complete world matrix by parenting its mesh to the same ground-world transform.
            if (shadow.transform.parent != source.transform.parent) shadow.transform.SetParent(source.transform.parent, true);
            shadow.transform.localScale = new Vector3(size.x, size.y, 1);
            if (!Mathf.Approximately(opacity, source.color.a))
            {
                opacity = source.color.a;
                properties.SetFloat("_Opacity", opacity);
                shadow.SetPropertyBlock(properties);
            }
        }

        void OnDestroy()
        {
            // It is a sibling so the sprite's pitch/scale cannot shear the ground oval.
            if (shadow == null) return;
            if (Application.isPlaying) Destroy(shadow.gameObject); else DestroyImmediate(shadow.gameObject);
        }
    }
}
