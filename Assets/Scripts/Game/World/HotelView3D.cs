using UnityEngine;

namespace BadAppleHotel.Game
{
    /// <summary>Logical movement stays on XY; negative Z is height. The view never rotates.</summary>
    public static class HotelView3D
    {
        public const float FollowSize = 6f;
        public static readonly Quaternion Rotation = Quaternion.Euler(0, 0, -45) * Quaternion.Euler(-40, 0, 0);
        public static readonly Vector3 Forward = Rotation * Vector3.forward;
        public static readonly Vector3 Right = Rotation * Vector3.right;
        public static readonly Vector3 Up = Rotation * Vector3.up;
        public static readonly Quaternion SpriteRotation = Quaternion.LookRotation(new Vector3(Forward.x,Forward.y,0),Vector3.back);
        public static readonly Vector3 SpriteScale = new Vector3(1,1/Mathf.Sin(40*Mathf.Deg2Rad),1);
        public static Vector2 Facing(Vector2 world) => new Vector2(Vector2.Dot(world, Right), Vector2.Dot(world, Up));
        public static Vector2 Move(Vector2 screen)
        {
            Vector2 world = (Vector2)Right * screen.x + ((Vector2)Up).normalized * screen.y;
            return Vector2.ClampMagnitude(world, 1);
        }
        public static Vector2 GroundPoint(Camera camera, Vector2 screen)
        {
            var ray = camera.ScreenPointToRay(screen);
            return ray.GetPoint(-ray.origin.z / ray.direction.z);
        }
        public static Vector2 ViewExtents(float size, float aspect)
        {
            // Ground-plane footprint of the four screen corners under this orthographic camera.
            Vector3 x = Right * size * aspect, y = Up * size;
            x -= Forward * (x.z / Forward.z); y -= Forward * (y.z / Forward.z);
            return new Vector2(Mathf.Abs(x.x) + Mathf.Abs(y.x), Mathf.Abs(x.y) + Mathf.Abs(y.y));
        }
        public static Vector2 Clamp(Vector2 target, float size, float aspect, int width, int height)
        {
            // Clamp the focal point, not its footprint: at an isometric map corner, clamping
            // the footprint can push the followed actor completely off screen.
            return new Vector2(Mathf.Clamp(target.x,0,width),Mathf.Clamp(target.y,0,height));
        }
        public static void Billboard(SpriteRenderer sr, Vector2 ground, float lift = 0.02f)
        {
            float footOffset = sr.sprite == null ? 0 : -Mathf.Min(0,sr.sprite.bounds.min.y) * SpriteScale.y;
            sr.transform.SetPositionAndRotation(new Vector3(ground.x, ground.y, -lift-footOffset), SpriteRotation);
            sr.transform.localScale = SpriteScale;
            sr.sortingOrder = 5000 - Mathf.RoundToInt((ground.x + ground.y) * 10);
        }
    }
}
